using System;
using System.Windows;
using System.Windows.Controls;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class PasswordManagerView : UserControl
    {
        private CredentialSettingsViewModel? viewModel;
        public Window Window { get; set; }

        public PasswordManagerView()
        {
            InitializeComponent();
        }

        public void AttachViewModel(CredentialSettingsViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
        }


        private void CatAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PasswordWindow { Owner = Window };
            var ret = dlg.ShowDialog(Window);
            if (ret.HasValue && ret.Value)
            {
                GetViewModel().Add(dlg.HostName, dlg.UserName, dlg.Password);
            }
        }

        private void CatEdit_Click(object sender, RoutedEventArgs e)
        {
            var index = LvPasswords.SelectedIndex;
            if (index >= 0)
            {
                var model = GetViewModel();
                var dlg = new PasswordWindow { Owner = Window };
                dlg.SetPassword(model.GetAt(index));
                var ret = dlg.ShowDialog(Window);
                if (ret.HasValue && ret.Value)
                {
                    model.Replace(index, dlg.HostName, dlg.UserName, dlg.Password);
                }
            }
        }

        private void CatDel_Click(object sender, RoutedEventArgs e)
        {
            var index = LvPasswords.SelectedIndex;
            if (index >= 0)
            {
                GetViewModel().RemoveAt(index);
            }
        }

        private CredentialSettingsViewModel GetViewModel() =>
            viewModel ?? throw new InvalidOperationException("Credential Settings ViewModel was not attached.");
    }
}
