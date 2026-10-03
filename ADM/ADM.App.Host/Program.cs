using Microsoft.Win32;
using NativeMessaging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ADM.Core;
using ADM.Core.BrowserMonitoring;

namespace ADM.App.Host
{
    class Program
    {
        private static IpcClient? _ipcClient;
        private static BrowserProtocolPipeClient? _protocolPipeClient;
        private static Stream? stdin;
        private static Stream? stdout;
        static void Main(string[] args)
        {
            Trace.WriteLine($"[adm-native-messaging-host] startup");

            var debugMode = Environment.GetEnvironmentVariable("ADM_DEBUG_MODE");
            if (!string.IsNullOrEmpty(debugMode) && debugMode == "1")
            {
                var logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "messaging-log.txt");
                Trace.Listeners.Add(new TextWriterTraceListener(logFile, "myListener"));
                Trace.AutoFlush = true;
            }

            Debug("Application_Startup");

            var isFirefox = true;
            if (args.Length > 0 && args[0].StartsWith("chrome-extension:"))
            {
                isFirefox = false;
            }

            stdin = Console.OpenStandardInput();
            stdout = Console.OpenStandardOutput();

            var browserOrigin = args.Length > 0 ? args[0] : "adm-browser-helper@axioos.app";

            try
            {
                Func<byte[], byte[]> forwardToDesktop = input => ForwardProtocolRequest(browserOrigin, input);
                Func<bool> ensureDesktop = () => EnsureDesktopProtocolConnection(browserOrigin, isFirefox);
                var protocolSession = new BrowserProtocolV1HostSession(SendToBrowser, forwardToDesktop, ensureDesktop);
                var legacyConfigStarted = false;
                while (true)
                {
                    var bytesFromBrowser = ReadMessageBytes(stdin);
                    if (protocolSession.TryHandle(bytesFromBrowser))
                    {
                        continue;
                    }
                    EnsureLegacyDesktopConnection(isFirefox);
                    if (!legacyConfigStarted)
                    {
                        ReadConfigUpdateFromADM();
                        legacyConfigStarted = true;
                    }
                    var text = Encoding.UTF8.GetString(bytesFromBrowser);
                    Debug(text);
                    var msg = JsonConvert.DeserializeObject<DownloadMessage>
                    (
                        text,
                        new JsonSerializerSettings
                        {
                            MissingMemberHandling = MissingMemberHandling.Ignore
                        }
                    );
                    SendArgsToADM(msg);
                }
            }
            catch (EndOfStreamException)
            {
                Debug("The browser closed the connection");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Debug(ex.Message, ex);
                Environment.Exit(1);
            }
        }

        public static void SendArgsToADM(DownloadMessage? msg)
        {
            Debug("SendArgsToADM...");

            if (msg == null)
            {
                return;
            }

            if (msg.Vid != null)
            {
                Debug($"vid: {msg.Vid}");
                SendVideoId(msg.Vid);
                return;
            }

            if (msg.Clear.HasValue && msg.Clear.Value)
            {
                SendClearCmd();
                return;
            }

            if (msg.TabUpdate != null)
            {
                SendTabUpdate(msg.TabUpdate);
                return;
            }

            if (msg.RequestData != null)
            {
                SendHeaderData(msg.RequestData);
                return;
            }

            if (msg.Url == null)
            {
                return;
            }

            var arguments = new List<string>();
            if (msg.Cookie != null)
            {
                arguments.Add("--cookie");
                arguments.Add(msg.Cookie);
            }

            if (msg.Headers != null)
            {
                foreach (var header in msg.Headers)
                {
                    arguments.Add("-H");
                    arguments.Add(header);
                }
            }

            if (msg.FileSize > 0)
            {
                arguments.Add("--known-file-size");
                arguments.Add(msg.FileSize + "");
            }

            if (!string.IsNullOrEmpty(msg.MimeType))
            {
                arguments.Add("--known-mime-type");
                arguments.Add(msg.MimeType!);
            }

            if (!string.IsNullOrEmpty(msg.FileName))
            {
                arguments.Add("--output");
                arguments.Add(msg.FileName!);
            }
            arguments.Add(msg.Url);
            _ipcClient!.Send(arguments);
        }

