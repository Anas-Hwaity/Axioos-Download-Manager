using System;
using System.IO;
using System.Windows;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Navigation;
using ADM.Core.Util;
using ADM.Wpf.UI.Win32;
using System.Windows.Interop;

namespace ADM.Wpf.UI.Dialogs.ChromeIntegrator
{
    public partial class IntegrationWindow : Window
    {
        private int page = 0;
        private readonly IApplicationRuntimeContext runtimeContext;
        private readonly IExternalNavigationService externalNavigationService;
        public IntegrationWindow(Browser browser, bool browserLaunched, IApplicationRuntimeContext runtimeContext, IExternalNavigationService externalNavigationService)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
            InitializeComponent();
            runtimeContext.SubscribeApplicationEvent(ApplicationContext_ApplicationEvent);
            if (browserLaunched)
            {
                page = 1;
            }
            Page0.Browser = browser;
            Page2.Browser = browser;
            Page1.Browser = browser;
            RenderPage();
        }

        private void ApplicationContext_ApplicationEvent(object sender, ApplicationEvent e)
        {
            if (e.EventType == "ExtensionRegistered")
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Page4.SuccessResult = true;
                    File.AppendAllText(System.IO.Path.Combine(Config.AppDir, "browser-integration-attempted"), "");
                    BtnBack.Visibility = Visibility.Collapsed;
                }));
            }
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

        private void RenderPage()
        {
            this.Page0.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
            this.Page1.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            this.Page2.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
            this.Page3.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
            this.Page4.Visibility = page == 4 ? Visibility.Visible : Visibility.Collapsed;
            this.BtnNext.Visibility = page == 4 ? Visibility.Collapsed : Visibility.Visible;
            this.BtnBack.Visibility = page == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (page < 4)
            {
                page++;
            }
            RenderPage();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (page > 0)
            {
                page--;
            }
            RenderPage();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            runtimeContext.UnsubscribeApplicationEvent(ApplicationContext_ApplicationEvent);
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            externalNavigationService.OpenUrl(Links.ManualExtensionInstallGuideUrl);
        }
    }
}
