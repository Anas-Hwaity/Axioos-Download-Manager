using System;
using System.Collections.Generic;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;

namespace ADM.Core.BrowserMonitoring
{
    public interface IVideoDetectionRuntimeContext
    {
        IApplicationCore CoreService { get; }
        IVideoTracker VideoTracker { get; }
        int MinVideoSize { get; }
        int NetworkTimeout { get; }
    }

    public interface IVideoTrackerRuntimeContext
    {
        IApplicationCore CoreService { get; }
        IApplication Application { get; }
        ILinkRefresher LinkRefresher { get; }
        bool StartDownloadAutomatically { get; }
        ProxyInfo? Proxy { get; }
        void BroadcastConfigChange();
    }

    public interface IVideoTracker
    {
        void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, DualSourceHTTPDownloadInfo info);
        void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, MultiSourceDASHDownloadInfo info);
        void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, MultiSourceHLSDownloadInfo info);
        void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, SingleSourceHTTPDownloadInfo info);
        void AddVideoNotifications(IEnumerable<KeyValuePair<DualSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>> notifications);
        void AddVideoNotifications(IEnumerable<KeyValuePair<MultiSourceDASHDownloadInfo, StreamingVideoDisplayInfo>> notifications);
        void AddVideoNotifications(IEnumerable<KeyValuePair<MultiSourceHLSDownloadInfo, StreamingVideoDisplayInfo>> notifications);
        void AddVideoNotifications(IEnumerable<KeyValuePair<SingleSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>> notifications);
        bool IsFFmpegRequiredForDownload(string id);
        void StartVideoDownload(string videoId,
            string name,
            string? folder,
            bool startImmediately,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            int maxSpeedLimit,
            string? queueId,
            bool convertToMp3 = false
            );

        event EventHandler<MediaInfoEventArgs> MediaAdded;
        event EventHandler<MediaInfoEventArgs> MediaUpdated;
        void ClearVideoList();
        void AddVideoDownload(string videoId);
        List<MediaInfo> GetVideoList();
        void BeginPageSession(string tabId, string pageSessionId);
        void EndTabSession(string tabId);
        bool IsCurrentPageSession(string tabId, string pageSessionId);
        bool IsRegisteredPageSession(string tabId, string pageSessionId);
        string? CurrentPageSession(string tabId);
        void UpdateMediaTitle(string tabId, string pageSessionId, string tabUrl, string tabTitle);
        void RemoveAnalyzerMedia(string tabId, string tabUrl);
    }
}