using System;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Legacy;
using ADM.Core.Navigation;
using ADM.Core.Rules;
using ADM.Core.Telemetry;
using ADM.Wpf.UI.Dialogs.Settings;
using ADM.Wpf.UI.Dialogs.LanguageSettings;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;
using ADM.Wpf.UI.Dialogs.Settings.Services;
using ADM.Wpf.UI.Navigation;
using ADM.Wpf.UI.Common.Helpers;
using ADM.Wpf.UI.Rules;
using ADM.Wpf.UI.Dashboard;
using ADM.Wpf.UI.History;
using ADM.Core.DataAccess;
using ADMApp = ADM.Core.Application;

namespace ADM.Wpf.UI
{
    internal sealed class DesktopComposition
    {
        private readonly ILegacyApplicationContextAdapter applicationContextAdapter;
        private readonly IVideoTracker videoTracker;
        private readonly IClipboardMonitor clipboardMonitor;
        private readonly ILinkRefresher linkRefresher;

        internal DesktopComposition(
            ApplicationCore core,
            ADMApp application,
            MainWindow mainWindow,
            IPlatformUIService platformUIService,
            ILegacyApplicationContextAdapter applicationContextAdapter,
            IVideoTracker videoTracker,
            IClipboardMonitor clipboardMonitor,
            ILinkRefresher linkRefresher,
            IDownloadTelemetryService downloadTelemetryService,
            IDownloadDashboardCommandService downloadDashboardCommandService,
            IApplicationRuntimeContext runtimeContext)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
            Application = application ?? throw new ArgumentNullException(nameof(application));
            MainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            PlatformUIService = platformUIService ?? throw new ArgumentNullException(nameof(platformUIService));
            this.applicationContextAdapter = applicationContextAdapter ?? throw new ArgumentNullException(nameof(applicationContextAdapter));
            this.videoTracker = videoTracker ?? throw new ArgumentNullException(nameof(videoTracker));
            this.clipboardMonitor = clipboardMonitor ?? throw new ArgumentNullException(nameof(clipboardMonitor));
            this.linkRefresher = linkRefresher ?? throw new ArgumentNullException(nameof(linkRefresher));
            DownloadTelemetryService = downloadTelemetryService ?? throw new ArgumentNullException(nameof(downloadTelemetryService));
            DownloadDashboardCommandService = downloadDashboardCommandService ?? throw new ArgumentNullException(nameof(downloadDashboardCommandService));
            RuntimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        }

        internal ApplicationCore Core { get; }
        internal ADMApp Application { get; }
        internal MainWindow MainWindow { get; }
        internal IPlatformUIService PlatformUIService { get; }
        internal IDownloadTelemetryService DownloadTelemetryService { get; }
        internal IDownloadDashboardCommandService DownloadDashboardCommandService { get; }
        internal IApplicationRuntimeContext RuntimeContext { get; }

