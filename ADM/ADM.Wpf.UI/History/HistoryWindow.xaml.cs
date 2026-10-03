using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TraceLog;
using ADM.Core.DataAccess;

namespace ADM.Wpf.UI.History
{
    public partial class HistoryWindow : Window
    {
        private readonly HistoryViewModel viewModel;
        private readonly IHistoryActionService actionService;
        private readonly IHistoryQueryService historyQueryService;
        private bool loadingTags;

        public HistoryWindow(IHistoryQueryService historyQueryService, IHistoryActionService actionService)
        {
            InitializeComponent();
            this.actionService = actionService ?? throw new ArgumentNullException(nameof(actionService));
            this.historyQueryService = historyQueryService ?? throw new ArgumentNullException(nameof(historyQueryService));
            viewModel = new HistoryViewModel(historyQueryService, 100);
            DataContext = viewModel;
            Loaded += HistoryWindow_Loaded;
            Closed += HistoryWindow_Closed;
        }

        private HistoryRecord? Selected => HistoryList.SelectedItem as HistoryRecord;

        private async void HistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTags();
            await viewModel.LoadAsync();
        }

        private void LoadTags()
        {
            IReadOnlyList<string> tags;
            try
            {
                tags = historyQueryService.ListTags();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "History tags could not be listed");
                tags = new List<string>();
            }
            loadingTags = true;
            try
            {
                TagBox.Items.Clear();
                TagBox.Items.Add(TryFindResource("HISTORY_TAG_ANY") as string ?? "Any tag");
                foreach (var tag in tags)
                {
                    TagBox.Items.Add(tag);
                }
                TagBox.SelectedIndex = 0;
                TagPanel.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                loadingTags = false;
            }
        }

        private Task ApplyFiltersAsync()
        {
            var domain = HistoryQueryRequest.NormalizeDomain(DomainBox.Text);
            DomainBox.Text = domain ?? string.Empty;
            viewModel.Query.SearchText = Normalize(SearchBox.Text);
            viewModel.Query.Domain = domain;
            viewModel.Query.Tag = TagBox.SelectedIndex > 0 ? TagBox.SelectedItem as string : null;
            return viewModel.RefreshAsync();
        }

        private async void ApplyFilters_Click(object sender, RoutedEventArgs e) => await ApplyFiltersAsync();

        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            loadingTags = true;
            try
            {
                SearchBox.Clear();
                DomainBox.Clear();
                if (TagBox.Items.Count > 0) TagBox.SelectedIndex = 0;
            }
            finally
            {
                loadingTags = false;
            }
            await ApplyFiltersAsync();
        }

        private async void Filter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await ApplyFiltersAsync();
        }

        private async void TagBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loadingTags || !IsLoaded) return;
            await ApplyFiltersAsync();
        }

        private async void Previous_Click(object sender, RoutedEventArgs e) => await viewModel.PreviousPageAsync();
        private async void Next_Click(object sender, RoutedEventArgs e) => await viewModel.NextPageAsync();

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is HistoryRecord record) actionService.OpenFile(record);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is HistoryRecord record) actionService.OpenFolder(record);
        }

        private void CopySafeUrl_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is HistoryRecord record && !string.IsNullOrWhiteSpace(record.SafeUrlDisplay))
                Clipboard.SetText(record.SafeUrlDisplay);
        }

        private void CopyFullUrl_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not HistoryRecord record || string.IsNullOrWhiteSpace(record.PrimaryUrl)) return;
            var sensitive = record.PrimaryUrl.IndexOf("?", StringComparison.Ordinal) >= 0 || record.PrimaryUrl.IndexOf('#') >= 0;
            if (sensitive && MessageBox.Show(this, "The full source URL may contain sensitive query or fragment data. Copy it anyway?", "History", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            Clipboard.SetText(record.PrimaryUrl);
        }

        private void DownloadAgain_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is HistoryRecord record && actionService.CanDownloadAgain(record)) actionService.DownloadAgain(record);
        }

        private void HistoryWindow_Closed(object? sender, EventArgs e) => viewModel.Dispose();
        private static string? Normalize(string? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }
    }
}
