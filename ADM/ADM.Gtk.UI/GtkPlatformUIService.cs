using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translations;
using ADM.Core;
using ADM.Core.Downloader;
using ADM.Core.Legacy;
using ADM.Core.UI;
using ADM.Core.Util;
using ADM.GtkUI.Dialogs;
using ADM.GtkUI.Dialogs.BatchWindow;
using ADM.GtkUI.Dialogs.ChromeIntegrator;
using ADM.GtkUI.Dialogs.DownloadComplete;
using ADM.GtkUI.Dialogs.DownloadSelection;
using ADM.GtkUI.Dialogs.LinkRefresh;
using ADM.GtkUI.Dialogs.NewDownload;
using ADM.GtkUI.Dialogs.NewVideoDownload;
using ADM.GtkUI.Dialogs.ProgressWindow;
using ADM.GtkUI.Dialogs.Properties;
using ADM.GtkUI.Dialogs.QueueScheduler;
using ADM.GtkUI.Dialogs.Settings;
using ADM.GtkUI.Dialogs.SpeedLimiter;
using ADM.GtkUI.Dialogs.Updater;
using ADM.GtkUI.Dialogs.VideoDownloader;
using ADM.GtkUI.Utils;

namespace ADM.GtkUI
{
    public class GtkPlatformUIService : IPlatformUIService
    {
        private MainWindow? window;
        private WindowGroup? windowGroup;
        private readonly IApplication application;
        private readonly IApplicationCore core;
        private readonly IDownloadCreationPreferences downloadCreationPreferences;
        private readonly IApplicationRuntimeContext runtimeContext;
        private readonly ILinkRefresher linkRefresher;

        public GtkPlatformUIService(MainWindow mainWindow, IApplication application, IApplicationCore core, IDownloadCreationPreferences downloadCreationPreferences, IApplicationRuntimeContext runtimeContext, ILinkRefresher linkRefresher)
        {
            this.application = application ?? throw new ArgumentNullException(nameof(application));
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            this.downloadCreationPreferences = downloadCreationPreferences ?? throw new ArgumentNullException(nameof(downloadCreationPreferences));
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.linkRefresher = linkRefresher ?? throw new ArgumentNullException(nameof(linkRefresher));
            window = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            windowGroup = window.GetWindowGroup();
        }


        private Window GetMainWindow()
        {
            return window!;
        }

        private WindowGroup GetWindowGroup()
        {
            return windowGroup!;
        }

        public void ShowSpeedLimiterWindow()
        {
            var window = SpeedLimiterWindow.CreateFromGladeFile();
            SpeedLimiterUIController.Run(window, runtimeContext);
        }

        public string? SaveFileDialog(string? initialPath, string? defaultExt, string? filter)
        {
            return GtkHelper.SaveFile(GetMainWindow(), initialPath);
        }

        public string? OpenFileDialog(string? initialPath, string? defaultExt, string? filter)
        {
            return GtkHelper.SelectFile(GetMainWindow());
        }

        public IQueuesWindow CreateQueuesAndSchedulerWindow()
        {
            return QueueSchedulerDialog.CreateFromGladeFile(GetMainWindow(), GetWindowGroup());
        }

        public void ShowDownloadSelectionWindow(FileNameFetchMode mode, IEnumerable<IRequestData> downloads)
        {
            var dsvc = new DownloadSelectionUIController(DownloadSelectionWindow.CreateFromGladeFile(),
                FileNameFetchMode.FileNameAndExtension, downloads, application, core, downloadCreationPreferences);
            dsvc.Run();
        }

        public void ShowRefreshLinkDialog(InProgressDownloadItem entry)
        {
            var dlg = LinkRefreshWindow.CreateFromGladeFile();
            var ret = LinkRefreshDialogUIController.RefreshLink(entry, dlg, linkRefresher);
            if (!ret)
            {
                GtkHelper.ShowMessageBox(GetMainWindow(), TextResource.GetText("NO_REFRESH_LINK"));
                return;
            }
        }

