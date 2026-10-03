using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class NetworkSettingsView : UserControl
    {
        private NetworkSettingsViewModel? viewModel;

        public NetworkSettingsView()
        {
            InitializeComponent();
        }

        public void AttachViewModel(NetworkSettingsViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
            TxtProxyPassword.Password = model.ProxyPassword;
        }

        private NetworkSettingsViewModel GetViewModel() =>
            viewModel ?? throw new InvalidOperationException("Network Settings ViewModel was not attached.");

        private void TxtProxyPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            GetViewModel().ProxyPassword = TxtProxyPassword.Password;
        }


        private void TxtSpeedLimit_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Int32.TryParse(e.Text, out _);
        }

        private void TxtSpeedLimit_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!Int32.TryParse(text, out _)) e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }

        private void TxtSpeedLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtMaxSpeedLimit.Text) || !Int32.TryParse(TxtMaxSpeedLimit.Text, out _))
            {
                TxtMaxSpeedLimit.Text = "0";
            }
        }
    }
}
