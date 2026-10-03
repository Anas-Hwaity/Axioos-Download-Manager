using System;
using ADM.Core.Navigation;
using ADM.Wpf.UI.Common.Helpers;
using ADM.Wpf.UI.Diagnostics;
using ADM.Wpf.UI.Dialogs.Settings;
using ADM.Wpf.UI.Dialogs.LanguageSettings;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Navigation
{
    public sealed class WpfNavigationService : INavigationService
    {
        private readonly MainWindow mainWindow;
        private readonly Func<int, SettingsWindow> settingsWindowFactory;
        private readonly Func<LanguageSettingsWindow> languageSettingsWindowFactory;
        private SettingsWindow? openSettings;

        public WpfNavigationService(MainWindow mainWindow, Func<int, SettingsWindow> settingsWindowFactory, Func<LanguageSettingsWindow> languageSettingsWindowFactory)
        {
            this.mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            this.settingsWindowFactory = settingsWindowFactory ?? throw new ArgumentNullException(nameof(settingsWindowFactory));
            this.languageSettingsWindowFactory = languageSettingsWindowFactory ?? throw new ArgumentNullException(nameof(languageSettingsWindowFactory));
        }


        public void ShowLanguageSettings()
        {
            var languageSettings = languageSettingsWindowFactory();
            languageSettings.Owner = WindowOwner.Usable(mainWindow);
            languageSettings.ShowDialog(mainWindow);
        }

        public void ShowSettings(SettingsRoute route)
        {
            var pageIndex = (int)route;
            var current = openSettings;
            if (current != null)
            {
                current.Activate();
                return;
            }
            AcceptanceDiagnostics.RecordStage("settings.constructor.start", pageIndex.ToString());
            var settings = settingsWindowFactory(pageIndex);
            settings.Owner = WindowOwner.Usable(mainWindow);
            settings.Closed += (_, _) =>
            {
                if (ReferenceEquals(openSettings, settings)) openSettings = null;
            };
            AcceptanceDiagnostics.RecordStage("settings.constructor.complete", pageIndex.ToString());
            AcceptanceDiagnostics.RecordStage("settings.show-dialog.start", pageIndex.ToString());
            openSettings = settings;
            try
            {
                settings.ShowDialog(mainWindow);
            }
            finally
            {
                if (ReferenceEquals(openSettings, settings)) openSettings = null;
            }
            AcceptanceDiagnostics.RecordStage("settings.show-dialog.return", pageIndex.ToString());
        }
    }
}