        public void ShowPropertiesDialog(DownloadItemBase ent, string cookies, Dictionary<string, List<string>> headers)
        {
            using var propWin = PropertiesDialog.CreateFromGladeFile(GetMainWindow(), GetWindowGroup());
            propWin.FileName = ent.Name;
            propWin.Folder = ent.TargetDir ?? FileHelper.GetDownloadFolderByFileName(ent.Name);
            propWin.Address = ent.PrimaryUrl;
            propWin.FileSize = FormattingHelper.FormatSize(ent.Size);
            propWin.DateAdded = ent.DateAdded.ToLongDateString() + " " + ent.DateAdded.ToLongTimeString();
            propWin.DownloadType = ent.DownloadType;
            propWin.Referer = ent.RefererUrl;
            propWin.Cookies = cookies;
            propWin.Headers = headers;
            propWin.Run();
            propWin.Destroy();
            propWin.Dispose();
        }

        public void ShowYoutubeDLDialog()
        {
            var win = new VideoDownloaderUIController(VideoDownloaderWindow.CreateFromGladeFile(), application, core, downloadCreationPreferences);
            win.Run();
        }

        public void ShowBatchDownloadWindow()
        {
            var uvc = new BatchDownloadUIController(BatchDownloadWindow.CreateFromGladeFile(GetMainWindow()), application);
            uvc.Run();
        }

        public void ShowSettingsDialog(int page = 0)
        {
            using var win = SettingsDialog.CreateFromGladeFile(GetMainWindow(), GetWindowGroup());
            win.SetActivePage(page);
            win.LoadConfig();
            win.Run();
            win.Destroy();
        }

        public AuthenticationInfo? PromtForCredentials(string message)
        {
            var dlg = CredentialsDialog.CreateFromGladeFile(GetMainWindow(), GetWindowGroup());
            dlg.PromptText = message ?? "Authentication required";
            dlg.Run();
            if (dlg.Result)
            {
                return dlg.Credentials;
            }
            return null;
        }

        public void ShowBrowserMonitoringDialog()
        {
            ShowSettingsDialog(0);
        }

        public IUpdaterUI CreateUpdateUIDialog()
        {
            return UpdaterWindow.CreateFromGladeFile();
        }

        public void ShowMessageBox(object? window, string message)
        {
            if (window is not Window owner)
            {
                owner = GetMainWindow();
            }
            GtkHelper.ShowMessageBox(owner, message);
        }

        public IQueuesWindow CreateQueuesAndSchedulerWindow(IEnumerable<DownloadQueue> queues)
        {
            return QueueSchedulerDialog.CreateFromGladeFile(GetMainWindow(), GetWindowGroup());
        }

        public IQueueSelectionDialog CreateQueueSelectionDialog()
        {
            var qsd = QueueSelectionDialog.CreateFromGladeFile(GetMainWindow(), GetWindowGroup());
            return qsd;
        }
        public IDownloadCompleteDialog CreateDownloadCompleteDialog()
        {
            var win = DownloadCompleteDialog.CreateFromGladeFile();
            return win;
        }

        public INewDownloadDialog CreateNewDownloadDialog(bool empty)
        {
            var window = NewDownloadWindow.CreateFromGladeFile();
            window.IsEmpty = empty;
            return window;
        }

        public INewVideoDownloadDialog CreateNewVideoDialog()
        {
            var window = NewVideoDownloadWindow.CreateFromGladeFile();
            return window;
        }

        public IProgressWindow CreateProgressWindow(string downloadId)
        {
            var prgWin = DownloadProgressWindow.CreateFromGladeFile();
            prgWin.DownloadId = downloadId;
            return prgWin;
        }

        public AuthenticationInfo? PromtForCredentials(object window, string message)
        {
            throw new NotImplementedException();
        }

        public void ShowMediaNotification()
        {
            try
            {
                PlatformHelper.SpawnSubProcess("notify-send", new string[] { TextResource.GetText("MSG_VID_CAP") });
            }
            catch { }
        }

        public void CreateAndShowMediaGrabber()
        {
            var win = ADM.GtkUI.Dialogs.MediaGrabber.MediaGrabberWindow.CreateFromGladeFile();
            win.Show();
        }

        public void ShowExtensionRegistrationWindow()
        {
            var win = RegisterExtensionWindow.CreateFromGladeFile();
            win.Show();
        }
    }
}
