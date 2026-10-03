using System;
using System.Windows;
using System.Windows.Controls;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class AppearanceSettingsView : UserControl
    {
        private GeneralSettingsViewModel? viewModel;

        public AppearanceSettingsView()
        {
            InitializeComponent();
        }

        public void AttachViewModel(GeneralSettingsViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel == null) return;
            if (sender is FrameworkElement element && element.Tag is string presetId)
            {
                viewModel.ApplyPreset(presetId);
            }
        }
    }
}
