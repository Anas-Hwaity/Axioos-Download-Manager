using System;
using System.Collections.Generic;
using TraceLog;
using Translations;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.Legacy;
using ADM.Core.Util;

namespace ADM.Core.UI
{
    public class NewDownloadDialogUIController
    {
        public static void CreateAndShowDialog(
            INewDownloadDialog window,
            IApplication application,
            IApplicationCore core,
            IDownloadCreationPreferences preferences,
            Message? message = null,
            Action? destroyCallback = null)
        {
            var outcome = message?.CreationOutcome;
            var outcomeReported = false;
            void ReportOutcome(string? downloadId)
            {
                if (outcomeReported) return;
                outcomeReported = true;
                if (outcome == null) return;
                try
                {
                    outcome(downloadId);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "New download outcome could not be reported");
                }
            }
            window.DestroyEvent += (_, _) =>
            {
                destroyCallback?.Invoke();
                ReportOutcome(null);
            };
            window.SetFolderValues(preferences.GetFolderValues());
            window.SeletedFolderIndex = preferences.GetInitialFolderIndex();

            var fileName = string.Empty;
            if (message != null)
            {
                window.Url = message.Url;
                fileName = FileHelper.SanitizeFileName(message.File ?? FileHelper.GetFileName(new Uri(message.Url)));
                window.SelectedFileName = fileName;
                var contentLength = 0L;
                var header = message.GetResponseHeaderFirstValue("Content-Length");
                if (!string.IsNullOrEmpty(header))
                {
                    long.TryParse(header, out contentLength);
                }
                window.SetFileSizeText(contentLength > 0 ? FormattingHelper.FormatSize(contentLength) : "---");
            }
            else
            {
                var url = application.GetUrlFromClipboard();
                if (!string.IsNullOrEmpty(url))
                {
                    window.Url = url;
                    window.SelectedFileName = FileHelper.SanitizeFileName(FileHelper.GetFileName(new Uri(url)));
                }
                window.UrlChangedEvent += (_, _) =>
                {
                    if (Helpers.IsUriValid(window.Url))
                    {
                        window.SelectedFileName = FileHelper.SanitizeFileName(FileHelper.GetFileName(new Uri(window.Url)));
                        fileName = window.SelectedFileName;
                    }
                };
            }

            window.FileBrowsedEvent += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.SelectedFile)) return;
                preferences.RecordFileBrowsed(args.SelectedFile);
                if (sender is IFileSelectable selectable)
                {
                    selectable.SetFolderValues(preferences.GetFolderValues());
                    selectable.SeletedFolderIndex = 2;
                }
            };
            window.DropdownSelectionChangedEvent += (sender, args) =>
            {
                if (sender is IFileSelectable selectable)
                {
                    preferences.UpdateFolderSelection(selectable.SeletedFolderIndex, args.SelectedFile);
                }
            };
            window.UrlBlockedEvent += (_, _) =>
            {
                if (!Helpers.IsUriValid(window.Url)) return;
                preferences.BlockHost(new Uri(window.Url).Host);
                window.DisposeWindow();
            };
            window.DownloadClicked += (_, _) => OnDownloadClicked(window, core, preferences, fileName, preferences.ResolveSelectedFolder(window.SeletedFolderIndex), message, true, ReportOutcome);
            window.DownloadLaterClicked += (_, e) => OnDownloadClicked(window, core, preferences, fileName, preferences.ResolveSelectedFolder(window.SeletedFolderIndex), message, false, ReportOutcome, e.QueueId);
            window.QueueSchedulerClicked += (sender, _) => application.ShowQueueWindow(sender);
            window.ShowWindow();
        }

        internal static int? SpeedLimitChoice(bool enabled, int valueKiB, IDownloadCreationPreferences preferences)
        {
            var setting = SpeedLimiter.SettingFromDialog(enabled, valueKiB, preferences.EnableSpeedLimit, preferences.DefaultDownloadSpeed);
            return setting == SpeedLimiter.FollowGlobal ? (int?)null : setting;
        }

        internal static bool KeepsSuppliedName(Message? message, string? url)
        {
            if (message == null || !message.HasSuppliedFileName) return false;
            return string.Equals(message.Url, url, StringComparison.Ordinal);
        }

        private static Dictionary<string, List<string>>? WithUserAgent(Dictionary<string, List<string>>? headers, string fallbackUserAgent)
        {
            if (headers == null) return null;
            foreach (var name in headers.Keys)
            {
                if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase)) return headers;
            }
            headers["User-Agent"] = new List<string> { fallbackUserAgent };
            return headers;
        }

        private static void OnDownloadClicked(
            INewDownloadDialog window,
            IApplicationCore core,
            IDownloadCreationPreferences preferences,
            string fileName,
            string? selectedFolder,
            Message? message,
            bool startImmediately,
            Action<string?> reportOutcome,
            string? queueId = null)
        {
            if (!Helpers.IsUriValid(window.Url))
            {
                window.ShowMessageBox(TextResource.GetText("MSG_INVALID_URL"));
                return;
            }
            if (string.IsNullOrEmpty(window.SelectedFileName))
            {
                window.ShowMessageBox(TextResource.GetText("MSG_NO_FILE"));
                return;
            }
            var contentLength = 0L;
            var header = message?.GetResponseHeaderFirstValue("Content-Length") ?? message?.GetResponseHeaderFirstValue("content-length");
            if (!string.IsNullOrEmpty(header))
            {
                long.TryParse(header, out contentLength);
            }
            var downloadId = core.StartDownload(
                new SingleSourceHTTPDownloadInfo
                {
                    Uri = window.Url,
                    Headers = WithUserAgent(message?.RequestHeaders, preferences.FallbackUserAgent),
                    Cookies = message?.Cookies,
                    ContentLength = contentLength
                },
                FileHelper.SanitizeFileName(window.SelectedFileName),
                window.SelectedFileName != fileName || KeepsSuppliedName(message, window.Url) ? FileNameFetchMode.None : FileNameFetchMode.FileNameAndExtension,
                selectedFolder,
                startImmediately,
                window.Authentication,
                window.Proxy ?? preferences.DefaultProxy,
                queueId,
                false,
                SpeedLimitChoice(window.EnableSpeedLimit, window.SpeedLimit, preferences));
            reportOutcome(downloadId);
            window.DisposeWindow();
        }
    }
}
