using ADM.Core.BrowserMonitoring;

namespace ADM.Core.Settings
{
    public sealed class BrowserMonitoringSettingsState
    {
        public string ChromeWebStoreUrl { get; set; } = string.Empty;
        public string FirefoxExtensionUrl { get; set; } = string.Empty;
        public string FileExtensions { get; set; } = string.Empty;
        public string VideoExtensions { get; set; } = string.Empty;
        public string BlockedHosts { get; set; } = string.Empty;
        public int MinVideoSize { get; set; }
        public bool MonitorClipboard { get; set; }
        public bool FetchServerTimeStamp { get; set; }
        public bool ShowNotification { get; set; }
    }

    public interface IBrowserMonitoringSettingsService
    {
        BrowserMonitoringSettingsState Load();
        void Save(BrowserMonitoringSettingsState state);
        string DefaultFileExtensions { get; }
        string DefaultVideoExtensions { get; }
        string DefaultBlockedHosts { get; }
        void LaunchBrowser(Browser browser);
        void PrepareFirefoxExtension();
        void LaunchFirefox();
        void OpenVideoTutorial();
    }
}
