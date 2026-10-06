using System;
using System.Text;
using System.Net;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ADM.Core.Util;
using ADM.Core.HttpServer;
using System.Threading;
using TraceLog;
using Translations;
using System.IO;
using System.Collections.Generic;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.SocialAnalysis;
using ADM.Compatibility;

namespace ADM.Core.BrowserMonitoring
{
    public class IpcHttpMessageProcessor
    {
        private const int MaxSyncedVideos = 200;
        private const int MaxSyncedAnalyses = 64;
        private sealed class BrowserAnalysisStatus
        {
            public string TabId { get; set; } = string.Empty;
            public string PageSessionId { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string State { get; set; } = "queued";
            public string Message { get; set; } = string.Empty;
            public string ErrorCode { get; set; } = string.Empty;
            public DateTime StartedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        private readonly object analysisSync = new();
        private readonly Dictionary<string, BrowserAnalysisStatus> analysisStatuses = new();
        private readonly ISocialAnalysisService socialAnalysisService;

        private NanoServer server;
        private readonly IApplicationRuntimeContext runtimeContext;
        private static string[] blockedHeaders = { "accept", "if", "authorization", "proxy", "connection", "expect", "TE",
            "upgrade", "range", "cookie", "transfer-encoding", "content-type", "content-length","content-encoding" };

        public IpcHttpMessageProcessor(IApplicationRuntimeContext runtimeContext, ISocialAnalysisService socialAnalysisService)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.socialAnalysisService = socialAnalysisService ?? throw new ArgumentNullException(nameof(socialAnalysisService));
            server = new NanoServer(IPAddress.Loopback, 8597);
            server.RequestReceived += (sender, args) =>
            {
                HandleRequest(args.RequestContext);
            };
        }

        public void Run()
        {
            new Thread(() =>
            {
                try
                {
                    server.Start();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex.ToString());
                    if (SingleInstance.AnotherAccountIsRunning())
                    {
                        Log.Debug("Browser control is held by another Windows account");
                        return;
                    }
                    runtimeContext.Application.ShowMessageBox(null, TextResource.GetText("MSG_ALREADY_RUNNING"));
                }
            }).Start();
        }

        internal JObject HandleProtocolControlCommand(string requestPath, JToken? payload)
        {
            var bodyText = payload == null || payload.Type == JTokenType.Null
                ? "{}"
                : payload.ToString(Formatting.None);
            using var tcp = new System.Net.Sockets.TcpClient();
            var context = new RequestContext(
                requestPath,
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                Encoding.UTF8.GetBytes(bodyText),
                tcp,
                false);

            switch (requestPath)
            {
                case "/sync": break;
                case "/download": OnDownloadMessage(context); break;
                case "/media": OnMediaMessage(context); break;
                case "/tab-update": OnTabUpdateMessage(context); break;
                case "/page-session": OnPageSessionMessage(context); break;
                case "/tab-closed": OnTabClosedMessage(context); break;
                case "/analyze-page-video": OnAnalyzePageVideoMessage(context); break;
                case "/cancel-page-video-analysis": OnCancelPageVideoAnalysisMessage(context); break;
                case "/vid": OnVideoDownloadMessage(context); break;
                case "/clear": runtimeContext.VideoTracker.ClearVideoList(); break;
                default: throw new ArgumentException("Unsupported browser protocol control command: " + requestPath);
            }

            var configJson = CreateConfigJson();
            return string.IsNullOrWhiteSpace(configJson) ? new JObject() : JObject.Parse(configJson);
        }

        public void HandleRequest(RequestContext context)
        {
            try
            {
                ValidateLocalControlOrigin(context);
                switch (context.RequestPath)
                {
                    case "/sync":
                        break;
                    case "/download":
                        OnDownloadMessage(context);
                        break;
                    case "/media":
                        OnMediaMessage(context);
                        break;
                    case "/tab-update":
                        OnTabUpdateMessage(context);
                        break;
                    case "/page-session":
                        OnPageSessionMessage(context);
                        break;
                    case "/tab-closed":
                        OnTabClosedMessage(context);
                        break;
                    case "/analyze-page-video":
                        OnAnalyzePageVideoMessage(context);
                        break;
                    case "/cancel-page-video-analysis":
                        OnCancelPageVideoAnalysisMessage(context);
                        break;
                    case "/vid":
                        OnVideoDownloadMessage(context);
                        break;
                    case "/clear":
                        runtimeContext.VideoTracker.ClearVideoList();
                        break;
                    case "/link":
                        OnBatchMessage(context);
                        break;
                    case "/args":
                        OnArgsMessage(context);
                        break;
                    default:
                        throw new ArgumentException("Unsupported request: " + context.RequestPath);
                }
                OnSyncMessage(context);
            }
            catch (Exception ex)
            {
                Log.Debug(ex.ToString());
                throw;
            }
        }


