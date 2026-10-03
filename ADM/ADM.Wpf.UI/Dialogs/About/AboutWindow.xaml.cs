using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ADM.Core;
using ADM.Core.Navigation;
using ADM.Core.Util;
using ADM.Wpf.UI.Common;
using ADM.Wpf.UI.Win32;
using TraceLog;

namespace ADM.Wpf.UI.Dialogs.About
{
    public partial class AboutWindow : Window, IDialog
    {
        private readonly IExternalNavigationService externalNavigationService;

        public AboutWindow(IExternalNavigationService externalNavigationService)
        {
            this.externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
            InitializeComponent();
            this.TxtAppVersion.Text = AppInfo.APP_VERSION_TEXT;
            this.TxtCopyright.Text = AppInfo.APP_COPYRIGHT_TEXT;
            this.TxtDeveloper.Text = ProductIdentity.DeveloperName;
            this.TxtTelegram.Text = ProductIdentity.DeveloperTelegram;
            this.TxtWebsite.Text = AppInfo.APP_HOMEPAGE_TEXT;
            this.TxtOSInfo.Text = Environment.OSVersion.ToString();
            this.TxtNetFxInfo.Text = GetNetImageVersion();
            this.TxtMSIXInfo.Text = "App container: " + MsixHelper.IsAppContainer;
            this.TxtBaseVersion.Text = AppInfo.BASE_TEXT;
            BuildReleaseNotes();
        }

        private void BuildReleaseNotes()
        {
            NotesPanel.Children.Add(new TextBlock { Text = ReleaseNotes.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            foreach (var section in ReleaseNotes.Sections)
            {
                NotesPanel.Children.Add(new TextBlock { Text = section.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
                foreach (var item in section.Items)
                {
                    var line = new TextBlock { Text = "\u2022  " + item, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 6) };
                    NotesPanel.Children.Add(line);
                }
            }
            var footer = new TextBlock { Text = AppInfo.BASE_TEXT, Margin = new Thickness(0, 10, 0, 0), FontSize = 11 };
            footer.SetResourceReference(TextBlock.ForegroundProperty, "GlassMutedTextBrush");
            NotesPanel.Children.Add(footer);
        }

        public bool Result { get; set; }

        private string GetNetImageVersion()
        {
            try
            {
#if NET35
                return Environment.Version.ToString();
#else
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes(true).OfType<TargetFrameworkAttribute>().First().FrameworkDisplayName;
#endif
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Target framework metadata lookup failed");
                return Environment.Version.ToString();
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

        private void BtnTelegram_Click(object sender, RoutedEventArgs e)
        {
            externalNavigationService.OpenUrl(ProductIdentity.DeveloperTelegramUrl);
        }

        private void BtnGitHub_Click(object sender, RoutedEventArgs e)
        {
            externalNavigationService.OpenUrl(Links.HomePageUrl);
        }
    }
}
