using System.IO;
using Translations;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Legacy;
using ADM.Core.MediaProcessor;
using ADM.Core.Util;

namespace ADM.Core.UI
{
    public class NewVideoDownloadDialogUIController
    {
        public static void ShowVideoDownloadDialog(
            INewVideoDownloadDialog window,
            IApplication application,
            IVideoTracker videoTracker,
            IDownloadCreationPreferences preferences,
            string id,
            string name,
            long size,
            string? contentType)
        {
            window.SetFolderValues(preferences.GetFolderValues());
            window.SeletedFolderIndex = preferences.GetInitialFolderIndex();
            window.SelectedFileName = FileHelper.SanitizeFileName(name);
            window.FileSize = FormattingHelper.FormatSize(size);

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

            if (!string.IsNullOrEmpty(contentType))
            {
                var mime = contentType.ToLowerInvariant();
                if (mime.StartsWith("audio") && !(mime.Contains("mpeg") || mime.Contains("mp3")))
                {
                    window.ShowMp3Checkbox = true;
                }
            }

            window.DownloadClicked += (_, _) =>
            {
                if (string.IsNullOrEmpty(window.SelectedFileName))
                {
                    window.ShowMessageBox(TextResource.GetText("MSG_NO_FILE"));
                    return;
                }
                if (videoTracker.IsFFmpegRequiredForDownload(id) && !FFmpegMediaProcessor.IsFFmpegInstalled())
                {
                    if (application.Confirm(window, TextResource.GetText("MSG_FFMPEG_MISSING")))
                    {
                        PlatformHelper.OpenBrowser(Links.HelperToolsUrl);
                    }
                    return;
                }
                Start(window, videoTracker, preferences, id, true, null);
            };
            window.DownloadLaterClicked += (_, e) =>
            {
                if (string.IsNullOrEmpty(window.SelectedFileName))
                {
                    window.ShowMessageBox(TextResource.GetText("MSG_NO_FILE"));
                    return;
                }
                Start(window, videoTracker, preferences, id, false, e.QueueId);
            };
            window.QueueSchedulerClicked += (sender, _) => application.ShowQueueWindow(sender);
            window.ShowWindow();
        }

        private static void Start(INewVideoDownloadDialog window, IVideoTracker videoTracker, IDownloadCreationPreferences preferences, string id, bool startImmediately, string? queueId)
        {
            var name = FileHelper.SanitizeFileName(window.SelectedFileName);
            if (window.IsMp3CheckboxChecked) name = AddMp3Extension(name);
            videoTracker.StartVideoDownload(
                id,
                name,
                preferences.ResolveSelectedFolder(window.SeletedFolderIndex),
                startImmediately,
                window.Authentication,
                window.Proxy ?? preferences.DefaultProxy,
                window.EnableSpeedLimit ? window.SpeedLimit : 0,
                queueId,
                window.IsMp3CheckboxChecked);
            window.DisposeWindow();
        }

        private static string AddMp3Extension(string name)
        {
            return $"{Path.GetFileNameWithoutExtension(name)}.mp3";
        }
    }
}
