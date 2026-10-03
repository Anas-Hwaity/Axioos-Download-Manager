using System;
using System.Collections.Generic;
using System.IO;
using TraceLog;
using Translations;
using ADM.Core.BrowserMonitoring;
using ADM.Core.DataAccess;
using ADM.Core.Downloader;
using ADM.Core.Legacy;
using ADM.Core.UI;
using ADM.Core.Updater;
using ADM.Core.Util;
using YDLWrapper;

namespace ADM.Core
{
    public class Application : IApplication
    {
        private delegate void UpdateItemCallBack(string id, string targetFileName, long size);
        private readonly IApplicationCore core;
        private readonly IVideoTracker videoTracker;
        private readonly IDownloadCreationPreferences downloadCreationPreferences;
        private readonly IMainCommandService mainCommands;
        private readonly IApplicationRuntimeContext runtimeContext;
        private Action<string, int, double, long> updateProgressAction;
        public event EventHandler WindowLoaded;

        public Application(IApplicationCore core, IVideoTracker videoTracker, IDownloadCreationPreferences downloadCreationPreferences, IMainCommandService mainCommands, IApplicationRuntimeContext runtimeContext)
        {
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            this.videoTracker = videoTracker ?? throw new ArgumentNullException(nameof(videoTracker));
            this.downloadCreationPreferences = downloadCreationPreferences ?? throw new ArgumentNullException(nameof(downloadCreationPreferences));
            this.mainCommands = mainCommands ?? throw new ArgumentNullException(nameof(mainCommands));
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.runtimeContext.SubscribeInitialized(AppInstance_Initialized);
            this.updateProgressAction = new Action<string, int, double, long>(this.UpdateProgressOnUI);
        }

        private void AppInstance_Initialized(object? sender, EventArgs e)
        {
            AppDB.Instance.Init(Path.Combine(Config.AppDir, "downloads.db"));
            AttachedEventHandler();
            LoadDownloadList();
            UpdateToolbarButtonState();
            AppUpdater.QueryNewVersion(runtimeContext);
            WindowLoaded += (_, _) => runtimeContext.ClipboardMonitor.Start();
        }

        public void AddItemToTop(
            string id,
            string targetFileName,
            string? targetDir,
            DateTime date,
            long fileSize,
            string type,
            FileNameFetchMode fileNameFetchMode,
            string primaryUrl,
            DownloadStartType startType,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            IReadOnlyList<string>? tags = null)
        {
            var downloadEntry = new InProgressDownloadItem
            {
                Name = targetFileName,
                DateAdded = date,
                DownloadType = type,
                Id = id,
                Progress = 0,
                Size = fileSize,
                Status = startType == DownloadStartType.Waiting ? DownloadStatus.Waiting : DownloadStatus.Stopped,
                TargetDir = targetDir,
                PrimaryUrl = primaryUrl,
                Authentication = authentication,
                Proxy = proxyInfo
            };
            AppDB.Instance.Downloads.AddNewDownload(downloadEntry, tags);

            RunOnUiThread(() =>
            {
                runtimeContext.MainWindow.AddToTop(downloadEntry);
                runtimeContext.MainWindow.SwitchToInProgressView();
                runtimeContext.MainWindow.ClearInProgressViewSelection();
                UpdateToolbarButtonState();
            });
        }

        public bool Confirm(object? window, string text)
        {
            return runtimeContext.MainWindow.Confirm(window, text);
        }

        public IDownloadCompleteDialog CreateDownloadCompleteDialog()
        {
            return runtimeContext.PlatformUIService.CreateDownloadCompleteDialog();
        }

        public INewDownloadDialog CreateNewDownloadDialog(bool empty)
        {
            return runtimeContext.PlatformUIService.CreateNewDownloadDialog(empty);
        }

        public INewVideoDownloadDialog CreateNewVideoDialog()
        {
            return runtimeContext.PlatformUIService.CreateNewVideoDialog();
        }

