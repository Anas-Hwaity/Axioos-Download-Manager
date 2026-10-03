using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Navigation;
using ADM.Core.Util;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.MediaCapture
{
    public partial class MediaCaptureWindow : Window
    {
        private readonly IVideoTracker videoTracker;
        private readonly IExternalNavigationService externalNavigationService;

        private void AddMedia(MediaInfo info)
        {
            lvVideos.Add(info);
        }

        private void UpdateMedia(MediaInfo info)
        {
            var index = -1;
            var k = 0;
            foreach (var item in lvVideos)
            {
                if (item.ID == info.ID)
                {
                    index = k;
                    break;
                }
                k++;
            }
            lvVideos[index] = info;
        }

        private void VideoTracker_MediaAdded(object sender, MediaInfoEventArgs e)
        {
            Dispatcher.BeginInvoke(mediaAction, e.MediaInfo);
        }

        private void VideoTracker_MediaUpdated(object sender, MediaInfoEventArgs e)
        {
            Dispatcher.BeginInvoke(mediaUpdated, e.MediaInfo);
        }

        private ObservableCollection<MediaInfo> lvVideos = new();
        private Action<MediaInfo> mediaAction;
        private Action<MediaInfo> mediaUpdated;

        public MediaCaptureWindow(IVideoTracker videoTracker, IExternalNavigationService externalNavigationService)
        {
            this.videoTracker = videoTracker ?? throw new ArgumentNullException(nameof(videoTracker));
            this.externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
            InitializeComponent();
            videoTracker.MediaAdded += VideoTracker_MediaAdded;
            videoTracker.MediaUpdated += VideoTracker_MediaUpdated;
            this.mediaAction = new(this.AddMedia);
            this.mediaUpdated = new(this.UpdateMedia);
            var list = videoTracker.GetVideoList();
            if (list.Count > 0)
            {
                foreach (var mi in list)
                {
                    lvVideos.Add(mi);
                }
            }
            this.LvVideos.ItemsSource = this.lvVideos;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            videoTracker.ClearVideoList();
            this.lvVideos.Clear();
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            var index = this.LvVideos.SelectedIndex;
            if (index >= 0)
            {
                var item = this.lvVideos[index];
                videoTracker.AddVideoDownload(item.ID);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            videoTracker.MediaAdded -= VideoTracker_MediaAdded;
            videoTracker.MediaUpdated -= VideoTracker_MediaUpdated;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NativeMethods.DisableMinMaxButton(this);

#if NET45_OR_GREATER
            if (ADM.Wpf.UI.App.Skin == Skin.Dark)
            {
                var helper = new WindowInteropHelper(this);
                helper.EnsureHandle();
                DarkModeHelper.UseImmersiveDarkMode(helper.Handle, true);
            }
#endif
        }

        private void HowToLink_MouseDown(object sender, MouseButtonEventArgs e)
        {
            externalNavigationService.OpenUrl(Links.MediaGrabberHowToUrl);
        }

        private void ChkTopMost_Checked(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
        }

        private void ChkTopMost_Unchecked(object sender, RoutedEventArgs e)
        {
            this.Topmost = false;
        }
    }
}
