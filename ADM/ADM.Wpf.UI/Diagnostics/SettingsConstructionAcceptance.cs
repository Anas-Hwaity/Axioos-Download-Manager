using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using ADM.Core.Util;
using ADM.Wpf.UI.Dialogs.Settings;

namespace ADM.Wpf.UI.Diagnostics
{
    internal static class SettingsConstructionAcceptance
    {
        private sealed class MatrixCase
        {
            public string name { get; set; } = string.Empty;
            public string suite { get; set; } = "settings-construction";
            public string status { get; set; } = "failed";
            public string detail { get; set; } = string.Empty;
        }

        public static int Run()
        {
            var resultPath = AcceptanceTestEnvironment.SettingsConstructionResultPath;
            if (string.IsNullOrWhiteSpace(resultPath) || !Path.IsPathRooted(resultPath))
            {
                throw new InvalidOperationException("Absolute Settings construction result path is required.");
            }

            var cases = new List<MatrixCase>();
            RunCase(cases, "wpf-sta-thread", () =>
            {
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                {
                    throw new InvalidOperationException("Settings construction matrix is not running on an STA thread.");
                }
            });

            RunCase(cases, "production-application-resources", () =>
            {
                if (Application.Current?.Resources == null)
                {
                    throw new InvalidOperationException("Production Application resources are unavailable.");
                }
            });

            ProbeMergedDictionaries(cases);

            RunCase(cases, "child-view.BrowserMonitoringView", () => { _ = new BrowserMonitoringView(); });
            RunCase(cases, "child-view.GeneralSettingsView", () => { _ = new GeneralSettingsView(); });
            RunCase(cases, "child-view.NetworkSettingsView", () => { _ = new NetworkSettingsView(); });
            RunCase(cases, "child-view.PasswordManagerView", () => { _ = new PasswordManagerView(); });
            RunCase(cases, "child-view.AdvancedSettingsView", () => { _ = new AdvancedSettingsView(); });
            RunCase(cases, "child-view.AppearanceSettingsView", () => { _ = new AppearanceSettingsView(); });
            RunCase(cases, "axioos-theme.every-theme-builds", () =>
            {
                foreach (var theme in AxioosThemes.Themes)
                {
                    foreach (var backdrop in AxioosThemes.Backdrops)
                    {
                        foreach (var accent in AxioosThemes.Accents)
                        {
                            var dictionary = AxioosThemeService.Build(theme, backdrop.Id, accent.Id);
                            if (!(dictionary["GlassTextBrush"] is System.Windows.Media.SolidColorBrush))
                                throw new InvalidOperationException("Theme " + theme.Id + " did not produce a text brush.");
                        }
                    }
                }
            });

            RunCase(cases, "settings-window.general-route", () =>
            {
                var window = DesktopCompositionRoot.CreateSettingsWindow(1);
                window.Close();
            });
            RunCase(cases, "settings-window.browser-monitoring-route", () =>
            {
                var window = DesktopCompositionRoot.CreateSettingsWindow(0);
                window.Close();
            });
            RunCase(cases, "settings-window.appearance-route", () =>
            {
                var window = DesktopCompositionRoot.CreateSettingsWindow(5);
                window.Close();
            });

            var failed = cases.Count(item => item.status != "passed");
            var document = new
            {
                schemaVersion = 1,
                scope = "phase-0-settings-production-resource-construction",
                correlationId = AcceptanceTestEnvironment.CorrelationId ?? string.Empty,
                overall = failed == 0 ? "passed" : "failed",
                testCases = cases,
            };

            var directory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(resultPath, JsonConvert.SerializeObject(document, Formatting.Indented) + Environment.NewLine);
            return failed == 0 ? 0 : 1;
        }

        private static void ProbeMergedDictionaries(List<MatrixCase> cases)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count == 0)
            {
                RunCase(cases, "resource-dictionary.none", () =>
                {
                    throw new InvalidOperationException("Production Application has no merged resource dictionaries.");
                });
                return;
            }

            for (var index = 0; index < merged.Count; index++)
            {
                var dictionary = merged[index];
                var capturedIndex = index;
                var name = $"resource-dictionary.{capturedIndex:D2}.{dictionary.GetType().Name}";
                RunCase(cases, name, () => ProbeDictionary(dictionary));
            }
        }

        private static void ProbeDictionary(ResourceDictionary production)
        {
            if (production is SkinResourceDictionary skin)
            {
                var reload = new SkinResourceDictionary
                {
                    LightSource = skin.LightSource,
                    DarkSource = skin.DarkSource,
                };
                _ = reload.Count;
                return;
            }

            if (production is TranslationResourceDictionary)
            {
                var reload = DesktopCompositionRoot.CreateTranslationResourceDictionary();
                _ = reload.Count;
                return;
            }

            if (production.Source != null)
            {
                var reload = new ResourceDictionary { Source = production.Source };
                _ = reload.Count;
                return;
            }

            foreach (var key in production.Keys)
            {
                if (!production.Contains(key))
                {
                    throw new InvalidOperationException("Inline production resource key could not be resolved: " + key);
                }
                _ = production[key];
            }
        }

        private static void RunCase(List<MatrixCase> cases, string name, Action action)
        {
            AcceptanceDiagnostics.RecordStage("settings-construction.case.start", name);
            try
            {
                action();
                cases.Add(new MatrixCase { name = name, status = "passed", detail = "constructed/probed with production resources" });
                AcceptanceDiagnostics.RecordStage("settings-construction.case.complete", name);
            }
            catch (Exception exception)
            {
                cases.Add(new MatrixCase { name = name, status = "failed", detail = exception.ToString() });
                AcceptanceDiagnostics.RecordException("settings-construction.case.failed:" + name, exception);
            }
        }
    }
}