        public IProgressWindow CreateProgressWindow(string downloadId)
        {
            return runtimeContext.PlatformUIService.CreateProgressWindow(downloadId);
        }

        public void DownloadCanelled(string id)
        {
            DownloadFailed(id);
        }

        public void SetDownloadStatus(string id, DownloadStatus status)
        {
            AppDB.Instance.Downloads.UpdateDownloadStatus(id, status);
            var entry = GetInProgressDownloadEntry(id);
            if (entry != null) entry.Status = status;
            RunOnUiThread(UpdateToolbarButtonState);
        }

        public void DownloadFailed(string id)
        {
            AppDB.Instance.Downloads.UpdateDownloadStatus(id, DownloadStatus.Stopped);
            RunOnUiThread(() =>
            {
                CallbackActions.DownloadFailed(id, runtimeContext);
                UpdateToolbarButtonState();
            });
        }

        public void DownloadFinished(string id, long finalFileSize, string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                var name = Path.GetFileName(filePath);
                var folder = Path.GetDirectoryName(filePath);
                AppDB.Instance.Downloads.MarkAsFinished(id, finalFileSize, name, folder);
            }

            Log.Debug("Final file name: " + filePath);
            var downloadEntry = AppDB.Instance.Downloads.GetDownloadById(id);
            if (downloadEntry != null)
            {
                var finishedEntry = new FinishedDownloadItem
                {
                    Name = Path.GetFileName(filePath),
                    Id = downloadEntry.Id,
                    DateAdded = downloadEntry.DateAdded,
                    Size = downloadEntry.Size > 0 ? downloadEntry.Size : finalFileSize,
                    DownloadType = downloadEntry.DownloadType,
                    TargetDir = Path.GetDirectoryName(filePath)!,
                    PrimaryUrl = downloadEntry.PrimaryUrl,
                    Authentication = downloadEntry.Authentication,
                    Proxy = downloadEntry.Proxy
                };
                AppDB.Instance.Downloads.UpdateDownloadEntry(finishedEntry);

                RunOnUiThread(() =>
                {
                    var download = runtimeContext.MainWindow.FindInProgressItem(id);
                    if (download == null) return;

                    runtimeContext.MainWindow.AddToTop(finishedEntry);
                    runtimeContext.MainWindow.Delete(download);

                    QueueManager.RemoveFinishedDownload(download.DownloadEntry.Id);

                    if (runtimeContext.CoreService.ActiveDownloadCount == 0 && runtimeContext.MainWindow.IsInProgressViewSelected)
                    {
                        Log.Debug("switching to finished listview");
                        runtimeContext.MainWindow.SwitchToFinishedView();
                    }
                });
            }
        }

        public void DownloadStarted(string id)
        {
            RunOnUiThread(() =>
            {
                CallbackActions.DownloadStarted(id, runtimeContext);
                UpdateToolbarButtonState();
            });
        }

        public IEnumerable<InProgressDownloadItem> GetAllInProgressDownloads()
        {
            return runtimeContext.MainWindow.InProgressDownloads;
        }

        public InProgressDownloadItem? GetInProgressDownloadEntry(string downloadId)
        {
            return runtimeContext.MainWindow.FindInProgressItem(downloadId)?.DownloadEntry;
        }

        public string? GetUrlFromClipboard()
        {
            var text = runtimeContext.MainWindow.GetUrlFromClipboard();
            if (Helpers.IsUriValid(text))
            {
                return text;
            }
            return null;
        }

        public AuthenticationInfo? PromtForCredentials(string message)
        {
            return runtimeContext.PlatformUIService.PromtForCredentials(runtimeContext.MainWindow, message);
        }

        public void RenameFileOnUI(string id, string folder, string file)
        {
            if (!AppDB.Instance.Downloads.UpdateNameAndFolder(id, file, folder))
            {
                Log.Debug("RenameFileOnUI::failed");
            }
            RunOnUiThread(() =>
            {
                var downloadEntry = runtimeContext.MainWindow.FindInProgressItem(id);
                if (downloadEntry == null) return;
                if (file != null)
                {
                    downloadEntry.Name = file;
                }
                if (folder != null)
                {
                    downloadEntry.DownloadEntry.TargetDir = folder;
                }
            });
        }

