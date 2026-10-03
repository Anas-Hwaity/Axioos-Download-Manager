using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ADM.Core;
using ADM.Core.Util;
using ADM.Wpf.UI.Common;
using ADM.Wpf.UI.Diagnostics;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class SettingsWindow : Window, IDialog
    {
        private readonly UserControl[] pages;
        private readonly SettingsWindowViewModel viewModel;
        private readonly int originalGlassmorphismLevel;
        private readonly string originalTheme;
        private readonly string originalBackdrop;
        private readonly string originalAccent;
        private bool settingsSaved;

        public SettingsWindow(int selectedPageIndex, SettingsWindowViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            this.viewModel = viewModel;
            originalGlassmorphismLevel = viewModel.General.GlassmorphismLevel;
            originalTheme = viewModel.General.AppearanceTheme;
            originalBackdrop = viewModel.General.AppearanceBackdrop;
            originalAccent = viewModel.General.AppearanceAccent;
            viewModel.General.PropertyChanged += GeneralSettings_PropertyChanged;
            AcceptanceDiagnostics.RecordStage("settings.constructor.enter", selectedPageIndex.ToString());
            AcceptanceDiagnostics.RecordStage("settings.initialize-component.start", selectedPageIndex.ToString());
            InitializeComponent();
            AcceptanceDiagnostics.RecordStage("settings.initialize-component.complete", selectedPageIndex.ToString());
            DataContext = viewModel;
            AttachPage(NetworkSettingsView, viewModel.Network, NetworkSettingsView.AttachViewModel);
            AttachPage(AdvancedSettingsView, viewModel.Advanced, AdvancedSettingsView.AttachViewModel);
            AttachPage(PasswordManagerView, viewModel.Credentials, PasswordManagerView.AttachViewModel);
            AttachPage(BrowserMonitoringView, viewModel.BrowserMonitoring, BrowserMonitoringView.AttachViewModel);
            AttachPage(GeneralSettingsView, viewModel.General, GeneralSettingsView.AttachViewModel);
            AttachPage(RulesView, viewModel.Rules, RulesView.AttachViewModel);
            AttachPage(AppearanceSettingsView, viewModel.General, AppearanceSettingsView.AttachViewModel);
            pages = new UserControl[]
            {
                BrowserMonitoringView,
                GeneralSettingsView,
                NetworkSettingsView,
                PasswordManagerView,
                AdvancedSettingsView,
                AppearanceSettingsView,
                RulesView,
                DiagnosticsView
            };
            LbTitles.SelectedIndex = selectedPageIndex;
            GeneralSettingsView.Window = this;
            PasswordManagerView.Window = this;
            viewModel.Saved += (_, _) =>
            {
                settingsSaved = true;
                AxioosThemeService.Apply(viewModel.General.AppearanceTheme, viewModel.General.AppearanceBackdrop, viewModel.General.AppearanceAccent);
                GlassThemeManager.Apply(viewModel.General.GlassmorphismLevel);
                Close();
                Helpers.RunGC();
            };
            AcceptanceDiagnostics.RecordStage("settings.constructor.return", selectedPageIndex.ToString());
        }

        private static void AttachPage<TViewModel>(UserControl page, TViewModel viewModel, Action<TViewModel> attach)
        {
            AcceptanceDiagnostics.RecordStage("settings.populate-page.start", page.GetType().FullName);
            attach(viewModel);
            AcceptanceDiagnostics.RecordStage("settings.populate-page.complete", page.GetType().FullName);
        }

        public bool Result { get; set; } = false;

        protected override void OnSourceInitialized(EventArgs e)
        {
            AcceptanceDiagnostics.RecordStage("settings.source-initialized.enter");
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
            AcceptanceDiagnostics.RecordStage("settings.source-initialized.return");
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GeneralSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GeneralSettingsViewModel.GlassmorphismLevel))
            {
                GlassThemeManager.Apply(viewModel.General.GlassmorphismLevel);
            }
            else if (e.PropertyName == nameof(GeneralSettingsViewModel.AppearanceTheme) ||
                     e.PropertyName == nameof(GeneralSettingsViewModel.AppearanceBackdrop) ||
                     e.PropertyName == nameof(GeneralSettingsViewModel.AppearanceAccent))
            {
                AxioosThemeService.Apply(viewModel.General.AppearanceTheme, viewModel.General.AppearanceBackdrop, viewModel.General.AppearanceAccent);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            viewModel.General.PropertyChanged -= GeneralSettings_PropertyChanged;
            if (!settingsSaved)
            {
                var current = AxioosThemeService.Current;
                if (current == null || current.ThemeId != originalTheme || current.BackdropId != originalBackdrop || current.AccentId != originalAccent)
                {
                    AxioosThemeService.Apply(originalTheme, originalBackdrop, originalAccent);
                }
                GlassThemeManager.Apply(originalGlassmorphismLevel);
            }
            base.OnClosed(e);
        }

        private void LbTitles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowPage(LbTitles.SelectedIndex);
        }

        private void ShowPage(int index)
        {
            var page = pages[index];
            foreach (var candidate in pages)
            {
                candidate.Visibility = candidate == page ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
