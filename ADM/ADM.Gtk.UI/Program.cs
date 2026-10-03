using System;
using System.Net;
using Gtk;
using TraceLog;
using Translations;
using ADM.Core;
using ADM.Core.DataAccess;
using ADMApp = ADM.Core.Application;
using System.Linq;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Legacy;
using ADM.Core.Rules;
using ADM.Core.Telemetry;
using ADM.Core.Util;

namespace ADM.GtkUI
{
    class Program
    {
        private const string DisableCachingName = @"TestSwitch.LocalAppContext.DisableCaching";

        static void Main(string[] args)
        {
            Config.LoadConfig();
            var debugMode = Environment.GetEnvironmentVariable("ADM_DEBUG_MODE");
            if (!string.IsNullOrEmpty(debugMode) && debugMode == "1")
            {
                var logFile = System.IO.Path.Combine(Config.AppDir, "log.txt");
                Log.InitFileBasedTrace(System.IO.Path.Combine(Config.AppDir, "log.txt"));
            }
            Log.Debug("Application_Startup");
            Environment.SetEnvironmentVariable("GTK_USE_PORTAL", "1");
            Gtk.Application.Init("adm-app", ref args);
            GLib.ExceptionManager.UnhandledException += ExceptionManager_UnhandledException;
            var globalStyleSheet = @"
                                    .large-font{ font-size: 16px; }
                                    .medium-font{ font-size: 14px; }
                                    ";

            var screen = Gdk.Screen.Default;
            var provider = new CssProvider();
            provider.LoadFromData(globalStyleSheet);
            Gtk.StyleContext.AddProviderForScreen(screen, provider, 800);


            ServicePointManager.DefaultConnectionLimit = 100;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

            AppContext.SetSwitch(DisableCachingName, true);

            Log.Debug("Loading languages...");

            LoadLanguageTexts();

            if (Config.Instance.AllowSystemDarkTheme)
            {
                Gtk.Settings.Default.ThemeName = "Adwaita";
                Gtk.Settings.Default.ApplicationPreferDarkTheme = true;
            }

            var runtimeContext = new LegacyApplicationRuntimeContext();
            var downloadRulePolicy = new DownloadRulePolicy(new RuleEngine(), new EmptyDownloadRuleProvider());
            var downloadTelemetryService = new DownloadTelemetryService();
            var core = new ApplicationCore(new BrowserMonitor(runtimeContext), runtimeContext, downloadRulePolicy, downloadTelemetryService);
            var videoTracker = new VideoTracker(runtimeContext);
            var downloadCreationPreferences = new LegacyDownloadCreationPreferences();
            var mainCommands = new MainCommandService(runtimeContext);
            var app = new ADMApp(core, videoTracker, downloadCreationPreferences, mainCommands, runtimeContext);
            var win = new MainWindow();
            var linkRefresher = new LinkRefresher();
            var platformUIService = new GtkPlatformUIService(win, app, core, downloadCreationPreferences, runtimeContext, linkRefresher);

            Log.Debug("Configuring app context...");

            ApplicationContext.FirstRunCallback += ApplicationContext_FirstRunCallback;
            ApplicationContext.Configurer()
                .RegisterApplicationWindow(win)
                .RegisterApplication(app)
                .RegisterApplicationCore(core)
                .RegisterCapturedVideoTracker(videoTracker)
                .RegisterClipboardMonitor(new ClipboardMonitor(runtimeContext))
                .RegisterLinkRefresher(linkRefresher)
                .RegisterPlatformUIService(platformUIService)
                .Configure();

            Log.Debug("Processing arguments...");

            ArgsProcessor.Process(args, runtimeContext);

            Log.Debug("Gtk Run...");

            Gtk.Application.Run();
        }

        private static void ApplicationContext_FirstRunCallback(object? sender, EventArgs e)
        {
            PlatformHelper.EnableAutoStart(true);
        }

        private static void ExceptionManager_UnhandledException(GLib.UnhandledExceptionArgs args)
        {
            Log.Debug("GLib ExceptionManager_UnhandledException: " + args.ExceptionObject);
            args.ExitApplication = false;
        }

        private static void LoadLanguageTexts()
        {
            Log.Debug("Language loading ...");
            try
            {
                var indexFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Lang\index.txt");
                if (System.IO.File.Exists(indexFile))
                {
                    var lines = System.IO.File.ReadAllLines(indexFile);
                    foreach (var line in lines)
                    {
                        var index = line.IndexOf("=");
                        if (index > 0)
                        {
                            var name = line.Substring(0, index);
                            var value = line.Substring(index + 1);
                            if (name == Config.Instance.Language)
                            {
                                TextResource.Load(value);
                                break;
                            }
                        }
                    }
                }
                Log.Debug("Language loaded.");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
        }
    }
}
