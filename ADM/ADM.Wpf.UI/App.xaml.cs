using System;
using System.Net;
using System.Linq;
using System.Windows;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;
using System.Windows.Interop;
using ADM.Core.DataAccess;
using System.IO;
using ADMApp = ADM.Core.Application;
using ADM.Core.BrowserMonitoring;
using System.Diagnostics;
using ADM.Wpf.UI.Diagnostics;

namespace ADM.Wpf.UI
{
    public partial class App : System.Windows.Application
    {
        private const string DisableCachingName = @"TestSwitch.LocalAppContext.DisableCaching";

        public static Skin Skin = Skin.Dark;
        private ApplicationCore core;
        private ADMApp app;
        private MainWindow win;

        internal GlassAppearanceService GlassAppearance { get; } = new GlassAppearanceService();

        internal AxioosThemeService ThemeService { get; } = new AxioosThemeService();

        public App()
        {
            ServicePointManager.DefaultConnectionLimit = 100;

#if NET45_OR_GREATER
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#endif
#if NET46_OR_GREATER

            AppContext.SetSwitch(DisableCachingName, true);
#endif
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Trace.WriteLine("ADM app start");
            AcceptanceDiagnostics.RecordStage("application.startup.enter");
            var debugMode = Environment.GetEnvironmentVariable("ADM_DEBUG_MODE");
            if (!string.IsNullOrEmpty(debugMode) && debugMode == "1")
            {
                var logFile = Path.Combine(Config.AppDir, "log.txt");
                Log.InitFileBasedTrace(Path.Combine(Config.AppDir, "log.txt"));
            }
            Log.Debug($"Application_Startup::argCount->{Math.Max(0, Environment.GetCommandLineArgs().Length - 1)}");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            var runtimeContext = DesktopCompositionRoot.CreateRuntimeContext();
            Resources.MergedDictionaries.Insert(0, DesktopCompositionRoot.CreateTranslationResourceDictionary());
            try
            {
                AxioosThemeService.Apply(runtimeContext.AppearanceTheme, runtimeContext.AppearanceBackdrop, runtimeContext.AppearanceAccent);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Axioos theme startup failure");
            }
            if (!AcceptanceTestEnvironment.IsEnabled) RecordDesktopLocation();
            try
            {
                GlassThemeManager.Apply(runtimeContext.GlassmorphismLevel);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Glass theme startup failure");
            }

            if (AcceptanceTestEnvironment.SettingsConstructionEnabled)
            {
                AcceptanceDiagnostics.RecordStage("settings-construction.matrix.start");
                var exitCode = SettingsConstructionAcceptance.Run();
                AcceptanceDiagnostics.RecordStage("settings-construction.matrix.complete", exitCode.ToString());
                Shutdown(exitCode);
                return;
            }

            var composition = DesktopCompositionRoot.Compose(runtimeContext);
            core = composition.Core;
            win = composition.MainWindow;
            app = composition.Application;
            composition.Configure((callbackSender, callbackArgs) =>
                ApplicationContext_FirstRunCallback(
                    composition.Application,
                    composition.MainWindow,
                    composition.PlatformUIService,
                    composition.RuntimeContext));

            ArgsProcessor.Process(Environment.GetCommandLineArgs().Skip(1), composition.RuntimeContext);

            AppTrayIcon.AttachToSystemTray(composition.PlatformUIService.CreateAndShowMediaGrabber);
            AppTrayIcon.TrayClick += (_, _) =>
            {
                win.ShowAndActivate();
            };
        }