        public void ResumeDownload(string downloadId)
        {
            var idDict = new Dictionary<string, DownloadItemBase>();
            var download = runtimeContext.MainWindow.FindInProgressItem(downloadId);
            if (download == null) return;
            idDict[download.DownloadEntry.Id] = download.DownloadEntry;
            runtimeContext.CoreService.ResumeDownload(idDict);
        }

        public void RunOnUiThread(Action action)
        {
            runtimeContext.MainWindow.RunOnUIThread(action);
        }

        public void SetDownloadStatusWaiting(string id)
        {
            RunOnUiThread(() =>
            {
                var download = runtimeContext.MainWindow.FindInProgressItem(id);
                if (download == null) return;
                download.Status = DownloadStatus.Waiting;
                UpdateToolbarButtonState();
            });
        }

        public void ShowUpdateAvailableNotification()
        {
            RunOnUiThread(() =>
            {
                runtimeContext.MainWindow.ShowUpdateAvailableNotification();
            });
        }

        public void ShowDownloadCompleteDialog(string file, string folder)
        {
            RunOnUiThread(() =>
            {
                DownloadCompleteUIController.ShowDialog(CreateDownloadCompleteDialog(), file, folder);
            });
        }

        public void ShowMessageBox(object? window, string message)
        {
            runtimeContext.PlatformUIService.ShowMessageBox(window, message);
        }

        public void ShowNewDownloadDialog(Message message)
        {
            var url = message.Url;
            if (NewDownloadPromptTracker.IsPromptAlreadyOpen(url))
            {
                message.CreationOutcome?.Invoke(null);
                return;
            }
            runtimeContext.MainWindow.RunOnUIThread(() =>
            {
                var shown = false;
                INewDownloadDialog? dialog = null;
                try
                {
                    NewDownloadPromptTracker.PromptOpen(url);
                    dialog = this.CreateNewDownloadDialog(false);
                    NewDownloadDialogUIController.CreateAndShowDialog(dialog, this, core, downloadCreationPreferences, message,
                        () => NewDownloadPromptTracker.PromptClosed(url));
                    shown = true;
                }
                finally
                {
                    if (!shown)
                    {
                        NewDownloadPromptTracker.PromptClosed(url);
                        try
                        {
                            dialog?.DisposeWindow();
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Unshown new download dialog could not be closed");
                        }
                        message.CreationOutcome?.Invoke(null);
                    }
                }
            });
        }

        public void ShowVideoDownloadDialog(string videoId, string name, long size, string? contentType)
        {
            RunOnUiThread(() =>
            {
                NewVideoDownloadDialogUIController.ShowVideoDownloadDialog(this.CreateNewVideoDialog(), this, videoTracker, downloadCreationPreferences,
                    videoId, name, size, contentType);
            });
        }

        public void UpdateItem(string id, string targetFileName, long size)
        {
            if (!AppDB.Instance.Downloads.UpdateNameAndSize(id, size, targetFileName))
            {
                Log.Debug("UpdateItem::failed");
            }
            RunOnUiThread(() =>
            {
                var download = runtimeContext.MainWindow.FindInProgressItem(id);
                if (download == null) return;
                download.Name = targetFileName;
                download.Size = size;
            });
        }

        private void UpdateProgressOnUI(string id, int progress, double speed, long eta)
        {
            var downloadEntry = runtimeContext.MainWindow.FindInProgressItem(id);
            if (downloadEntry != null)
            {
                downloadEntry.Progress = progress;
                downloadEntry.DownloadSpeed = FormattingHelper.FormatSize(speed) + "/s";
                downloadEntry.ETA = FormattingHelper.ToHMS(eta);
            }
        }

