using System;
using System.Windows;
using TraceLog;

namespace ADM.Wpf.UI.Dashboard
{
    internal sealed class DownloadDashboardLauncher
    {
        private readonly Window owner;
        private readonly Func<DownloadDashboardViewModel> createViewModel;
        private readonly Action reportFailure;
        private DownloadDashboardWindow? open;

        internal DownloadDashboardLauncher(Window owner, Func<DownloadDashboardViewModel> createViewModel, Action reportFailure)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.createViewModel = createViewModel ?? throw new ArgumentNullException(nameof(createViewModel));
            this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        }

        internal void Show()
        {
            if (TryActivateOpenWindow()) return;
            DownloadDashboardViewModel? viewModel = null;
            DownloadDashboardWindow? dashboard = null;
            try
            {
                viewModel = createViewModel();
                dashboard = new DownloadDashboardWindow(viewModel);
                if (owner.IsLoaded) dashboard.Owner = owner;
                var created = dashboard;
                dashboard.Closed += (_, _) =>
                {
                    if (ReferenceEquals(open, created)) open = null;
                };
                dashboard.Show();
                open = dashboard;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Download dashboard could not be opened");
                open = null;
                Discard(dashboard);
                viewModel?.Dispose();
                ReportFailure();
            }
        }

        private bool TryActivateOpenWindow()
        {
            var current = open;
            if (current == null) return false;
            try
            {
                if (current.IsLoaded)
                {
                    if (current.WindowState == WindowState.Minimized) current.WindowState = WindowState.Normal;
                    current.Activate();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Open download dashboard could not be activated");
            }
            open = null;
            Discard(current);
            return false;
        }

        private static void Discard(DownloadDashboardWindow? window)
        {
            if (window == null) return;
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Unusable download dashboard window could not be closed");
            }
        }

        private void ReportFailure()
        {
            try
            {
                reportFailure();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Download dashboard failure could not be shown");
            }
        }
    }
}
