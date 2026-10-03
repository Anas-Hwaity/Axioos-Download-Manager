using System;
using System.Collections.Generic;
using System.Text;
using TraceLog;
using ADM.Core.Collections;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.Util;

namespace ADM.Core.BrowserMonitoring
{
    public class VideoTracker : IVideoTracker
    {
        private readonly IVideoTrackerRuntimeContext runtimeContext;

        public VideoTracker(IVideoTrackerRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        }

        private GenericOrderedDictionary<string, (DualSourceHTTPDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> ytVideoList = new();
        private GenericOrderedDictionary<string, (SingleSourceHTTPDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> videoList = new();
        private GenericOrderedDictionary<string, (MultiSourceHLSDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> hlsVideoList = new();
        private GenericOrderedDictionary<string, (MultiSourceDASHDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> dashVideoList = new();

        public void ClearVideoList()
        {
            ytVideoList.Clear();
            hlsVideoList.Clear();
            dashVideoList.Clear();
            videoList.Clear();
            runtimeContext.BroadcastConfigChange();
        }

        public bool IsFFmpegRequiredForDownload(string id)
        {
            return ytVideoList.ContainsKey(id) || dashVideoList.ContainsKey(id) || hlsVideoList.ContainsKey(id);
        }

        public void StartVideoDownload(string videoId,
            string name,
            string? folder,
            bool startImmediately,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            int maxSpeedLimit,
            string? queueId,
            bool convertToMp3 = false
            )
        {
            if (ytVideoList.ContainsKey(videoId))
            {
                runtimeContext.CoreService.StartDownload(ytVideoList[videoId].Info, name, FileNameFetchMode.ExtensionOnly,
                        folder, startImmediately, authentication, proxyInfo, Helpers.GetSpeedLimit(), queueId);
            }
            else if (videoList.ContainsKey(videoId))
            {
                runtimeContext.CoreService.StartDownload(videoList[videoId].Info, name, convertToMp3 ? FileNameFetchMode.None : FileNameFetchMode.ExtensionOnly,
                    folder, startImmediately, authentication, proxyInfo, Helpers.GetSpeedLimit(), queueId, convertToMp3);
            }
            else if (hlsVideoList.ContainsKey(videoId))
            {
                runtimeContext.CoreService.StartDownload(hlsVideoList[videoId].Info, name, FileNameFetchMode.ExtensionOnly,
                    folder, startImmediately, authentication, proxyInfo, Helpers.GetSpeedLimit(), queueId);
            }
            else if (dashVideoList.ContainsKey(videoId))
            {
                runtimeContext.CoreService.StartDownload(dashVideoList[videoId].Info, name, FileNameFetchMode.ExtensionOnly,
                    folder, startImmediately, authentication, proxyInfo, Helpers.GetSpeedLimit(), queueId);
            }
        }

        public List<(string ID, string File, string DisplayName, DateTime Time)> GetVideoList(bool encode = true)
        {
            lock (this)
            {
                var list = new List<(string ID, string File, string DisplayName, DateTime Time)>();
                foreach (var e in ytVideoList)
                {
                    list.Add((e.Key, encode ? Helpers.EncodeToCharCode(e.Value.Info.File) : e.Value.Info.File, e.Value.DisplayInfo.Quality, e.Value.DisplayInfo.CreationTime));
                }
                foreach (var e in videoList)
                {
                    list.Add((e.Key, encode ? Helpers.EncodeToCharCode(e.Value.Info.File) : e.Value.Info.File, e.Value.DisplayInfo.Quality, e.Value.DisplayInfo.CreationTime));
                }
                foreach (var e in hlsVideoList)
                {
                    list.Add((e.Key, encode ? Helpers.EncodeToCharCode(e.Value.Info.File) : e.Value.Info.File, e.Value.DisplayInfo.Quality, e.Value.DisplayInfo.CreationTime));
                }
                foreach (var e in dashVideoList)
                {
                    list.Add((e.Key, encode ? Helpers.EncodeToCharCode(e.Value.Info.File) : e.Value.Info.File, e.Value.DisplayInfo.Quality, e.Value.DisplayInfo.CreationTime));
                }
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                return list;
            }
        }

        public void AddVideoNotifications(IEnumerable<(DualSourceHTTPDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    ytVideoList.Add(Guid.NewGuid().ToString(), info);
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotifications(IEnumerable<(SingleSourceHTTPDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    videoList.Add(Guid.NewGuid().ToString(), info);
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotifications(IEnumerable<(MultiSourceHLSDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    hlsVideoList.Add(Guid.NewGuid().ToString(), info);
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotifications(IEnumerable<(MultiSourceDASHDownloadInfo Info, StreamingVideoDisplayInfo DisplayInfo)> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    dashVideoList.Add(Guid.NewGuid().ToString(), info);
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, DualSourceHTTPDownloadInfo info)
        {
            lock (this)
            {
                ytVideoList.Add(Guid.NewGuid().ToString(), (info, displayInfo));
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, SingleSourceHTTPDownloadInfo info)
        {
            lock (this)
            {
                videoList.Add(Guid.NewGuid().ToString(), (info, displayInfo));
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, MultiSourceHLSDownloadInfo info)
        {
            lock (this)
            {
                var id = Guid.NewGuid().ToString();
                hlsVideoList.Add(id, (info, displayInfo));
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, MultiSourceDASHDownloadInfo info)
        {
            lock (this)
            {
                var id = Guid.NewGuid().ToString();
                Log.Debug("DASH video added with id: " + id);
                dashVideoList.Add(id, (info, displayInfo));
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoDownload(string videoId)
        {
            var name = string.Empty;
            var size = 0L;
            var contentType = string.Empty;
            var valid = false;
            if (ytVideoList.ContainsKey(videoId))
            {
                if (runtimeContext.LinkRefresher.LinkAccepted(ytVideoList[videoId].Info)) return;
                name = ytVideoList[videoId].Info.File;
                size = ytVideoList[videoId].DisplayInfo.Size;
                contentType = ytVideoList[videoId].Info.ContentType1;
                valid = true;
            }
            else if (videoList.ContainsKey(videoId))
            {
                if (runtimeContext.LinkRefresher.LinkAccepted(videoList[videoId].Info)) return;
                name = videoList[videoId].Info.File;
                size = videoList[videoId].DisplayInfo.Size;
                contentType = videoList[videoId].Info.ContentType;
                valid = true;
            }
            else if (hlsVideoList.ContainsKey(videoId))
            {
                Log.Debug("Download HLS video added with id: " + videoId);
                name = hlsVideoList[videoId].Info.File;
                valid = true;
                try
                {
                    contentType = hlsVideoList[videoId].Info.ContentType;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                }
            }
            else if (dashVideoList.ContainsKey(videoId))
            {
                Log.Debug("Download DASH video added with id: " + videoId);
                name = dashVideoList[videoId].Info.File;
                contentType = dashVideoList[videoId].Info.ContentType;
                valid = true;
            }
            if (valid)
            {
                if (runtimeContext.StartDownloadAutomatically)
                {
                    StartVideoDownload(
                        videoId, Helpers.SanitizeFileName(name),
                        null, true, null, runtimeContext.Proxy,
                    Helpers.GetSpeedLimit(), null);
                }
                else
                {
                    runtimeContext.Application.ShowVideoDownloadDialog(videoId, name, size, contentType);
                }
            }
        }
    }
}
