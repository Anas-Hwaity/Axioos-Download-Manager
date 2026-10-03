using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TraceLog;
using Translations;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Downloader;
using ADM.Core.UI;
using ADM.Core.Util;

namespace ADM.Core.Legacy
{
    public interface ILegacyApplicationContextAdapter
    {
        void Configure(
            EventHandler firstRunCallback,
            IApplicationWindow applicationWindow,
            IApplication application,
            IApplicationCore applicationCore,
            IVideoTracker videoTracker,
            IClipboardMonitor clipboardMonitor,
            ILinkRefresher linkRefresher,
            IPlatformUIService platformUIService);
    }

    public interface IDownloadCreationPreferences
    {
        string[] GetFolderValues();
        int GetInitialFolderIndex();
        void RecordFileBrowsed(string folder);
        void UpdateFolderSelection(int index, string? selectedFile);
        string? ResolveSelectedFolder(int index);
        string GetInitialDownloadLocation();
        void RememberSelectedFolder(string folder);
        ProxyInfo? DefaultProxy { get; }
        string FallbackUserAgent { get; }
        int DefaultDownloadSpeed { get; }
        bool EnableSpeedLimit { get; }
        string GetCategoryDisplayName(string fileName);
        void OpenWindowsProxySettings();
        void BlockHost(string host);
    }

    public sealed class LegacyApplicationContextAdapter : ILegacyApplicationContextAdapter
    {
        public void Configure(
            EventHandler firstRunCallback,
            IApplicationWindow applicationWindow,
            IApplication application,
            IApplicationCore applicationCore,
            IVideoTracker videoTracker,
            IClipboardMonitor clipboardMonitor,
            ILinkRefresher linkRefresher,
            IPlatformUIService platformUIService)
        {
            if (firstRunCallback == null) throw new ArgumentNullException(nameof(firstRunCallback));
            if (applicationWindow == null) throw new ArgumentNullException(nameof(applicationWindow));
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (applicationCore == null) throw new ArgumentNullException(nameof(applicationCore));
            if (videoTracker == null) throw new ArgumentNullException(nameof(videoTracker));
            if (clipboardMonitor == null) throw new ArgumentNullException(nameof(clipboardMonitor));
            if (linkRefresher == null) throw new ArgumentNullException(nameof(linkRefresher));
            if (platformUIService == null) throw new ArgumentNullException(nameof(platformUIService));

            ApplicationContext.FirstRunCallback += firstRunCallback;
            ApplicationContext.Configurer()
                .RegisterApplicationWindow(applicationWindow)
                .RegisterApplication(application)
                .RegisterApplicationCore(applicationCore)
                .RegisterCapturedVideoTracker(videoTracker)
                .RegisterClipboardMonitor(clipboardMonitor)
                .RegisterLinkRefresher(linkRefresher)
                .RegisterPlatformUIService(platformUIService)
                .Configure();
        }
    }

    public sealed class LegacyDownloadCreationPreferences : IDownloadCreationPreferences
    {
        public ProxyInfo? DefaultProxy => Config.Instance.Proxy;
        public string FallbackUserAgent => Config.Instance.FallbackUserAgent;
        public int DefaultDownloadSpeed => Config.Instance.DefaltDownloadSpeed;
        public bool EnableSpeedLimit => Config.Instance.EnableSpeedLimit;

        public string GetCategoryDisplayName(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName).ToUpperInvariant();
            var category = Config.Instance.Categories.FirstOrDefault(item =>
                item.FileExtensions != null && item.FileExtensions.Contains(extension));
            return string.IsNullOrEmpty(category.Name)
                ? "---"
                : category.IsPredefined ? TextResource.GetText(category.Name) : category.DisplayName;
        }

        public string[] GetFolderValues()
        {
            if (!Config.Instance.RecentFolders.Contains(Config.Instance.DefaultDownloadFolder))
            {
                Config.Instance.RecentFolders.Insert(0, Config.Instance.DefaultDownloadFolder);
            }
            var values = new string[Config.Instance.RecentFolders.Count + 2];
            values[0] = TextResource.GetText("ND_AUTO_CAT");
            values[1] = TextResource.GetText("BTN_BROWSE");
            for (var i = 0; i < Config.Instance.RecentFolders.Count; i++)
            {
                values[i + 2] = Config.Instance.RecentFolders[i];
            }
            return values;
        }

        public int GetInitialFolderIndex()
        {
            if (Config.Instance.FolderSelectionMode == FolderSelectionMode.Auto)
            {
                return 0;
            }
            var index = GetFolderValues().ToList().IndexOf(Config.Instance.UserSelectedDownloadFolder);
            if (index > 1)
            {
                return index;
            }
            Config.Instance.FolderSelectionMode = FolderSelectionMode.Auto;
            return 0;
        }

        public void RecordFileBrowsed(string folder)
        {
            Helpers.UpdateRecentFolderList(folder);
        }