        private static void SendHeaderData(RequestData data)
        {
            Debug("Going to send media...");
            if (data.Url == null)
            {
                return;
            }
            var arguments = new List<string>();
            arguments.Add("--media");
            if (data.RequestHeaders != null)
            {
                foreach (var header in data.RequestHeaders)
                {
                    foreach (var value in header.Value)
                    {
                        arguments.Add("-H");
                        arguments.Add(header.Key + ":" + value);
                    }
                }
            }
            if (data.ResponseHeaders != null)
            {
                var fileSize = GetFileSize(data.ResponseHeaders);
                var mimeType = GetMediaType(data.ResponseHeaders);
                Debug("Mime: " + mimeType);
                if (fileSize > 0)
                {
                    arguments.Add("--known-file-size");
                    arguments.Add(fileSize + "");
                }
                if (!string.IsNullOrEmpty(mimeType))
                {
                    arguments.Add("--known-mime-type");
                    arguments.Add(mimeType!);
                }
            }
            if (!string.IsNullOrEmpty(data.File))
            {
                arguments.Add("--output");
                arguments.Add(data.File);
            }
            if (!string.IsNullOrEmpty(data.TabUrl))
            {
                arguments.Add("--tab-url");
                arguments.Add(data.TabUrl);
            }
            if (!string.IsNullOrEmpty(data.TabId))
            {
                arguments.Add("--tab-id");
                arguments.Add(data.TabId);
            }
            arguments.Add(data.Url);
            Debug(string.Join(",", arguments));
            _ipcClient!.Send(arguments);
        }

        private static void SendVideoId(string vid)
        {
            Debug("########Going to send vid id...");
            if (string.IsNullOrEmpty(vid))
            {
                return;
            }
            var arguments = new List<string>();
            arguments.Add("--media-vid");
            arguments.Add(vid);
            Debug(string.Join(",", arguments));
            _ipcClient!.Send(arguments);
        }

        private static void SendClearCmd()
        {
            Debug("########Going to send clear command...");
            var arguments = new List<string>();
            arguments.Add("--media-clear");
            Debug(string.Join(",", arguments));
            _ipcClient!.Send(arguments);
        }

        private static void SendTabUpdate(TabInfo tab)
        {
            Debug("########Going to send tab update...");
            if (tab.Url == null || tab.Title == null)
            {
                return;
            }
            var arguments = new List<string>();
            arguments.Add("--media-tab-url");
            arguments.Add(tab.Url);
            arguments.Add("--media-tab-title");
            arguments.Add(tab.Title);
            Debug(string.Join(",", arguments));
            _ipcClient!.Send(arguments);
        }

