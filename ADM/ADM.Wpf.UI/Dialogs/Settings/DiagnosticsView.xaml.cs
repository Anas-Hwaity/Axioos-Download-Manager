using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using ADM.Core.Diagnostics;
using TraceLog;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class DiagnosticsView : UserControl
    {
        public DiagnosticsView()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshSummary();
        }

        private void RefreshSummary()
        {
            try
            {
                SummaryText.Text = UserDiagnosticsService.ToDisplayText(UserDiagnosticsService.Capture());
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Diagnostics summary failed");
                SummaryText.Text = "Diagnostics unavailable: " + ex.GetType().Name;
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                FileName = "adm-diagnostics.zip",
                DefaultExt = ".zip",
                Filter = "ZIP archive (*.zip)|*.zip"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                UserDiagnosticsService.ExportZip(dialog.FileName);
                MessageBox.Show("Sanitized diagnostics exported.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Diagnostics export failed");
                MessageBox.Show("Diagnostics export failed: " + ex.GetType().Name, "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
