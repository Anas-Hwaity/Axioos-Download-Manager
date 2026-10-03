using System;
using System.Windows;
using System.Windows.Interop;
using ADM.Core;
using ADM.Wpf.UI.Common;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.LanguageSettings
{
    public partial class LanguageSettingsWindow : Window, IDialog
    {
        private readonly LanguageSettingsViewModel viewModel;
        public bool Result { get; set; } = false;

        public LanguageSettingsWindow(LanguageSettingsViewModel viewModel)
        {
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = viewModel;
            viewModel.Reload();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NativeMethods.DisableMinMaxButton(this);
#if NET45_OR_GREATER
            if (App.Skin == Skin.Dark)
            {
                var helper = new WindowInteropHelper(this);
                helper.EnsureHandle();
                DarkModeHelper.UseImmersiveDarkMode(helper.Handle, true);
            }
#endif
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel.SaveSelectedLanguage())
            {
                Close();
                Result = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Result = false;
        }
    }
}
