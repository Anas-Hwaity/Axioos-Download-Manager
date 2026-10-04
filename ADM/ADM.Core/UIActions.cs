using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TraceLog;
using Translations;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.DataAccess;
using ADM.Core.Downloader;
using ADM.Core.IO;
using ADM.Core.UI;
using ADM.Core.Util;

namespace ADM.Core
{
    public interface IMainCommandService
    {
        void DeleteDownloads(bool inProgressOnly, Action<bool>? callback);
        void OnDblClick();
        void OpenSelectedFolder();
        void OpenSelectedFile();
        void StopSelectedDownloads();
        void ResumeDownloads();
        void MoveToQueue();
        void MoveToQueue(string[] selectedIds, bool prompt = false, Action? callback = null);
        void SaveAs();
        void RefreshLink();
        void ShowProgressWindow();
        void CopyURL1();
        void CopyURL2();
        void ShowSeletectedItemProperties();
        void CopyFile();
        void RestartDownload();
    }

    public interface IMainCommandContext
    {
        IApplicationWindow MainWindow { get; }
        IApplicationCore CoreService { get; }
        IPlatformUIService PlatformUIService { get; }
        bool DoubleClickOpenFile { get; }
        bool OpenFolder(string? folder, string fileName);
        bool OpenFile(string file);
    }

    public interface IApplicationRuntimeContext : IMainCommandContext, BrowserMonitoring.IVideoDetectionRuntimeContext, BrowserMonitoring.IVideoTrackerRuntimeContext, UI.IProgressWindowRuntimeContext
    {
        new IApplicationCore CoreService { get; }
        new IPlatformUIService PlatformUIService { get; }
        new IApplication Application { get; }
        IClipboardMonitor ClipboardMonitor { get; }
        new ILinkRefresher LinkRefresher { get; }
        new IVideoTracker VideoTracker { get; }
        IReadOnlyList<Category> Categories { get; }
        bool IsBrowserMonitoringEnabled { get; }
        bool ShowNotification { get; }
        int GlassmorphismLevel { get; }
        string AppearanceTheme { get; }
        string AppearanceBackdrop { get; }
        string AppearanceAccent { get; }
        string BrowserProtocolStateDirectory { get; }
        string DownloadRulesFile { get; }
        void EnableRunOnLogon();
        void SubscribeInitialized(EventHandler handler);
        new void BroadcastConfigChange();
        void UpdateSpeedLimit(bool enabled, int speed);
    }


    public sealed class MainCommandService : IMainCommandService
    {
        private readonly IMainCommandContext context;

        public MainCommandService(IMainCommandContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }
        public void DeleteDownloads(bool inProgressOnly, Action<bool>? callback)
        {
            if (inProgressOnly)
            {
                var selectedItems = context.MainWindow.SelectedInProgressRows;
                if (context.MainWindow.Confirm(context.MainWindow, TextResource.GetText("DEL_SEL_TEXT")))
                {
                    context.CoreService.StopDownloads(selectedItems.Where(x => x != null).Select(x => x.DownloadEntry.Id).ToList());
                    foreach (var item in selectedItems)
                    {
                        if (item != null)
                        {
                            context.CoreService.RemoveDownload(item.DownloadEntry, false);
                            context.MainWindow.Delete(item);
                            AppDB.Instance.Downloads.RemoveDownloadById(item.DownloadEntry.Id);
                        }
                    }
                    callback?.Invoke(true);
                }
            }
            else
            {
                var selectedRows = context.MainWindow.SelectedFinishedRows;
                context.MainWindow.ConfirmDelete(TextResource.GetText("DEL_SEL_TEXT"),
                    out bool approved, out bool deleteFiles);
                if (approved)
                {
                    foreach (var selectedRow in selectedRows)
                    {
                        context.CoreService.RemoveDownload(selectedRow.DownloadEntry, deleteFiles);
                        context.MainWindow.Delete(selectedRow);
                        AppDB.Instance.Downloads.RemoveDownloadById(selectedRow.DownloadEntry.Id);
                    }
                    callback?.Invoke(false);
                }
            }
        }

