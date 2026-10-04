using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TraceLog;
using Translations;
using ADM.Core;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.UI;
using ADM.Core.Util;
using YDLWrapper;

namespace ADM.Core.UI
{
    public class VideoDownloaderUIController
    {
        private YDLProcess? ydl;
        private List<YDLVideoEntry> videoItemList;
        private List<int> videoQualities;
        private IVideoDownloadView view;
        private readonly IApplication application;
        private readonly IApplicationCore core;
        private readonly Legacy.IDownloadCreationPreferences downloadCreationPreferences;

        public VideoDownloaderUIController(IVideoDownloadView view, IApplication application, IApplicationCore core, Legacy.IDownloadCreationPreferences downloadCreationPreferences)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.application = application ?? throw new ArgumentNullException(nameof(application));
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            this.downloadCreationPreferences = downloadCreationPreferences ?? throw new ArgumentNullException(nameof(downloadCreationPreferences));

            var browsers = new Dictionary<string, string>
            {
                ["Google Chrome"] = "chrome",
                ["Microsoft Edge"] = "edge",
                ["Mozilla Firefox"] = "firefox",
                ["Brave"] = "brave",
                ["Opera"] = "opera",
                ["Chromium"] = "chromium",
                ["Safari"] = "safari",
                ["Vivaldi"] = "vivaldi"
            };

            this.view.AllowedBrowsers = browsers.Keys.ToList();

            view.SearchClicked += (_, _) =>
            {
                var url = view.Url;
                string? browser = null;
                if (!string.IsNullOrEmpty(view.SelectedBrowser))
                {
                    browsers.TryGetValue(view.SelectedBrowser!, out browser);
                }
                if (Helpers.IsUriValid(url))
                {
                    view.SwitchToProcessingPage();
                    ProcessVideo(url, browser, result => application.RunOnUiThread(() =>
                    {
                        if (result != null)
                        {
                            view.SwitchToFinalPage();
                            SetVideoResultList(result);
                        }
                        else
                        {
                            view.SwitchToErrorPage();
                        }
                    }));
                }
                else
                {
                    application.ShowMessageBox(view, TextResource.GetText("MSG_INVALID_URL"));
                }
            };

            view.CancelClicked += (_, _) =>
            {
                CancelOperation();
                view.SwitchToInitialPage();
            };

            view.WindowClosed += (_, _) =>
            {
                CancelOperation();
            };

            view.BrowseClicked += (_, _) =>
            {
                var folder = view.SelectFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    view.DownloadLocation = folder;
                    downloadCreationPreferences.RememberSelectedFolder(folder);
                }
            };

