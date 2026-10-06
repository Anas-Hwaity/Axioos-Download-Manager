using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using TraceLog;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.Util;
using ADM.Core.SocialAnalysis;
#if WINDOWS
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace ADM.Core.BrowserMonitoring
{
    internal sealed class BrowserProtocolPipeServer : IDisposable
    {
        private volatile bool disposed;
        private NamedPipeServerStream? pendingListener;
        private readonly IApplicationRuntimeContext runtimeContext;
        private readonly BrowserTakeoverOwnershipStore ownershipStore;
        private readonly IBrowserSessionSink browserSessionSink;
        private readonly ISocialAnalysisService socialAnalysisService;
        private readonly IpcHttpMessageProcessor controlProcessor;

        internal BrowserProtocolPipeServer(IApplicationRuntimeContext runtimeContext, IBrowserSessionSink browserSessionSink, ISocialAnalysisService socialAnalysisService, IpcHttpMessageProcessor controlProcessor)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.browserSessionSink = browserSessionSink ?? throw new ArgumentNullException(nameof(browserSessionSink));
            this.socialAnalysisService = socialAnalysisService ?? throw new ArgumentNullException(nameof(socialAnalysisService));
            this.controlProcessor = controlProcessor ?? throw new ArgumentNullException(nameof(controlProcessor));
            ownershipStore = new BrowserTakeoverOwnershipStore(runtimeContext.BrowserProtocolStateDirectory);
            try
            {
                var released = ownershipStore.ReleaseUnconfirmed();
                if (released > 0) Log.Debug("Released " + released + " browser takeover requests left unconfirmed by an earlier session");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Unconfirmed browser takeover requests could not be released");
            }
        }

        internal void Run()
        {
            var listener = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "ADM browser protocol pipe"
            };
            listener.Start();
        }

        private void ListenLoop()
        {
            while (!disposed)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = CreateServerPipe();
                    BrowserProtocolRuntimeDiagnostics.MarkListening();
                    pendingListener = pipe;
                    pipe.WaitForConnection();
                    pendingListener = null;
                    var connected = pipe;
                    pipe = null;
                    BrowserProtocolRuntimeDiagnostics.MarkConnectionOpened();
                    var worker = new Thread(() => HandleConnection(connected))
                    {
                        IsBackground = true,
                        Name = "ADM browser protocol pipe client"
                    };
                    worker.Start();
                }
                catch (ObjectDisposedException) when (disposed)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!disposed)
                    {
                        BrowserProtocolRuntimeDiagnostics.MarkListenerFault(ex);
                        Log.Debug(ex, "Browser protocol pipe listener failed");
                        Thread.Sleep(ex is UnauthorizedAccessException ? 30000 : 1000);
                    }
                }
                finally
                {
                    pendingListener = null;
                    pipe?.Dispose();
                }
            }
        }

        private static NamedPipeServerStream CreateServerPipe()
        {
#if WINDOWS
            var currentUser = WindowsIdentity.GetCurrent().User ??
                throw new InvalidOperationException("Current Windows user SID is unavailable.");
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
            return new NamedPipeServerStream(
                BrowserProtocolV1.DesktopPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, 0, security);
#else
            return new NamedPipeServerStream(
                BrowserProtocolV1.DesktopPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
#endif
        }

        private void HandleConnection(NamedPipeServerStream pipe)
        {
            try
            {
                HandleConnectionCore(pipe);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Browser protocol pipe client ended with an error");
            }
            finally
            {
                BrowserProtocolRuntimeDiagnostics.MarkConnectionClosed();
            }
        }

        private void HandleConnectionCore(NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                var negotiated = false;
                while (pipe.IsConnected)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = NativeMessageSerializer.ReadMessageBytes(pipe);
                    }
                    catch (IOException)
                    {
                        return;
                    }

                    JObject request;
                    try
                    {
                        request = BrowserProtocolJson.ParseBounded(bytes);
                    }
                    catch (JsonException)
                    {
                        WriteResult(pipe, null, "rejected", "MalformedJson", "The pipe message is not valid JSON.");
                        return;
                    }

                    var messageId = request.Value<string>("messageId");
                    if (!Guid.TryParse(messageId, out _))
                    {
                        WriteResult(pipe, null, "rejected", "InvalidMessageId", "messageId must be a UUID.");
                        continue;
                    }
                    var validMessageId = messageId!;
                    if (request.Value<int?>("protocolVersion") != BrowserProtocolV1.ProtocolVersion)
                    {
                        WriteResult(pipe, validMessageId, "unsupported", "UnsupportedProtocolVersion", "No compatible desktop pipe protocol version is available.");
                        continue;
                    }

                    var type = request.Value<string>("type") ?? string.Empty;
                    if (!negotiated)
                    {
                        if (type != "BrowserHostHello")
                        {
                            BrowserProtocolRuntimeDiagnostics.MarkHandshakeRejected();
                            WriteResult(pipe, validMessageId, "rejected", "HandshakeRequired", "BrowserHostHello must be the first pipe message.");
                            continue;
                        }
                        var payload = request["payload"] as JObject;
                        if (!string.Equals(payload?.Value<string>("productIdentity"), BrowserProtocolV1.BrowserHostProductIdentity, StringComparison.Ordinal))
                        {
                            BrowserProtocolRuntimeDiagnostics.MarkHandshakeRejected();
                            WriteResult(pipe, validMessageId, "rejected", "InvalidProductIdentity", "The browser-host product identity is not accepted.");
                            continue;
                        }
                        var origin = payload?.Value<string>("origin") ?? string.Empty;
                        if (!IsSupportedBrowserOrigin(origin))
                        {
                            BrowserProtocolRuntimeDiagnostics.MarkHandshakeRejected();
                            WriteResult(pipe, validMessageId, "rejected", "InvalidBrowserOrigin", "The browser origin is not accepted.");
                            continue;
                        }
                        negotiated = true;
                        BrowserProtocolRuntimeDiagnostics.MarkHandshakeAccepted();
                        WriteEnvelope(pipe, NewEnvelope("BrowserHostHelloAck", validMessageId, new JObject
                        {
                            ["selectedProtocolVersion"] = BrowserProtocolV1.ProtocolVersion,
                            ["productIdentity"] = BrowserProtocolV1.BrowserHostProductIdentity
                        }));
                        continue;
                    }

                    if (type == "Ping")
                    {
                        WriteResult(pipe, validMessageId, "accepted", "Ready", "The desktop browser-protocol endpoint is ready.");
                        continue;
                    }
                    if (type == "QueryTakeoverOwnership")
                    {
                        HandleOwnershipQuery(pipe, validMessageId, request["payload"] as JObject);
                        continue;
                    }
                    if (type == "DownloadTakeoverRequest")
                    {
                        HandleDownloadTakeover(pipe, validMessageId, request["payload"] as JObject);
                        continue;
                    }
                    if (type == "SocialAnalysisSessionMaterial")
                    {
                        HandleSocialAnalysisSessionMaterial(pipe, validMessageId, request["payload"] as JObject);
                        continue;
                    }
                    if (type == "SyncState")
                    {
                        HandleSyncState(pipe, validMessageId);
                        continue;
                    }
                    if (type == "BrowserControlCommand")
                    {
                        HandleBrowserControlCommand(pipe, validMessageId, request["payload"] as JObject);
                        continue;
                    }

                    WriteResult(pipe, validMessageId, "unsupported", "UnsupportedMessageType", "This command has not migrated to the desktop pipe endpoint yet.");
                }
            }
        }


        private void HandleSyncState(Stream pipe, string messageId)
        {
            try
            {
                var config = controlProcessor.HandleProtocolControlCommand("/sync", null);
                WriteEnvelope(pipe, NewEnvelope("CommandResult", messageId, new JObject
                {
                    ["status"] = "accepted",
                    ["code"] = "SyncStateReady",
                    ["detail"] = "Desktop browser state is available.",
                    ["config"] = config
                }));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Browser protocol sync-state request failed");
                WriteResult(pipe, messageId, "error", "SyncStateFailed", "The desktop could not provide browser state.");
            }
        }

        private void HandleBrowserControlCommand(Stream pipe, string messageId, JObject? payload)
        {
            var route = payload?.Value<string>("route") ?? string.Empty;
            var allowed = route == "/download" || route == "/media" || route == "/tab-update" ||
                          route == "/page-session" || route == "/tab-closed" ||
                          route == "/analyze-page-video" || route == "/cancel-page-video-analysis" ||
                          route == "/vid" || route == "/clear";
            if (!allowed)
            {
                WriteResult(pipe, messageId, "rejected", "UnsupportedBrowserControlRoute", "The browser control route is not supported by protocol v1.");
                return;
            }
            try
            {
                var config = controlProcessor.HandleProtocolControlCommand(route, payload?["data"]);
                WriteEnvelope(pipe, NewEnvelope("CommandResult", messageId, new JObject
                {
                    ["status"] = "accepted",
                    ["code"] = "BrowserControlAccepted",
                    ["detail"] = "Browser control command was processed by the desktop.",
                    ["config"] = config
                }));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Browser protocol control command failed");
                WriteResult(pipe, messageId, "error", "BrowserControlFailed", "The desktop could not process the browser control command.");
            }
        }

        private void HandleSocialAnalysisSessionMaterial(Stream pipe, string messageId, JObject? payload)
        {
            var tabId = payload?.Value<string>("tabId") ?? string.Empty;
            var pageSessionId = payload?.Value<string>("pageSessionId") ?? string.Empty;
            var targetUrl = payload?.Value<string>("targetUrl") ?? string.Empty;
            var cookieHeader = payload?.Value<string>("cookieHeader") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tabId) || string.IsNullOrWhiteSpace(pageSessionId) ||
                !Uri.TryCreate(targetUrl, UriKind.Absolute, out var target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) ||
                runtimeContext.VideoTracker.CurrentPageSession(tabId) == null ||
                !socialAnalysisService.IsSupportedSocialUrl(targetUrl))
            {
                WriteResult(pipe, messageId, "rejected", "InvalidSocialAnalysisSession", "Session material must target the current supported social page.");
                return;
            }
            if (string.IsNullOrEmpty(cookieHeader) || cookieHeader.Length > 65536 ||
                cookieHeader.IndexOf('\r') >= 0 || cookieHeader.IndexOf('\n') >= 0)
            {
                WriteResult(pipe, messageId, "rejected", "InvalidSessionCookies", "Browser session cookies are missing or invalid.");
                return;
            }
            var operationId = tabId + "|" + targetUrl;
            var material = new BrowserSessionMaterial
            {
                Origin = new Uri(target.GetLeftPart(UriPartial.Authority)),
                CookieHeader = cookieHeader
            };
            if (!browserSessionSink.StoreSession(operationId, material))
            {
                WriteResult(pipe, messageId, "rejected", "SessionMaterialRejected", "Browser session material could not be accepted.");
                return;
            }
            WriteResult(pipe, messageId, "accepted", "SessionMaterialAccepted", "Scoped browser session material is ready for one authenticated analysis retry.");
        }

        private void HandleOwnershipQuery(Stream pipe, string messageId, JObject? payload)
        {
            var identity = payload?.Value<string>("browserDownloadIdentity") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identity))
            {
                WriteResult(pipe, messageId, "rejected", "InvalidIdempotencyKey", "browserDownloadIdentity is required.");
                return;
            }
            var record = ownershipStore.Get(identity);
            if (record == null)
            {
                WriteResult(pipe, messageId, "rejected", "OwnershipNotFound", "No durable desktop ownership record exists.");
                return;
            }
            if (record.State == "accepted" && !BrowserTakeoverOwnershipStore.IsPromptId(record.DesktopDownloadId))
            {
                WriteResult(pipe, messageId, "accepted", "DurableAccepted", "Desktop ownership is durable.", record.DesktopDownloadId);
                return;
            }
            if (record.State == "pending")
            {
                WriteResult(pipe, messageId, "busy", "OwnershipPending", "Desktop ownership is still pending.");
                return;
            }
            WriteResult(pipe, messageId, "rejected", "OwnershipDeclined", "Desktop did not take this browser download.");
        }

        private void HandleDownloadTakeover(Stream pipe, string messageId, JObject? payload)
        {
            var identity = payload?.Value<string>("browserDownloadIdentity") ?? string.Empty;
            var url = payload?.Value<string>("finalUrl") ?? payload?.Value<string>("url") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identity) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                WriteResult(pipe, messageId, "rejected", "InvalidTakeoverRequest", "A stable browser identity and HTTP(S) URL are required.");
                return;
            }
            var existing = ownershipStore.Get(identity);
            if (existing != null)
            {
                if (existing.State == "accepted" && !BrowserTakeoverOwnershipStore.IsPromptId(existing.DesktopDownloadId))
                    WriteResult(pipe, messageId, "duplicate", "AlreadyAccepted", "Desktop already owns this browser download.", existing.DesktopDownloadId);
                else if (existing.State == "pending")
                    WriteResult(pipe, messageId, "busy", "OwnershipPending", "A prior takeover request is still pending.");
                else
                    WriteResult(pipe, messageId, "rejected", "OwnershipDeclined", "Desktop did not take this browser download.");
                return;
            }
            if (!ownershipStore.TryReserve(identity, out _))
            {
                WriteResult(pipe, messageId, "busy", "OwnershipPending", "A concurrent takeover request already owns this idempotency key.");
                return;
            }
            try
            {
                var requestedFile = payload?.Value<string>("filename");
                var rawFileName = string.IsNullOrWhiteSpace(requestedFile) ? FileHelper.GetFileName(uri) : requestedFile;
                var file = FileHelper.SanitizeFileName(rawFileName ?? "download") ?? "download";
                var headers = TakeoverRequestHeaders(payload);
                var cookies = TakeoverCookies(payload);
                var message = new Message { Url = url, File = file, RequestHeaders = headers, Cookies = cookies };
                message.FileNameIsFromUrl = string.IsNullOrWhiteSpace(requestedFile);
                var totalSize = payload?.Value<long?>("totalSize") ?? 0;
                if (totalSize > 0)
                {
                    message.ResponseHeaders["Content-Length"] = new List<string> { totalSize.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                }
                if (runtimeContext.LinkRefresher.LinkAccepted(message))
                {
                    var refreshedId = "refresh-" + Guid.NewGuid().ToString("N");
                    ownershipStore.MarkAccepted(identity, refreshedId);
                    WriteResult(pipe, messageId, "accepted", "DurableAccepted", "Desktop used this link to refresh a waiting download.", refreshedId);
                    return;
                }
                if (!runtimeContext.StartDownloadAutomatically)
                {
                    message.CreationOutcome = downloadId => CompleteConfirmation(identity, downloadId);
                    runtimeContext.Application.ShowNewDownloadDialog(message);
                    WriteResult(pipe, messageId, "busy", "ConfirmationRequired", "Axioos is asking where to save this download.");
                    return;
                }
                var info = new SingleSourceHTTPDownloadInfo { Uri = url, File = file, Cookies = cookies };
                if (headers.ContainsKey("User-Agent")) info.Headers = headers;
                var nameMode = string.IsNullOrWhiteSpace(requestedFile) ? FileNameFetchMode.FileNameAndExtension : FileNameFetchMode.None;
                var desktopId = runtimeContext.CoreService.StartDownload(
                    info, file, nameMode, null, true, null, runtimeContext.Proxy, null, false);
                if (string.IsNullOrWhiteSpace(desktopId))
                {
                    ownershipStore.ClearPending(identity);
                    WriteResult(pipe, messageId, "error", "DesktopCreateFailed", "Desktop could not durably create the download.");
                    return;
                }
                ownershipStore.MarkAccepted(identity, desktopId!);
                WriteResult(pipe, messageId, "accepted", "DurableAccepted", "Desktop durably accepted the browser download.", desktopId);
            }
            catch (Exception ex)
            {
                ownershipStore.ClearPending(identity);
                Log.Debug(ex, "Browser takeover creation failed");
                WriteResult(pipe, messageId, "error", "DesktopCreateFailed", "Desktop could not durably create the download.");
            }
        }

        private void CompleteConfirmation(string identity, string? desktopDownloadId)
        {
            try
            {
                if (desktopDownloadId == null || desktopDownloadId.Trim().Length == 0)
                {
                    ownershipStore.MarkDeclined(identity);
                    return;
                }
                ownershipStore.MarkAccepted(identity, desktopDownloadId);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Browser takeover confirmation could not be recorded");
                try
                {
                    ownershipStore.ClearPending(identity);
                }
                catch (Exception clearError)
                {
                    Log.Debug(clearError, "Unrecorded browser takeover request could not be released");
                }
            }
        }

        private static bool IsSingleLine(string value, int maxLength)
        {
            return value.Length > 0 && value.Length <= maxLength && value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\0') < 0;
        }

        private static Dictionary<string, List<string>> TakeoverRequestHeaders(JObject? payload)
        {
            var headers = new Dictionary<string, List<string>>();
            var referrer = payload?.Value<string>("referrer");
            if (referrer != null && IsSingleLine(referrer, 8192) && Uri.TryCreate(referrer, UriKind.Absolute, out var referrerUri) &&
                (referrerUri.Scheme == Uri.UriSchemeHttp || referrerUri.Scheme == Uri.UriSchemeHttps))
            {
                headers["Referer"] = new List<string> { referrer };
            }
            var userAgent = payload?.Value<string>("userAgent");
            if (userAgent != null && IsSingleLine(userAgent, 512))
            {
                headers["User-Agent"] = new List<string> { userAgent };
            }
            return headers;
        }

        private static string? TakeoverCookies(JObject? payload)
        {
            var cookies = payload?.Value<string>("cookieHeader");
            if (cookies == null || !IsSingleLine(cookies, 65536)) return null;
            return cookies;
        }

        private static bool IsSupportedBrowserOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                   origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(origin, "adm-browser-helper@axioos.app", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject NewEnvelope(string type, string? replyTo, JObject payload)
        {
            return new JObject
            {
                ["protocolVersion"] = BrowserProtocolV1.ProtocolVersion,
                ["messageId"] = Guid.NewGuid().ToString("D"),
                ["sessionId"] = JValue.CreateNull(),
                ["type"] = type,
                ["sentAtUtc"] = DateTime.UtcNow.ToString("o"),
                ["replyTo"] = replyTo == null ? JValue.CreateNull() : new JValue(replyTo),
                ["payload"] = payload
            };
        }

        private static void WriteResult(Stream pipe, string? replyTo, string status, string code, string detail, string? desktopDownloadId = null)
        {
            var payload = new JObject
            {
                ["status"] = status,
                ["code"] = code,
                ["detail"] = detail
            };
            if (!string.IsNullOrWhiteSpace(desktopDownloadId)) payload["desktopDownloadId"] = desktopDownloadId;
            WriteEnvelope(pipe, NewEnvelope("CommandResult", replyTo, payload));
        }

        private static void WriteEnvelope(Stream pipe, JObject envelope)
        {
            NativeMessageSerializer.WriteMessage(pipe, envelope.ToString(Formatting.None));
        }

        public void Dispose()
        {
            disposed = true;
            BrowserProtocolRuntimeDiagnostics.MarkDisposed();
            try { pendingListener?.Dispose(); } catch { }
        }
    }
}