        public void OnDblClick()
        {
            if (context.MainWindow.IsInProgressViewSelected)
            {
                ShowSeletectedItemProperties();
            }
            else
            {
                if (context.DoubleClickOpenFile)
                {
                    OpenSelectedFile();
                }
                else
                {
                    OpenSelectedFolder();
                }
            }
        }

        public void OpenSelectedFolder()
        {
            var selectedRows = context.MainWindow.SelectedFinishedRows;
            if (selectedRows.Count > 0)
            {
                var row = selectedRows[0];
                var ent = row.DownloadEntry;
                if (!context.OpenFolder(ent.TargetDir, ent.Name))
                {
                    context.PlatformUIService.ShowMessageBox(context.MainWindow, TextResource.GetText("ERR_MSG_FILE_NOT_FOUND_MSG"));
                }
                return;
            }
            context.PlatformUIService.ShowMessageBox(context.MainWindow, TextResource.GetText("NO_ITEM_SELECTED"));
        }

        public void OpenSelectedFile()
        {
            var selectedRows = context.MainWindow.SelectedFinishedRows;
            if (selectedRows.Count > 0)
            {
                var row = selectedRows[0];
                var ent = row.DownloadEntry;
                if (!string.IsNullOrEmpty(ent.TargetDir))
                {
                    var file = Path.Combine(ent.TargetDir, ent.Name);
                    if (!context.OpenFile(file))
                    {
                        context.PlatformUIService.ShowMessageBox(context.MainWindow, TextResource.GetText("ERR_MSG_FILE_NOT_FOUND_MSG"));
                    }
                    return;
                }
                else
                {
                    Log.Debug("Path is null");
                }
            }
            context.PlatformUIService.ShowMessageBox(context.MainWindow, TextResource.GetText("NO_ITEM_SELECTED"));
        }

        public void StopSelectedDownloads()
        {
            context.CoreService.StopDownloads(context.MainWindow.SelectedInProgressRows.Select(x => x.DownloadEntry.Id), true);
        }

        public void ResumeDownloads()
        {
            var idDict = new Dictionary<string, DownloadItemBase>();
            var list = context.MainWindow.SelectedInProgressRows;
            foreach (var item in list)
            {
                idDict[item.DownloadEntry.Id] = item.DownloadEntry;
            }
            context.CoreService.ResumeDownload(idDict);
        }

        public void MoveToQueue()
        {
            var selectedIds = context.MainWindow.SelectedInProgressRows?.Select(x => x.DownloadEntry.Id)?.ToArray() ?? new string[0];
            MoveToQueue(selectedIds);
        }

        public void MoveToQueue(string[] selectedIds, bool prompt = false, Action? callback = null)
        {
            if (prompt && !context.MainWindow.Confirm(context.MainWindow, "Add to queue?"))
            {
                return;
            }
            using var queueSelectionDialog = context.PlatformUIService.CreateQueueSelectionDialog();
            queueSelectionDialog.SetData(QueueManager.Queues.Select(q => q.Name), QueueManager.Queues.Select(q => q.ID), selectedIds);

            queueSelectionDialog.QueueSelected += (s, e) =>
            {
                var downloadIds = e.DownloadIds;
                QueueManager.AddDownloadsToQueue(e.SelectedQueueId, downloadIds.ToArray());
            };
            queueSelectionDialog.ShowWindow();
        }

        public void SaveAs()
        {
            var rows = context.MainWindow.SelectedInProgressRows;
            if (rows == null || rows.Count < 1) return;
            var item = rows[0].DownloadEntry;
            var file = context.PlatformUIService.SaveFileDialog(Path.Combine(item.TargetDir ?? FileHelper.GetDownloadFolderByFileName(item.Name), item.Name), null, null);
            if (file == null)
            {
                return;
            }
            Log.Debug("folder: " + Path.GetDirectoryName(file) + " file: " + Path.GetFileName(file));
            context.CoreService.RenameDownload(item.Id, Path.GetDirectoryName(file)!, Path.GetFileName(file));
        }

        public void RefreshLink()
        {
            var selected = context.MainWindow.SelectedInProgressRows;
            if (selected == null || selected.Count == 0) return;
            context.PlatformUIService.ShowRefreshLinkDialog(selected[0].DownloadEntry);
        }