            view.DownloadClicked += View_DownloadClicked;
            view.DownloadLaterClicked += View_DownloadLaterClicked;
            view.QueueSchedulerClicked += (s, e) =>
            {
                application.ShowQueueWindow(s);
            };
        }

        private void View_DownloadLaterClicked(object? sender, DownloadLaterEventArgs e)
        {
            DownloadSelectedItems(false, e.QueueId);
        }

        private void View_DownloadClicked(object? sender, EventArgs e)
        {
            DownloadSelectedItems(true, null);
        }

        public void Run()
        {
            var url = application.GetUrlFromClipboard();
            if (url != null && Helpers.IsUriValid(url))
            {
                view.Url = url;
            }
            view.DownloadLocation = Helpers.GetVideoDownloadFolder();
            view.ShowWindow();
        }

        private void SetVideoResultList(List<YDLVideoEntry> items)
        {
            if (items == null) return;

            this.videoItemList = items;

            var formatSet = new HashSet<int>();
            foreach (var item in items)
            {
                if (item.Formats != null)
                {
                    item.Formats.ForEach(item =>
                    {
                        if (!string.IsNullOrEmpty(item.Height))
                        {
                            if (Int32.TryParse(item.Height, out int height))
                            {
                                formatSet.Add(height);
                            }
                        }
                    });
                }
            }
            var formatsList = new List<int>(formatSet);
            formatsList.Sort();
            formatsList.Reverse();
            this.videoQualities = formatsList;

            var videoList = this.videoItemList.Select(x => x.Title);
            var formatList = this.videoQualities.Select(n => $"{n}p");

            view.SetVideoResultList(videoList, formatList);

            if (formatsList.Count > 0)
            {
                view.SelectedFormat = 0;
            }
        }

        private void CancelOperation()
        {
            try
            {
                if (ydl != null)
                {
                    ydl.Cancel();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error cancelling ydl");
            }
        }

        private void ProcessVideo(string url, string? browser, Action<List<YDLVideoEntry>?> callback)
        {
            var operation = new YDLProcess
            {
                Uri = new Uri(url),
                BrowserName = browser
            };
            ydl = operation;
            var worker = new Thread(() =>
            {
                try
                {
                    operation.Start();
                    callback.Invoke(YDLOutputParser.Parse(operation.JsonOutputFile));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error while running youtube-dl");
                    callback.Invoke(null);
                }
                finally
                {
                    TryDeleteAnalyzerOutput(operation.JsonOutputFile);
                    if (ReferenceEquals(ydl, operation)) ydl = null;
                }
            })
            {
                IsBackground = true,
                Name = "ADM video metadata analysis"
            };
            worker.Start();
        }

        private static void TryDeleteAnalyzerOutput(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Unable to remove temporary analyzer metadata");
            }
        }

        private void DownloadSelectedItems(bool startImmediately, string? queueId)
        {
            if (string.IsNullOrEmpty(view.DownloadLocation))
            {
                application!.ShowMessageBox(view, TextResource.GetText("MSG_CAT_FOLDER_MISSING"));
                return;
            }
            if (this.view.SelectedItemCount == 0)
            {
                application!.ShowMessageBox(view, TextResource.GetText("BAT_SELECT_ITEMS"));
                return;
            }
            var quality = -1;
            if (view.SelectedFormat >= 0)
            {
                quality = this.videoQualities[view.SelectedFormat];
            }

            var selectedIndices = view.SelectedRows;
            foreach (var index in selectedIndices)
            {
                var entry = videoItemList[index];
                var fmt = FindMatchingFormatByQuality(entry, quality);
                if (fmt.HasValue)
                {
                    AddDownload(fmt.Value, startImmediately, queueId);
                }
            }
            view.CloseWindow();
        }

        private YDLVideoFormatEntry? FindMatchingFormatByQuality(YDLVideoEntry videoEntry, int quality = -1)
        {
            if (videoEntry.Formats.Count == 0) return null;
            if (quality == -1)
            {
                return videoEntry.Formats[0];
            }
            var fmt = FindOnlyMatchingMp4(videoEntry, quality);
            if (fmt != null)
            {
                return fmt;
            }
            foreach (var format in videoEntry.Formats)
            {
                if (!string.IsNullOrEmpty(format.Height) &&
                    Int32.TryParse(format.Height, out int height) &&
                    height == quality)
                {
                    return format;
                }
            }
            var max = -1;
            foreach (var format in videoEntry.Formats)
            {
                if (!string.IsNullOrEmpty(format.Height) &&
                    Int32.TryParse(format.Height, out int height) &&
                    height > 0 &&
                    quality > height)
                {
                    if (height > max)
                    {
                        max = height;
                        fmt = format;
                    }
                }
            }
            if (fmt != null)
            {
                return fmt;
            }
            return videoEntry.Formats[0];
        }

        private YDLVideoFormatEntry? FindOnlyMatchingMp4(YDLVideoEntry videoEntry, int quality)
        {
            if (videoEntry.Formats.Count == 0) return null;
            foreach (var format in videoEntry.Formats)
            {
                if (!string.IsNullOrEmpty(format.Height) &&
                    Int32.TryParse(format.Height, out int height) &&
                    height == quality &&
                    (format.FileExt?.ToLowerInvariant()?.EndsWith("mp4") ?? false))
                {
                    return format;
                }
            }
            return null;
        }

        private void AddDownload(YDLVideoFormatEntry videoEntry, bool startImmediately, string? queueId)
        {
            IRequestData? info = videoEntry.YDLEntryType switch
            {
                YDLEntryType.Http => new SingleSourceHTTPDownloadInfo
                {
                    Uri = videoEntry.VideoUrl
                },
                YDLEntryType.Dash => new DualSourceHTTPDownloadInfo
                {
                    Uri1 = videoEntry.VideoUrl,
                    Uri2 = videoEntry.AudioUrl
                },
                YDLEntryType.Hls => new MultiSourceHLSDownloadInfo
                {
                    VideoUri = videoEntry.VideoUrl,
                    AudioUri = videoEntry.AudioUrl
                },
                YDLEntryType.MpegDash => new MultiSourceDASHDownloadInfo
                {
                    VideoSegments = videoEntry.VideoFragments?.Select(x => new Uri(new Uri(videoEntry.FragmentBaseUrl), x.Path)).ToList(),
                    AudioSegments = videoEntry.AudioFragments?.Select(x => new Uri(new Uri(videoEntry.FragmentBaseUrl), x.Path)).ToList(),
                    AudioFormat = videoEntry.AudioFormat != null ? "." + videoEntry.AudioFormat : null,
                    VideoFormat = videoEntry.VideoFormat != null ? "." + videoEntry.VideoFormat : null,
                    Url = videoEntry.VideoUrl
                },
            };
            if (info != null)
            {
                core!.StartDownload(
                        info,
                        videoEntry.Title + "." + videoEntry.FileExt,
                        FileNameFetchMode.None,
                        view.DownloadLocation,
                        startImmediately,
                        view.Authentication,
                        view.Proxy ?? downloadCreationPreferences.DefaultProxy,
                        queueId,
                        false,
                        NewDownloadDialogUIController.SpeedLimitChoice(view.EnableSpeedLimit, view.SpeedLimit, downloadCreationPreferences)
                    );
            }
        }
    }
}
