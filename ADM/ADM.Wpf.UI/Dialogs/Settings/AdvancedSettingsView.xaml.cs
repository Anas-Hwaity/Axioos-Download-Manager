using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class AdvancedSettingsView : UserControl
    {
        private AdvancedSettingsViewModel? viewModel;

        public AdvancedSettingsView()
        {
            InitializeComponent();
        }

        public void AttachViewModel(AdvancedSettingsViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
        }


        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var fd = new OpenFileDialog();
            var ret = fd.ShowDialog();
            if (ret.HasValue && ret.Value)
            {
                var model = viewModel ?? throw new InvalidOperationException("Advanced Settings ViewModel was not attached.");
                model.AntiVirusExecutable = fd.FileName;
            }
        }
    }
}
