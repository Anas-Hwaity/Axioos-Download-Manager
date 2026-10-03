using System;
using System.Linq;
using ADM.Core.Downloader.Progressive;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;

namespace ADM.Core
{
    public class LinkRefresher : ILinkRefresher
    {
        private readonly object refreshSync = new();
        private HTTPDownloaderBase? refreshLinkCandidate;
        private EventHandler? refreshAcceptedHandler;

        public event EventHandler<LinkRefreshStateChangedEventArgs>? RefreshStateChanged;

        public bool LinkAccepted(Message message)
        {
            EventHandler? handler;
            string downloadId;
            lock (refreshSync)
            {
                var candidate = refreshLinkCandidate;
                if (candidate == null || !IsMatchingSingleSourceLink(candidate, message)) return false;
                downloadId = candidate.Id ?? string.Empty;
                var info = new SingleSourceHTTPDownloadInfo
                {
                    Uri = message.Url,
                    Headers = message.RequestHeaders,
                    Cookies = message.Cookies
                };
                ((SingleSourceHTTPDownloader)candidate).SetDownloadInfo(info);
                handler = CompleteRefreshLocked();
            }
            handler?.Invoke(this, EventArgs.Empty);
            RaiseRefreshState(downloadId, LinkRefreshState.Refreshed);
            return true;
        }

        public bool LinkAccepted(SingleSourceHTTPDownloadInfo info)
        {
            EventHandler? handler;
            string downloadId;
            lock (refreshSync)
            {
                var candidate = refreshLinkCandidate;
                if (candidate == null || !IsMatchingSingleSourceLink(candidate, info)) return false;
                downloadId = candidate.Id ?? string.Empty;
                ((SingleSourceHTTPDownloader)candidate).SetDownloadInfo(info);
                handler = CompleteRefreshLocked();
            }
            handler?.Invoke(this, EventArgs.Empty);
            RaiseRefreshState(downloadId, LinkRefreshState.Refreshed);
            return true;
        }

        public bool LinkAccepted(DualSourceHTTPDownloadInfo info)
        {
            EventHandler? handler;
            string downloadId;
            lock (refreshSync)
            {
                var candidate = refreshLinkCandidate;
                if (candidate == null || !IsMatchingDualSourceLink(candidate, info)) return false;
                downloadId = candidate.Id ?? string.Empty;
                ((DualSourceHTTPDownloader)candidate).SetDownloadInfo(info);
                handler = CompleteRefreshLocked();
            }
            handler?.Invoke(this, EventArgs.Empty);
            RaiseRefreshState(downloadId, LinkRefreshState.Refreshed);
            return true;
        }

        private EventHandler? CompleteRefreshLocked()
        {
            var handler = refreshAcceptedHandler;
            refreshLinkCandidate = null;
            refreshAcceptedHandler = null;
            return handler;
        }

        private static bool IsMatchingSingleSourceLink(HTTPDownloaderBase candidate, Message message)
        {
            if (!(candidate is SingleSourceHTTPDownloader)) return false;
            var header = message.ResponseHeaders.Keys.Where(key => key.Equals("content-length", StringComparison.InvariantCultureIgnoreCase)).ToArray();
            if (header.Length != 1) return false;
            var values = message.ResponseHeaders[header[0]];
            if (values == null || values.Count != 1 || !Int64.TryParse(values[0], out var contentLength)) return false;
            return candidate.FileSize == contentLength && candidate.FileSize > 0;
        }

        private static bool IsMatchingSingleSourceLink(HTTPDownloaderBase candidate, SingleSourceHTTPDownloadInfo info)
        {
            if (!(candidate is SingleSourceHTTPDownloader)) return false;
            return candidate.FileSize == info.ContentLength && candidate.FileSize > 0;
        }

        private static bool IsMatchingDualSourceLink(HTTPDownloaderBase candidate, DualSourceHTTPDownloadInfo info)
        {
            if (!(candidate is DualSourceHTTPDownloader)) return false;
            return info.ContentLength > 0 && candidate.FileSize == info.ContentLength;
        }

        public bool TryAddToWatchList(HTTPDownloaderBase downloader, EventHandler refreshedHandler)
        {
            var accepted = false;
            lock (refreshSync)
            {
                if (refreshLinkCandidate == null)
                {
                    refreshLinkCandidate = downloader;
                    refreshAcceptedHandler = refreshedHandler;
                    accepted = true;
                }
                else
                {
                    accepted = ReferenceEquals(refreshLinkCandidate, downloader);
                }
            }
            if (accepted) RaiseRefreshState(downloader?.Id ?? string.Empty, LinkRefreshState.WaitingForReplacementSource);
            return accepted;
        }

        public bool ClearWatchList(HTTPDownloaderBase downloader)
        {
            var downloadId = string.Empty;
            lock (refreshSync)
            {
                if (!ReferenceEquals(refreshLinkCandidate, downloader)) return false;
                downloadId = downloader?.Id ?? string.Empty;
                refreshLinkCandidate = null;
                refreshAcceptedHandler = null;
            }
            RaiseRefreshState(downloadId, LinkRefreshState.Stopped);
            return true;
        }

        private void RaiseRefreshState(string downloadId, LinkRefreshState state)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) return;
            RefreshStateChanged?.Invoke(this, new LinkRefreshStateChangedEventArgs(downloadId, state));
        }
    }
}
