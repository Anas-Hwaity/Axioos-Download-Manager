using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ADM.Core.UI;
using ADM.Core;
using ADM.Core.Downloader;

namespace ADM.Core
{
    public interface IApplication
    {
        void UpdateProgress(string id, int progress, double speed, long eta);
        void DownloadFinished(string id, long finalFileSize, string filePath);
        void DownloadFailed(string id);
        void DownloadCanelled(string id);
        void SetDownloadStatus(string id, DownloadStatus status);

        public void AddItemToTop(string id, string targetFileName,
            string? targetDir, DateTime date,
            long fileSize, string type, FileNameFetchMode fileNameFetchMode,
            string primaryUrl, DownloadStartType startType,
            AuthenticationInfo? authentication, ProxyInfo? proxyInfo,
            IReadOnlyList<string>? tags = null);

        void UpdateItem(string id, string targetFileName, long size);

        INewDownloadDialog CreateNewDownloadDialog(bool empty);

        INewVideoDownloadDialog CreateNewVideoDialog();

        public void ShowNewDownloadDialog(Message message);

        public void ShowVideoDownloadDialog(string videoId, string name, long size, string? contentType);
        public bool Confirm(object? window, string text);
        public void DownloadStarted(string id);

        public IProgressWindow CreateProgressWindow(string downloadId);

        public void ShowMessageBox(object? window, string message);

        public void ShowDownloadCompleteDialog(string file, string folder);

        public IDownloadCompleteDialog CreateDownloadCompleteDialog();

        string? GetUrlFromClipboard();

        public void ResumeDownload(string downloadId);


        public InProgressDownloadItem? GetInProgressDownloadEntry(string downloadId);

        public void RunOnUiThread(Action action);

        public void SetDownloadStatusWaiting(string id);

        public IEnumerable<InProgressDownloadItem> GetAllInProgressDownloads();


        void RenameFileOnUI(string id, string folder, string file);

        public AuthenticationInfo? PromtForCredentials(string message);
        public void ShowUpdateAvailableNotification();

        public void InstallLatestYtDlp();


        void ShowQueueWindow(object window);

        void ShowDownloadSelectionWindow(FileNameFetchMode mode, IEnumerable<IRequestData> downloads);

        IPlatformClipboardMonitor GetPlatformClipboardMonitor();

        event EventHandler WindowLoaded;
    }

    public enum DownloadStartType
    {
        Waiting, Stopped, Scheduled
    }

    public enum UpdateAction
    {
        LaunchBrowser, DownloadExternalApps
    }





}