        public void ShowProgressWindow()
        {
            var selected = context.MainWindow.SelectedInProgressRows;
            if (selected == null || selected.Count == 0) return;
            context.CoreService.ShowProgressWindow(selected[0].DownloadEntry.Id);
        }

        public void CopyURL1()
        {
            var selected = context.MainWindow.SelectedInProgressRows;
            if (selected == null || selected.Count == 0) return;
            var url = context.CoreService.GetPrimaryUrl(selected[0].DownloadEntry);
            if (url != null)
            {
                context.MainWindow.SetClipboardText(url);
            }
        }

        public void CopyURL2()
        {
            var selected = context.MainWindow.SelectedFinishedRows;
            if (selected == null || selected.Count == 0) return;
            var url = context.CoreService.GetPrimaryUrl(selected[0].DownloadEntry);
            if (url != null)
            {
                context.MainWindow.SetClipboardText(url);
            }
        }

        public void ShowSeletectedItemProperties()
        {
            DownloadItemBase? ent = null;
            if (context.MainWindow.IsInProgressViewSelected)
            {
                var rows = context.MainWindow.SelectedInProgressRows;
                if (rows.Count > 0)
                {
                    ent = rows[0].DownloadEntry;
                }
            }
            else
            {
                var rows = context.MainWindow.SelectedFinishedRows;
                if (rows.Count > 0)
                {
                    ent = rows[0].DownloadEntry;
                }
            }
            if (ent == null) return;

            var emptyCookie = string.Empty;
            var emptyHeaders = new Dictionary<string, List<string>>();

            var cookies = emptyCookie;
            var headers = emptyHeaders;
            try
            {
                switch (ent.DownloadType)
                {
                    case "Http":
                        {
                            var info = RequestDataIO.LoadSingleSourceHTTPDownloadInfo(ent.Id);
                            cookies = info?.Cookies ?? emptyCookie;
                            headers = info?.Headers ?? emptyHeaders;
                            break;
                        }
                    case "Dash":
                        {
                            var info = RequestDataIO.LoadDualSourceHTTPDownloadInfo(ent.Id);
                            cookies = info?.Cookies1 ?? emptyCookie;
                            headers = info?.Headers1 ?? emptyHeaders;
                            break;
                        }
                    case "Hls":
                        {
                            var info = RequestDataIO.LoadMultiSourceHLSDownloadInfo(ent.Id);
                            cookies = info?.Cookies ?? emptyCookie;
                            headers = info?.Headers ?? emptyHeaders;
                            break;
                        }
                    case "Mpd-Dash":
                        {
                            var info = RequestDataIO.LoadMultiSourceDASHDownloadInfo(ent.Id);
                            cookies = info?.Cookies ?? emptyCookie;
                            headers = info?.Headers ?? emptyHeaders;
                            break;
                        }
                }
            }
            catch { }
            context.PlatformUIService.ShowPropertiesDialog(ent, cookies, headers);
        }

        public void CopyFile()
        {
            var selected = context.MainWindow.SelectedFinishedRows;
            if (selected == null || selected.Count == 0) return;
            var entry = selected[0].DownloadEntry;
            var file = Path.Combine(entry.TargetDir, entry.Name);
            if (File.Exists(file))
            {
                context.MainWindow.SetClipboardFile(file);
            }
            else
            {
                context.PlatformUIService.ShowMessageBox(context.MainWindow, TextResource.GetText("ERR_MSG_FILE_NOT_FOUND_MSG"));
            }
        }

        public void RestartDownload()
        {
            DownloadItemBase? ent = null;
            if (context.MainWindow.IsInProgressViewSelected)
            {
                var rows = context.MainWindow.SelectedInProgressRows;
                if (rows.Count > 0)
                {
                    ent = rows[0].DownloadEntry;
                }
            }
            else
            {
                var rows = context.MainWindow.SelectedFinishedRows;
                if (rows.Count > 0)
                {
                    ent = rows[0].DownloadEntry;
                }
            }
            if (ent == null) return;
            if (!context.CoreService.RestartDownload(ent))
            {
                context.PlatformUIService.ShowMessageBox(context.MainWindow, TextResource.GetText("MSG_RESTART_FAILED"));
            }
        }
    }
}
