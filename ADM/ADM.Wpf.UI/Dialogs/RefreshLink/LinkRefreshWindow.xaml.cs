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
using Translations;
using ADM.Core;
using ADM.Wpf.UI.Win32;
using TraceLog;

namespace ADM.Wpf.UI.Dialogs.RefreshLink
{
    public partial class LinkRefreshWindow : Window, IRefreshLinkDialog
    {
        public LinkRefreshWindow()
        {
            InitializeComponent();
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

        public event EventHandler? WatchingStopped;

        public void ShowWindow()
        {
            Show();
        }

        public void LinkReceived()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    MessageBox.Show(this, TextResource.GetText("MSG_REF_LINK_MSG"));
                    Close();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Refresh link completion UI failed");
                }
            }));
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            WatchingStopped?.Invoke(this, EventArgs.Empty);
        }
    }
}
