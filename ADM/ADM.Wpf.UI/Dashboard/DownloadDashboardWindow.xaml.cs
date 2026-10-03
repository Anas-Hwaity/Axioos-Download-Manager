using System;
using System.Windows;

namespace ADM.Wpf.UI.Dashboard
{
    public partial class DownloadDashboardWindow : Window
    {
        private readonly DownloadDashboardViewModel viewModel;

        public DownloadDashboardWindow(DownloadDashboardViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;
            Closed += DownloadDashboardWindow_Closed;
        }

        private void DownloadDashboardWindow_Closed(object? sender, EventArgs e)
        {
            viewModel.Dispose();
        }
    }
}
