using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Translations;
using ADM.Core.UI;
using ADM.Core;
using ADM.Wpf.UI.Dialogs.SpeedLimiter;
using ADM.Wpf.UI.Win32;
using TraceLog;

namespace ADM.Wpf.UI.Dialogs.ProgressWindow
{
    public partial class DownloadProgressWindow : Window, IProgressWindow
    {
        private readonly IProgressWindowRuntimeContext runtimeContext;

        public DownloadProgressWindow(IProgressWindowRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            InitializeComponent();
            this.Closed += DownloadProgressWindow_Closed;
            actSpeedUpdate = value => this.TxtSpeed.Text = value;
            actEtaUpdate = value => this.TxtETA.Text = value;
            actStatusUpdate = value => this.TxtStatus.Text = value;

            runtimeContext.SubscribeApplicationEvent(ApplicationContext_ApplicationEvent);

#if NET45_OR_GREATER
            this.TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo
            {
                Description = "",
                ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal
            };
#endif

            actPrgUpdate = value =>
            {
                if (!limitRefreshedForRun)
                {
                    limitRefreshedForRun = true;
                    RefreshSpeedLimitText();
                }
                var val = value >= 0 && value <= 100 ? value : 0;
                this.PrgProgress.Value = val;
                var prg = value >= 0 && value <= 100 ? value + "% " : "";
                this.Title = $"{prg}\u200E{FileNameText}";
#if NET45_OR_GREATER
                this.TaskbarItemInfo.Description = this.Title;
                this.TaskbarItemInfo.ProgressValue = val / 100.0;
#endif
            };
        }

        private void DownloadProgressWindow_Closed(object sender, EventArgs e)
        {
            runtimeContext.UnsubscribeApplicationEvent(ApplicationContext_ApplicationEvent);
        }

        private void ApplicationContext_ApplicationEvent(object sender, ApplicationEvent e)
        {
            if (e.EventType == "ConfigChanged")
            {
                Dispatcher.BeginInvoke(new Action(RefreshSpeedLimitText));
            }
        }

        public string FileNameText
        {
            get => this.TxtFileName.Text;
            set
            {
                Dispatcher.BeginInvoke(new Action(() => SetFileText(value)));
            }
        }

        public string UrlText
        {
            get => this.TxtUrl.Text;
            set
            {
                Dispatcher.BeginInvoke(new Action(() => TxtUrl.Text = value));
            }
        }

        public string FileSizeText
        {
            get => this.TxtStatus.Text;
            set
            {
                Dispatcher.BeginInvoke(actStatusUpdate, value);
            }
        }

        public string DownloadSpeedText
        {
            get => this.TxtSpeed.Text;
            set
            {
                Dispatcher.BeginInvoke(actSpeedUpdate, value);
            }
        }

        public string DownloadETAText
        {
            get => this.TxtETA.Text;
            set
            {
                Dispatcher.BeginInvoke(actEtaUpdate, value);
            }
        }

        public int DownloadProgress
        {
            get => (int)this.PrgProgress.Value;
            set
            {
                Dispatcher.BeginInvoke(actPrgUpdate, value);
            }
        }

        public string DownloadId
        {
            get => this.downloadId;
            set
            {
                this.downloadId = value;
                Dispatcher.BeginInvoke(new Action(RefreshSpeedLimitText));
            }
        }

        public void DestroyWindow()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Close();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Progress window close failed");
                }
            }));
        }

        public void ShowProgressWindow()
        {
            Dispatcher.BeginInvoke(new Action(() => this.Show()));
        }

        public void DownloadFailed(ErrorDetails error)
        {
            Dispatcher.BeginInvoke(new Action<ErrorDetails>(error =>
                {
                    TxtStatus.Text = error.Message;
                    BtnPause.Content = TextResource.GetText("MENU_RESUME");
                    BtnPause.Tag = new();
                    TxtETA.Text = string.Empty;
                }), error);
        }

        public void DownloadCancelled()
        {
            Dispatcher.BeginInvoke(new Action(() =>
                {
                    TxtStatus.Text = TextResource.GetText("MSG_DWN_STOP");
                    TxtETA.Text = string.Empty;
                    BtnPause.Content = TextResource.GetText("MENU_RESUME");
                    BtnPause.Tag = new();
                }));
        }

        public void DownloadStarted()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                limitRefreshedForRun = false;
                BtnPause.Content = TextResource.GetText("MENU_PAUSE");
                BtnPause.Tag = null;
            }));
        }

        private void RefreshSpeedLimitText()
        {
            try
            {
                var setting = string.IsNullOrEmpty(downloadId) ? 0 : runtimeContext.CoreService.GetDownloadSpeedLimit(downloadId);
                var limit = ADM.Core.Downloader.SpeedLimiter.EffectiveLimit(setting, runtimeContext.EnableSpeedLimit, runtimeContext.DefaultDownloadSpeed);
                SetSpeedLimitText(limit > 0, limit);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The speed limit label could not be refreshed");
            }
        }

        private void SetSpeedLimitText(bool enable, int limit)
        {
            if (enable && limit > 0)
            {
                TxtSpeedLimit.Text = $"{TextResource.GetText("SPEED_LIMIT_TITLE")} - {limit}K/S";
            }
            else
            {
                TxtSpeedLimit.Text = TextResource.GetText("MSG_NO_SPEED_LIMIT");
            }
        }

        private void SetFileText(string value)
        {
            TxtFileName.Text = value;
            var prg = PrgProgress.Value >= 0 && PrgProgress.Value <= 100 ? PrgProgress.Value + "% " : "";
            this.Title = $"{prg}\u200E{value}";
        }

        private void StopDownload(bool close)
        {
            if (downloadId != null)
            {
                runtimeContext.CoreService.PauseDownloads(new List<string> { downloadId }, close);
            }
        }

        private Action<string> actSpeedUpdate, actEtaUpdate, actStatusUpdate;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopDownload(true);
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (BtnPause.Tag != null)
            {
                runtimeContext.Application.ResumeDownload(downloadId);
                BtnPause.Content = TextResource.GetText("MENU_PAUSE");
                BtnPause.Tag = null;
            }
            else
            {
                StopDownload(false);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopDownload(true);
        }

        private void BtnHide_Click(object sender, RoutedEventArgs e)
        {
            Closing -= Window_Closing;
            runtimeContext.CoreService.HideProgressWindow(downloadId);
        }

        private void TxtSpeedLimit_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var id = downloadId;
            if (string.IsNullOrEmpty(id))
            {
                runtimeContext.PlatformUIService.ShowSpeedLimiterWindow();
                return;
            }
            var window = new SpeedLimiterWindow { Owner = this };
            window.Title = TextResource.GetText("SPEED_LIMIT_TITLE") + " - " + TxtFileName.Text;
            SpeedLimiterUIController.RunForDownload(window, runtimeContext, id, RefreshSpeedLimitText);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            NativeMethods.DisableMaxButton(this);
#if NET45_OR_GREATER
            if (ADM.Wpf.UI.App.Skin == Skin.Dark)
            {
                var helper = new WindowInteropHelper(this);
                helper.EnsureHandle();
                DarkModeHelper.UseImmersiveDarkMode(helper.Handle, true);
            }
#endif
        }

        private Action<int> actPrgUpdate;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshSpeedLimitText();
        }

        private string downloadId = string.Empty;
        private bool limitRefreshedForRun;
    }
}