        public void UpdateProgress(string id, int progress, double speed, long eta)
        {
            if (!AppDB.Instance.Downloads.UpdateDownloadProgress(id, progress))
            {
                Log.Debug("UpdateProgress::failed");
            }
            runtimeContext.MainWindow.RunOnUIThread(this.updateProgressAction, id, progress, speed, eta);
        }

        private void LoadDownloadList()
        {
            try
            {
                if (AppDB.Instance.Downloads.LoadDownloads(out var inProgressDownloads, out var finishedDownloads))
                {
                    runtimeContext.MainWindow.InProgressDownloads = inProgressDownloads;
                    runtimeContext.MainWindow.FinishedDownloads = finishedDownloads;
                    return;
                }
                else
                {
                    Log.Debug("Could not load download list");
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "LoadDownloadList");
            }
        }

        private void DisableButton(IButton button)
        {
            button.Enable = false;
        }

        private void EnableButton(IButton button)
        {
            button.Enable = true;
        }

        private void UpdateToolbarButtonState()
        {
            DisableButton(runtimeContext.MainWindow.OpenFileButton);
            DisableButton(runtimeContext.MainWindow.OpenFolderButton);
            DisableButton(runtimeContext.MainWindow.PauseButton);
            DisableButton(runtimeContext.MainWindow.ResumeButton);
            DisableButton(runtimeContext.MainWindow.DeleteButton);

            if (runtimeContext.MainWindow.IsInProgressViewSelected)
            {
                runtimeContext.MainWindow.OpenFileButton.Visible = runtimeContext.MainWindow.OpenFolderButton.Visible = false;
                runtimeContext.MainWindow.PauseButton.Visible = runtimeContext.MainWindow.ResumeButton.Visible = true;
                var selectedRows = runtimeContext.MainWindow.SelectedInProgressRows;
                if (selectedRows.Count > 0)
                {
                    EnableButton(runtimeContext.MainWindow.DeleteButton);
                }
                if (selectedRows.Count > 1)
                {
                    EnableButton(runtimeContext.MainWindow.ResumeButton);
                    EnableButton(runtimeContext.MainWindow.PauseButton);
                }
                else if (selectedRows.Count == 1)
                {
                    var ent = selectedRows[0];
                    var isActive = runtimeContext.CoreService.IsDownloadActive(ent.DownloadEntry.Id);
                    if (isActive)
                    {
                        EnableButton(runtimeContext.MainWindow.PauseButton);
                    }
                    else
                    {
                        EnableButton(runtimeContext.MainWindow.ResumeButton);
                    }
                }
            }
            else
            {
                runtimeContext.MainWindow.OpenFileButton.Visible = runtimeContext.MainWindow.OpenFolderButton.Visible = true;
                runtimeContext.MainWindow.PauseButton.Visible = runtimeContext.MainWindow.ResumeButton.Visible = false;
                if (runtimeContext.MainWindow.SelectedFinishedRows.Count > 0)
                {
                    EnableButton(runtimeContext.MainWindow.DeleteButton);
                }

                if (runtimeContext.MainWindow.SelectedFinishedRows.Count == 1)
                {
                    EnableButton(runtimeContext.MainWindow.OpenFileButton);
                    EnableButton(runtimeContext.MainWindow.OpenFolderButton);
                }
            }
        }

        private void DeleteDownloads()
        {
            mainCommands.DeleteDownloads(runtimeContext.MainWindow.IsInProgressViewSelected, null);
        }

