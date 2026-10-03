using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Translations;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Downloader;
using ADM.Core.Legacy;
using ADM.Core.Navigation;
using ADM.Core.UI;
using ADM.Core.Util;
using ADM.Wpf.UI.Common.Helpers;
using ADM.Wpf.UI.Diagnostics;
using ADM.Wpf.UI.Dialogs.BatchDownload;
using ADM.Wpf.UI.Dialogs.ChromeIntegrator;
using ADM.Wpf.UI.Dialogs.CompletedDialog;
using ADM.Wpf.UI.Dialogs.CredentialDialog;
using ADM.Wpf.UI.Dialogs.DownloadSelection;
using ADM.Wpf.UI.Dialogs.MediaCapture;
using ADM.Wpf.UI.Dialogs.NewDownload;
using ADM.Wpf.UI.Dialogs.NewVideoDownload;
using ADM.Wpf.UI.Dialogs.ProgressWindow;
using ADM.Wpf.UI.Dialogs.PropertiesDialog;
using ADM.Wpf.UI.Dialogs.QueuesWindow;
using ADM.Wpf.UI.Dialogs.RefreshLink;
using ADM.Wpf.UI.Dialogs.Settings;
using ADM.Wpf.UI.Dialogs.SpeedLimiter;
using ADM.Wpf.UI.Dialogs.Updater;
using ADM.Wpf.UI.Dialogs.VideoDownloader;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI
{
    public class WpfPlatformUIService : IPlatformUIService
    {
        private Window? window;

        private Window? mediaGrabberWindow;
        private readonly INavigationService navigationService;
        private readonly IApplication application;
        private readonly IApplicationCore core;
        private readonly IDownloadCreationPreferences downloadCreationPreferences;
        private readonly IVideoTracker videoTracker;
        private readonly IApplicationRuntimeContext runtimeContext;
        private readonly ILinkRefresher linkRefresher;
        private readonly IExternalNavigationService externalNavigationService;

        public WpfPlatformUIService(
            MainWindow mainWindow,
            INavigationService navigationService,
            IApplication application,
            IApplicationCore core,
            IDownloadCreationPreferences downloadCreationPreferences,
            IVideoTracker videoTracker,
            IApplicationRuntimeContext runtimeContext,
            ILinkRefresher linkRefresher,
            IExternalNavigationService externalNavigationService)
        {
            this.navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            this.application = application ?? throw new ArgumentNullException(nameof(application));
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            this.downloadCreationPreferences = downloadCreationPreferences ?? throw new ArgumentNullException(nameof(downloadCreationPreferences));
            this.videoTracker = videoTracker ?? throw new ArgumentNullException(nameof(videoTracker));
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.linkRefresher = linkRefresher ?? throw new ArgumentNullException(nameof(linkRefresher));
            this.externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
            window = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        private Window GetMainWindow()
        {
            return window!;
        }

        public void ShowSpeedLimiterWindow()
        {
            var window = new SpeedLimiterWindow();
            SpeedLimiterUIController.Run(window, runtimeContext);
        }

        public string? SaveFileDialog(string? initialPath, string? defaultExt, string? filter)
        {
            var fc = new SaveFileDialog();
            if (!string.IsNullOrEmpty(initialPath))
            {
                fc.FileName = initialPath;
            }
            if (!string.IsNullOrEmpty(defaultExt))
            {
                fc.DefaultExt = defaultExt;
            }
            if (!string.IsNullOrEmpty(filter))
            {
                fc.Filter = filter;
            }
            var owner = WindowOwner.Usable(GetMainWindow());
            var ret = owner == null ? fc.ShowDialog() : fc.ShowDialog(owner);
            if (ret.HasValue && ret.Value)
            {
                return fc.FileName;
            }
            return null;
        }

        public string? OpenFileDialog(string? initialPath, string? defaultExt, string? filter)
        {
            var fc = new OpenFileDialog();
            if (!string.IsNullOrEmpty(initialPath))
            {
                fc.FileName = initialPath;
            }
            if (!string.IsNullOrEmpty(defaultExt))
            {
                fc.DefaultExt = defaultExt;
            }
            if (!string.IsNullOrEmpty(filter))
            {
                fc.Filter = filter;
            }
            var owner = WindowOwner.Usable(GetMainWindow());
            var ret = owner == null ? fc.ShowDialog() : fc.ShowDialog(owner);
            if (ret.HasValue && ret.Value)
            {
                return fc.FileName;
            }
            return null;
        }

        public void ShowRefreshLinkDialog(InProgressDownloadItem entry)
        {
            var dlg = new LinkRefreshWindow();
            var ret = LinkRefreshDialogUIController.RefreshLink(entry, dlg, linkRefresher);
            if (!ret)
            {
                ShowMessageBox(GetMainWindow(), TextResource.GetText("NO_REFRESH_LINK"));
                return;
            }
        }

        public void ShowPropertiesDialog(DownloadItemBase ent, string cookies, Dictionary<string, List<string>> headers)
        {
            var propertiesWindow = new DownloadPropertiesWindow
            {
                FileName = ent.Name,
                Folder = ent.TargetDir ?? FileHelper.GetDownloadFolderByFileName(ent.Name),
                Address = ent.PrimaryUrl,
                FileSize = FormattingHelper.FormatSize(ent.Size),
                DateAdded = ent.DateAdded.ToLongDateString() + " " + ent.DateAdded.ToLongTimeString(),
                DownloadType = ent.DownloadType,
                Referer = ent.RefererUrl,
                Cookies = cookies,
                Headers = headers,
                Owner = WindowOwner.Usable(GetMainWindow())
            };
            propertiesWindow.ShowDialog(GetMainWindow());
        }

        public void ShowYoutubeDLDialog()
        {
            var ydlWindow = new VideoDownloaderWindow(downloadCreationPreferences, externalNavigationService) { Owner = WindowOwner.Usable(GetMainWindow()) };
            var win = new VideoDownloaderUIController(ydlWindow, application, core, downloadCreationPreferences);
            win.Run();
        }

        public void ShowBatchDownloadWindow()
        {
            var uvc = new BatchDownloadUIController(new BatchDownloadWindow { Owner = WindowOwner.Usable(GetMainWindow()) }, application);
            uvc.Run();
        }

        public void ShowSettingsDialog(int page = 0)
        {
            AcceptanceDiagnostics.RecordStage("settings.service.enter", page.ToString());
            var route = page switch
            {
                0 => SettingsRoute.BrowserMonitoring,
                2 => SettingsRoute.Network,
                3 => SettingsRoute.Credentials,
                4 => SettingsRoute.Advanced,
                _ => SettingsRoute.General
            };
            navigationService.ShowSettings(route);
        }

        public AuthenticationInfo? PromtForCredentials(object window, string message)
        {
            var wnd = (Window)window;
            var dlg = new CredentialsPromptDialog { PromptText = message ?? "Authentication required", Owner = WindowOwner.Usable(wnd) };
            var ret = dlg.ShowDialog(wnd);
            if (ret.HasValue && ret.Value)
            {
                return dlg.Credentials;
            }
            return null;
        }

        public void ShowBrowserMonitoringDialog()
        {
            AcceptanceDiagnostics.RecordStage("browser-monitoring.service.enter");
            navigationService.ShowSettings(SettingsRoute.BrowserMonitoring);
        }

        public IUpdaterUI CreateUpdateUIDialog()
        {
            return new UpdaterWindow();
        }

        public void ShowMessageBox(object? window, string message)
        {
            var wnd = window is IApplication || window == GetMainWindow() || window == null ? GetMainWindow() : (Window)window;
            wnd.Dispatcher.Invoke(new Action(() =>
            {
                MessageBox.Show(wnd, message);
            }));
        }

        public void ShowDownloadSelectionWindow(FileNameFetchMode mode, IEnumerable<IRequestData> downloads)
        {
            var dsvc = new DownloadSelectionUIController(new DownloadSelectionWindow(downloadCreationPreferences), FileNameFetchMode.FileNameAndExtension, downloads, application, core, downloadCreationPreferences);
            dsvc.Run();
        }

        public IQueuesWindow CreateQueuesAndSchedulerWindow()
        {
            return new ManageQueueDialog(application);
        }

        public IQueueSelectionDialog CreateQueueSelectionDialog()
        {
            return new QueueSelectionWindow() { Owner = WindowOwner.Usable(GetMainWindow()) };
        }

        public IDownloadCompleteDialog CreateDownloadCompleteDialog()
        {
            return new DownloadCompleteWindow { };
        }

        public INewDownloadDialog CreateNewDownloadDialog(bool empty)
        {
            return new NewDownloadWindow(downloadCreationPreferences) { IsEmpty = empty };
        }

        public INewVideoDownloadDialog CreateNewVideoDialog()
        {
            return new NewVideoDownloadWindow(downloadCreationPreferences);
        }

        public IProgressWindow CreateProgressWindow(string downloadId)
        {
            return new DownloadProgressWindow(runtimeContext)
            {
                DownloadId = downloadId
            };
        }

        public void ShowMediaNotification()
        {
            AppTrayIcon.ShowNotification(runtimeContext.ShowNotification);
        }

        public void CreateAndShowMediaGrabber()
        {
            if (this.mediaGrabberWindow == null)
            {
                var mediaGrabber = new MediaCaptureWindow(videoTracker, externalNavigationService);
                this.mediaGrabberWindow = mediaGrabber;
                this.mediaGrabberWindow.Closing += MediaGrabberWindow_Closing;
            }
            this.mediaGrabberWindow.ShowActivated = true;
            this.mediaGrabberWindow.Show();
        }

        private void MediaGrabberWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.mediaGrabberWindow!.Closing -= MediaGrabberWindow_Closing;
            this.mediaGrabberWindow = null;
        }

        public void ShowExtensionRegistrationWindow()
        {
            var wnd = new ExtensionRegistration();
            wnd.Show();
        }
    }
}
