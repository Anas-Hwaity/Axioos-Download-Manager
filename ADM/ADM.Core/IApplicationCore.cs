using System;
using System.Collections.Generic;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.UI;

namespace ADM.Core
{
    public enum DownloadCancellationPolicy
    {
        RetainPartial,
        DiscardPartial
    }

    public interface IApplicationCore
    {
        public Version AppVerion { get; }
        public string AppPlatform { get; }

        public void AddDownload(Message message);

        public string? StartDownload(
            IRequestData info,
            string fileName,
            FileNameFetchMode fileNameFetchMode,
            string? targetFolder,
            bool startImmediately,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            string? queueId,
            bool convertToMp3,
            int? speedLimitKiB = null);

        public int GetDownloadSpeedLimit(string id);

        public void SetDownloadSpeedLimit(string id, int setting);

        public void StopDownloads(IEnumerable<string> list, bool closeProgressWindow = false);

        public void PauseDownloads(IEnumerable<string> list, bool closeProgressWindow = false);

        public bool CancelDownload(string id, DownloadCancellationPolicy policy = DownloadCancellationPolicy.RetainPartial);

        public void ResumeDownload(Dictionary<string, DownloadItemBase> list, bool nonInteractive = false);

        public void ResumeNonInteractiveDownloads(IEnumerable<string> idList);

        public bool IsDownloadActive(string id);

        public int ActiveDownloadCount { get; }

        public void RenameDownload(string id, string folder, string file);

        public AuthenticationInfo? PromptForCredential(string id, string message);

        public bool RestartDownload(DownloadItemBase entry);

        public string? GetPrimaryUrl(DownloadItemBase entry);

        public void RemoveDownload(DownloadItemBase entry, bool deleteDownloadedFile, bool removeInfo = true);

        public void ShowProgressWindow(string downloadId);

        public void HideProgressWindow(string id);

        public void Export(string path);

        public void Import(string path);

        void AddBatchLinks(List<Message> messages);
    }
}