        private void AttachedEventHandler()
        {
            runtimeContext.MainWindow.NewDownloadClicked += (s, e) =>
            {
                runtimeContext.MainWindow.RunOnUIThread(() =>
                {
                    NewDownloadDialogUIController.CreateAndShowDialog(CreateNewDownloadDialog(true), this, core, downloadCreationPreferences);
                });
            };

            runtimeContext.MainWindow.YoutubeDLDownloadClicked += (s, e) =>
            {
                try
                {
                    var exec = YDLProcess.FindYDLBinary();
                }
                catch
                {
                    if (Confirm(runtimeContext.MainWindow, TextResource.GetText("MSG_YTDLP_DOWNLOAD")))
                    {
                        InstallLatestYtDlp();
                    }
                    return;
                }
                runtimeContext.PlatformUIService.ShowYoutubeDLDialog();
            };

            runtimeContext.MainWindow.BatchDownloadClicked += (s, e) =>
            {
                runtimeContext.PlatformUIService.ShowBatchDownloadWindow();
            };

            runtimeContext.MainWindow.SelectionChanged += (s, e) =>
            {
                UpdateToolbarButtonState();
            };

            runtimeContext.MainWindow.CategoryChanged += (s, e) =>
            {
                UpdateToolbarButtonState();
            };

            runtimeContext.MainWindow.NewButton.Clicked += (s, e) =>
            {
                runtimeContext.MainWindow.OpenNewDownloadMenu();
            };

            runtimeContext.MainWindow.DeleteButton.Clicked += (a, b) =>
            {
                DeleteDownloads();
            };

            runtimeContext.MainWindow.DownloadListDoubleClicked += (a, b) => mainCommands.OnDblClick();

            runtimeContext.MainWindow.OpenFolderButton.Clicked += (a, b) => mainCommands.OpenSelectedFolder();

            runtimeContext.MainWindow.OpenFileButton.Clicked += (a, b) =>
            {
                mainCommands.OpenSelectedFile();
            };

            runtimeContext.MainWindow.PauseButton.Clicked += (a, b) =>
            {
                if (runtimeContext.MainWindow.IsInProgressViewSelected)
                {
                    mainCommands.StopSelectedDownloads();
                }
            };

            runtimeContext.MainWindow.ResumeButton.Clicked += (a, b) =>
            {
                if (runtimeContext.MainWindow.IsInProgressViewSelected)
                {
                    mainCommands.ResumeDownloads();
                }
            };

            runtimeContext.MainWindow.SettingsClicked += (s, e) =>
            {
                runtimeContext.PlatformUIService.ShowSettingsDialog(1);
            };

            runtimeContext.MainWindow.BrowserMonitoringSettingsClicked += (s, e) =>
            {
                runtimeContext.PlatformUIService.ShowBrowserMonitoringDialog();
            };

            runtimeContext.MainWindow.ClearAllFinishedClicked += (s, e) =>
            {
                runtimeContext.MainWindow.DeleteAllFinishedDownloads();
                AppDB.Instance.Downloads.RemoveAllFinished();
            };

            runtimeContext.MainWindow.ImportClicked += (s, e) =>
            {
                var file = runtimeContext.PlatformUIService.OpenFileDialog(null, "zip", null);
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    Log.Debug("Importing from: " + file);
                    runtimeContext.CoreService.Import(file!);
                }
                LoadDownloadList();
            };

            runtimeContext.MainWindow.ExportClicked += (s, e) =>
            {
                var file = runtimeContext.PlatformUIService.SaveFileDialog("adm-download-list.zip", "zip", "All files (*.*)|*.*");
                if (!string.IsNullOrEmpty(file))
                {
                    Log.Debug("Exporting to: " + file);
                    runtimeContext.CoreService.Export(file!);
                }
            };

            runtimeContext.MainWindow.HelpClicked += (s, e) =>
            {
                PlatformHelper.OpenBrowser(Links.SupportUrl);
            };

            runtimeContext.MainWindow.UpdateClicked += (s, e) =>
            {
                if (AppUpdater.IsAppUpdateAvailable)
                {
                    PlatformHelper.OpenBrowser(AppUpdater.GetUpdatePage(runtimeContext.CoreService));
                    return;
                }
                if (AppUpdater.IsComponentUpdateAvailable)
                {
                    if (runtimeContext.MainWindow.Confirm(runtimeContext.MainWindow, AppUpdater.ComponentUpdateText))
                    {
                        LaunchUpdater(UpdateMode.YoutubeDLUpdateOnly);
                    }
                    return;
                }
                runtimeContext.PlatformUIService.ShowMessageBox(runtimeContext.MainWindow, TextResource.GetText("MSG_NO_UPDATE"));
            };

