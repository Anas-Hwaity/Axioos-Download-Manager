using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ADM.Core.UI;
using ADM.Core;
using ADM.Core.MediaProcessor;
using ADM.Core.Util;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Collections;
using System.Timers;
using TraceLog;
using Translations;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Progressive;
using ADM.Core.DataAccess;
using ADM.Core.IO;
using ADM.Core.Rules;
using ADM.Core.Telemetry;

#if !NET5_0_OR_GREATER
using ADM.Compatibility;
#endif

namespace ADM.Core
{
    public class ApplicationCore : IApplicationCore
    {
        public Version AppVerion => new(AppInfo.APP_VERSION);
        public string AppPlatform => PlatformHelper.GetAppPlatform();

        private Dictionary<string, KeyValuePair<IBaseDownloader, bool>> liveDownloads = new();
        private GenericOrderedDictionary<string, bool> queuedDownloads = new();
        private GenericOrderedDictionary<string, IProgressWindow> activeProgressWindows = new();
        private Scheduler scheduler;
        private Timer awakePingTimer;
        private readonly IBrowserMonitoringService browserMonitoringService;
        private readonly IApplicationRuntimeContext runtimeContext;
        private readonly DownloadRulePolicy downloadRulePolicy;
        private readonly IDownloadTelemetrySink telemetrySink;
        private readonly Dictionary<string, long> telemetryRevisions = new();
        private readonly Dictionary<string, double> telemetrySmoothedSpeeds = new();
        private readonly Dictionary<string, DownloadStopIntent> stopIntents = new();
        private readonly Dictionary<string, string> telemetryRefreshSourceStates = new();
        private readonly Dictionary<string, long> telemetrySourceGenerations = new();
        private bool linkRefreshTelemetrySubscribed;

        private enum DownloadStopIntent
        {
            Pause,
            CancelRetainPartial,
            CancelDiscardPartial
        }
        public int ActiveDownloadCount { get => liveDownloads.Count + queuedDownloads.Count; }

        public ApplicationCore(IBrowserMonitoringService browserMonitoringService, IApplicationRuntimeContext runtimeContext, DownloadRulePolicy downloadRulePolicy, IDownloadTelemetrySink telemetrySink)
        {
            this.browserMonitoringService = browserMonitoringService ?? throw new ArgumentNullException(nameof(browserMonitoringService));
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.downloadRulePolicy = downloadRulePolicy ?? throw new ArgumentNullException(nameof(downloadRulePolicy));
            this.telemetrySink = telemetrySink ?? throw new ArgumentNullException(nameof(telemetrySink));
            this.runtimeContext.SubscribeInitialized(AppInstance_Initialized);
        }

        private void AppInstance_Initialized(object sender, EventArgs e)
        {
            awakePingTimer = new Timer(60000)
            {
                AutoReset = true
            };
            awakePingTimer.Elapsed += (a, b) => PlatformHelper.SendKeepAlivePing();

            try
            {
                QueueManager.Load();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.ToString());
            }

            SubscribeLinkRefreshTelemetry();
            StartScheduler();
            StartBrowserMonitoring();
        }


        private void SubscribeLinkRefreshTelemetry()
        {
            if (linkRefreshTelemetrySubscribed) return;
            var refresher = runtimeContext.LinkRefresher;
            if (refresher == null) return;
            refresher.RefreshStateChanged += HandleLinkRefreshStateChanged;
            linkRefreshTelemetrySubscribed = true;
        }

