using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;
using ADM.Core.UI;
using ADM.Wpf.UI.Win32;

namespace ADM.Wpf.UI.Dialogs.CompletedDialog
{
    public partial class DownloadCompleteWindow : Window, IDownloadCompleteDialog
    {
        public event EventHandler<DownloadCompleteDialogEventArgs>? FileOpenClicked;
        public event EventHandler<DownloadCompleteDialogEventArgs>? FolderOpenClicked;
        public event EventHandler? DontShowAgainClickd;

        public string FileNameText
        {
            get => TxtFileName.Text;
            set => TxtFileName.Text = value;
        }

        public string FolderText
        {
            get => TxtLocation.Text;
            set => TxtLocation.Text = value;
        }

        public DownloadCompleteWindow()
        {
            InitializeComponent();
        }

        private void TxtDontShowCompleteDialog_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DontShowAgainClickd?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private bool actionTaken;

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (actionTaken) return;
            actionTaken = true;
            string path;
            try
            {
                path = System.IO.Path.Combine(TxtLocation.Text, TxtFileName.Text);
            }
            catch (ArgumentException ex)
            {
                Log.Debug(ex, "Finished download path could not be built");
                Close();
                return;
            }
            Close();
            FileOpenClicked?.Invoke(sender, new DownloadCompleteDialogEventArgs
            {
                Path = path
            });
        }

        public void ShowDownloadCompleteDialog()
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

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (actionTaken) return;
            actionTaken = true;
            var folder = TxtLocation.Text;
            var fileName = TxtFileName.Text;
            Close();
            FolderOpenClicked?.Invoke(sender, new DownloadCompleteDialogEventArgs
            {
                Path = folder,
                FileName = fileName
            });
        }
    }
}
