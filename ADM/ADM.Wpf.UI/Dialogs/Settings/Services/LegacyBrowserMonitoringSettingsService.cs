using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TraceLog;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Settings;
using ADM.Core.Util;

namespace ADM.Wpf.UI.Dialogs.Settings.Services
{
    internal sealed class LegacyBrowserMonitoringSettingsService : IBrowserMonitoringSettingsService
    {
        public string DefaultFileExtensions => string.Join(",", Config.DefaultFileExtensions);
        public string DefaultVideoExtensions => string.Join(",", Config.DefaultVideoExtensions);
        public string DefaultBlockedHosts => string.Join(",", Config.DefaultBlockedHosts);

        public BrowserMonitoringSettingsState Load()
        {
            var config = Config.Instance;
            return new BrowserMonitoringSettingsState
            {
                ChromeWebStoreUrl = Links.ManualExtensionInstallGuideUrl,
                FirefoxExtensionUrl = Links.FirefoxExtensionUrl,
                FileExtensions = string.Join(",", config.FileExtensions),
                VideoExtensions = string.Join(",", config.VideoExtensions),
                BlockedHosts = string.Join(",", config.BlockedHosts),
                MinVideoSize = config.MinVideoSize,
                MonitorClipboard = config.MonitorClipboard,
                FetchServerTimeStamp = config.FetchServerTimeStamp,
                ShowNotification = config.ShowNotification
            };
        }

        public void Save(BrowserMonitoringSettingsState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var config = Config.Instance;
            config.FileExtensions = ParseList(state.FileExtensions);
            config.VideoExtensions = ParseList(state.VideoExtensions);
            config.BlockedHosts = ParseList(state.BlockedHosts);
            config.FetchServerTimeStamp = state.FetchServerTimeStamp;
            config.MonitorClipboard = state.MonitorClipboard;
            config.MinVideoSize = state.MinVideoSize;
            config.ShowNotification = state.ShowNotification;
        }

        public void LaunchBrowser(Browser browser)
        {
            if (MsixHelper.IsAppContainer)
            {
                MsixHelper.CopyExtension();
            }
            var folder = ExtensionFolder();
            if (folder != null)
            {
                try
                {
                    System.Windows.Clipboard.SetText(folder);
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Extension folder could not be shown");
                }
            }
            var page = browser switch
            {
                Browser.MSEdge => "edge://extensions",
                Browser.Opera => "opera://extensions",
                Browser.Brave => "brave://extensions",
                Browser.Vivaldi => "vivaldi://extensions",
                _ => "chrome://extensions"
            };
            if (!BrowserLauncher.LaunchBrowser(browser, page, null))
            {
                throw new FileNotFoundException(browser + " is not installed in a standard location.");
            }
        }

        public string? ExtensionFolder()
        {
            var candidates = new System.Collections.Generic.List<string>();
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (var depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "chrome-extension"));
            }
            candidates.Add(Path.Combine(Config.AppDir, "chrome-extension"));
            return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "manifest.json")));
        }

        public void PrepareFirefoxExtension()
        {
            if (MsixHelper.IsAppContainer)
            {
                MsixHelper.CopyExtension();
            }
        }

        public void LaunchFirefox()
        {
            BrowserLauncher.LaunchFirefox(Links.FirefoxExtensionUrl, null);
        }

        public void OpenVideoTutorial()
        {
            PlatformHelper.OpenBrowser(Links.VideoDownloadTutorialUrl);
        }

        private static string[] ParseList(string value)
        {
            return (value ?? string.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        }
    }
}
