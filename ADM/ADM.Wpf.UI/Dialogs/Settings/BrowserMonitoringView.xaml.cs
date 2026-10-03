using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TraceLog;
using Translations;
using ADM.Core.BrowserMonitoring;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class BrowserMonitoringView : UserControl
    {
        private BrowserMonitoringSettingsViewModel? viewModel;

        public BrowserMonitoringView()
        {
            InitializeComponent();
        }

        internal void AttachViewModel(BrowserMonitoringSettingsViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
        }


        private BrowserMonitoringSettingsViewModel GetViewModel()
        {
            return viewModel ?? throw new InvalidOperationException("Browser Monitoring Settings view model has not been attached.");
        }

        private void BrowserButtonClick(Browser browser)
        {
            try
            {
                GetViewModel().LaunchBrowser(browser);
                MessageBox.Show(TextResource.GetText("MSG_BROWSER_LOAD_UNPACKED"));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error launching " + browser);
                MessageBox.Show($"{TextResource.GetText("MSG_BROWSER_LAUNCH_FAILED")} {browser}");
            }
        }

        private void BtnChrome_Click(object sender, RoutedEventArgs e) => BrowserButtonClick(Browser.Chrome);
        private void BtnEdge_Click(object sender, RoutedEventArgs e) => BrowserButtonClick(Browser.MSEdge);
        private void BtnOpera_Click(object sender, RoutedEventArgs e) => BrowserButtonClick(Browser.Opera);
        private void BtnBrave_Click(object sender, RoutedEventArgs e) => BrowserButtonClick(Browser.Brave);
        private void BtnVivaldi_Click(object sender, RoutedEventArgs e) => BrowserButtonClick(Browser.Vivaldi);

        private void BtnFirefox_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel().PrepareFirefoxExtension();
            try
            {
                GetViewModel().LaunchFirefox();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error launching Firefox");
                MessageBox.Show($"{TextResource.GetText("MSG_BROWSER_LAUNCH_FAILED")} Firefox");
            }
        }

        private void BtnCopy1_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(GetViewModel().ChromeWebStoreUrl);
        private void BtnCopy2_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(GetViewModel().FirefoxExtensionUrl);
        private void BtnDefault1_Click(object sender, RoutedEventArgs e) => GetViewModel().ResetFileExtensions();
        private void BtnDefault2_Click(object sender, RoutedEventArgs e) => GetViewModel().ResetVideoExtensions();
        private void BtnDefault3_Click(object sender, RoutedEventArgs e) => GetViewModel().ResetBlockedHosts();
        private void VideoWikiLink_MouseDown(object sender, MouseButtonEventArgs e) => GetViewModel().OpenVideoTutorial();
    }
}
