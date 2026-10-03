using System;
using System.IO;
using TraceLog;
using ADM.Core;
using ADM.Core.UI;

namespace ADM.Core
{
    internal static class CallbackActions
    {
        public static void DownloadStarted(string id, IApplicationRuntimeContext runtimeContext)
        {
            var download = runtimeContext.MainWindow.FindInProgressItem(id);
            if (download == null) return;
            download.Status = DownloadStatus.Downloading;
        }

        public static void DownloadFailed(string id, IApplicationRuntimeContext runtimeContext)
        {
            var download = runtimeContext.MainWindow.FindInProgressItem(id);
            if (download == null) return;
            download.Status = DownloadStatus.Stopped;
        }

        public static void DownloadFinished(string id, long finalFileSize, string filePath, Action callback, IApplicationRuntimeContext runtimeContext)
        {
            Log.Debug("Final file name: " + filePath);
            var download = runtimeContext.MainWindow.FindInProgressItem(id);
            if (download == null) return;
            var downloadEntry = download.DownloadEntry;
            downloadEntry.Progress = 100;

            var finishedEntry = new FinishedDownloadItem
            {
                Name = Path.GetFileName(filePath),
                Id = downloadEntry.Id,
                DateAdded = downloadEntry.DateAdded,
                Size = downloadEntry.Size > 0 ? downloadEntry.Size : finalFileSize,
                DownloadType = downloadEntry.DownloadType,
                TargetDir = Path.GetDirectoryName(filePath)!,
                PrimaryUrl = downloadEntry.PrimaryUrl,
                Authentication = downloadEntry.Authentication,
                Proxy = downloadEntry.Proxy
            };

            runtimeContext.MainWindow.AddToTop(finishedEntry);
            runtimeContext.MainWindow.Delete(download);

            QueueManager.RemoveFinishedDownload(download.DownloadEntry.Id);

            if (runtimeContext.CoreService.ActiveDownloadCount == 0 && runtimeContext.MainWindow.IsInProgressViewSelected)
            {
                Log.Debug("switching to finished listview");
                runtimeContext.MainWindow.SwitchToFinishedView();
            }

            callback.Invoke();
        }
    }
}