        internal void Configure(EventHandler firstRunCallback)
        {
            applicationContextAdapter.Configure(
                firstRunCallback,
                MainWindow,
                Application,
                Core,
                videoTracker,
                clipboardMonitor,
                linkRefresher,
                PlatformUIService);
        }
    }

    internal static class DesktopCompositionRoot
    {
        internal static IApplicationRuntimeContext CreateRuntimeContext()
        {
            return new LegacyApplicationRuntimeContext();
        }

        internal static TranslationResourceDictionary CreateTranslationResourceDictionary()
        {
            var languageSettingsService = new LegacyLanguageSettingsService();
            return new TranslationResourceDictionary(languageSettingsService);
        }

        internal static DesktopComposition Compose()
        {
            return Compose(CreateRuntimeContext());
        }

        internal static DesktopComposition Compose(IApplicationRuntimeContext runtimeContext)
        {
            if (runtimeContext == null) throw new ArgumentNullException(nameof(runtimeContext));
            var applicationContextAdapter = new LegacyApplicationContextAdapter();
            var browserMonitoringService = new BrowserMonitor(runtimeContext);
            var ruleProvider = new PersistentDownloadRuleProvider(runtimeContext.DownloadRulesFile);
            var downloadRulePolicy = new DownloadRulePolicy(new RuleEngine(), ruleProvider);
            var downloadTelemetryService = new DownloadTelemetryService();
            var core = new ApplicationCore(browserMonitoringService, runtimeContext, downloadRulePolicy, downloadTelemetryService);
            var externalNavigationService = new LegacyExternalNavigationService();
            var mainWindow = new MainWindow(externalNavigationService, runtimeContext);
            mainWindow.ShellTelemetry = downloadTelemetryService;
            var videoTracker = new VideoTracker(runtimeContext);
            var downloadCreationPreferences = new LegacyDownloadCreationPreferences();
            var mainCommands = new MainCommandService(runtimeContext);
            var application = new ADMApp(core, videoTracker, downloadCreationPreferences, mainCommands, runtimeContext);
            var clipboardMonitor = new ClipboardMonitor(runtimeContext);
            var linkRefresher = new LinkRefresher();
            var navigationService = new WpfNavigationService(mainWindow, index => CreateSettingsWindow(index, runtimeContext, ruleProvider), CreateLanguageSettingsWindow);
            mainWindow.LanguageSettingsClicked += (_, _) => navigationService.ShowLanguageSettings();
            var platformUIService = new WpfPlatformUIService(mainWindow, navigationService, application, core, downloadCreationPreferences, videoTracker, runtimeContext, linkRefresher, externalNavigationService);
            var downloadDashboardCommandService = new DownloadDashboardCommandService(core, platformUIService);
            mainWindow.MediaGrabberClicked += (_, _) => platformUIService.CreateAndShowMediaGrabber();
            mainWindow.HistoryClicked += (_, _) =>
            {
                var historyQuery = AppDB.Instance.History;
                if (historyQuery == null) return;
                var historyWindow = new HistoryWindow(historyQuery, new HistoryActionService(core)) { Owner = WindowOwner.Usable(mainWindow) };
                historyWindow.ShowDialog();
            };
            var dashboardLauncher = new DownloadDashboardLauncher(
                mainWindow,
                () => new DownloadDashboardViewModel(
                    downloadTelemetryService,
                    downloadDashboardCommandService,
                    new WpfDashboardUiDispatcher(mainWindow.Dispatcher)),
                () => platformUIService.ShowMessageBox(mainWindow, Translations.TextResource.GetText("MSG_DASHBOARD_FAILED")));
            mainWindow.DashboardClicked += (_, _) => dashboardLauncher.Show();
            return new DesktopComposition(
                core,
                application,
                mainWindow,
                platformUIService,
                applicationContextAdapter,
                videoTracker,
                clipboardMonitor,
                linkRefresher,
                downloadTelemetryService,
                downloadDashboardCommandService,
                runtimeContext);
        }
        internal static LanguageSettingsWindow CreateLanguageSettingsWindow()
        {
            var languageSettingsService = new LegacyLanguageSettingsService();
            var languageSettingsViewModel = new LanguageSettingsViewModel(languageSettingsService);
            return new LanguageSettingsWindow(languageSettingsViewModel);
        }

        internal static SettingsWindow CreateSettingsWindow(int selectedPageIndex)
        {
            return CreateSettingsWindow(selectedPageIndex, new LegacyApplicationRuntimeContext());
        }

        internal static SettingsWindow CreateSettingsWindow(int selectedPageIndex, IApplicationRuntimeContext runtimeContext)
        {
            return CreateSettingsWindow(selectedPageIndex, runtimeContext, new PersistentDownloadRuleProvider(runtimeContext.DownloadRulesFile));
        }

        internal static SettingsWindow CreateSettingsWindow(int selectedPageIndex, IApplicationRuntimeContext runtimeContext, IMutableDownloadRuleProvider ruleProvider)
        {
            var networkSettingsService = new LegacyNetworkSettingsService();
            var networkSettingsViewModel = new NetworkSettingsViewModel(networkSettingsService);
            var advancedSettingsService = new LegacyAdvancedSettingsService();
            var advancedSettingsViewModel = new AdvancedSettingsViewModel(advancedSettingsService, !MsixHelper.IsAppContainer);
            var credentialSettingsService = new LegacyCredentialSettingsService();
            var credentialSettingsViewModel = new CredentialSettingsViewModel(credentialSettingsService);
            var browserMonitoringSettingsService = new LegacyBrowserMonitoringSettingsService();
            var browserMonitoringSettingsViewModel = new BrowserMonitoringSettingsViewModel(browserMonitoringSettingsService);
            var generalSettingsService = new LegacyGeneralSettingsService();
            var generalSettingsViewModel = new GeneralSettingsViewModel(generalSettingsService);
            var settingsCommitService = new LegacySettingsCommitService(runtimeContext);
            var ruleEditorViewModel = new RuleEditorViewModel(ruleProvider, new RuleEngine());
            var settingsWindowViewModel = new SettingsWindowViewModel(networkSettingsViewModel, advancedSettingsViewModel, credentialSettingsViewModel, browserMonitoringSettingsViewModel, generalSettingsViewModel, ruleEditorViewModel, settingsCommitService);
            return new SettingsWindow(selectedPageIndex, settingsWindowViewModel);
        }

    }
}