        public static void ReadConfigUpdateFromADM()
        {
            new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        Debug("Receive config from _ADM...");
                        var lines = _ipcClient!.Receive();
                        Debug(string.Join("\n", lines));
                        Debug("Received config from _ADM...");
                        SendToBrowser(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
                    }
                }
                catch (Exception ex)
                {
                    Debug("Error receving message from ADM", ex);
                    Environment.Exit(1);
                }
            }).Start();
        }

        public static void SendToBrowser(byte[] msgBytes)
        {
            if (msgBytes == null) throw new ArgumentNullException(nameof(msgBytes));
            if (msgBytes.Length > BrowserProtocolV1.MaxApplicationMessageBytes)
            {
                throw new ArgumentException($"Message length too long: {msgBytes.Length}", nameof(msgBytes));
            }
            stdout!.Write(BitConverter.GetBytes(msgBytes.Length), 0, 4);
            stdout!.Write(msgBytes, 0, msgBytes.Length);
            stdout!.Flush();
        }

        public static byte[] ReadMessageBytes(Stream stdin)
        {
            var b4 = new byte[4];
            ReadFully(stdin, b4, 4);
            var syncLength = BitConverter.ToInt32(b4, 0);
            if (syncLength < 0 || syncLength > BrowserProtocolV1.MaxApplicationMessageBytes)
            {
                throw new ArgumentException($"Message length invalid: {syncLength}");
            }
            var bytes = new byte[syncLength];
            ReadFully(stdin, bytes, syncLength);
            return bytes;
        }

        private static void ReadFully(Stream stream, byte[] buf, int bytesToRead)
        {
            var rem = bytesToRead;
            var index = 0;
            while (rem > 0)
            {
                var c = stream.Read(buf, index, rem);
                if (c == 0) throw new EndOfStreamException("Unexpected EOF");
                index += c;
                rem -= c;
            }
        }

        private static byte[] ForwardProtocolRequest(string browserOrigin, byte[] request)
        {
            if (_protocolPipeClient == null && !TryConnectProtocolPipe(browserOrigin, BrowserProtocolV1.PassiveDesktopProbeTimeout))
            {
                throw new IOException("Desktop browser-protocol endpoint is unavailable.");
            }
            try
            {
                return _protocolPipeClient!.SendCommand(request);
            }
            catch
            {
                _protocolPipeClient?.Dispose();
                _protocolPipeClient = null;
                if (!TryConnectProtocolPipe(browserOrigin, BrowserProtocolV1.PassiveDesktopProbeTimeout)) throw;
                return _protocolPipeClient!.SendCommand(request);
            }
        }

        private static bool EnsureDesktopProtocolConnection(string browserOrigin, bool isFirefox)
        {
            if (_protocolPipeClient != null || TryConnectProtocolPipe(browserOrigin, BrowserProtocolV1.PassiveDesktopProbeTimeout))
            {
                return true;
            }

            StartDesktopIfAbsent(isFirefox);
            var deadline = DateTime.UtcNow + BrowserProtocolV1.NativeHostConnectionTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var remaining = deadline - DateTime.UtcNow;
                var attempt = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);
                if (attempt > TimeSpan.Zero && TryConnectProtocolPipe(browserOrigin, attempt)) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        private static bool TryConnectProtocolPipe(string browserOrigin, TimeSpan timeout)
        {
            if (_protocolPipeClient != null) return true;
            try
            {
                var client = new BrowserProtocolPipeClient();
                client.Connect(browserOrigin, timeout);
                _protocolPipeClient = client;
                Debug("Connected to ADM browser protocol pipe.");
                return true;
            }
            catch (Exception ex)
            {
                _protocolPipeClient?.Dispose();
                _protocolPipeClient = null;
                Debug("Browser protocol pipe unavailable.", ex);
                return false;
            }
        }

        private static void StartDesktopIfAbsent(bool isFirefox)
        {
            using var launchMutex = new Mutex(false, ProductIdentity.BrowserHostLaunchMutexName);
            var ownsLaunchMutex = false;
            try
            {
                try
                {
                    ownsLaunchMutex = launchMutex.WaitOne(BrowserProtocolV1.NativeHostConnectionTimeout);
                }
                catch (AbandonedMutexException)
                {
                    ownsLaunchMutex = true;
                }
                if (!ownsLaunchMutex || IsDesktopInstanceRunning()) return;
                Debug("Desktop is absent; explicit native request is launching ADM.");
                CreateADMInstance(isFirefox);
            }
            finally
            {
                if (ownsLaunchMutex)
                {
                    try { launchMutex.ReleaseMutex(); } catch { }
                }
            }
        }

        private static bool IsDesktopInstanceRunning()
        {
            try
            {
                using var mutex = Mutex.OpenExisting(ProductIdentity.GlobalMutexName);
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static void EnsureLegacyDesktopConnection(bool isFirefox)
        {
            if (_ipcClient != null) return;
            StartDesktopIfAbsent(isFirefox);
            EnsureLegacyConnection();
        }

        private static void EnsureLegacyConnection()
        {
            if (_ipcClient != null) return;
            Exception? lastError = null;
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var client = new IpcClient();
                    client.Connect(ProductIdentity.LegacyControlPort, ProductIdentity.LegacyIpcConnectTimeoutMilliseconds, ProductIdentity.LegacyIpcIoTimeoutMilliseconds);
                    _ipcClient = client;
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Thread.Sleep(1000);
                }
            }
            throw new IOException("Unable to connect to the legacy ADM localhost transport.", lastError);
        }

        private static void CreateADMInstance(bool isFirefox, bool minimized = true)
        {
            try
            {

#if NET6_0
                var exe = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."), "adm-app");
                ProcessStartInfo psi = new()
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Arguments = "--background"
                };
                psi.EnvironmentVariables.Add("GTK_USE_PORTAL", "1");

                Debug("ADM instance creating...");
                Process.Start(psi);
#else
                var exe = ResolveDesktopExecutable();
                if (exe == null)
                {
                    Debug("Desktop executable could not be found next to the native host or in the recorded desktop path.");
                    return;
                }
                Debug(exe);
                if (isFirefox)
                {
                    if (!Win32NativeProcess.Win32CreateProcess(exe, $"\"{exe}\" --background"))
                    {
                        Debug("Win32 create process failed!");
                    }
                }
                else
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = exe,
                        UseShellExecute = true,
                        Arguments = "--background"
                    };

                    Debug("ADM instance creating...");
                    Process.Start(psi);
                }
