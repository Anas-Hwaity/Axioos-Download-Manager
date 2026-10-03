using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;
using ADM.Wpf.UI.Diagnostics;
using ADM.Wpf.UI.Dialogs.Settings.Services;

namespace ADM.Wpf.UI
{
    internal sealed class WebShellHost
    {
        internal const string HostName = "app.axioos.local";
        internal const string ClassicVariable = "AXIOOS_CLASSIC_UI";
        internal const string AcceptanceVariable = "AXIOOS_WEBSHELL_ACCEPTANCE";

        private readonly MainWindow window;
        private readonly WebShellSettingsService settings = new WebShellSettingsService();
        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer watchdog;
        private WebView2? webView;
        private UIElement? classicContent;
        private string lastState = string.Empty;
        private string lastDesktop = string.Empty;
        private bool ready;

        public WebShellHost(MainWindow window)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    PushState(false);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Axioos shell state refresh failed");
                }
            };
            watchdog = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(12) };
            watchdog.Tick += (_, _) =>
            {
                watchdog.Stop();
                if (!ready)
                {
                    Log.Debug("Axioos shell did not report ready in time; the classic window stays in use");
                    RestoreClassic();
                }
            };
        }

        internal static string ShellFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebShell");

        internal static bool IsEnabled
        {
            get
            {
                if (IsOne(Environment.GetEnvironmentVariable(ClassicVariable))) return false;
                if (AcceptanceTestEnvironment.IsEnabled && !IsOne(Environment.GetEnvironmentVariable(AcceptanceVariable))) return false;
                return File.Exists(Path.Combine(ShellFolder, "index.html"));
            }
        }

        private static bool IsOne(string? value) => string.Equals(value, "1", StringComparison.Ordinal);

        public void Attach()
        {
            if (!IsEnabled) return;
            try
            {
                classicContent = window.Content as UIElement;
                if (classicContent == null) return;
                var view = new WebView2
                {
                    Visibility = Visibility.Visible,
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 10, 17, 32)
                };
                view.Loaded += (_, _) =>
                {
                    if (!ready && view.Visibility == Visibility.Visible) watchdog.Start();
                };
                window.Content = null;
                var root = new Grid();
                root.Children.Add(classicContent);
                webView = view;
                root.Children.Add(view);
                classicContent.Visibility = Visibility.Hidden;
                window.Content = root;
                AcceptanceDiagnostics.RecordStage("webshell.attach");
                _ = InitializeAsync();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Axioos shell could not attach; the classic window stays in use");
                RestoreClassic();
            }
        }

        private void ReloadShell()
        {
            try
            {
                if (webView == null || webView.Visibility != Visibility.Visible) return;
                ready = false;
                webView.CoreWebView2?.Reload();
                watchdog.Stop();
                watchdog.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Axioos shell could not reload; the classic window is used");
                RestoreClassic();
            }
        }

        private string ShellCacheFolder()
        {
            string name;
            try
            {
                name = "webview2-" + ShellFingerprint();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Shell files could not be fingerprinted");
                name = "webview2";
            }
            try
            {
                var stale = Directory.GetDirectories(settings.AppDirectory, "webview2*")
                    .Where(old => !string.Equals(Path.GetFileName(old), name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (var old in stale)
                    {
                        try { Directory.Delete(old, true); } catch (Exception ex) { Log.Debug(ex, "Old shell cache is still in use"); }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Old shell caches could not be listed");
            }
            return Path.Combine(settings.AppDirectory, name);
        }

        private static string ShellFingerprint()
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var buffer = new MemoryStream();
            foreach (var file in Directory.GetFiles(ShellFolder, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(file.Substring(ShellFolder.Length));
                buffer.Write(nameBytes, 0, nameBytes.Length);
                var content = File.ReadAllBytes(file);
                buffer.Write(content, 0, content.Length);
            }
            var hash = sha.ComputeHash(buffer.ToArray());
            return string.Concat(hash.Take(8).Select(part => part.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                var dataFolder = ShellCacheFolder();
                Directory.CreateDirectory(dataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                if (webView == null) return;
                await webView.EnsureCoreWebView2Async(environment);
                var core = webView.CoreWebView2;
                core.Settings.AreDevToolsEnabled = IsOne(Environment.GetEnvironmentVariable("AXIOOS_WEBSHELL_DEVTOOLS"));
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.SetVirtualHostNameToFolderMapping(HostName, ShellFolder, CoreWebView2HostResourceAccessKind.Deny);
                core.NavigationStarting += (_, e) =>
                {
                    if (!IsShellUri(e.Uri)) e.Cancel = true;
                };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.ProcessFailed += (_, e) =>
                {
                    Log.Debug("Axioos shell process failed: " + e.ProcessFailedKind);
                    if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                    {
                        window.Dispatcher.BeginInvoke(new Action(RestoreClassic));
                    }
                    else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    {
                        window.Dispatcher.BeginInvoke(new Action(ReloadShell));
                    }
                };
                core.WebMessageReceived += OnWebMessage;
                core.Navigate("https://" + HostName + "/index.html");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Axioos shell could not start WebView2; the classic window stays in use");
                RestoreClassic();
            }
        }

        internal static bool IsShellUri(string? uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
            return parsed.Scheme == Uri.UriSchemeHttps && string.Equals(parsed.Host, HostName, StringComparison.OrdinalIgnoreCase);
        }

        private void RestoreClassic()
        {
            timer.Stop();
            watchdog.Stop();
            ready = false;
            if (classicContent != null) classicContent.Visibility = Visibility.Visible;
            if (webView != null) webView.Visibility = Visibility.Collapsed;
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            JObject? message;
            try
            {
                message = JObject.Parse(e.WebMessageAsJson);
            }
            catch (JsonException ex)
            {
                Log.Debug(ex, "Axioos shell sent a message that is not JSON");
                return;
            }
            window.Dispatcher.BeginInvoke(new Action(() => HandleQueued(message)));
        }

        private void HandleQueued(JObject message)
        {
            try
            {
                Handle(message);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Axioos shell command failed");
                Post(new JObject { ["type"] = "toast", ["title"] = "That did not work", ["body"] = "Axioos could not finish this action. Details are in the log." });
            }
        }

        private void Handle(JObject message)
        {
            var cmd = message.Value<string>("cmd") ?? string.Empty;
            switch (cmd)
            {
                case "ready":
                    if (webView == null || webView.Visibility != Visibility.Visible) return;
                    ready = true;
                    watchdog.Stop();
                    lastDesktop = DesktopSignature();
                    Post(new JObject { ["type"] = "appearance", ["state"] = LoadAppearance(), ["desktop"] = DesktopState() });
                    PushState(true);
                    timer.Start();
                    AcceptanceDiagnostics.RecordStage("webshell.ready");
                    break;
                case "select":
                    var ids = (message["ids"] as JArray)?.Select(token => token.ToString()).Where(id => id.Length > 0).ToList() ?? new List<string>();
                    window.ShellSelect(ids, message.Value<string>("kind") != "finished");
                    PushState(true);
                    break;
                case "button":
                    window.ShellClickButton(message.Value<string>("name") ?? string.Empty);
                    PushState(true);
                    break;
                case "menu":
                    var items = window.ShellPrepareMenu(message.Value<string>("kind") != "finished");
                    var list = new JArray();
                    foreach (var item in items.OfType<MenuItemWrapper>())
                    {
                        list.Add(new JObject
                        {
                            ["name"] = item.Name,
                            ["text"] = Convert.ToString(item.Menu.Header, CultureInfo.CurrentCulture) ?? item.Name,
                            ["enabled"] = item.Enabled,
                            ["visible"] = item.Menu.Visibility == Visibility.Visible
                        });
                    }
                    Post(new JObject { ["type"] = "menu", ["items"] = list });
                    break;
                case "menuInvoke":
                    window.ShellInvokeMenu(message.Value<string>("name") ?? string.Empty);
                    PushState(true);
                    break;
                case "contextAction":
                    window.ShellPrepareMenu(true);
                    window.ShellInvokeMenu(message.Value<string>("name") ?? string.Empty);
                    PushState(true);
                    break;
                case "open":
                    window.ShellOpenSelected();
                    break;
                case "action":
                    window.ShellCommand(message.Value<string>("name") ?? string.Empty);
                    PushState(true);
                    break;
                case "addUrl":
                    AddUrl(message.Value<string>("url"));
                    break;
                case "openUrl":
                    var which = message.Value<string>("which");
                    if (which == "telegram") window.ShellNavigation.OpenUrl(ProductIdentity.DeveloperTelegramUrl);
                    else if (which == "github") window.ShellNavigation.OpenUrl(ProductIdentity.DeveloperGitHubUrl);
                    break;
                case "appearance":
                    SaveAppearance(message["state"] as JObject, message["desktop"] as JObject);
                    break;
            }
        }

        private void AddUrl(string? url)
        {
            if (url == null || string.IsNullOrWhiteSpace(url) || url.Length > 8192) return;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeFtp) return;
            window.ShellRuntimeContext.Application.ShowNewDownloadDialog(new Message { Url = parsed.AbsoluteUri });
        }

        internal JObject LoadAppearance()
        {
            var stored = settings.ShellAppearance;
            if (!string.IsNullOrWhiteSpace(stored))
            {
                try
                {
                    return JObject.Parse(stored);
                }
                catch (JsonException ex)
                {
                    Log.Debug(ex, "Stored Axioos shell appearance is not valid JSON; defaults are used");
                }
            }
            return new JObject { ["theme"] = settings.Theme, ["glass"] = settings.GlassLevel };
        }

        internal JObject DesktopState()
        {
            return new JObject
            {
                ["theme"] = settings.Theme,
                ["backdrop"] = settings.Backdrop,
                ["accent"] = settings.Accent,
                ["glass"] = settings.GlassLevel
            };
        }

        private string DesktopSignature() => DesktopState().ToString(Formatting.None);

        private void SaveAppearance(JObject? state, JObject? desktop)
        {
            if (state == null) return;
            var text = state.ToString(Formatting.None);
            if (text.Length > 8192) return;
            int? glassLevel = null;
            var glass = desktop?["glass"];
            if (glass != null && (glass.Type == JTokenType.Integer || glass.Type == JTokenType.Float))
            {
                glassLevel = (int)Math.Round((double)glass);
            }
            settings.Save(text, desktop?.Value<string>("theme"), desktop?.Value<string>("backdrop"), desktop?.Value<string>("accent"), glassLevel);
            AxioosThemeService.Apply(settings.Theme, settings.Backdrop, settings.Accent);
            GlassThemeManager.Apply(settings.GlassLevel);
            lastDesktop = DesktopSignature();
        }

        private void PushState(bool force)
        {
            if (!ready || webView?.CoreWebView2 == null) return;
            var desktop = DesktopSignature();
            if (desktop != lastDesktop)
            {
                lastDesktop = desktop;
                Post(new JObject { ["type"] = "appearance", ["state"] = LoadAppearance(), ["desktop"] = DesktopState() });
            }
            var state = BuildState();
            var text = state.ToString(Formatting.None);
            if (!force && text == lastState) return;
            lastState = text;
            Post(new JObject { ["type"] = "state", ["state"] = state });
        }

        private void Post(JObject message)
        {
            try
            {
                webView?.CoreWebView2?.PostWebMessageAsJson(message.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Axioos shell message could not be delivered");
            }
        }

        internal JObject BuildState()
        {
            var items = new JArray();
            foreach (var row in window.ShellInProgressRows)
            {
                var entry = row.DownloadEntry;
                var progressItem = Item(entry, "progress", entry.Progress, MapStatus(entry.Status), row.StatusText, entry.DownloadSpeed, entry.ETA);
                AddLiveDetails(progressItem, entry, window.ShellTelemetry);
                items.Add(progressItem);
            }
            foreach (var row in window.ShellFinishedRows)
            {
                var entry = row.DownloadEntry;
                var finishedItem = Item(entry, "finished", 100, "done", "Finished", null, null);
                AddLiveDetails(finishedItem, entry, null);
                items.Add(finishedItem);
            }
            var buttons = new JObject();
            foreach (var pair in window.ShellButtons)
            {
                buttons[pair.Key] = new JObject { ["enabled"] = pair.Value.Enable, ["visible"] = pair.Value.Visible };
            }
            var categories = new JArray();
            foreach (var category in settings.Categories)
            {
                categories.Add(new JObject
                {
                    ["id"] = category.Name ?? category.DisplayName ?? string.Empty,
                    ["name"] = category.DisplayName ?? category.Name ?? string.Empty,
                    ["icon"] = CategoryIcon(category.Name),
                    ["exts"] = new JArray((category.FileExtensions ?? new HashSet<string>()).Select(ext => ext.Trim().TrimStart('.').ToLowerInvariant()).Where(ext => ext.Length > 0).Distinct())
                });
            }
            return new JObject
            {
                ["items"] = items,
                ["buttons"] = buttons,
                ["categories"] = categories,
                ["monitoring"] = settings.BrowserMonitoringEnabled,
                ["speedLimitEnabled"] = settings.SpeedLimitEnabled,
                ["speedLimitKiB"] = settings.SpeedLimitKiB,
                ["update"] = window.ShellUpdateAvailable,
                ["version"] = AppInfo.APP_VERSION_TEXT,
                ["baseVersion"] = AppInfo.BASE_TEXT,
                ["releaseSummary"] = ReleaseNotes.Summary,
                ["releaseNotes"] = new JArray(ReleaseNotes.Sections.Select(section => new JObject { ["title"] = section.Title, ["items"] = new JArray(section.Items) })),
                ["developer"] = new JObject { ["name"] = ProductIdentity.DeveloperName, ["telegram"] = ProductIdentity.DeveloperTelegram }
            };
        }

        internal static string MapStatus(DownloadStatus status)
        {
            switch (status)
            {
                case DownloadStatus.Downloading: return "active";
                case DownloadStatus.Waiting: return "queued";
                case DownloadStatus.Finished: return "done";
                default: return "paused";
            }
        }

        internal static string CategoryIcon(string? name)
        {
            var value = (name ?? string.Empty).ToUpperInvariant();
            if (value.Contains("VIDEO")) return "video";
            if (value.Contains("MUSIC") || value.Contains("AUDIO")) return "music";
            if (value.Contains("DOC")) return "doc";
            if (value.Contains("COMPRESS") || value.Contains("ARCHIVE")) return "zip";
            if (value.Contains("PROG") || value.Contains("APP")) return "app";
            if (value.Contains("IMAGE") || value.Contains("PICTURE")) return "image";
            return "other";
        }

        private static JObject Item(DownloadItemBase entry, string kind, int progress, string status, string? statusText, string? speed, string? eta)
        {
            var host = string.Empty;
            if (Uri.TryCreate(entry.PrimaryUrl, UriKind.Absolute, out var uri)) host = uri.Host;
            return new JObject
            {
                ["id"] = entry.Id,
                ["kind"] = kind,
                ["downloadType"] = entry.DownloadType ?? string.Empty,
                ["name"] = entry.Name ?? string.Empty,
                ["size"] = entry.Size,
                ["progress"] = Math.Max(0, Math.Min(100, progress)),
                ["status"] = status,
                ["statusText"] = statusText ?? string.Empty,
                ["speedText"] = speed ?? string.Empty,
                ["etaText"] = eta ?? string.Empty,
                ["host"] = host,
                ["dir"] = entry.TargetDir ?? string.Empty,
                ["added"] = entry.DateAdded.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["addedText"] = FriendlyDate(entry.DateAdded)
            };
        }

        internal static string SourceLabel(string? downloadType)
        {
            switch ((downloadType ?? string.Empty).ToLowerInvariant())
            {
                case "dual":
                case "dash":
                case "mpegdash":
                    return "Video with separate audio";
                case "hls":
                    return "Video stream";
                default:
                    return "Direct link";
            }
        }

        internal static void AddLiveDetails(JObject item, DownloadItemBase entry, ADM.Core.Telemetry.IDownloadTelemetryService? telemetry)
        {
            item["via"] = SourceLabel(entry.DownloadType);
            item["resume"] = "Unknown";
            if (telemetry == null || entry.Id == null || !telemetry.TryGet(entry.Id, out var snapshot) || snapshot == null) return;
            item["connections"] = Math.Max(0, snapshot.ActiveConnections);
            item["resume"] = snapshot.ResumeCapability == ADM.Core.Telemetry.DownloadResumeCapability.Yes ? "Supported"
                : snapshot.ResumeCapability == ADM.Core.Telemetry.DownloadResumeCapability.No ? "Not supported" : "Unknown";
            if (snapshot.TotalBytes.HasValue && snapshot.TotalBytes.Value > 0) item["size"] = snapshot.TotalBytes.Value;
            item["downloaded"] = Math.Max(0, snapshot.DownloadedBytes);
            if (item.Value<string>("status") == "paused" && snapshot.LifecycleState == "Failed")
            {
                item["status"] = "failed";
                if (snapshot.LastError.IndexOf(' ') > 0) item["statusText"] = snapshot.LastError;
            }
        }

        internal static string FriendlyDate(DateTime value)
        {
            var today = DateTime.Now.Date;
            if (value.Date == today) return "Today, " + value.ToString("t", CultureInfo.CurrentCulture);
            if (value.Date == today.AddDays(-1)) return "Yesterday";
            return value.ToString("d", CultureInfo.CurrentCulture);
        }
    }
}
