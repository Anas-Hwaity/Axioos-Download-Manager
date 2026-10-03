using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Translations;
using ADM.Core;
using ADM.Core.Legacy;
using ADM.Core.UI;
using ADM.Wpf.UI.Common.Helpers;
using ADM.Wpf.UI.Dialogs.AdvancedDownloadOption;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.NewDownload
{
    public partial class NewDownloadWindow : Window, INewDownloadDialog
    {
        private AuthenticationInfo? authentication;
        private readonly IDownloadCreationPreferences downloadCreationPreferences;
        private ProxyInfo? proxy;
        private int speedLimit;
        private bool enableSpeedLimit;
        private int previousIndex = 0;
        private ObservableCollection<string> dropdownItems = new();

        public NewDownloadWindow(IDownloadCreationPreferences downloadCreationPreferences)
        {
            this.downloadCreationPreferences = downloadCreationPreferences ?? throw new ArgumentNullException(nameof(downloadCreationPreferences));
            proxy = downloadCreationPreferences.DefaultProxy;
            speedLimit = downloadCreationPreferences.DefaultDownloadSpeed;
            enableSpeedLimit = downloadCreationPreferences.EnableSpeedLimit;
            InitializeComponent();
            CmbLocation.ItemsSource = dropdownItems;
        }

        public bool IsEmpty { get => !TxtUrl.IsReadOnly; set => TxtUrl.IsReadOnly = !value; }
        public string Url { get => TxtUrl.Text; set => TxtUrl.Text = value; }
        public AuthenticationInfo? Authentication { get => authentication; set => authentication = value; }
        public ProxyInfo? Proxy { get => proxy; set => proxy = value; }
        public int SpeedLimit { get => speedLimit; set => speedLimit = value; }
        public bool EnableSpeedLimit { get => enableSpeedLimit; set => enableSpeedLimit = value; }
        public string SelectedFileName { get => TxtFile.Text; set => TxtFile.Text = value; }
        public int SeletedFolderIndex
        {
            get => CmbLocation.SelectedIndex;
            set
            {
                CmbLocation.SelectedIndex = value;
                previousIndex = value;
            }
        }

        public event EventHandler? DownloadClicked;
        public event EventHandler? DestroyEvent;
        public event EventHandler? UrlChangedEvent;
        public event EventHandler? UrlBlockedEvent;
        public event EventHandler? QueueSchedulerClicked;
        public event EventHandler<DownloadLaterEventArgs>? DownloadLaterClicked;
        public event EventHandler<FileBrowsedEventArgs>? FileBrowsedEvent;
        public event EventHandler<FileBrowsedEventArgs>? DropdownSelectionChangedEvent;

        public void DisposeWindow()
        {
            this.Close();
        }

        public void Invoke(Action callback)
        {
            Dispatcher.BeginInvoke(callback);
        }

        public void SetFileSizeText(string text)
        {
            this.TxtFileSize.Text = text;
        }

        public void SetFolderValues(string[] values)
        {
            if (dropdownItems.Count > 0)
            {
                dropdownItems = new();
                CmbLocation.ItemsSource = dropdownItems;
            }
            previousIndex = 0;
            foreach (var item in values)
            {
                dropdownItems.Add(item);
            }
        }

        public void ShowMessageBox(string message)
        {
            MessageBox.Show(this, message);
        }

        public void ShowWindow()
        {
            this.Show();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
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
        }

        private void CmbLocation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            e.Handled = true;
            if (CmbLocation.SelectedIndex == 1)
            {
                using var fb = new System.Windows.Forms.FolderBrowserDialog();
                if (fb.ShowDialog(new WinformsWindow(this)) == System.Windows.Forms.DialogResult.OK)
                {
                    this.FileBrowsedEvent?.Invoke(this, new FileBrowsedEventArgs(fb.SelectedPath));
                }
                else
                {
                    CmbLocation.SelectedIndex = previousIndex;
                }
            }
            else
            {
                previousIndex = CmbLocation.SelectedIndex;
                this.DropdownSelectionChangedEvent?.Invoke(this, new FileBrowsedEventArgs((string)CmbLocation.SelectedItem));
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this.DestroyEvent?.Invoke(this, EventArgs.Empty);
        }

        private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            UrlChangedEvent?.Invoke(sender, e);
        }

        private void TxtFile_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCategoryText();
        }

        private void UpdateCategoryText()
        {
            TxtCategory.Text = downloadCreationPreferences.GetCategoryDisplayName(TxtFile.Text);
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            DownloadClicked?.Invoke(sender, e);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnDownloadLater_Click(object sender, RoutedEventArgs e)
        {
            ShowQueuesContextMenu();
        }

        private void btnAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AdvancedDownloadOptionDialog(downloadCreationPreferences)
            {
                Authentication = Authentication,
                Proxy = Proxy,
                EnableSpeedLimit = EnableSpeedLimit,
                SpeedLimit = SpeedLimit,
                Owner = this
            };
            var ret = dlg.ShowDialog(this);
            if (ret.HasValue && ret.Value)
            {
                Authentication = dlg.Authentication;
                Proxy = dlg.Proxy;
                EnableSpeedLimit = dlg.EnableSpeedLimit;
                SpeedLimit = dlg.SpeedLimit;
            }
        }

        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            UrlBlockedEvent?.Invoke(sender, EventArgs.Empty);
        }

        private void ShowQueuesContextMenu()
        {
            DownloadLaterMenuHelper.PopulateMenuAndAttachEvents(DownloadLaterClicked, btnDownloadLater, this);
        }

        private void DontAddToQueueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.DownloadLaterClicked?.Invoke(this, new DownloadLaterEventArgs(string.Empty));
        }

        private void QueueAndSchedulerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.QueueSchedulerClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
