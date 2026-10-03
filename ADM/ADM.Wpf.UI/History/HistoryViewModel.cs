using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ADM.Core.DataAccess;

namespace ADM.Wpf.UI.History
{
    public sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IHistoryQueryService historyQueryService;
        private CancellationTokenSource? activeQuery;
        private int queryGeneration;
        private int totalCount;
        private bool isLoading;

        public HistoryViewModel(IHistoryQueryService historyQueryService, int pageSize = 100)
        {
            this.historyQueryService = historyQueryService ?? throw new ArgumentNullException(nameof(historyQueryService));
            Query = new HistoryQueryRequest
            {
                Completed = true,
                PageSize = Math.Max(1, Math.Min(HistoryQueryRequest.MaxPageSize, pageSize)),
                Offset = 0,
                SortField = HistorySortField.DateAdded,
                SortDirection = HistorySortDirection.Descending
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<HistoryRecord> Items { get; } = new ObservableCollection<HistoryRecord>();

        public HistoryQueryRequest Query { get; }

        public int TotalCount
        {
            get => totalCount;
            private set
            {
                if (totalCount == value) return;
                totalCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanNext));
                OnPropertyChanged(nameof(CanPrevious));
            }
        }

        public bool IsLoading
        {
            get => isLoading;
            private set
            {
                if (isLoading == value) return;
                isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool CanNext => Query.Offset + Query.PageSize < TotalCount;

        public bool CanPrevious => Query.Offset > 0;

        public async Task LoadAsync()
        {
            var previous = activeQuery;
            var cancellation = new CancellationTokenSource();
            activeQuery = cancellation;
            previous?.Cancel();
            previous?.Dispose();

            var generation = ++queryGeneration;
            var token = cancellation.Token;
            var request = SnapshotQuery(Query);
            IsLoading = true;

            try
            {
                var page = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var result = historyQueryService.Query(request);
                    token.ThrowIfCancellationRequested();
                    return result;
                }, token);

                if (generation != queryGeneration || token.IsCancellationRequested) return;

                Items.Clear();
                foreach (var item in page.Items)
                {
                    Items.Add(item);
                }
                Query.Offset = page.Offset;
                Query.PageSize = page.PageSize;
                TotalCount = page.TotalCount;
                OnPropertyChanged(nameof(CanNext));
                OnPropertyChanged(nameof(CanPrevious));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                if (generation == queryGeneration)
                {
                    IsLoading = false;
                }
            }
        }

        public Task RefreshAsync()
        {
            Query.Offset = 0;
            OnPropertyChanged(nameof(CanPrevious));
            return LoadAsync();
        }

        public Task NextPageAsync()
        {
            if (!CanNext) return Task.CompletedTask;
            Query.Offset += Query.PageSize;
            OnPropertyChanged(nameof(CanPrevious));
            return LoadAsync();
        }

        public Task PreviousPageAsync()
        {
            if (!CanPrevious) return Task.CompletedTask;
            Query.Offset = Math.Max(0, Query.Offset - Query.PageSize);
            OnPropertyChanged(nameof(CanPrevious));
            return LoadAsync();
        }

        public void Dispose()
        {
            queryGeneration++;
            activeQuery?.Cancel();
            activeQuery?.Dispose();
            activeQuery = null;
        }

        private static HistoryQueryRequest SnapshotQuery(HistoryQueryRequest source)
        {
            return new HistoryQueryRequest
            {
                SearchText = source.SearchText,
                Completed = source.Completed,
                DateFrom = source.DateFrom,
                DateTo = source.DateTo,
                DownloadType = source.DownloadType,
                Domain = source.Domain,
                MinSize = source.MinSize,
                MaxSize = source.MaxSize,
                Tag = source.Tag,
                SortField = source.SortField,
                SortDirection = source.SortDirection,
                PageSize = Math.Max(1, Math.Min(HistoryQueryRequest.MaxPageSize, source.PageSize)),
                Offset = Math.Max(0, source.Offset)
            };
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