#endif
            }
            catch (Exception ex)
            {
                Debug(ex.ToString());
            }
        }

        private static string? ResolveDesktopExecutable()
        {
            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ProductIdentity.DesktopExecutableName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ProductIdentity.DesktopExecutableName)
            };
#if !NET6_0
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ProductIdentity.DesktopRegistryKey);
                if (key?.GetValue(ProductIdentity.DesktopPathValue) is string recorded && recorded.Length > 0) candidates.Add(recorded);
            }
            catch (Exception ex)
            {
                Debug("Recorded desktop path is unavailable.", ex);
            }
#endif
            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) return full;
                }
                catch (Exception ex)
                {
                    Debug("Desktop candidate path is invalid.", ex);
                }
            }
            return null;
        }

        private static void Debug(string msg, Exception? ex2 = null)
        {
            Trace.WriteLine($"[adm-native-messaging-host {DateTime.Now}] {msg}");
            if (ex2 != null)
            {
                Trace.WriteLine($"[adm-native-messaging-host {DateTime.Now}] {ex2}");
            }
            Console.Error.WriteLine(msg);
            Console.Error.Flush();
        }

        private static long GetFileSize(Dictionary<string, List<string>> headers)
        {
            if (headers == null) return -1;
            try
            {
                foreach (var key in headers.Keys)
                {
                    if (key.ToUpperInvariant() == "CONTENT-LENGTH")
                    {
                        return headers[key].Count > 0 ? Int64.Parse(headers[key][0].Trim()) : -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug(ex.Message, ex);
            }
            return -1;
        }

        private static string? GetMediaType(Dictionary<string, List<string>> headers)
        {
            if (headers == null) return null;
            try
            {
                foreach (var key in headers.Keys)
                {
                    if (key.ToUpperInvariant() == "CONTENT-TYPE")
                    {
                        return headers[key].Count > 0 ? headers[key][0].Trim() : null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug(ex.Message, ex);
            }
            return null;
        }

        private static string? GetReferer(Dictionary<string, string>? headers)
        {
            if (headers == null) return null;
            try
            {
                foreach (var key in headers.Keys)
                {
                    if (key.ToUpperInvariant() == "REFERER")
                    {
                        return headers[key].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug(ex.Message, ex);
            }
            return null;
        }
    }
}