            runtimeContext.MainWindow.BrowserMonitoringButtonClicked += (s, e) =>
            {
                if (Config.Instance.IsBrowserMonitoringEnabled)
                {
                    Config.Instance.IsBrowserMonitoringEnabled = false;
                }
                else
                {
                    Config.Instance.IsBrowserMonitoringEnabled = true;
                }
                Config.SaveConfig();
                runtimeContext.BroadcastConfigChange();
                runtimeContext.MainWindow.UpdateBrowserMonitorButton();
            };

            runtimeContext.MainWindow.SupportPageClicked += (s, e) =>
            {
                PlatformHelper.OpenBrowser(Links.SupportUrl);
            };

            runtimeContext.MainWindow.BugReportClicked += (s, e) =>
            {
                PlatformHelper.OpenBrowser(Links.IssueUrl);
            };

            runtimeContext.MainWindow.CheckForUpdateClicked += (s, e) =>
            {
                PlatformHelper.OpenBrowser(AppUpdater.GetUpdatePage(runtimeContext.CoreService));
            };

            runtimeContext.MainWindow.SchedulerClicked += (s, e) =>
            {
                ShowQueueWindow(runtimeContext.MainWindow);
            };

            runtimeContext.MainWindow.WindowCreated += (s, e) =>
            {
                this.WindowLoaded?.Invoke(this, EventArgs.Empty);
            };

            AttachContextMenuEvents();