        private void HandleLinkRefreshStateChanged(object? sender, LinkRefreshStateChangedEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.DownloadId)) return;
            switch (e.State)
            {
                case LinkRefreshState.WaitingForReplacementSource:
                    telemetryRefreshSourceStates[e.DownloadId] = "WaitingForReplacementSource";
                    break;
                case LinkRefreshState.Refreshed:
                    telemetryRefreshSourceStates[e.DownloadId] = "Refreshed";
                    telemetrySourceGenerations[e.DownloadId] = telemetrySourceGenerations.TryGetValue(e.DownloadId, out var current) ? current + 1 : 1;
                    break;
                case LinkRefreshState.Stopped:
                    telemetryRefreshSourceStates[e.DownloadId] = "Stopped";
                    break;
            }

            if (liveDownloads.TryGetValue(e.DownloadId, out var live) && live.Key != null)
            {
                PublishTelemetry(live.Key, "SourceRefresh");
            }
        }

        public void StartBrowserMonitoring()
        {
            browserMonitoringService.Run();
        }

        public string? StartDownload(
            IRequestData downloadInfo,
            string fileName,
            FileNameFetchMode fileNameFetchMode,
            string? targetFolder,
            bool startImmediately,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            string? queueId,
            bool convertToMp3,
            int? speedLimitKiB = null)
        {
            var transferProxy = FollowsGlobalProxy(proxyInfo) ? null : proxyInfo;
            var rulePolicy = downloadRulePolicy.EvaluateCreation(downloadInfo, fileName, targetFolder, startImmediately, queueId);
            ApplyRulePolicy(rulePolicy, ref targetFolder, ref startImmediately, ref queueId);
            Log.Debug($"Starting download: {fileName} {fileNameFetchMode} {convertToMp3}");

            IBaseDownloader? http;

            switch (downloadInfo)
            {
                case SingleSourceHTTPDownloadInfo info:
                    http = new SingleSourceHTTPDownloader(info, authentication: authentication,
                        proxy: transferProxy, mediaProcessor: new FFmpegMediaProcessor(),
                        convertToMp3: convertToMp3);
                    RequestDataIO.SaveDownloadInfo(http.Id!, info, convertToMp3 || info.ConvertToMp3);
                    break;
                case DualSourceHTTPDownloadInfo info:
                    http = new DualSourceHTTPDownloader(info, authentication: authentication,
                        proxy: transferProxy, mediaProcessor: new FFmpegMediaProcessor());
                    RequestDataIO.SaveDownloadInfo(http.Id!, info);
                    break;
                case MultiSourceHLSDownloadInfo info:
                    http = new MultiSourceHLSDownloader(info, authentication: authentication,
                        proxy: transferProxy, mediaProcessor: new FFmpegMediaProcessor());
                    RequestDataIO.SaveDownloadInfo(http.Id!, info);
                    break;
                case MultiSourceDASHDownloadInfo info:
                    http = new MultiSourceDASHDownloader(info, authentication: authentication,
                        proxy: transferProxy, mediaProcessor: new FFmpegMediaProcessor());
                    RequestDataIO.SaveDownloadInfo(http.Id!, info);
                    break;
                default:
                    Log.Debug("Unknow request info :: skipping download");
                    return null;
            }

            http.ConfigureTransferPolicy(speedLimitKiB ?? rulePolicy.SpeedLimitKiB, rulePolicy.MaxConnections);

            if (!string.IsNullOrEmpty(queueId))
            {
                QueueManager.AddDownloadsToQueue(queueId!, new string[] { http.Id! });
            }
            http.SetFileName(FileHelper.SanitizeFileName(fileName), fileNameFetchMode);
            http.SetTargetDirectory(targetFolder);
            StartDownload(http, targetFolder, startImmediately, authentication, proxyInfo, rulePolicy.CategoryTags);
            return http.Id;
        }

        private static bool FollowsGlobalProxy(ProxyInfo? requested)
        {
            if (!requested.HasValue) return true;
            var global = Config.Instance.Proxy;
            if (!global.HasValue) return requested.Value.ProxyType == ProxyType.System;
            var a = requested.Value;
            var b = global.Value;
            if (a.ProxyType != b.ProxyType) return false;
            if (a.ProxyType != ProxyType.Custom) return true;
            return string.Equals(a.Host ?? string.Empty, b.Host ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && a.Port == b.Port
                && (a.UserName ?? string.Empty) == (b.UserName ?? string.Empty)
                && (a.Password ?? string.Empty) == (b.Password ?? string.Empty);
        }

        public int GetDownloadSpeedLimit(string id)
        {
            try
            {
                IBaseDownloader? live;
                lock (this)
                {
                    live = liveDownloads.GetValueOrDefault(id).Key;
                }
                if (live != null) return live.SpeedLimitSetting;
                var entry = AppDB.Instance.Downloads.GetDownloadById(id);
                return entry == null ? 0 : DownloadStateIO.ReadSpeedLimit(id, entry.DownloadType);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The speed limit of the download could not be read");
                return 0;
            }
        }

        public void SetDownloadSpeedLimit(string id, int setting)
        {
            try
            {
                IBaseDownloader? live;
                lock (this)
                {
                    live = liveDownloads.GetValueOrDefault(id).Key;
                }
                if (live != null)
                {
                    live.SetSpeedLimit(setting);
                    return;
                }
                var entry = AppDB.Instance.Downloads.GetDownloadById(id);
                if (entry != null) DownloadStateIO.WriteSpeedLimit(id, entry.DownloadType, setting);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The speed limit of the download could not be changed");
            }
        }

        private IRequestData? LoadRequest(DownloadItemBase entry, out bool convertToMp3)
        {
            convertToMp3 = false;
            switch (entry.DownloadType)
            {
                case "Http":
                    var info = RequestDataIO.LoadSingleSourceHTTPDownloadInfo(entry.Id);
                    convertToMp3 = info?.ConvertToMp3 ?? false;
                    return info;
                case "Dash":
                    return RequestDataIO.LoadDualSourceHTTPDownloadInfo(entry.Id);
                case "Hls":
                    return RequestDataIO.LoadMultiSourceHLSDownloadInfo(entry.Id);
                case "Mpd-Dash":
                    var dash = RequestDataIO.LoadMultiSourceDASHDownloadInfo(entry.Id);
                    if (dash == null) return null;
                    var segments = (dash.AudioSegments?.Count ?? 0) + (dash.VideoSegments?.Count ?? 0);
                    return segments > 0 ? dash : null;
                default:
                    return null;
            }
        }

        private static FileNameFetchMode SettledNameMode(DownloadItemBase entry, bool convertToMp3)
        {
            var settled = convertToMp3 || entry is FinishedDownloadItem || entry.Size > 0;
            return settled || !Enum.IsDefined(typeof(FileNameFetchMode), entry.FileNameFetchMode)
                ? FileNameFetchMode.None
                : entry.FileNameFetchMode;
        }

        private int? RuleConnectionLimit(DownloadItemBase entry)
        {
            try
            {
                var request = LoadRequest(entry, out _);
                if (request == null) return null;
                return downloadRulePolicy.ConnectionLimitFor(request, entry.Name);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The connection rule for the resumed download could not be evaluated");
                return null;
            }
        }

        private static void ApplyRulePolicy(DownloadCreationRuleOutcome policy, ref string? targetFolder, ref bool startImmediately, ref string? queueId)
        {
            targetFolder = policy.DestinationFolder;
            queueId = policy.QueueId;
            if (policy.StartImmediately.HasValue) startImmediately = policy.StartImmediately.Value;
        }

        private void StartDownload(IBaseDownloader download,
            string? targetDir,
            bool startImmediately,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            IReadOnlyList<string>? categoryTags)
        {
            if (!awakePingTimer.Enabled)
            {
                Log.Debug("Starting keep awaik timer");
                awakePingTimer.Start();
            }
            var id = download.Id;
            var startType = DownloadStartType.Waiting;

            if (!startImmediately)
            {
                startType = DownloadStartType.Stopped;
            }
            else
            {
                lock (this)
                {
                    if (liveDownloads.Count >= Config.Instance.MaxParallelDownloads)
                    {
                        startImmediately = false;
                        queuedDownloads[id] = false;
                    }
                    else
                    {
                        liveDownloads[download.Id] = new KeyValuePair<IBaseDownloader, bool>(download, false);
                    }
                }
            }

            runtimeContext.Application.AddItemToTop(id, download.TargetFileName, targetDir, DateTime.Now,
                download.FileSize, download.Type, download.FileNameFetchMode,
                download.PrimaryUrl?.ToString(), startType, authentication,
                proxyInfo, categoryTags);
            PublishTelemetry(download, startType == DownloadStartType.Stopped ? "Stopped" : "Waiting");

            if (startImmediately)
            {
                download.Started += HandleDownloadStart;
                download.Probed += HandleProbeResult;
                download.Finished += DownloadFinished;
                download.ProgressChanged += DownloadProgressChanged;
                download.AssembingProgressChanged += AssembleProgressChanged;
                download.Cancelled += DownloadCancelled;
                download.Failed += DownloadFailed;

                var showProgress = Config.Instance.ShowProgressWindow;
                if (showProgress)
                {
                    runtimeContext.Application.RunOnUiThread(() =>
                    {
                        var prgWin = CreateProgressWindow(download);
                        activeProgressWindows[download.Id] = prgWin;
                        prgWin.FileNameText = download.TargetFileName;
                        prgWin.FileSizeText = $"{TextResource.GetText("STAT_DOWNLOADING")} ...";
                        prgWin.UrlText = download.PrimaryUrl?.ToString() ?? string.Empty;
                        prgWin.ShowProgressWindow();
                    });
                }

                download.Start();
            }
            else
            {
                download.SaveForLater();
            }
        }

        public void AddBatchLinks(List<Message> messages)
        {
            var list = new List<IRequestData>(messages.Count);
            foreach (var message in messages)
            {
                var url = message.Url;
                if (string.IsNullOrEmpty(url)) continue;
                var file = FileHelper.SanitizeFileName(message.File ?? FileHelper.GetFileName(new Uri(message.Url)));
                var si = new SingleSourceHTTPDownloadInfo
                {
                    Uri = url,
                    File = file,
                    Headers = message?.RequestHeaders,
                    Cookies = message?.Cookies,
                    KeepFileName = message != null && message.HasSuppliedFileName
                };
                list.Add(si);
            }
            runtimeContext.Application.ShowDownloadSelectionWindow(FileNameFetchMode.FileNameAndExtension, list);
        }

        public void AddDownload(Message message)
        {
            if (runtimeContext.LinkRefresher.LinkAccepted(message)) return;

            if (Config.Instance.StartDownloadAutomatically)
            {
                var url = message.Url;
                var file = FileHelper.SanitizeFileName(message.File ?? FileHelper.GetFileName(new Uri(message.Url)));
                StartDownload(
                    new SingleSourceHTTPDownloadInfo
                    {
                        Uri = url,
                        File = file,
                        Headers = message?.RequestHeaders,
                        Cookies = message?.Cookies
                    },
                    file,
                    message != null && message.HasSuppliedFileName ? FileNameFetchMode.None : FileNameFetchMode.FileNameAndExtension,
                    null,
                    true,
                    null,
                    Config.Instance.Proxy, null, false);
            }
            else
            {
                Log.Debug("Adding download");
                runtimeContext.Application.ShowNewDownloadDialog(message);
            }
        }

        public void ResumeNonInteractiveDownloads(IEnumerable<string> idList)
        {
            foreach (var id in idList)
            {
                var entry = AppDB.Instance.Downloads.GetDownloadById(id);
                if (entry != null)
                {
                    ResumeDownload(new Dictionary<string, DownloadItemBase> { [id] = entry }, true);
                }
            }
        }

        public void ResumeDownload(Dictionary<string, DownloadItemBase> list,
            bool nonInteractive = false)
        {
            if (!awakePingTimer.Enabled)
            {
                Log.Debug("Starting keep awake timer");
                awakePingTimer.Start();
            }

            foreach (var item in list)
            {
                if (item.Value is InProgressDownloadItem statusEntry &&
                    (statusEntry.Status == DownloadStatus.Cancelling || statusEntry.Status == DownloadStatus.Cancelled))
                {
                    continue;
                }
                lock (this)
                {
                    if (liveDownloads.ContainsKey(item.Key) || queuedDownloads.ContainsKey(item.Key)) continue;
                }
                IBaseDownloader? download = null;
                switch (item.Value.DownloadType)
                {
                    case "Http":
                        download = new SingleSourceHTTPDownloader((string)item.Key,
                             mediaProcessor: new FFmpegMediaProcessor());
                        break;
                    case "Dash":
                        download = new DualSourceHTTPDownloader((string)item.Key,
                            mediaProcessor: new FFmpegMediaProcessor());
                        break;
                    case "Hls":
                        download = new MultiSourceHLSDownloader(item.Key,
                            mediaProcessor: new FFmpegMediaProcessor());
                        break;
                    case "Mpd-Dash":
                        download = new MultiSourceDASHDownloader(item.Key,
                            mediaProcessor: new FFmpegMediaProcessor());
                        break;
                    default:
                        continue;
                }
                bool waitInQueue;
                lock (this)
                {
                    if (liveDownloads.ContainsKey(item.Key) || queuedDownloads.ContainsKey(item.Key)) continue;
                    waitInQueue = liveDownloads.Count >= Config.Instance.MaxParallelDownloads;
                    if (waitInQueue) queuedDownloads[item.Key] = nonInteractive;
                    else liveDownloads[item.Key] = new KeyValuePair<IBaseDownloader, bool>(download, nonInteractive);
                }
                if (waitInQueue)
                {
                    runtimeContext.Application.RunOnUiThread(() =>
                    {
                        runtimeContext.Application.SetDownloadStatusWaiting(item.Key);
                        Log.Debug("Setting status waiting...");
                    });
                    continue;
                }
                download.Started += HandleDownloadStart;
                download.Probed += HandleProbeResult;
                download.Finished += DownloadFinished;
                download.ProgressChanged += DownloadProgressChanged;
                download.AssembingProgressChanged += AssembleProgressChanged;
                download.Cancelled += DownloadCancelled;
                download.Failed += DownloadFailed;
                download.SetTargetDirectory(item.Value.TargetDir);
                download.SetFileName(item.Value.Name, SettledNameMode(item.Value, false));
                download.UseCredentials(item.Value.Authentication);
                download.SetMaxConnections(RuleConnectionLimit(item.Value));
                var showProgressWindow = Config.Instance.ShowProgressWindow;
                if (showProgressWindow && !nonInteractive)
                {
                    var prgWin = GetProgressWindow(download);
                    runtimeContext.Application.RunOnUiThread(() =>
                    {
                        if (prgWin == null)
                        {
                            prgWin = CreateProgressWindow(download);
                            activeProgressWindows[download.Id] = prgWin;
                        }
                        prgWin.FileNameText = download.TargetFileName;
                        prgWin.FileSizeText = $"{TextResource.GetText("STAT_DOWNLOADING")} ...";
                        prgWin.DownloadStarted();
                        prgWin.ShowProgressWindow();
                    });
                }
                download.Resume();
            }
        }

        public void ShowProgressWindow(string downloadId)
        {
            try
            {

                if (!liveDownloads.ContainsKey(downloadId))
                {
                    return;
                }
                var downloader = liveDownloads[downloadId].Key;
                var prgWin = CreateOrGetProgressWindow(downloader);
                prgWin.FileNameText = downloader.TargetFileName;
                prgWin.FileSizeText = $"{TextResource.GetText("STAT_DOWNLOADING")} ...";
                prgWin.ShowProgressWindow();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error showing progress window");
            }
        }

        public void PauseDownloads(IEnumerable<string> list, bool closeProgressWindow = false)
        {
            var remaining = new List<string>();
            foreach (var id in new List<string>(list))
            {
                IBaseDownloader? http;
                lock (this)
                {
                    http = liveDownloads.GetValueOrDefault(id).Key;
                }
                if (http is MultiSourceHLSDownloader recording && (recording.FinishLiveCapture() || recording.IsFinishingLiveCapture))
                {
                    if (closeProgressWindow) HideProgressWindow(id);
                    continue;
                }
                remaining.Add(id);
            }
            StopDownloads(remaining, closeProgressWindow);
        }

        public void StopDownloads(IEnumerable<string> list, bool closeProgressWindow = false)
        {
            var ids = new List<string>(list);
            foreach (var id in ids)
            {
                stopIntents[id] = DownloadStopIntent.Pause;
                IBaseDownloader? http;
                bool wasQueued;
                lock (this)
                {
                    http = liveDownloads.GetValueOrDefault(id).Key;
                    wasQueued = http == null && queuedDownloads.Remove(id);
                }
                if (http != null)
                {
                    http.Stop();
                }
                else if (wasQueued)
                {
                    stopIntents.Remove(id);
                    runtimeContext.Application.DownloadCanelled(id);
                    if (activeProgressWindows.ContainsKey(id))
                    {
                        activeProgressWindows[id].DownloadCancelled();
                    }
                }
                else
                {
                    stopIntents.Remove(id);
                }

                if (activeProgressWindows.ContainsKey(id) && closeProgressWindow)
                {
                    var prgWin = activeProgressWindows[id];
                    activeProgressWindows.Remove(id);
                    prgWin.DestroyWindow();
                    Log.Debug("Progress window removed");
                }
            }
        }

        public bool CancelDownload(string id, DownloadCancellationPolicy policy = DownloadCancellationPolicy.RetainPartial)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            var entry = AppDB.Instance.Downloads.GetDownloadById(id) as InProgressDownloadItem;
            if (entry == null) return false;
            if (entry.Status == DownloadStatus.Cancelled || entry.Status == DownloadStatus.Cancelling) return true;

            runtimeContext.Application.SetDownloadStatus(id, DownloadStatus.Cancelling);
            stopIntents[id] = policy == DownloadCancellationPolicy.DiscardPartial
                ? DownloadStopIntent.CancelDiscardPartial
                : DownloadStopIntent.CancelRetainPartial;

            IBaseDownloader? http;
            lock (this)
            {
                http = liveDownloads.GetValueOrDefault(id).Key;
                if (http == null) queuedDownloads.Remove(id);
            }
            if (http != null)
            {
                http.Stop();
                return true;
            }

            FinalizeCancellation(id, entry, policy);
            stopIntents.Remove(id);
            return true;
        }

        private void FinalizeCancellation(string id, InProgressDownloadItem entry, DownloadCancellationPolicy policy)
        {
            runtimeContext.Application.SetDownloadStatus(id, DownloadStatus.Cancelled);
            if (policy == DownloadCancellationPolicy.DiscardPartial)
            {
                RemoveDownload(entry, false, false);
            }
        }

        void DownloadProgressChanged(object source, ProgressResultEventArgs args)
        {
            lock (this)
            {
                var http = source as IBaseDownloader;
                runtimeContext.Application.UpdateProgress(http.Id, args.Progress, args.DownloadSpeed, args.Eta);
                PublishTelemetry(http, "Downloading", args);
                if (activeProgressWindows.ContainsKey(http.Id))
                {
                    var prgWin = activeProgressWindows[http.Id];
                    prgWin.DownloadProgress = args.Progress;
                    prgWin.FileSizeText = $"{TextResource.GetText("STAT_DOWNLOADING")} {FormattingHelper.FormatSize(args.Downloaded)} / {FormattingHelper.FormatSize(http.FileSize)}";
                    prgWin.DownloadSpeedText = FormattingHelper.FormatSize((long)args.DownloadSpeed) + "/s";
                    prgWin.DownloadETAText = $"{TextResource.GetText("MSG_TIME_LEFT")}: {FormattingHelper.ToHMS(args.Eta)}";
                }
            }
        }

        void AssembleProgressChanged(object source, ProgressResultEventArgs args)
        {
            lock (this)
            {
                var http = source as IBaseDownloader;
                PublishTelemetry(http, "Assembling", args);
                if (activeProgressWindows.ContainsKey(http.Id))
                {
                    var prgWin = activeProgressWindows[http.Id];
                    prgWin.DownloadProgress = args.Progress;
                    prgWin.FileSizeText = $"{TextResource.GetText("STAT_ASSEMBLING")} {FormattingHelper.FormatSize(args.Downloaded)} / {FormattingHelper.FormatSize(http.FileSize)}";
                    prgWin.DownloadSpeedText = "---";
                    prgWin.DownloadETAText = "---";
                }
            }
        }

        void DownloadFinished(object source, EventArgs args)
        {
            lock (this)
            {
                var http = source as IBaseDownloader;
                PublishTelemetry(http, "Finished");
                DetachEventHandlers(http);
                RemoveStateFiles(http.Id, false);
                DownloadOriginMarker.TryMark(http.TargetFile);
                runtimeContext.Application.DownloadFinished(http.Id, http.FileSize < 0 ? new FileInfo(http.TargetFile).Length : http.FileSize, http.TargetFile);

                var showCompleteDialog = false;
                if (liveDownloads.ContainsKey(http.Id))
                {
                    var nonInteractive = liveDownloads[http.Id].Value;
                    liveDownloads.Remove(http.Id);

                    if (!nonInteractive && Config.Instance.ShowDownloadCompleteWindow)
                    {
                        showCompleteDialog = true;
                    }
                }

                if (activeProgressWindows.ContainsKey(http.Id))
                {
                    var prgWin = activeProgressWindows[http.Id];
                    activeProgressWindows.Remove(http.Id);
                    prgWin.DownloadId = null;
                    prgWin.DestroyWindow();
                }

                if (showCompleteDialog)
                {
                    runtimeContext.Application.ShowDownloadCompleteDialog(http.TargetFileName, Path.GetDirectoryName(http.TargetFile));
                }

                if (Config.Instance.ScanWithAntiVirus)
                {
                    PlatformHelper.RunAntivirus(Config.Instance.AntiVirusExecutable, Config.Instance.AntiVirusArgs, http.TargetFile);
                }

                Helpers.RunGC();
                ProcessNextQueuedItem(true);
            }
        }

        void DownloadFailed(object source, DownloadFailedEventArgs args)
        {
            lock (this)
            {
                Log.Debug("Download failed: " + args.ErrorCode);
                var http = source as IBaseDownloader;
                PublishTelemetry(http, "Failed", lastError: args.ErrorCode.ToString());
                DetachEventHandlers(http);
                liveDownloads.Remove(http.Id);
                runtimeContext.Application.DownloadFailed(http.Id);
                if (activeProgressWindows.ContainsKey(http.Id))
                {
                    var prgWin = activeProgressWindows[http.Id];
                    prgWin.DownloadFailed(new ErrorDetails { Message = ErrorMessages.GetLocalizedErrorMessage(args.ErrorCode) });
                }

                Helpers.RunGC();
                ProcessNextQueuedItem(false);
            }
        }

        void DownloadCancelled(object source, EventArgs args)
        {
            lock (this)
            {
                var http = source as IBaseDownloader;
                var id = http.Id;
                stopIntents.TryGetValue(id, out var intent);
                stopIntents.Remove(id);

                if (intent == DownloadStopIntent.CancelRetainPartial || intent == DownloadStopIntent.CancelDiscardPartial)
                {
                    Log.Debug("Download cancelled by user intent");
                    PublishTelemetry(http, "Cancelled");
                }
                else
                {
                    Log.Debug("Download paused");
                    PublishTelemetry(http, "Paused");
                }

                DetachEventHandlers(http);
                liveDownloads.Remove(id);

                if (intent == DownloadStopIntent.CancelRetainPartial || intent == DownloadStopIntent.CancelDiscardPartial)
                {
                    var entry = AppDB.Instance.Downloads.GetDownloadById(id) as InProgressDownloadItem;
                    if (entry != null)
                    {
                        FinalizeCancellation(id, entry, intent == DownloadStopIntent.CancelDiscardPartial
                            ? DownloadCancellationPolicy.DiscardPartial
                            : DownloadCancellationPolicy.RetainPartial);
                    }
                }
                else
                {
                    runtimeContext.Application.DownloadCanelled(id);
                }

                if (activeProgressWindows.ContainsKey(id))
                {
                    activeProgressWindows[id].DownloadCancelled();
                }

                Helpers.RunGC();
                ProcessNextQueuedItem(false);
            }
        }

        void HandleProbeResult(object source, EventArgs args)
        {
            lock (this)
            {
                var http = source as IBaseDownloader;
                PublishTelemetry(http, "Probed");
                runtimeContext.Application.UpdateItem(http.Id, http.TargetFileName, http.FileSize > 0 ? http.FileSize : 0);
                if (activeProgressWindows.ContainsKey(http.Id))
                {
                    var prgWin = activeProgressWindows[http.Id];
                    prgWin.FileNameText = http.TargetFileName;
                    prgWin.FileSizeText =
                        $"{TextResource.GetText("STAT_DOWNLOADING")} {FormattingHelper.FormatSize(0)} / {FormattingHelper.FormatSize(http.FileSize)}";
                }
            }
        }

        void HandleDownloadStart(object source, EventArgs args)
        {
            lock (this)
            {
                var http = source as IBaseDownloader;
                PublishTelemetry(http, "Downloading");
                runtimeContext.Application.DownloadStarted(http.Id);
            }
        }

        private void PublishTelemetry(IBaseDownloader download, string lifecycleState, ProgressResultEventArgs? progress = null, string lastError = "")
        {
            try
            {
                PublishTelemetrySnapshot(download, lifecycleState, progress, lastError);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The live details of a download could not be updated");
            }
        }

        private void PublishTelemetrySnapshot(IBaseDownloader download, string lifecycleState, ProgressResultEventArgs? progress, string lastError)
        {
            if (download == null || string.IsNullOrWhiteSpace(download.Id)) return;
            var id = download.Id;
            var currentSpeed = progress?.DownloadSpeed ?? 0;
            var downloaded = progress?.Downloaded ?? SafeGetDownloaded(download);
            var total = download.FileSize > 0 ? download.FileSize : (long?)null;
            var percent = progress?.Progress ?? (total.HasValue && total.Value > 0 ? Math.Min(100.0, downloaded * 100.0 / total.Value) : 0);
            var transfer = download is IDownloadTelemetrySource telemetrySource
                ? telemetrySource.CaptureTransferTelemetry()
                : new DownloadTransferTelemetryProjection(DownloadResumeCapability.Unknown, 0, Array.Empty<DownloadRangeTelemetry>());
            var snapshot = new DownloadTelemetrySnapshot(
                id,
                download.TargetFileName ?? string.Empty,
                download.PrimaryUrl?.Host ?? string.Empty,
                lifecycleState,
                percent,
                downloaded,
                total,
                currentSpeed,
                GetSmoothedTelemetrySpeed(id, currentSpeed),
                progress == null ? null : (double?)progress.Eta,
                transfer.ResumeCapability,
                transfer.ActiveConnections,
                transfer.Ranges,
                CreateSafeTelemetryUrl(download.PrimaryUrl),
                lifecycleState == "Failed" ? "Available" : (lifecycleState == "Cancelled" ? "Unavailable" : string.Empty),
                telemetryRefreshSourceStates.TryGetValue(id, out var refreshState) ? refreshState : string.Empty,
                lastError,
                AppDB.Instance.RecoveryStatuses.FirstOrDefault(status => string.Equals(status.DownloadId, id, StringComparison.Ordinal))?.Code ?? string.Empty,
                Array.Empty<DownloadSpeedSample>(),
                DateTime.UtcNow,
                sourceGeneration: telemetrySourceGenerations.TryGetValue(id, out var sourceGeneration) ? sourceGeneration : 0,
                revision: NextTelemetryRevision(id));
            telemetrySink.Publish(snapshot);
        }

        private static long SafeGetDownloaded(IBaseDownloader download)
        {
            try
            {
                return Math.Max(0, download.GetTotalDownloaded());
            }
            catch
            {
                return 0;
            }
        }

        private long NextTelemetryRevision(string downloadId)
        {
            var revision = telemetryRevisions.TryGetValue(downloadId, out var current) ? current + 1 : 1;
            telemetryRevisions[downloadId] = revision;
            return revision;
        }

        private double GetSmoothedTelemetrySpeed(string downloadId, double currentSpeed)
        {
            if (currentSpeed <= 0)
            {
                return telemetrySmoothedSpeeds.TryGetValue(downloadId, out var existing) ? existing : 0;
            }
            var smoothed = telemetrySmoothedSpeeds.TryGetValue(downloadId, out var previous)
                ? previous * 0.8 + currentSpeed * 0.2
                : currentSpeed;
            telemetrySmoothedSpeeds[downloadId] = smoothed;
            return smoothed;
        }

        private static string CreateSafeTelemetryUrl(Uri? uri)
        {
            if (uri == null) return string.Empty;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return uri.Scheme + "://" + uri.Authority + uri.AbsolutePath;
        }

        private IProgressWindow? GetProgressWindow(IBaseDownloader downloader)
        {
            IProgressWindow? prgWin = null;
#pragma warning disable CS8604
            if (activeProgressWindows.ContainsKey(downloader.Id))
#pragma warning restore CS8604
            {
                prgWin = activeProgressWindows[downloader.Id];
            }
            return prgWin;
        }

        private IProgressWindow CreateOrGetProgressWindow(IBaseDownloader downloader)
        {
            IProgressWindow prgWin = null;
            if (activeProgressWindows.ContainsKey(downloader.Id))
            {
                prgWin = activeProgressWindows[downloader.Id];
            }
            else
            {
                prgWin = CreateProgressWindow(downloader);
                activeProgressWindows[downloader.Id] = prgWin;
            }
            return prgWin;
        }

        private IProgressWindow CreateProgressWindow(IBaseDownloader downloader)
        {
            var prgWin = runtimeContext.Application.CreateProgressWindow(downloader.Id);
            prgWin.UrlText = runtimeContext.Application.GetInProgressDownloadEntry(downloader.Id)?.PrimaryUrl;
            prgWin.DownloadSpeedText = "---";
            prgWin.DownloadETAText = "---";
            prgWin.FileSizeText = "---";
            return prgWin;
        }

        public bool IsDownloadActive(string id)
        {
            return liveDownloads.ContainsKey(id) || queuedDownloads.ContainsKey(id);
        }

        private void ProcessNextQueuedItem(bool finished)
        {
            if (queuedDownloads.Count > 0)
            {
                var kv = queuedDownloads.First();
                queuedDownloads.Remove(kv.Key);
                var entry = AppDB.Instance.Downloads.GetDownloadById(kv.Key);
                if (entry != null)
                {
                    ResumeDownload(new Dictionary<string, DownloadItemBase> { [kv.Key] = entry }, kv.Value);
                }
                return;
            }
            if (liveDownloads.Count > 0) return;
            if (awakePingTimer.Enabled)
            {
                Log.Debug("Stopping keep awake timer");
                awakePingTimer.Stop();
            }
            if (!finished) return;
            if (Config.Instance.RunCommandAfterCompletion)
            {
                try
                {
                    PlatformHelper.RunCommand(Config.Instance.AfterCompletionCommand);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "The program set to run after downloads finish could not be started");
                }
            }
            if (Config.Instance.ShutdownAfterAllFinished)
            {
                try
                {
                    PlatformHelper.ShutDownPC();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "The shutdown after downloads finish could not be started");
                }
            }
        }

        public void StartScheduler()
        {
            this.scheduler = new Scheduler(runtimeContext);
            this.scheduler.Start();
        }

        public void RenameDownload(string id, string folder, string file)
        {
            if (liveDownloads.ContainsKey(id))
            {
                var downloader = liveDownloads[id].Key;
                downloader.SetTargetDirectory(folder);
                downloader.SetFileName(file, downloader.FileNameFetchMode);
            }
            runtimeContext.Application.RenameFileOnUI(id, folder, file);
        }

        private void DetachEventHandlers(IBaseDownloader download)
        {
            try
            {
                download.Started -= HandleDownloadStart;
                download.Probed -= HandleProbeResult;
                download.Finished -= DownloadFinished;
                download.ProgressChanged -= DownloadProgressChanged;
                download.AssembingProgressChanged -= AssembleProgressChanged;
                download.Cancelled -= DownloadCancelled;
                download.Failed -= DownloadFailed;
            }
            catch { }
        }

        public AuthenticationInfo? PromptForCredential(string id, string message)
        {
            try
            {
                if (liveDownloads[id].Value)
                {
                    return null;
                }
                return runtimeContext.Application.PromtForCredentials(message);
            }
            catch { }
            return null;
        }

        public void HideProgressWindow(string id)
        {
            if (activeProgressWindows.ContainsKey(id))
            {
                var prgWin = activeProgressWindows[id];
                activeProgressWindows.Remove(id);
                prgWin.DestroyWindow();
            }
        }

        public string? GetPrimaryUrl(DownloadItemBase entry)
        {
            if (entry == null) return null;
            switch (entry.DownloadType)
            {
                case "Http":
                    var h1 = RequestDataIO.LoadSingleSourceHTTPDownloadInfo(entry.Id);
                    if (h1 != null)
                    {
                        return h1.Uri;
                    }
                    break;
                case "Dash":
                    var h2 = RequestDataIO.LoadDualSourceHTTPDownloadInfo(entry.Id);
                    if (h2 != null)
                    {
                        return h2.Uri1;
                    }
                    break;
                case "Hls":
                    var hls = RequestDataIO.LoadMultiSourceHLSDownloadInfo(entry.Id);
                    if (hls != null)
                    {
                        return hls.VideoUri;
                    }
                    break;
                case "Mpd-Dash":
                    var dash = RequestDataIO.LoadMultiSourceDASHDownloadInfo(entry.Id);
                    if (dash != null)
                    {
                        return dash.Url;
                    }
                    break;
            }

            return null;
        }

        private List<string> GetStateFiles(string id, bool deleteInfo)
        {
            var files = new List<string>();
            if (deleteInfo)
            {
                files.Add(Path.Combine(Config.DataDir, id + ".info"));
            }
            files.Add(Path.Combine(Config.DataDir, id + ".state"));
            files.Add(Path.Combine(Config.DataDir, id + ".state.1"));
            files.Add(Path.Combine(Config.DataDir, id + ".state.2"));
            files.AddRange(Directory.GetFiles(Config.DataDir, id + ".state.3.*"));
            return files;
        }

        private void RemoveStateFiles(string id, bool deleteInfo)
        {
            var stateFiles = GetStateFiles(id, deleteInfo);

            foreach (var file in stateFiles)
            {
                if (File.Exists(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, ex.Message);
                    }
                }
            }
        }

        public void RemoveDownload(DownloadItemBase entry, bool deleteDownloadedFile, bool removeInfo = true)
        {
            try
            {
                if (entry == null) return;
                string? tempDir = null;
                try
                {
                    switch (entry.DownloadType)
                    {
                        case "Http":
                            tempDir = DownloadStateIO.LoadSingleSourceHTTPDownloaderState(entry.Id)?.TempDir;
                            break;
                        case "Dash":
                            tempDir = DownloadStateIO.LoadDualSourceHTTPDownloaderState(entry.Id)?.TempDir;
                            break;
                        case "Hls":
                            tempDir = DownloadStateIO.LoadMultiSourceHLSDownloadState(entry.Id)?.TempDirectory;
                            break;
                        case "Mpd-Dash":
                            tempDir = DownloadStateIO.LoadMultiSourceDASHDownloadState(entry.Id)?.TempDirectory;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "No saved state was found for the removed download");
                }

                RemoveStateFiles(entry.Id, removeInfo);

                if (entry is FinishedDownloadItem && deleteDownloadedFile)
                {
                    try
                    {
                        var file = Path.Combine(entry.TargetDir, entry.Name);
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "The downloaded file could not be deleted");
                    }
                }

                try
                {
                    if ((tempDir == null || tempDir.Length == 0) && entry.Id != null && entry.Id.Length > 0)
                    {
                        tempDir = Path.Combine(Config.Instance.TempDir, entry.Id);
                    }
                    if (entry is InProgressDownloadItem)
                    {
                        OutputFileStaging.ReleaseReservation(tempDir, entry.TargetDir);
                        OutputFileStaging.DiscardLeftovers(entry.TargetDir, entry.Id);
                    }
                    OutputFileStaging.DeleteFolder(tempDir);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
        }

        public bool RestartDownload(DownloadItemBase entry)
        {
            if (entry == null) return false;
            if (entry is InProgressDownloadItem && IsDownloadActive(entry.Id)) return false;
            try
            {
                var request = LoadRequest(entry, out var convertToMp3);
                if (request == null) return false;
                var previousLimit = 0;
                var proxy = entry.Proxy;
                if (entry is InProgressDownloadItem)
                {
                    previousLimit = GetDownloadSpeedLimit(entry.Id);
                    try
                    {
                        proxy = DownloadStateIO.ReadProxy(entry.Id, entry.DownloadType) ?? Config.Instance.Proxy;
                        if (!convertToMp3 && entry.DownloadType == "Http")
                        {
                            convertToMp3 = DownloadStateIO.LoadSingleSourceHTTPDownloaderState(entry.Id).ConvertToMp3;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "The saved transfer settings of the download could not be read");
                    }
                }
                var created = this.StartDownload(request, entry.Name,
                               SettledNameMode(entry, convertToMp3),
                               entry.TargetDir, true, entry.Authentication, proxy, null,
                               convertToMp3, previousLimit == 0 ? (int?)null : previousLimit);
                if (created == null || created.Length == 0) return false;
                RemoveDownload(entry, false, entry is InProgressDownloadItem);
                if (entry is InProgressDownloadItem)
                {
                    var previousId = entry.Id;
                    AppDB.Instance.Downloads.RemoveDownloadById(previousId);
                    runtimeContext.Application.RunOnUiThread(() =>
                    {
                        var row = runtimeContext.MainWindow.FindInProgressItem(previousId);
                        if (row != null) runtimeContext.MainWindow.Delete(row);
                    });
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error restarting download");
                return false;
            }
        }

        public void Export(string path)
        {
            if (ImportExport.Export(path)) return;
            runtimeContext.Application.ShowMessageBox(null, TextResource.GetText("MSG_EXPORT_FAILED"));
        }

        public void Import(string path)
        {
            var imported = ImportExport.Import(path);
            runtimeContext.Application.ShowMessageBox(null, TextResource.GetText(imported ? "MSG_IMPORT_DONE" : "MSG_IMPORT_FAILED"));
        }
    }
}
