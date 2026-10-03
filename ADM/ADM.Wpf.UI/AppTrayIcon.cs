using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TraceLog;
using Translations;
using ADM.Core;

namespace ADM.Wpf.UI
{
    internal static class AppTrayIcon
    {
        private static NotifyIcon? notifyIcon;

        public static event EventHandler? TrayClick;

        public static void ShowNotification(bool enabled)
        {
            try
            {
                if (notifyIcon != null && enabled)
                {
                    notifyIcon.ShowBalloonTip(30000, TextResource.GetText("MSG_DOWNLOAD_VIDEO"),
                        TextResource.GetText("MSG_DWN_VID_DESC"), ToolTipIcon.Info);

                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
        }


        public static void AttachToSystemTray(Action notificationClicked)
        {

            var ctx = new ContextMenu();

            var menuExit = new MenuItem
            {
                Text = TextResource.GetText("MENU_EXIT")
            };
            menuExit.Click += (_, _) => Environment.Exit(0);

            var menuRestore = new MenuItem
            {
                Text = TextResource.GetText("MSG_RESTORE")
            };
            menuRestore.Click += (sender, e) => TrayClick?.Invoke(sender, e);

            ctx.MenuItems.Add(menuRestore);
            ctx.MenuItems.Add(menuExit);

            notifyIcon = new NotifyIcon
            {
                Text = ProductIdentity.ShortName,
                Visible = true,
                Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adm-logo.ico")),
                ContextMenu = ctx
            };
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            notifyIcon.BalloonTipClicked += (_, _) => notificationClicked();

        }

        private static void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TrayClick?.Invoke(sender, e);
            }
        }

        public static void DetachFromSystemTray()
        {
            notifyIcon?.Dispose();
            notifyIcon = null;
        }
    }
}