            runtimeContext.MainWindow.InProgressContextMenuOpening += (_, _) => InProgressContextMenuOpening();
            runtimeContext.MainWindow.FinishedContextMenuOpening += (_, _) => FinishedContextMenuOpening();
        }

        public void ShowQueueWindow(object window)
        {
            QueueWindowManager.ShowWindow(window, runtimeContext.PlatformUIService.CreateQueuesAndSchedulerWindow(), runtimeContext.CoreService);
        }

        private void LaunchUpdater(UpdateMode updateMode)
        {
            var updateDlg = runtimeContext.PlatformUIService.CreateUpdateUIDialog();
            var commonUpdateUi = new ComponentUpdaterUIController(updateDlg, updateMode, runtimeContext.CoreService.AppVerion);
            updateDlg.Load += (_, _) => commonUpdateUi.StartUpdate();
            updateDlg.Finished += (_, _) =>
            {
                RunOnUiThread(() =>
                {
                    runtimeContext.MainWindow.ClearUpdateInformation();
                });
            };
            updateDlg.Show();
        }

        private void AttachContextMenuEvents()
        {
            try
            {
                runtimeContext.MainWindow.MenuItemMap["pause"].Clicked += (_, _) => mainCommands.StopSelectedDownloads();
                runtimeContext.MainWindow.MenuItemMap["resume"].Clicked += (_, _) => mainCommands.ResumeDownloads();
                runtimeContext.MainWindow.MenuItemMap["delete"].Clicked += (_, _) => DeleteDownloads();
                runtimeContext.MainWindow.MenuItemMap["saveAs"].Clicked += (_, _) => mainCommands.SaveAs();
                runtimeContext.MainWindow.MenuItemMap["refresh"].Clicked += (_, _) => mainCommands.RefreshLink();
                runtimeContext.MainWindow.MenuItemMap["moveToQueue"].Clicked += (_, _) => mainCommands.MoveToQueue();
                runtimeContext.MainWindow.MenuItemMap["showProgress"].Clicked += (_, _) => mainCommands.ShowProgressWindow();
                runtimeContext.MainWindow.MenuItemMap["copyURL"].Clicked += (_, _) => mainCommands.CopyURL1();
                runtimeContext.MainWindow.MenuItemMap["copyURL1"].Clicked += (_, _) => mainCommands.CopyURL2();
                runtimeContext.MainWindow.MenuItemMap["properties"].Clicked += (_, _) => mainCommands.ShowSeletectedItemProperties();
                runtimeContext.MainWindow.MenuItemMap["open"].Clicked += (_, _) => mainCommands.OpenSelectedFile();
                runtimeContext.MainWindow.MenuItemMap["openFolder"].Clicked += (_, _) => mainCommands.OpenSelectedFolder();
                runtimeContext.MainWindow.MenuItemMap["deleteDownloads"].Clicked += (_, _) => DeleteDownloads();
                runtimeContext.MainWindow.MenuItemMap["copyFile"].Clicked += (_, _) => mainCommands.CopyFile();
                runtimeContext.MainWindow.MenuItemMap["properties1"].Clicked += (_, _) => mainCommands.ShowSeletectedItemProperties();
                runtimeContext.MainWindow.MenuItemMap["downloadAgain"].Clicked += (_, _) => mainCommands.RestartDownload();
                runtimeContext.MainWindow.MenuItemMap["restart"].Clicked += (_, _) => mainCommands.RestartDownload();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
        }

        private void InProgressContextMenuOpening()
        {
            foreach (var menu in runtimeContext.MainWindow.MenuItems)
            {
                menu.Enabled = false;
            }
            runtimeContext.MainWindow.MenuItemMap["delete"].Enabled = true;
            runtimeContext.MainWindow.MenuItemMap["schedule"].Enabled = true;
            runtimeContext.MainWindow.MenuItemMap["moveToQueue"].Enabled = true;
            var selectedRows = runtimeContext.MainWindow.SelectedInProgressRows;
            if (selectedRows.Count > 1)
            {
                runtimeContext.MainWindow.MenuItemMap["pause"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["resume"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["showProgress"].Enabled = true;
            }
            else if (selectedRows.Count == 1)
            {
                runtimeContext.MainWindow.MenuItemMap["showProgress"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["copyURL"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["saveAs"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["refresh"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["properties"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["saveAs"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["saveAs"].Enabled = true;
                runtimeContext.MainWindow.MenuItemMap["copyURL"].Enabled = true;

                var ent = selectedRows[0].DownloadEntry;
                if (ent == null) return;
                var isActive = runtimeContext.CoreService.IsDownloadActive(ent.Id);
                Log.Debug("Selected item active: " + isActive);
                if (isActive)
                {
                    runtimeContext.MainWindow.MenuItemMap["pause"].Enabled = true;
                }
                else
                {
                    runtimeContext.MainWindow.MenuItemMap["resume"].Enabled = true;
                    runtimeContext.MainWindow.MenuItemMap["restart"].Enabled = true;
                }
            }
        }

        private void FinishedContextMenuOpening()
        {
            foreach (var menu in runtimeContext.MainWindow.MenuItems)
            {
                menu.Enabled = false;
            }

            runtimeContext.MainWindow.MenuItemMap["deleteDownloads"].Enabled = true;

            var selectedRows = runtimeContext.MainWindow.SelectedFinishedRows;
            if (selectedRows.Count == 1)
            {
                foreach (var menu in runtimeContext.MainWindow.MenuItems)
                {
                    menu.Enabled = true;
                }
            }
        }

        public void InstallLatestYtDlp()
        {
            LaunchUpdater(UpdateMode.YoutubeDLUpdateOnly );
        }

        public void ShowDownloadSelectionWindow(FileNameFetchMode mode, IEnumerable<IRequestData> downloads)
        {
            RunOnUiThread(() =>
            {
                runtimeContext.PlatformUIService.ShowDownloadSelectionWindow(mode, downloads);
            });
        }

        public IPlatformClipboardMonitor GetPlatformClipboardMonitor()
        {
            return runtimeContext.MainWindow.GetClipboardMonitor();
        }
    }
}
