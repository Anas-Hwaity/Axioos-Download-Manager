using System;
using System.Windows;
using System.Windows.Controls;
using ADM.Wpf.UI.Dialogs.Settings.ViewModels;
using WinForms = System.Windows.Forms;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class GeneralSettingsView : UserControl
    {
        private GeneralSettingsViewModel? viewModel;
        public Window Window { get; set; }

        public GeneralSettingsView()
        {
            InitializeComponent();
        }

        public void AttachViewModel(GeneralSettingsViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
        }


        private GeneralSettingsViewModel GetViewModel() =>
            viewModel ?? throw new InvalidOperationException("General Settings ViewModel was not attached.");

        private void CatAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoryEditWindow { Owner = Window };
            var ret = dlg.ShowDialog(Window);
            if (ret.HasValue && ret.Value)
                GetViewModel().AddCategory(dlg.CategoryName, dlg.Folder, dlg.FileTypes);
        }

        private void CatEdit_Click(object sender, RoutedEventArgs e)
        {
            var index = LvCategories.SelectedIndex;
            if (index < 0) return;
            var dlg = new CategoryEditWindow { Owner = Window };
            dlg.SetCategory(GetViewModel().GetCategoryAt(index));
            var ret = dlg.ShowDialog(Window);
            if (ret.HasValue && ret.Value)
                GetViewModel().ReplaceCategory(index, dlg.CategoryName, dlg.Folder, dlg.FileTypes);
        }

        private void CatDel_Click(object sender, RoutedEventArgs e)
        {
            var index = LvCategories.SelectedIndex;
            if (index >= 0) GetViewModel().RemoveCategoryAt(index);
        }

        private void CatDef_Click(object sender, RoutedEventArgs e) => GetViewModel().ResetCategories();

        private void BtnTempFolderBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var folderBrowser = new WinForms.FolderBrowserDialog();
            if (folderBrowser.ShowDialog() == WinForms.DialogResult.OK)
                GetViewModel().TempFolder = folderBrowser.SelectedPath;
        }

        private void BtnDownloadFolderBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var folderBrowser = new WinForms.FolderBrowserDialog();
            if (folderBrowser.ShowDialog() == WinForms.DialogResult.OK)
                GetViewModel().DefaultDownloadFolder = folderBrowser.SelectedPath;
        }
    }
}
