using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Translations;
using ADM.Core;
using ADM.Core.Navigation;
using ADM.Core.Util;
using YDLWrapper;

namespace ADM.Wpf.UI.Dialogs.VideoDownloader
{
    public partial class VideoDownloaderPage1 : UserControl
    {
        public string UrlText { get => TxtUrl.Text; set => TxtUrl.Text = value; }
        public string UserNameText => TxtUserName.Text;
        public string PasswordText => TxtPassword.Password;
        public bool UseCredentials => ChkAuth.IsChecked.HasValue ? ChkAuth.IsChecked.Value : false;

        public EventHandler? SearchClicked;
        public Window ParentWindow { get; set; }
        private IExternalNavigationService? externalNavigationService;

        public VideoDownloaderPage1()
        {
            InitializeComponent();
        }


        public void AttachExternalNavigationService(IExternalNavigationService externalNavigationService)
        {
            this.externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
        }

        public void InitPage(Window parentWindow)
        {
            this.ParentWindow = parentWindow;
            try
            {
                var exec = YDLProcess.FindYDLBinary();
                ChkReadCookie.Visibility = CmbBrowser.Visibility = exec.BinaryType == YtBinaryType.YtDlp
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                if (MessageBox.Show(ParentWindow, TextResource.GetText("MSG_HELPER_TOOLS_MISSING"), ProductIdentity.ShortName, MessageBoxButton.YesNo)
                    == MessageBoxResult.Yes)
                {
                    externalNavigationService!.OpenUrl(Links.HelperToolsUrl);
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            SearchClicked?.Invoke(this, EventArgs.Empty);
        }

        private void ChkReadCookie_Checked(object sender, RoutedEventArgs e)
        {
            this.CmbBrowser.IsEnabled = ChkReadCookie.IsChecked ?? false;
        }
    }
}
