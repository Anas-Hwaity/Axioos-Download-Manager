using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Settings;

namespace ADM.Wpf.UI.Dialogs.Settings.ViewModels
{
    public sealed class BrowserMonitoringSettingsViewModel : INotifyPropertyChanged
    {
        private readonly IBrowserMonitoringSettingsService settingsService;
        private string chromeWebStoreUrl = string.Empty;
        private string firefoxExtensionUrl = string.Empty;
        private string fileExtensions = string.Empty;
        private string videoExtensions = string.Empty;
        private string blockedHosts = string.Empty;
        private int minVideoSize;
        private bool monitorClipboard;
        private bool fetchServerTimeStamp;
        private bool showNotification;

        public BrowserMonitoringSettingsViewModel(IBrowserMonitoringSettingsService settingsService)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public int[] MinimumVideoSizes { get; } = new[] { 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768 };
        public string ChromeWebStoreUrl { get => chromeWebStoreUrl; private set => Set(ref chromeWebStoreUrl, value ?? string.Empty); }
        public string FirefoxExtensionUrl { get => firefoxExtensionUrl; private set => Set(ref firefoxExtensionUrl, value ?? string.Empty); }
        public string FileExtensions { get => fileExtensions; set => Set(ref fileExtensions, value ?? string.Empty); }
        public string VideoExtensions { get => videoExtensions; set => Set(ref videoExtensions, value ?? string.Empty); }
        public string BlockedHosts { get => blockedHosts; set => Set(ref blockedHosts, value ?? string.Empty); }
        public int MinVideoSize { get => minVideoSize; set => Set(ref minVideoSize, value); }
        public bool MonitorClipboard { get => monitorClipboard; set => Set(ref monitorClipboard, value); }
        public bool FetchServerTimeStamp { get => fetchServerTimeStamp; set => Set(ref fetchServerTimeStamp, value); }
        public bool ShowNotification { get => showNotification; set => Set(ref showNotification, value); }

        public void Reload()
        {
            var state = settingsService.Load();
            ChromeWebStoreUrl = state.ChromeWebStoreUrl;
            FirefoxExtensionUrl = state.FirefoxExtensionUrl;
            FileExtensions = state.FileExtensions;
            VideoExtensions = state.VideoExtensions;
            BlockedHosts = state.BlockedHosts;
            MinVideoSize = state.MinVideoSize;
            MonitorClipboard = state.MonitorClipboard;
            FetchServerTimeStamp = state.FetchServerTimeStamp;
            ShowNotification = state.ShowNotification;
        }

        public void Save()
        {
            settingsService.Save(new BrowserMonitoringSettingsState
            {
                ChromeWebStoreUrl = ChromeWebStoreUrl,
                FirefoxExtensionUrl = FirefoxExtensionUrl,
                FileExtensions = FileExtensions,
                VideoExtensions = VideoExtensions,
                BlockedHosts = BlockedHosts,
                MinVideoSize = MinVideoSize,
                MonitorClipboard = MonitorClipboard,
                FetchServerTimeStamp = FetchServerTimeStamp,
                ShowNotification = ShowNotification
            });
        }

        public void ResetFileExtensions() => FileExtensions = settingsService.DefaultFileExtensions;
        public void ResetVideoExtensions() => VideoExtensions = settingsService.DefaultVideoExtensions;
        public void ResetBlockedHosts() => BlockedHosts = settingsService.DefaultBlockedHosts;
        public void LaunchBrowser(Browser browser) => settingsService.LaunchBrowser(browser);
        public void PrepareFirefoxExtension() => settingsService.PrepareFirefoxExtension();
        public void LaunchFirefox() => settingsService.LaunchFirefox();
        public void OpenVideoTutorial() => settingsService.OpenVideoTutorial();

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