        private static void ValidateLocalControlOrigin(RequestContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var origins = context.RequestHeaders.GetValueOrDefault("Origin");
            if (origins == null || origins.Count == 0) return;
            if (origins.Count != 1) throw new UnauthorizedAccessException("Ambiguous browser Origin header.");

            var origin = origins[0] ?? string.Empty;
            if (context.RequestPath == "/args")
                throw new UnauthorizedAccessException("The instance-control endpoint does not accept browser-originated requests.");
            if (origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase))
                return;

            throw new UnauthorizedAccessException("Cross-site browser access to the local ADM control endpoint is not allowed.");
        }

        private static string AnalysisKey(string tabId, string url)
        {
            return (tabId ?? string.Empty) + "|" + (url ?? string.Empty);
        }

        private void OnAnalyzePageVideoMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null || string.IsNullOrEmpty(msg.Url) || string.IsNullOrEmpty(msg.TabId) ||
                string.IsNullOrEmpty(msg.PageSessionId))
            {
                return;
            }
            if (!socialAnalysisService.IsSupportedSocialUrl(msg.Url))
            {
                SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "failed",
                    "yt-dlp analysis is available for public web pages only.", "UnsupportedSocialHost");
                return;
            }
            if (!TryBeginAnalysis(msg.TabId, msg.PageSessionId, msg.Url))
            {
                return;
            }
            var thread = new Thread(() => AnalyzePageVideo(msg))
            {
                IsBackground = true,
                Name = "ADM browser video analysis"
            };
            thread.Start();
        }

        private bool TryBeginAnalysis(string tabId, string pageSessionId, string url)
        {
            var key = AnalysisKey(tabId, url);
            lock (analysisSync)
            {
                if (analysisStatuses.TryGetValue(key, out var current) && current.Url == url &&
                    (current.State == "queued" || current.State == "running")) return false;
                analysisStatuses[key] = new BrowserAnalysisStatus
                {
                    TabId = tabId, PageSessionId = pageSessionId, Url = url, State = "queued",
                    Message = "Accepted by Axioos. Preparing yt-dlp…", StartedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
            }
            runtimeContext.BroadcastConfigChange();
            return true;
        }

        private void OnCancelPageVideoAnalysisMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null || string.IsNullOrEmpty(msg.Url) || string.IsNullOrEmpty(msg.TabId))
            {
                return;
            }

            var key = AnalysisKey(msg.TabId, msg.Url);
            lock (analysisSync)
            {
                if (!analysisStatuses.TryGetValue(key, out var status) || status.Url != msg.Url ||
                    (status.State != "queued" && status.State != "running"))
                {
                    return;
                }
                status.State = "cancelled";
                status.Message = "Analysis cancelled by user.";
                status.ErrorCode = "Cancelled";
                status.UpdatedAt = DateTime.UtcNow;
            }
            socialAnalysisService.Cancel(key);
            runtimeContext.BroadcastConfigChange();
        }

        private bool IsAnalysisCancelled(string tabId, string pageSessionId, string url)
        {
            var key = AnalysisKey(tabId, url);
            lock (analysisSync)
            {
                return analysisStatuses.TryGetValue(key, out var status) && status.Url == url && status.State == "cancelled";
            }
        }

        private void SetAnalysisStatus(string tabId, string pageSessionId, string url, string state, string message, string errorCode = "")
        {
            var key = AnalysisKey(tabId, url);
            var currentSession = runtimeContext.VideoTracker.CurrentPageSession(tabId);
            lock (analysisSync)
            {
                if (!analysisStatuses.TryGetValue(key, out var status) || status.Url != url)
                {
                    status = new BrowserAnalysisStatus { TabId = tabId, PageSessionId = pageSessionId, Url = url, StartedAt = DateTime.UtcNow };
                    analysisStatuses[key] = status;
                }
                if (status.State == "cancelled" && state != "queued") return;
                status.PageSessionId = currentSession ?? status.PageSessionId;
                status.State = state;
                status.Message = message ?? string.Empty;
                status.ErrorCode = errorCode ?? string.Empty;
                status.UpdatedAt = DateTime.UtcNow;
            }
            runtimeContext.BroadcastConfigChange();
        }

        private List<BrowserAnalysisStatus> GetAnalysisStatuses()
        {
            lock (analysisSync)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-20);
                foreach (var key in analysisStatuses.Where(kv => kv.Value.UpdatedAt < cutoff).Select(kv => kv.Key).ToList())
                    analysisStatuses.Remove(key);
                return analysisStatuses.Values.OrderByDescending(v => v.UpdatedAt).Take(MaxSyncedAnalyses).Select(v => new BrowserAnalysisStatus
                {
                    TabId = v.TabId, PageSessionId = v.PageSessionId, Url = v.Url, State = v.State, Message = v.Message, ErrorCode = v.ErrorCode,
                    StartedAt = v.StartedAt, UpdatedAt = v.UpdatedAt
                }).ToList();
            }
        }

        private static string HostOf(string? url)
        {
            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return string.Empty;
            var host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.") ? host.Substring(4) : host;
        }

        private void RebindAnalysisStatuses(string tabId, string pageSessionId, string? tabUrl)
        {
            var host = HostOf(tabUrl);
            var abandoned = new List<string>();
            lock (analysisSync)
            {
                foreach (var entry in analysisStatuses.Where(kv => kv.Value.TabId == tabId).ToList())
                {
                    if (host.Length > 0 && HostOf(entry.Value.Url) != host)
                    {
                        if (entry.Value.State == "queued" || entry.Value.State == "running")
                        {
                            entry.Value.State = "cancelled";
                            entry.Value.Message = "You left the page, so the analysis was stopped.";
                            entry.Value.ErrorCode = "Cancelled";
                            entry.Value.UpdatedAt = DateTime.UtcNow;
                            abandoned.Add(entry.Key);
                        }
                        continue;
                    }
                    entry.Value.PageSessionId = pageSessionId;
                }
            }
            foreach (var key in abandoned) socialAnalysisService.Cancel(key);
            runtimeContext.BroadcastConfigChange();
        }

        private void ClearAnalysisStatusForTab(string tabId)
        {
            lock (analysisSync)
            {
                foreach (var key in analysisStatuses.Where(kv => kv.Value.TabId == tabId).Select(kv => kv.Key).ToList())
                    analysisStatuses.Remove(key);
            }
        }

        private static Dictionary<string, List<string>> BuildAnalyzerHeaders(ExtensionData msg)
        {
            var headers = new Dictionary<string, List<string>>();
            if (!string.IsNullOrEmpty(msg.UserAgent)) headers["User-Agent"] = new List<string> { msg.UserAgent };
            if (!string.IsNullOrEmpty(msg.Url)) headers["Referer"] = new List<string> { msg.Url };
            return headers;
        }

        private static int HeightOf(SocialMediaVariant item) => Int32.TryParse(item.Height, out var h) ? h : 0;
        private static int ContainerRank(SocialMediaVariant item)
        {
            var ext = (item.FileExtension ?? string.Empty).ToLowerInvariant();
            if (ext == "mp4") return 4; if (ext == "mkv") return 3; if (ext == "webm") return 2; return 1;
        }
        private static string AnalyzerLabel(SocialMediaVariant item)
        {
            var parts = new List<string>();
            if (HeightOf(item) > 0) parts.Add(HeightOf(item) + "p");
            if (!string.IsNullOrEmpty(item.FileExtension)) parts.Add(item.FileExtension.ToUpperInvariant());
            if (!string.IsNullOrEmpty(item.VideoCodec)) parts.Add(item.VideoCodec);
            if (!string.IsNullOrEmpty(item.AudioBitrateKbps)) parts.Add(item.AudioBitrateKbps + " kbps audio");
            if (parts.Count == 0) parts.Add("yt-dlp video");
            return string.Join("  •  ", parts);
        }

        private bool AddAnalyzedFormat(SocialMediaVariant item, string title, ExtensionData msg)
        {
            var headers = BuildAnalyzerHeaders(msg);
            var ext = string.IsNullOrEmpty(item.FileExtension) ? "mp4" : item.FileExtension.ToLowerInvariant();
            var file = FileHelper.SanitizeTitle(title, "video") + "." + ext;
            var display = new StreamingVideoDisplayInfo
            {
                Quality = AnalyzerLabel(item), CreationTime = DateTime.Now, TabUrl = msg.Url, TabId = msg.TabId,
                PageSessionId = msg.PageSessionId, Source = MediaSources.YtDlp
            };
            try
            {
                switch (item.Kind)
                {
                    case SocialMediaVariantKind.Http:
                        if (string.IsNullOrEmpty(item.VideoUrl)) return false;
                        if (!string.IsNullOrEmpty(item.AudioUrl))
                            runtimeContext.VideoTracker.AddVideoNotification(display, new DualSourceHTTPDownloadInfo
                            { Uri1 = item.VideoUrl, Uri2 = item.AudioUrl, Headers1 = headers, Headers2 = headers, Cookies1 = msg.Cookie, Cookies2 = msg.Cookie, File = file });
                        else
                            runtimeContext.VideoTracker.AddVideoNotification(display, new SingleSourceHTTPDownloadInfo
                            { Uri = item.VideoUrl, Headers = headers, Cookies = msg.Cookie, File = file });
                        return true;
                    case SocialMediaVariantKind.Dash:
                        if (string.IsNullOrEmpty(item.VideoUrl) || string.IsNullOrEmpty(item.AudioUrl)) return false;
                        runtimeContext.VideoTracker.AddVideoNotification(display, new DualSourceHTTPDownloadInfo
                        { Uri1 = item.VideoUrl, Uri2 = item.AudioUrl, Headers1 = headers, Headers2 = headers, Cookies1 = msg.Cookie, Cookies2 = msg.Cookie, File = file });
                        return true;
                    case SocialMediaVariantKind.Hls:
                        if (string.IsNullOrEmpty(item.VideoUrl)) return false;
                        runtimeContext.VideoTracker.AddVideoNotification(display, new MultiSourceHLSDownloadInfo
                        { VideoUri = item.VideoUrl, AudioUri = item.AudioUrl, Headers = headers, Cookies = msg.Cookie, File = file });
                        return true;
                    case SocialMediaVariantKind.MpegDash:
                        if (string.IsNullOrEmpty(item.FragmentBaseUrl) || item.VideoFragments == null || item.VideoFragments.Count == 0 ||
                            item.AudioFragments == null || item.AudioFragments.Count == 0) return false;
                        var baseUri = new Uri(item.FragmentBaseUrl);
                        runtimeContext.VideoTracker.AddVideoNotification(display, new MultiSourceDASHDownloadInfo
                        {
                            VideoSegments = item.VideoFragments.Select(x => new Uri(baseUri, x.Path)).ToList(),
                            AudioSegments = item.AudioFragments.Select(x => new Uri(baseUri, x.Path)).ToList(),
                            AudioFormat = item.AudioFormat != null ? "." + item.AudioFormat : null,
                            VideoFormat = item.VideoFormat != null ? "." + item.VideoFormat : null, Headers = headers, Cookies = msg.Cookie, File = file, Url = msg.Url
                        });
                        return true;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "Unable to expose analyzed browser format"); }
            return false;
        }

        private void AnalyzePageVideo(ExtensionData msg)
        {
            var operationId = AnalysisKey(msg.TabId, msg.Url);
            try
            {
                if (IsAnalysisCancelled(msg.TabId, msg.PageSessionId, msg.Url)) return;
                SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "running", "Starting yt-dlp for this page…");
                var phase = "Starting yt-dlp for this page";
                var phaseSync = new object();
                var finished = false;
                SocialAnalysisResult result;
                var heartbeat = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        string text;
                        lock (phaseSync)
                        {
                            if (finished) return;
                            text = phase;
                        }
                        if (IsAnalysisCancelled(msg.TabId, msg.PageSessionId, msg.Url)) return;
                        lock (phaseSync)
                        {
                            if (finished) return;
                            SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "running", text);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Analysis heartbeat failed");
                    }
                }, null, 3000, 3000);
                try
                {
                    result = socialAnalysisService.Analyze(new SocialAnalysisRequest
                    {
                        OperationId = operationId,
                        Url = msg.Url,
                        TimeoutSeconds = 110,
                        Progress = text => { lock (phaseSync) phase = text; }
                    });
                }
                finally
                {
                    lock (phaseSync) finished = true;
                    heartbeat.Dispose();
                }
                if (IsAnalysisCancelled(msg.TabId, msg.PageSessionId, msg.Url)) return;
                msg.PageSessionId = runtimeContext.VideoTracker.CurrentPageSession(msg.TabId) ?? msg.PageSessionId;
                if (!result.IsSuccess)
                {
                    SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "failed",
                        string.IsNullOrEmpty(result.Message) ? "yt-dlp analysis failed." : result.Message, result.ErrorCode);
                    return;
                }

                SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "running", "yt-dlp returned metadata; ranking formats…");
                runtimeContext.VideoTracker.RemoveAnalyzerMedia(msg.TabId, msg.Url);
                var seen = new HashSet<string>();
                var added = 0;
                foreach (var format in result.Variants.OrderByDescending(HeightOf).ThenByDescending(ContainerRank))
                {
                    if (IsAnalysisCancelled(msg.TabId, msg.PageSessionId, msg.Url)) return;
                    if (HeightOf(format) <= 0) continue;
                    var key = $"{HeightOf(format)}|{(format.FileExtension ?? string.Empty).ToLowerInvariant()}|{format.Kind}";
                    if (!seen.Add(key)) continue;
                    if (AddAnalyzedFormat(format, format.Title, msg)) added++;
                    if (added >= 18) break;
                }
                if (added == 0)
                {
                    foreach (var format in result.Variants.Where(x => HeightOf(x) <= 0).OrderByDescending(ContainerRank))
                    {
                        if (IsAnalysisCancelled(msg.TabId, msg.PageSessionId, msg.Url)) return;
                        var key = $"0|{(format.FileExtension ?? string.Empty).ToLowerInvariant()}|{format.Kind}|{format.AudioBitrateKbps}";
                        if (!seen.Add(key)) continue;
                        if (AddAnalyzedFormat(format, format.Title, msg)) added++;
                        if (added >= 6) break;
                    }
                }
                if (added == 0)
                {
                    SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "failed", "yt-dlp found media, but Axioos could not expose a usable video format.");
                    return;
                }
                SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "ready", $"Found {added} downloadable video option{(added == 1 ? string.Empty : "s")}.");
            }
            catch (Exception ex)
            {
                if (IsAnalysisCancelled(msg.TabId, msg.PageSessionId, msg.Url)) return;
                Log.Debug(ex, "Browser social analysis failed");
                var message = string.IsNullOrEmpty(ex.Message) ? "yt-dlp analysis failed." : ex.Message;
                if (message.Length > 420) message = message.Substring(message.Length - 420);
                SetAnalysisStatus(msg.TabId, msg.PageSessionId, msg.Url, "failed", message, "UnhandledAnalysisError");
            }
        }

        private void OnArgsMessage(RequestContext context)
        {
            var args = JsonConvert.DeserializeObject<List<string>>(Encoding.UTF8.GetString(context.RequestBody!));
            if (args == null || args.Count == 0)
            {
                return;
            }
            ArgsProcessor.Process(args, runtimeContext);
        }

        private void OnVideoDownloadMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null)
            {
                return;
            }
            runtimeContext.VideoTracker.AddVideoDownload(msg.Vid);
        }

        private void OnTabUpdateMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null)
            {
                return;
            }
            runtimeContext.VideoTracker.UpdateMediaTitle(msg.TabId, msg.PageSessionId, msg.TabUrl, msg.TabTitle);
        }

        private void OnPageSessionMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null || string.IsNullOrEmpty(msg.TabId) || string.IsNullOrEmpty(msg.PageSessionId))
            {
                return;
            }
            if (runtimeContext.VideoTracker.IsRegisteredPageSession(msg.TabId, msg.PageSessionId))
            {
                return;
            }
            runtimeContext.VideoTracker.BeginPageSession(msg.TabId, msg.PageSessionId);
            RebindAnalysisStatuses(msg.TabId, msg.PageSessionId, msg.TabUrl);
            VideoUrlHelper.OnPageSessionChanged(msg.TabId, msg.PageSessionId);
        }

        private void OnTabClosedMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null || string.IsNullOrEmpty(msg.TabId))
            {
                return;
            }
            ClearAnalysisStatusForTab(msg.TabId);
            runtimeContext.VideoTracker.EndTabSession(msg.TabId);
            VideoUrlHelper.OnTabClosed(msg.TabId);
        }

        private void OnDownloadMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null)
            {
                return;
            }
            var dmsg = new Message();
            dmsg.Url = msg.Url;
            dmsg.RequestMethod = msg.Method;
            dmsg.RequestHeaders = msg.RequestHeaders;
            dmsg.ResponseHeaders = msg.ResponseHeaders;
            dmsg.Cookies = msg.Cookie;
            dmsg.File = string.IsNullOrWhiteSpace(msg.File) ? null! : FileHelper.SanitizeFileName(msg.File)!;
            dmsg.TabUrl = msg.TabUrl;
            dmsg.TabId = msg.TabId;
            dmsg.PageSessionId = msg.PageSessionId;
            dmsg.ObservedAtMonotonicMs = msg.ObservedAtMonotonicMs;
            RemoveBlockedHeaders(dmsg);
            runtimeContext.CoreService.AddDownload(dmsg);
        }

        private void OnMediaMessage(RequestContext context)
        {
            var msg = JsonConvert.DeserializeObject<ExtensionData>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msg == null)
            {
                return;
            }
            var dmsg = new Message();
            dmsg.Url = msg.Url;
            dmsg.RequestMethod = msg.Method;
            dmsg.RequestHeaders = msg.RequestHeaders;
            dmsg.ResponseHeaders = msg.ResponseHeaders;
            dmsg.Cookies = msg.Cookie;
            dmsg.File = FileHelper.SanitizeFileName(msg.File)!;
            dmsg.TabUrl = msg.TabUrl;
            dmsg.TabId = msg.TabId;
            dmsg.PageSessionId = msg.PageSessionId;
            dmsg.ObservedAtMonotonicMs = msg.ObservedAtMonotonicMs;
            RemoveBlockedHeaders(dmsg);
            if (!runtimeContext.VideoTracker.IsCurrentPageSession(dmsg.TabId, dmsg.PageSessionId))
            {
                return;
            }
            VideoUrlHelper.ProcessMediaMessage(dmsg, runtimeContext);
        }

        private void OnBatchMessage(RequestContext context)
        {
            var msgArr = JsonConvert.DeserializeObject<ExtensionData[]>(Encoding.UTF8.GetString(context.RequestBody!));
            if (msgArr == null)
            {
                return;
            }
            runtimeContext.CoreService.AddBatchLinks(msgArr.Select(msg =>
            {
                var dmsg = new Message();
                dmsg.Url = msg.Url;
                dmsg.RequestMethod = msg.Method;
                dmsg.RequestHeaders = msg.RequestHeaders;
                dmsg.ResponseHeaders = msg.ResponseHeaders;
                dmsg.Cookies = msg.Cookie;
                dmsg.File = string.IsNullOrWhiteSpace(msg.File) ? null! : FileHelper.SanitizeFileName(msg.File)!;
                dmsg.TabUrl = msg.TabUrl;
                dmsg.TabId = msg.TabId;
                dmsg.PageSessionId = msg.PageSessionId;
                dmsg.ObservedAtMonotonicMs = msg.ObservedAtMonotonicMs;
                RemoveBlockedHeaders(dmsg);
                return dmsg;
            }).ToList());
        }



        private void OnSyncMessage(RequestContext context)
        {
            var json = CreateConfigJson();
            context.ResponseStatus = new ResponseStatus
            {
                StatusCode = 200,
                StatusMessage = "OK"
            };
            context.AddResponseHeader("Content-Type", "application/json");
            context.AddResponseHeader("Cache-Control", "max-age=0, no-cache, must-revalidate");
            context.ResponseBody = Encoding.UTF8.GetBytes(json);
            context.SendResponse();
        }

        private string? CreateConfigJson()
        {
            try
            {
                var w = new StringWriter();
                using var writer = new JsonTextWriter(w);
                writer.CloseOutput = false;
                writer.Formatting = Formatting.None;

                writer.WriteStartObject();

                writer.WritePropertyName("enabled");
                writer.WriteValue(Config.Instance.IsBrowserMonitoringEnabled);

                writer.WritePropertyName("appearance");
                writer.WriteStartObject();
                writer.WritePropertyName("theme");
                writer.WriteValue(string.IsNullOrEmpty(Config.Instance.AppearanceTheme) ? "glacier" : Config.Instance.AppearanceTheme);
                writer.WritePropertyName("accent");
                writer.WriteValue(string.IsNullOrEmpty(Config.Instance.AppearanceAccent) ? "gradient" : Config.Instance.AppearanceAccent);
                writer.WriteEndObject();

                writer.WritePropertyName("fileExts");
                writer.WriteStartArray();
                foreach (var ext in Config.Instance.FileExtensions)
                {
                    writer.WriteValue(ext);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("blockedHosts");
                writer.WriteStartArray();
                foreach (var host in Config.Instance.BlockedHosts)
                {
                    writer.WriteValue(host);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("requestFileExts");
                writer.WriteStartArray();
                foreach (var ext in Config.Instance.VideoExtensions)
                {
                    writer.WriteValue(ext);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("mediaTypes");
                writer.WriteStartArray();
                foreach (var ext in new string[] { "audio/", "video/" })
                {
                    writer.WriteValue(ext);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("tabsWatcher");
                writer.WriteStartArray();
                foreach (var ext in new string[] { ".youtube.", "/watch?v=" })
                {
                    writer.WriteValue(ext);
                }
                writer.WriteEndArray();

                var videoList = runtimeContext.VideoTracker.GetVideoList();
                if (videoList.Count > MaxSyncedVideos) videoList = videoList.GetRange(videoList.Count - MaxSyncedVideos, MaxSyncedVideos);

                writer.WritePropertyName("videoList");
                writer.WriteStartArray();
                foreach (var video in videoList)
                {
                    writer.WriteStartObject();

                    writer.WritePropertyName("id");
                    writer.WriteValue(video.ID);

                    writer.WritePropertyName("text");
                    writer.WriteValue(video.Name);

                    writer.WritePropertyName("info");
                    writer.WriteValue(video.Description);

                    writer.WritePropertyName("tabId");
                    writer.WriteValue(video.TabId);

                    writer.WritePropertyName("pageSessionId");
                    writer.WriteValue(video.PageSessionId);

                    writer.WritePropertyName("source");
                    writer.WriteValue(video.Source);

                    writer.WritePropertyName("tabUrl");
                    writer.WriteValue(video.TabUrl);

                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WritePropertyName("analysisList");
                writer.WriteStartArray();
                foreach (var status in GetAnalysisStatuses())
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("tabId"); writer.WriteValue(status.TabId);
                    writer.WritePropertyName("pageSessionId"); writer.WriteValue(status.PageSessionId);
                    writer.WritePropertyName("url"); writer.WriteValue(status.Url);
                    writer.WritePropertyName("state"); writer.WriteValue(status.State);
                    writer.WritePropertyName("message"); writer.WriteValue(status.Message);
                    writer.WritePropertyName("code"); writer.WriteValue(status.ErrorCode);
                    writer.WritePropertyName("startedAt"); writer.WriteValue(status.StartedAt.ToUniversalTime().ToString("o"));
                    writer.WritePropertyName("updatedAt"); writer.WriteValue(status.UpdatedAt.ToUniversalTime().ToString("o"));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WritePropertyName("matchingHosts");
                writer.WriteStartArray();
                writer.WriteEndArray();

                writer.WriteEndObject();
                writer.Close();
                var str = w.ToString();
                return str;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error sending config");
                return null;
            }
        }

        private void RemoveBlockedHeaders(Message message)
        {
            foreach (var header in blockedHeaders)
            {
                string? keyName = null;
                foreach (var key in message.RequestHeaders.Keys)
                {
                    if (key.Equals(header, StringComparison.InvariantCultureIgnoreCase))
                    {
                        keyName = key;
                        break;
                    }
                }
                if (!String.IsNullOrEmpty(keyName))
                {
                    message.RequestHeaders.Remove(keyName!);
                }
            }
        }
    }
}
