using System;
using ADM.Core.Downloader.Progressive;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;

namespace ADM.Core
{
    public enum LinkRefreshState
    {
        WaitingForReplacementSource,
        Refreshed,
        Stopped
    }

    public sealed class LinkRefreshStateChangedEventArgs : EventArgs
    {
        public LinkRefreshStateChangedEventArgs(string downloadId, LinkRefreshState state)
        {
            DownloadId = downloadId ?? string.Empty;
            State = state;
        }

        public string DownloadId { get; }
        public LinkRefreshState State { get; }
    }

    public interface ILinkRefresher
    {
        event EventHandler<LinkRefreshStateChangedEventArgs>? RefreshStateChanged;

        bool TryAddToWatchList(HTTPDownloaderBase downloader, EventHandler refreshedHandler);
        bool ClearWatchList(HTTPDownloaderBase downloader);
        bool LinkAccepted(Message message);
        bool LinkAccepted(SingleSourceHTTPDownloadInfo info);
        bool LinkAccepted(DualSourceHTTPDownloadInfo info);
    }
}
