using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ADM.Core;
using ADM.Core.UI;

namespace ADM.Wpf.UI
{
    public partial class MainWindow
    {
        private WebShellHost? webShellHost;

        internal IReadOnlyList<InProgressDownloadEntryWrapper> ShellInProgressRows => inProgressList.ToList();

        internal IReadOnlyList<FinishedDownloadEntryWrapper> ShellFinishedRows => finishedList.ToList();

        internal bool ShellUpdateAvailable => BtnHelp.Tag != null;

        internal IApplicationRuntimeContext ShellRuntimeContext => runtimeContext;

        internal ADM.Core.Telemetry.IDownloadTelemetryService? ShellTelemetry { get; set; }

        internal ADM.Core.Navigation.IExternalNavigationService ShellNavigation => externalNavigationService;

        internal void AttachWebShell()
        {
            if (webShellHost != null) return;
            webShellHost = new WebShellHost(this);
            webShellHost.Attach();
        }

        internal IDictionary<string, IButton> ShellButtons => new Dictionary<string, IButton>(StringComparer.Ordinal)
        {
            ["new"] = newButton,
            ["delete"] = deleteButton,
            ["pause"] = pauseButton,
            ["resume"] = resumeButton,
            ["open"] = openFileButton,
            ["openFolder"] = openFolderButton
        };

        internal void ShellSelect(IReadOnlyCollection<string> ids, bool inProgress)
        {
            if (inProgress)
            {
                if (lvCategory.SelectedIndex != 0) SwitchToInProgressView();
                lvInProgress.SelectedItems.Clear();
                foreach (var row in inProgressList.Where(item => ids.Contains(item.DownloadEntry.Id)))
                {
                    lvInProgress.SelectedItems.Add(row);
                }
            }
            else
            {
                if (lvCategory.SelectedIndex != 1)
                {
                    SwitchToFinishedView();
                }
                else if (TxtSearch.Text.Length > 0)
                {
                    TxtSearch.Text = string.Empty;
                    ApplyFilter();
                }
                lvFinished.SelectedItems.Clear();
                foreach (var row in finishedList.Where(item => ids.Contains(item.DownloadEntry.Id)))
                {
                    lvFinished.SelectedItems.Add(row);
                }
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        internal bool ShellClickButton(string name)
        {
            Button? button = name switch
            {
                "delete" => BtnDelete,
                "pause" => BtnPause,
                "resume" => BtnResume,
                "open" => BtnOpen,
                "openFolder" => BtnOpenFolder,
                _ => null
            };
            if (button == null || !button.IsEnabled) return false;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            return true;
        }

        internal IList<IMenuItem> ShellPrepareMenu(bool inProgress)
        {
            if (inProgress)
            {
                InProgressContextMenuOpening?.Invoke(lvInProgress, EventArgs.Empty);
                return menuItems.Take(10).ToList();
            }
            FinishedContextMenuOpening?.Invoke(lvFinished, EventArgs.Empty);
            return menuItems.Skip(10).ToList();
        }

        internal bool ShellInvokeMenu(string name)
        {
            if (MenuItemMap == null || !MenuItemMap.TryGetValue(name, out var item)) return false;
            if (!(item is MenuItemWrapper wrapper) || !wrapper.Enabled || wrapper.Menu.Visibility != Visibility.Visible) return false;
            wrapper.Menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, wrapper.Menu));
            return true;
        }

        internal void ShellOpenSelected()
        {
            DownloadListDoubleClicked?.Invoke(this, EventArgs.Empty);
        }

        internal bool ShellCommand(string name)
        {
            var args = new RoutedEventArgs();
            switch (name)
            {
                case "newDownload": NewDownloadClicked?.Invoke(this, args); return true;
                case "videoDownload": YoutubeDLDownloadClicked?.Invoke(this, args); return true;
                case "batchDownload": BatchDownloadClicked?.Invoke(this, args); return true;
                case "scheduler": SchedulerClicked?.Invoke(this, args); return true;
                case "history": HistoryClicked?.Invoke(this, args); return true;
                case "dashboard": DashboardClicked?.Invoke(this, args); return true;
                case "mediaGrabber": MediaGrabberClicked?.Invoke(this, EventArgs.Empty); return true;
                case "settings": SettingsClicked?.Invoke(this, args); return true;
                case "monitoringSettings": BrowserMonitoringSettingsClicked?.Invoke(this, args); return true;
                case "language": LanguageSettingsClicked?.Invoke(this, args); return true;
                case "import": ImportClicked?.Invoke(this, args); return true;
                case "export": ExportClicked?.Invoke(this, args); return true;
                case "clearFinished": ClearAllFinishedClicked?.Invoke(this, args); return true;
                case "help": SupportPageClicked?.Invoke(this, args); return true;
                case "reportProblem": BugReportClicked?.Invoke(this, args); return true;
                case "checkUpdate": UpdateClicked?.Invoke(this, args); return true;
                case "update": UpdateClicked?.Invoke(this, args); return true;
                case "toggleMonitoring": BrowserMonitoringButtonClicked?.Invoke(this, args); return true;
                case "speedLimit": runtimeContext.PlatformUIService.ShowSpeedLimiterWindow(); return true;
                case "exit": Environment.Exit(0); return true;
                default: return false;
            }
        }
    }
}
