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
using ADM.Core;
using ADM.Core.Navigation;
using ADM.Core.Util;

namespace ADM.Wpf.UI.Dialogs.VideoDownloader
{
    public partial class VideoDownloaderPage4 : UserControl
    {
        private IExternalNavigationService? externalNavigationService;
        public VideoDownloaderPage4()
        {
            InitializeComponent();
        }

        public void AttachExternalNavigationService(IExternalNavigationService externalNavigationService)
        {
            this.externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            externalNavigationService!.OpenUrl(Links.VideoDownloadTutorialUrl);
        }
    }
}
