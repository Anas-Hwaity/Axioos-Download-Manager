using System;
using System.Collections.Generic;
using Translations;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.Legacy;
using ADM.Core.Util;

namespace ADM.Core.UI
{
    public class DownloadSelectionUIController
    {
        private readonly IDownloadSelectionView view;
        private readonly FileNameFetchMode mode;
        private readonly IApplication application;
        private readonly IApplicationCore core;
        private readonly IDownloadCreationPreferences preferences;

        public DownloadSelectionUIController(
            IDownloadSelectionView view,
            FileNameFetchMode mode,
            IEnumerable<IRequestData> downloads,
            IApplication application,
            IApplicationCore core,
            IDownloadCreationPreferences preferences)
        {
            this.view = view;
            this.mode = mode;
            this.application = application;
            this.core = core;
            this.preferences = preferences;
            view.DownloadLocation = preferences.GetInitialDownloadLocation();
            view.SetData(mode, downloads, PopuplateEntryWrapper);
            view.BrowseClicked += (_, _) =>
            {
                var folder = view.SelectFolder();
                if (folder is null || folder.Length == 0) return;
                view.DownloadLocation = folder;
                preferences.RememberSelectedFolder(folder);
            };
            view.DownloadClicked += View_DownloadClicked;
            view.DownloadLaterClicked += View_DownloadLaterClicked;
            view.QueueSchedulerClicked += (_, _) => application.ShowQueueWindow(view);
        }

        public void Run()
        {
            view.ShowWindow();
        }

        private void View_DownloadLaterClicked(object? sender, DownloadLaterEventArgs e)
        {
            DownloadSelectedItems(false, e.QueueId);
        }

        private void View_DownloadClicked(object? sender, EventArgs e)
        {
            DownloadSelectedItems(true, null);
        }

        private bool PopuplateEntryWrapper(IRequestData obj, IDownloadEntryWrapper entry)
        {
            if (obj is SingleSourceHTTPDownloadInfo shi)
            {
                entry.EntryType = "Http";
                entry.Name = shi.File ?? FileHelper.GetFileName(new Uri(shi.Uri));
            }
            else if (obj is DualSourceHTTPDownloadInfo dhi)
            {
                entry.EntryType = "Dash";
                entry.Name = dhi.File ?? FileHelper.GetFileName(new Uri(dhi.Uri1));
            }
            else if (obj is MultiSourceHLSDownloadInfo mhi)
            {
                entry.EntryType = "Hls";
                entry.Name = mhi.File ?? FileHelper.GetFileName(new Uri(mhi.VideoUri));
            }
            else if (obj is MultiSourceDASHDownloadInfo mdi)
            {
                entry.EntryType = "MpegDash";
                entry.Name = mdi.File ?? FileHelper.GetFileName(new Uri(mdi.Url));
            }
            else
            {
                return false;
            }
            entry.DownloadEntry = obj;
            return true;
        }

        private void AddDownload(IDownloadEntryWrapper wrapper, bool startImmediately, string? queueId)
        {
            var nameMode = wrapper.DownloadEntry is SingleSourceHTTPDownloadInfo single && single.KeepFileName ? FileNameFetchMode.None : mode;
            core.StartDownload(
                wrapper.DownloadEntry,
                wrapper.Name,
                nameMode,
                view.DownloadLocation,
                startImmediately,
                view.Authentication,
                view.Proxy ?? preferences.DefaultProxy,
                queueId,
                false,
                NewDownloadDialogUIController.SpeedLimitChoice(view.EnableSpeedLimit, view.SpeedLimit, preferences));
        }

        private void DownloadSelectedItems(bool startImmediately, string? queueId)
        {
            if (string.IsNullOrEmpty(view.DownloadLocation))
            {
                application.ShowMessageBox(view, TextResource.GetText("MSG_CAT_FOLDER_MISSING"));
                return;
            }
            if (view.SelectedRowCount == 0)
            {
                application.ShowMessageBox(view, TextResource.GetText("BAT_SELECT_ITEMS"));
                return;
            }
            foreach (var item in view.SelectedItems)
            {
                if (item.IsSelected) AddDownload(item, startImmediately, queueId);
            }
            view.CloseWindow();
        }
    }
}
