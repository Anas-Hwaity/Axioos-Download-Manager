using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TraceLog;
using ADM.Core.Collections;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.MediaProcessor;
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

        private GenericOrderedDictionary<string, KeyValuePair<DualSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>> ytVideoList = new();
        private GenericOrderedDictionary<string, KeyValuePair<SingleSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>> videoList = new();
        private GenericOrderedDictionary<string, KeyValuePair<MultiSourceHLSDownloadInfo, StreamingVideoDisplayInfo>> hlsVideoList = new();
        private GenericOrderedDictionary<string, KeyValuePair<MultiSourceDASHDownloadInfo, StreamingVideoDisplayInfo>> dashVideoList = new();
        private Dictionary<string, string> currentPageSessions = new();

        public event EventHandler<MediaInfoEventArgs> MediaAdded;
        public event EventHandler<MediaInfoEventArgs> MediaUpdated;

        public void BeginPageSession(string tabId, string pageSessionId)
        {
            if (string.IsNullOrEmpty(tabId) || tabId == "-1" || string.IsNullOrEmpty(pageSessionId))
            {
                return;
            }
            lock (this)
            {
                if (currentPageSessions.TryGetValue(tabId, out var current) && current == pageSessionId)
                {
                    return;
                }
                currentPageSessions[tabId] = pageSessionId;
                RemoveMediaForTabExceptSessionUnsafe(ytVideoList, tabId, pageSessionId);
                RemoveMediaForTabExceptSessionUnsafe(videoList, tabId, pageSessionId);
                RemoveMediaForTabExceptSessionUnsafe(hlsVideoList, tabId, pageSessionId);
                RemoveMediaForTabExceptSessionUnsafe(dashVideoList, tabId, pageSessionId);
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void EndTabSession(string tabId)
        {
            if (string.IsNullOrEmpty(tabId) || tabId == "-1")
            {
                return;
            }
            lock (this)
            {
                currentPageSessions.Remove(tabId);
                RemoveMediaForTabExceptSessionUnsafe(ytVideoList, tabId, null);
                RemoveMediaForTabExceptSessionUnsafe(videoList, tabId, null);
                RemoveMediaForTabExceptSessionUnsafe(hlsVideoList, tabId, null);
                RemoveMediaForTabExceptSessionUnsafe(dashVideoList, tabId, null);
                runtimeContext.BroadcastConfigChange();
            }
        }

        public bool IsCurrentPageSession(string tabId, string pageSessionId)
        {
            lock (this)
            {
                return IsCurrentPageSessionUnsafe(tabId, pageSessionId);
            }
        }

        public string? CurrentPageSession(string tabId)
        {
            if (string.IsNullOrEmpty(tabId)) return null;
            lock (this)
            {
                return currentPageSessions.TryGetValue(tabId, out var current) ? current : null;
            }
        }

        public bool IsRegisteredPageSession(string tabId, string pageSessionId)
        {
            if (string.IsNullOrEmpty(tabId) || string.IsNullOrEmpty(pageSessionId)) return false;
            lock (this)
            {
                return currentPageSessions.TryGetValue(tabId, out var current) && current == pageSessionId;
            }
        }

        private bool IsCurrentPageSessionUnsafe(string tabId, string pageSessionId)
        {
            if (string.IsNullOrEmpty(tabId) || tabId == "-1")
            {
                return true;
            }
            if (!currentPageSessions.TryGetValue(tabId, out var current))
            {
                return true;
            }
            return !string.IsNullOrEmpty(pageSessionId) && current == pageSessionId;
        }

        private bool ShouldAcceptMediaUnsafe(StreamingVideoDisplayInfo displayInfo)
        {
            if (string.IsNullOrEmpty(displayInfo.TabId) || displayInfo.TabId == "-1")
            {
                return true;
            }
            if (!currentPageSessions.TryGetValue(displayInfo.TabId, out var current))
            {
                if (!string.IsNullOrEmpty(displayInfo.PageSessionId))
                {
                    currentPageSessions[displayInfo.TabId] = displayInfo.PageSessionId;
                }
                return true;
            }
            return !string.IsNullOrEmpty(displayInfo.PageSessionId) && current == displayInfo.PageSessionId;
        }

        public void RemoveAnalyzerMedia(string tabId, string tabUrl)
        {
            if (string.IsNullOrEmpty(tabId) || string.IsNullOrEmpty(tabUrl)) return;
            var removed = false;
            lock (this)
            {
                removed |= RemoveAnalyzerMediaUnsafe(ytVideoList, tabId, tabUrl);
                removed |= RemoveAnalyzerMediaUnsafe(videoList, tabId, tabUrl);
                removed |= RemoveAnalyzerMediaUnsafe(hlsVideoList, tabId, tabUrl);
                removed |= RemoveAnalyzerMediaUnsafe(dashVideoList, tabId, tabUrl);
            }
            if (removed) runtimeContext.BroadcastConfigChange();
        }

        private static bool RemoveAnalyzerMediaUnsafe<T>(
            GenericOrderedDictionary<string, KeyValuePair<T, StreamingVideoDisplayInfo>> source,
            string tabId,
            string tabUrl)
        {
            var removed = false;
            var keys = new List<string>(source.Keys);
            foreach (var key in keys)
            {
                var displayInfo = source[key].Value;
                if (displayInfo.TabId == tabId && displayInfo.Source == MediaSources.YtDlp && displayInfo.TabUrl == tabUrl)
                {
                    source.Remove(key);
                    removed = true;
                }
            }
            return removed;
        }

        private static void RemoveMediaForTabExceptSessionUnsafe<T>(
            GenericOrderedDictionary<string, KeyValuePair<T, StreamingVideoDisplayInfo>> source,
            string tabId,
            string? pageSessionIdToKeep)
        {
            var keys = new List<string>(source.Keys);
            foreach (var key in keys)
            {
                var displayInfo = source[key].Value;
                if (displayInfo.TabId == tabId &&
                    (pageSessionIdToKeep == null || displayInfo.PageSessionId != pageSessionIdToKeep))
                {
                    source.Remove(key);
                }
            }
        }

        public void ClearVideoList()
        {
            lock (this)
            {
                ytVideoList.Clear();
                hlsVideoList.Clear();
                dashVideoList.Clear();
                videoList.Clear();
            }
            runtimeContext.BroadcastConfigChange();
        }

        private string GenerateUpdatedFileName(string oldFile, string newName)
        {
            var ext = Path.GetExtension(oldFile);
            var file = FileHelper.SanitizeTitle(newName, "video");
            if (!string.IsNullOrEmpty(ext))
            {
                file += ext;
            }
            return file!;
        }

        public void UpdateMediaTitle(string tabId, string pageSessionId, string tabUrl, string tabTitle)
        {
            lock (this)
            {
                if (!IsCurrentPageSessionUnsafe(tabId, pageSessionId))
                {
                    return;
                }
                var legacyUnscopedTitleUpdate = string.IsNullOrEmpty(tabId);
                foreach (var e in ytVideoList)
                {
                    var displayInfo = e.Value.Value;
                    if (displayInfo.TabUrl == tabUrl && (legacyUnscopedTitleUpdate ||
                        (displayInfo.TabId == tabId && displayInfo.PageSessionId == pageSessionId)))
                    {
                        e.Value.Key.File = GenerateUpdatedFileName(e.Value.Key.File, tabTitle);
                        this.MediaUpdated?.Invoke(this, new MediaInfoEventArgs
                        {
                            MediaInfo = new MediaInfo(e.Key, e.Value.Key.File, displayInfo.DescriptionText,
                                displayInfo.CreationTime, displayInfo.TabId, displayInfo.PageSessionId, displayInfo.Source)
                        });
                    }
                }

                foreach (var e in videoList)
                {
                    var displayInfo = e.Value.Value;
                    if (displayInfo.TabUrl == tabUrl && (legacyUnscopedTitleUpdate ||
                        (displayInfo.TabId == tabId && displayInfo.PageSessionId == pageSessionId)))
                    {
                        e.Value.Key.File = GenerateUpdatedFileName(e.Value.Key.File, tabTitle);
                        this.MediaUpdated?.Invoke(this, new MediaInfoEventArgs
                        {
                            MediaInfo = new MediaInfo(e.Key, e.Value.Key.File, displayInfo.DescriptionText,
                                displayInfo.CreationTime, displayInfo.TabId, displayInfo.PageSessionId, displayInfo.Source)
                        });
                    }
                }
            }
        }

        public bool IsFFmpegRequiredForDownload(string id)
        {
            lock (this)
            {
                return ytVideoList.ContainsKey(id) || dashVideoList.ContainsKey(id) || hlsVideoList.ContainsKey(id);
            }
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
            DualSourceHTTPDownloadInfo? dual = null;
            SingleSourceHTTPDownloadInfo? single = null;
            MultiSourceHLSDownloadInfo? hls = null;
            MultiSourceDASHDownloadInfo? dash = null;
            var speedLimit = maxSpeedLimit == 0 ? (int?)null : maxSpeedLimit;
            lock (this)
            {
                if (ytVideoList.TryGetValue(videoId, out var ytEntry)) dual = ytEntry.Key;
                else if (videoList.TryGetValue(videoId, out var singleEntry)) single = singleEntry.Key;
                else if (hlsVideoList.TryGetValue(videoId, out var hlsEntry)) hls = hlsEntry.Key;
                else if (dashVideoList.TryGetValue(videoId, out var dashEntry)) dash = dashEntry.Key;
            }
            if (dual != null)
            {
                runtimeContext.CoreService.StartDownload(dual, name, FileNameFetchMode.ExtensionOnly,
                        folder, startImmediately, authentication, proxyInfo, queueId, false, speedLimit);
            }
            else if (single != null)
            {
                runtimeContext.CoreService.StartDownload(single, name, convertToMp3 ? FileNameFetchMode.None : FileNameFetchMode.ExtensionOnly,
                    folder, startImmediately, authentication, proxyInfo, queueId, convertToMp3, speedLimit);
            }
            else if (hls != null)
            {
                runtimeContext.CoreService.StartDownload(hls, name, FileNameFetchMode.ExtensionOnly,
                    folder, startImmediately, authentication, proxyInfo, queueId, false, speedLimit);
            }
            else if (dash != null)
            {
                runtimeContext.CoreService.StartDownload(dash, name, FileNameFetchMode.ExtensionOnly,
                    folder, startImmediately, authentication, proxyInfo, queueId, false, speedLimit);
            }
            else
            {
                Log.Debug("Video is no longer available for download: " + videoId);
            }
        }

        public List<MediaInfo> GetVideoList()
        {
            lock (this)
            {
                var list = new List<MediaInfo>();
                foreach (var e in ytVideoList)
                {
                    if (!IsCurrentPageSessionUnsafe(e.Value.Value.TabId, e.Value.Value.PageSessionId))
                    {
                        continue;
                    }
                    list.Add(new MediaInfo(e.Key, e.Value.Key.File, e.Value.Value.DescriptionText,
                        e.Value.Value.CreationTime, e.Value.Value.TabId, e.Value.Value.PageSessionId, e.Value.Value.Source) { TabUrl = e.Value.Value.TabUrl ?? string.Empty });
                }
                foreach (var e in videoList)
                {
                    if (!IsCurrentPageSessionUnsafe(e.Value.Value.TabId, e.Value.Value.PageSessionId))
                    {
                        continue;
                    }
                    list.Add(new MediaInfo(e.Key, e.Value.Key.File, e.Value.Value.DescriptionText,
                        e.Value.Value.CreationTime, e.Value.Value.TabId, e.Value.Value.PageSessionId, e.Value.Value.Source) { TabUrl = e.Value.Value.TabUrl ?? string.Empty });
                }
                foreach (var e in hlsVideoList)
                {
                    if (!IsCurrentPageSessionUnsafe(e.Value.Value.TabId, e.Value.Value.PageSessionId))
                    {
                        continue;
                    }
                    list.Add(new MediaInfo(e.Key, e.Value.Key.File, e.Value.Value.DescriptionText,
                        e.Value.Value.CreationTime, e.Value.Value.TabId, e.Value.Value.PageSessionId, e.Value.Value.Source) { TabUrl = e.Value.Value.TabUrl ?? string.Empty });
                }
                foreach (var e in dashVideoList)
                {
                    if (!IsCurrentPageSessionUnsafe(e.Value.Value.TabId, e.Value.Value.PageSessionId))
                    {
                        continue;
                    }
                    list.Add(new MediaInfo(e.Key, e.Value.Key.File, e.Value.Value.DescriptionText,
                        e.Value.Value.CreationTime, e.Value.Value.TabId, e.Value.Value.PageSessionId, e.Value.Value.Source) { TabUrl = e.Value.Value.TabUrl ?? string.Empty });
                }
                list.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
                return list;
            }
        }

        public void AddVideoNotifications(IEnumerable<KeyValuePair<DualSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    if (!ShouldAcceptMediaUnsafe(info.Value))
                    {
                        continue;
                    }
                    var id = Guid.NewGuid().ToString();
                    ytVideoList.Add(id, info);
                    Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Key.Uri1?.ToString()));
                    Log.Debug("Video url2: " + SensitiveDataRedactor.UrlForLog(info.Key.Uri2?.ToString()));
                    this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                    {
                        MediaInfo = new MediaInfo(id, info.Key.File, info.Value.DescriptionText,
                        DateTime.Now, info.Value.TabId, info.Value.PageSessionId, info.Value.Source)
                    });
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotifications(IEnumerable<KeyValuePair<SingleSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    if (!ShouldAcceptMediaUnsafe(info.Value))
                    {
                        continue;
                    }
                    var id = Guid.NewGuid().ToString();
                    videoList.Add(id, info);
                    Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Key.Uri?.ToString()));
                    this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                    {
                        MediaInfo = new MediaInfo(id, info.Key.File, info.Value.DescriptionText,
                        DateTime.Now, info.Value.TabId, info.Value.PageSessionId, info.Value.Source)
                    });
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotifications(IEnumerable<KeyValuePair<MultiSourceHLSDownloadInfo, StreamingVideoDisplayInfo>> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    if (!ShouldAcceptMediaUnsafe(info.Value))
                    {
                        continue;
                    }
                    var id = Guid.NewGuid().ToString();
                    hlsVideoList.Add(id, info);
                    Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Key.VideoUri?.ToString()));
                    Log.Debug("Video url2: " + SensitiveDataRedactor.UrlForLog(info.Key.AudioUri?.ToString()));
                    this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                    {
                        MediaInfo = new MediaInfo(id, info.Key.File, info.Value.DescriptionText,
                        DateTime.Now, info.Value.TabId, info.Value.PageSessionId, info.Value.Source)
                    });
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotifications(IEnumerable<KeyValuePair<MultiSourceDASHDownloadInfo, StreamingVideoDisplayInfo>> notifications)
        {
            lock (this)
            {
                foreach (var info in notifications)
                {
                    if (!ShouldAcceptMediaUnsafe(info.Value))
                    {
                        continue;
                    }
                    var id = Guid.NewGuid().ToString();
                    dashVideoList.Add(id, info);
                    Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Key.Url));
                    this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                    {
                        MediaInfo = new MediaInfo(id, info.Key.File, info.Value.DescriptionText,
                        DateTime.Now, info.Value.TabId, info.Value.PageSessionId, info.Value.Source)
                    });
                }
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, DualSourceHTTPDownloadInfo info)
        {
            lock (this)
            {
                if (!ShouldAcceptMediaUnsafe(displayInfo))
                {
                    return;
                }
                var id = Guid.NewGuid().ToString();
                ytVideoList.Add(id, new KeyValuePair<DualSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>(info, displayInfo));
                Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Uri1?.ToString()));
                Log.Debug("Video url2: " + SensitiveDataRedactor.UrlForLog(info.Uri2?.ToString()));
                this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                {
                    MediaInfo = new MediaInfo(id, info.File, displayInfo.DescriptionText,
                    DateTime.Now, displayInfo.TabId, displayInfo.PageSessionId, displayInfo.Source)
                });
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, SingleSourceHTTPDownloadInfo info)
        {
            lock (this)
            {
                if (!ShouldAcceptMediaUnsafe(displayInfo))
                {
                    return;
                }
                var id = Guid.NewGuid().ToString();
                videoList.Add(id, new KeyValuePair<SingleSourceHTTPDownloadInfo, StreamingVideoDisplayInfo>(info, displayInfo));
                Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Uri?.ToString()));
                this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                {
                    MediaInfo = new MediaInfo(id, info.File, displayInfo.DescriptionText,
                    DateTime.Now, displayInfo.TabId, displayInfo.PageSessionId, displayInfo.Source)
                });
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, MultiSourceHLSDownloadInfo info)
        {
            lock (this)
            {
                if (!ShouldAcceptMediaUnsafe(displayInfo))
                {
                    return;
                }
                var id = Guid.NewGuid().ToString();
                hlsVideoList.Add(id, new KeyValuePair<MultiSourceHLSDownloadInfo, StreamingVideoDisplayInfo>(info, displayInfo));
                Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.VideoUri?.ToString()));
                Log.Debug("Video url2: " + SensitiveDataRedactor.UrlForLog(info.AudioUri?.ToString()));
                this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                {
                    MediaInfo = new MediaInfo(id, info.File, displayInfo.DescriptionText,
                    DateTime.Now, displayInfo.TabId, displayInfo.PageSessionId, displayInfo.Source)
                });
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoNotification(StreamingVideoDisplayInfo displayInfo, MultiSourceDASHDownloadInfo info)
        {
            lock (this)
            {
                if (!ShouldAcceptMediaUnsafe(displayInfo))
                {
                    return;
                }
                var id = Guid.NewGuid().ToString();
                Log.Debug("DASH video added with id: " + id);
                dashVideoList.Add(id, new KeyValuePair<MultiSourceDASHDownloadInfo, StreamingVideoDisplayInfo>(info, displayInfo));
                Log.Debug("Video url1: " + SensitiveDataRedactor.UrlForLog(info.Url));
                this.MediaAdded?.Invoke(this, new MediaInfoEventArgs
                {
                    MediaInfo = new MediaInfo(id, info.File, displayInfo.DescriptionText,
                    DateTime.Now, displayInfo.TabId, displayInfo.PageSessionId, displayInfo.Source)
                });
                runtimeContext.BroadcastConfigChange();
            }
        }

        public void AddVideoDownload(string videoId)
        {
            var name = string.Empty;
            var size = 0L;
            var contentType = string.Empty;
            var valid = false;
            object? accepted = null;
            lock (this)
            {
                if (ytVideoList.TryGetValue(videoId, out var ytEntry))
                {
                    accepted = ytEntry.Key;
                    name = ytEntry.Key.File;
                    size = ytEntry.Value.Size;
                    contentType = ytEntry.Key.ContentType1;
                    valid = true;
                }
                else if (videoList.TryGetValue(videoId, out var singleEntry))
                {
                    accepted = singleEntry.Key;
                    name = singleEntry.Key.File;
                    size = singleEntry.Value.Size;
                    contentType = singleEntry.Key.ContentType;
                    valid = true;
                }
                else if (hlsVideoList.TryGetValue(videoId, out var hlsEntry))
                {
                    Log.Debug("Download HLS video added with id: " + videoId);
                    name = hlsEntry.Key.File;
                    valid = true;
                    try
                    {
                        contentType = hlsEntry.Key.ContentType;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, ex.Message);
                    }
                }
                else if (dashVideoList.TryGetValue(videoId, out var dashEntry))
                {
                    Log.Debug("Download DASH video added with id: " + videoId);
                    name = dashEntry.Key.File;
                    contentType = dashEntry.Key.ContentType;
                    valid = true;
                }
            }
            if (accepted is DualSourceHTTPDownloadInfo acceptedDual && runtimeContext.LinkRefresher.LinkAccepted(acceptedDual)) return;
            if (accepted is SingleSourceHTTPDownloadInfo acceptedSingle && runtimeContext.LinkRefresher.LinkAccepted(acceptedSingle)) return;
            if (valid)
            {
                if (runtimeContext.StartDownloadAutomatically && IsFFmpegOK(videoId))
                {
                    StartVideoDownload(
                        videoId, FileHelper.SanitizeFileName(name),
                        null, true, null, runtimeContext.Proxy,
                    0, null);
                }
                else
                {
                    runtimeContext.Application.ShowVideoDownloadDialog(videoId, name, size, contentType);
                }
            }
        }

        private bool IsFFmpegOK(string id)
        {
            if (!IsFFmpegRequiredForDownload(id)) return true;
            return FFmpegMediaProcessor.IsFFmpegInstalled();
        }
    }

    public class MediaInfo
    {
        public MediaInfo(string id, string name, string description,
            DateTime date, string tabId, string pageSessionId, string? source = null)
        {
            this.Source = source == MediaSources.YtDlp ? MediaSources.YtDlp : MediaSources.Browser;
            this.ID = id;
            this.Name = name;
            this.Description = description;
            this.DateAdded = date;
            this.TabId = tabId;
            this.PageSessionId = pageSessionId;
        }

        public string ID { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime DateAdded { get; set; }
        public string TabId { get; set; }
        public string PageSessionId { get; set; }
        public string Source { get; set; }
        public string TabUrl { get; set; } = string.Empty;
    }

    public static class MediaSources
    {
        public const string Browser = "browser";
        public const string YtDlp = "ytdlp";
    }

    public class MediaInfoEventArgs :EventArgs
    {
        public MediaInfo MediaInfo { get; set; }
    }
}