        public void UpdateFolderSelection(int index, string? selectedFile)
        {
            if (index == 0)
            {
                Config.Instance.FolderSelectionMode = FolderSelectionMode.Auto;
            }
            else if (!string.IsNullOrEmpty(selectedFile))
            {
                Config.Instance.FolderSelectionMode = FolderSelectionMode.Manual;
                if (index > 1)
                {
                    Config.Instance.UserSelectedDownloadFolder = selectedFile!;
                }
            }
            Config.SaveConfig();
        }

        public string? ResolveSelectedFolder(int index)
        {
            if (Config.Instance.FolderSelectionMode == FolderSelectionMode.Auto) return null;
            if (index == 0 || index == 1)
            {
                Log.Debug($"Index value {index} is invalid for {Config.Instance.FolderSelectionMode}");
                return null;
            }
            return Config.Instance.RecentFolders.Count > 0
                ? Config.Instance.RecentFolders[index - 2]
                : Config.Instance.DefaultDownloadFolder;
        }

        public string GetInitialDownloadLocation()
        {
            return Helpers.GetManualDownloadFolder() ?? Config.Instance.DefaultDownloadFolder;
        }

        public void RememberSelectedFolder(string folder)
        {
            Config.Instance.UserSelectedDownloadFolder = folder;
            Helpers.UpdateRecentFolderList(folder);
        }

        public void OpenWindowsProxySettings()
        {
            PlatformHelper.OpenWindowsProxySettings();
        }

        public void BlockHost(string host)
        {
            var blockedHosts = Config.Instance.BlockedHosts.ToList();
            blockedHosts.Add(host);
            Config.Instance.BlockedHosts = blockedHosts.ToArray();
            Config.SaveConfig();
            ApplicationContext.BroadcastConfigChange();
        }
    }
}

namespace ADM.Core
{
    public sealed class LegacyApplicationRuntimeContext : IApplicationRuntimeContext, BrowserMonitoring.IVideoTrackerRuntimeContext
    {
        public IApplicationWindow MainWindow => ApplicationContext.MainWindow;
        public IApplicationCore CoreService => ApplicationContext.CoreService;
        public IPlatformUIService PlatformUIService => ApplicationContext.PlatformUIService;
        public IClipboardMonitor ClipboardMonitor => ApplicationContext.ClipboardMonitor;
        public IApplication Application => ApplicationContext.Application;
        public ILinkRefresher LinkRefresher => ApplicationContext.LinkRefresher;
        public IVideoTracker VideoTracker => ApplicationContext.VideoTracker;
        public IReadOnlyList<Category> Categories => Config.Instance.Categories.ToList();
        public bool IsBrowserMonitoringEnabled => Config.Instance.IsBrowserMonitoringEnabled;
        public bool ShowNotification => Config.Instance.ShowNotification;
        public int GlassmorphismLevel => Config.Instance.GlassmorphismLevel;
        public string AppearanceTheme => Config.Instance.AppearanceTheme ?? "glacier";
        public string AppearanceBackdrop => Config.Instance.AppearanceBackdrop ?? "aurora";
        public string AppearanceAccent => Config.Instance.AppearanceAccent ?? "gradient";
        public string BrowserProtocolStateDirectory => Path.Combine(Config.AppDir, "browser-protocol");
        public string DownloadRulesFile => Path.Combine(Config.AppDir, "download-rules-v1.json");
        public int MinVideoSize => Config.Instance.MinVideoSize;
        public int NetworkTimeout => Config.Instance.NetworkTimeout;
        public bool EnableSpeedLimit => Config.Instance.EnableSpeedLimit;
        public int DefaultDownloadSpeed => Config.Instance.DefaltDownloadSpeed;
        public bool StartDownloadAutomatically =>
            AcceptanceTestEnvironment.ForceStartDownloadAutomatically || Config.Instance.StartDownloadAutomatically;
        public ProxyInfo? Proxy => Config.Instance.Proxy;
        public bool DoubleClickOpenFile => Config.Instance.DoubleClickOpenFile;
        public bool OpenFolder(string? folder, string fileName) => !string.IsNullOrWhiteSpace(folder) && PlatformHelper.OpenFolder(folder!, fileName);
        public bool OpenFile(string file) => PlatformHelper.OpenFile(file);
        public void EnableRunOnLogon() => PlatformHelper.EnableAutoStartUnlessDeclined();
        public void SubscribeInitialized(EventHandler handler) => ApplicationContext.Initialized += handler;
        public void SubscribeApplicationEvent(EventHandler<ApplicationEvent> handler) => ApplicationContext.ApplicationEvent += handler;
        public void UnsubscribeApplicationEvent(EventHandler<ApplicationEvent> handler) => ApplicationContext.ApplicationEvent -= handler;
        public void BroadcastConfigChange() => ApplicationContext.BroadcastConfigChange();
        public void UpdateSpeedLimit(bool enabled, int speed)
        {
            lock (Config.Instance)
            {
                Config.Instance.EnableSpeedLimit = enabled ? speed > 0 : false;
                Config.Instance.DefaltDownloadSpeed = speed > 0 ? speed : 0;
                Config.SaveConfig();
            }
            ApplicationContext.BroadcastConfigChange();
        }
    }
}
