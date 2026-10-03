using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ADM.Core.Telemetry;

namespace ADM.Wpf.UI.Dashboard
{
    public interface IDashboardUiDispatcher
    {
        void Post(Action action);
    }

    public sealed class DownloadDashboardItemViewModel : INotifyPropertyChanged
    {
        public DownloadDashboardItemViewModel(DownloadTelemetrySnapshot snapshot, IReadOnlyList<DownloadSpeedSample> speedSeries, IReadOnlyList<string> errorHistory)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            SpeedSeries = speedSeries ?? throw new ArgumentNullException(nameof(speedSeries));
            ErrorHistory = errorHistory ?? throw new ArgumentNullException(nameof(errorHistory));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public DownloadTelemetrySnapshot Snapshot { get; private set; }
        public IReadOnlyList<DownloadSpeedSample> SpeedSeries { get; private set; }
        public IReadOnlyList<string> ErrorHistory { get; private set; }

        internal void Update(DownloadTelemetrySnapshot snapshot, IReadOnlyList<DownloadSpeedSample> speedSeries, IReadOnlyList<string> errorHistory)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            SpeedSeries = speedSeries ?? throw new ArgumentNullException(nameof(speedSeries));
            ErrorHistory = errorHistory ?? throw new ArgumentNullException(nameof(errorHistory));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public sealed class DownloadDashboardViewModel : IDisposable
    {
        private readonly IDownloadTelemetryService telemetryService;
        private readonly IDashboardUiDispatcher uiDispatcher;
        private readonly IDownloadDashboardCommandService commandService;
        private readonly DownloadTelemetrySeriesBuffer seriesBuffer;
        private readonly DownloadErrorHistoryBuffer errorHistoryBuffer;
        private readonly Dictionary<string, DownloadDashboardItemViewModel> itemsById = new Dictionary<string, DownloadDashboardItemViewModel>(StringComparer.Ordinal);
        private bool disposed;

        public DownloadDashboardViewModel(
            IDownloadTelemetryService telemetryService,
            IDownloadDashboardCommandService commandService,
            IDashboardUiDispatcher uiDispatcher,
            int speedSeriesCapacity = 120)
        {
            this.telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
            this.commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            this.uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            PauseCommand = new DashboardActionCommand(id => this.commandService.Pause(id));
            ResumeCommand = new DashboardActionCommand(id => this.commandService.Resume(id));
            RetryCommand = new DashboardActionCommand(id => this.commandService.Retry(id));
            CancelCommand = new DashboardActionCommand(id => this.commandService.Cancel(id));
            RefreshSourceCommand = new DashboardActionCommand(id => this.commandService.RefreshSource(id));
            ShowProgressCommand = new DashboardActionCommand(id => this.commandService.ShowProgress(id));
            seriesBuffer = new DownloadTelemetrySeriesBuffer(speedSeriesCapacity);
            errorHistoryBuffer = new DownloadErrorHistoryBuffer();
            telemetryService.Changed += TelemetryChanged;
            telemetryService.Removed += TelemetryRemoved;
            var initial = telemetryService.GetActive();
            uiDispatcher.Post(() =>
            {
                foreach (var snapshot in initial)
                {
                    ApplySnapshot(snapshot);
                }
            });
        }

        public ObservableCollection<DownloadDashboardItemViewModel> Items { get; } = new ObservableCollection<DownloadDashboardItemViewModel>();
        public ICommand PauseCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshSourceCommand { get; }
        public ICommand ShowProgressCommand { get; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            telemetryService.Changed -= TelemetryChanged;
            telemetryService.Removed -= TelemetryRemoved;
        }

        private void TelemetryChanged(object sender, DownloadTelemetryChangedEventArgs e)
        {
            uiDispatcher.Post(() => ApplySnapshot(e.Snapshot));
        }

        private void TelemetryRemoved(object sender, DownloadTelemetryRemovedEventArgs e)
        {
            uiDispatcher.Post(() => ApplyRemoved(e.DownloadId));
        }

        private void ApplySnapshot(DownloadTelemetrySnapshot snapshot)
        {
            if (disposed) return;
            if (itemsById.TryGetValue(snapshot.DownloadId, out var current))
            {
                if (snapshot.Revision > 0 && current.Snapshot.Revision > 0 && snapshot.Revision <= current.Snapshot.Revision) return;
                seriesBuffer.Append(snapshot);
                errorHistoryBuffer.Append(snapshot);
                current.Update(snapshot, seriesBuffer.Get(snapshot.DownloadId), errorHistoryBuffer.Get(snapshot.DownloadId));
                return;
            }

            seriesBuffer.Append(snapshot);
            errorHistoryBuffer.Append(snapshot);
            var item = new DownloadDashboardItemViewModel(snapshot, seriesBuffer.Get(snapshot.DownloadId), errorHistoryBuffer.Get(snapshot.DownloadId));
            itemsById.Add(snapshot.DownloadId, item);
            Items.Add(item);
        }

        private void ApplyRemoved(string downloadId)
        {
            if (disposed) return;
            if (!itemsById.TryGetValue(downloadId, out var current)) return;
            itemsById.Remove(downloadId);
            Items.Remove(current);
            seriesBuffer.Remove(downloadId);
            errorHistoryBuffer.Remove(downloadId);
        }
    }
}