        private static void RecordDesktopLocation()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exe == null || exe.Length == 0 || !File.Exists(exe)) return;
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ProductIdentity.DesktopRegistryKey))
                {
                    key?.SetValue(ProductIdentity.DesktopPathValue, exe);
                }
                using (var scheme = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProductIdentity.CustomProtocolScheme))
                {
                    scheme?.SetValue(null, "URL:" + ProductIdentity.DisplayName);
                    scheme?.SetValue("URL Protocol", string.Empty);
                }
                using (var command = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProductIdentity.CustomProtocolScheme + "\\shell\\open\\command"))
                {
                    command?.SetValue(null, "\"" + exe + "\" \"%1\"");
                }
                var folder = Path.GetDirectoryName(exe)!;
                var host = new[]
                {
                    Path.Combine(folder, ProductIdentity.NativeHostFolderName, ProductIdentity.NativeHostExecutableName),
                    Path.Combine(folder, ProductIdentity.NativeHostExecutableName)
                }.FirstOrDefault(File.Exists);
                if (host == null) return;
                var browsers = new[]
                {
                    @"Software\Google\Chrome\NativeMessagingHosts\",
                    @"Software\Microsoft\Edge\NativeMessagingHosts\",
                    @"Software\Chromium\NativeMessagingHosts\",
                    @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\"
                };
                string? manifestPath = null;
                foreach (var browser in browsers)
                {
                    var keyPath = browser + ProductIdentity.ChromeNativeHostName;
                    string? current = null;
                    using (var existing = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
                    {
                        current = existing?.GetValue(null) as string;
                    }
                    if (current != null && File.Exists(current) && ManifestPointsToHost(current, host)) continue;
                    if (manifestPath == null)
                    {
                        Directory.CreateDirectory(Config.AppDir);
                        manifestPath = Path.Combine(Config.AppDir, ProductIdentity.ChromeNativeHostName + ".json");
                        var manifest = new Newtonsoft.Json.Linq.JObject
                        {
                            ["name"] = ProductIdentity.ChromeNativeHostName,
                            ["description"] = "Native messaging host for " + ProductIdentity.DisplayName,
                            ["path"] = host,
                            ["type"] = "stdio",
                            ["allowed_origins"] = new Newtonsoft.Json.Linq.JArray(ProductIdentity.OfficialExtensionOrigin, ProductIdentity.UnpackedExtensionOrigin)
                        };
                        File.WriteAllText(manifestPath, manifest.ToString());
                    }
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                    {
                        key?.SetValue(null, manifestPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Desktop location or browser host registration failed");
            }
        }

        private static bool ManifestPointsToHost(string manifestPath, string host)
        {
            try
            {
                var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifestPath));
                var path = manifest["path"]?.ToString();
                if (path == null || path.Length == 0) return false;
                if (!Path.IsPathRooted(path)) path = Path.Combine(Path.GetDirectoryName(manifestPath)!, path);
                return string.Equals(Path.GetFullPath(path), Path.GetFullPath(host), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Existing browser host manifest could not be read");
                return false;
            }
        }

        private void ApplicationContext_FirstRunCallback(
            ADMApp application,
            MainWindow mainWindow,
            IPlatformUIService platformUIService,
            IApplicationRuntimeContext runtimeContext)
        {
            MsixHelper.CopyExtension();
            if (!MsixHelper.IsAppContainer)
            {
                Log.Debug("Not running inside app container");
                runtimeContext.EnableRunOnLogon();
            }
            application.RunOnUiThread(() =>
            {
                mainWindow.ShowAndActivate();
                platformUIService.ShowBrowserMonitoringDialog();
            });
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            AppTrayIcon.DetachFromSystemTray();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Debug(string.Format("Unhandled exception caught {0} and will {1}",
                   e.ExceptionObject,
                   e.IsTerminating ? "Terminating" : "Continue"));
            if (e.ExceptionObject is Exception exception)
            {
                AcceptanceDiagnostics.RecordException("app-domain.unhandled", exception);
            }
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Debug(string.Format("Unhandled dispatcher exception caught {0}; application will terminate",
                e.Exception));
            AcceptanceDiagnostics.RecordException("dispatcher.unhandled", e.Exception);
            if (AcceptanceTestEnvironment.IsEnabled)
            {
                AcceptanceDiagnostics.RecordStage("dispatcher.unhandled.fatal");
            }

            e.Handled = false;
        }

        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            Log.Debug("Application_SessionEnding: Session ending message received...");
            Environment.Exit(0);
        }
    }

    public enum Skin { Light, Dark }
}
