using System;
using System.Collections.Generic;

namespace ADM.Core.Telemetry
{
    public enum DownloadResumeCapability
    {
        Unknown,
        Yes,
        No
    }

    public sealed class DownloadRangeTelemetry
    {
        public DownloadRangeTelemetry(long start, long end, long downloaded, string status)
        {
            Start = start;
            End = end;
            Downloaded = downloaded;
            Status = status ?? string.Empty;
        }

        public long Start { get; }
        public long End { get; }
        public long Downloaded { get; }
        public string Status { get; }
    }

    public sealed class DownloadSpeedSample
    {
        public DownloadSpeedSample(DateTime timestampUtc, double bytesPerSecond)
        {
            TimestampUtc = timestampUtc;
            BytesPerSecond = bytesPerSecond;
        }

        public DateTime TimestampUtc { get; }
        public double BytesPerSecond { get; }
    }

    public sealed class DownloadTelemetrySnapshot
    {
        public DownloadTelemetrySnapshot(
            string downloadId,
            string fileName,
            string sourceHost,
            string status,
            double percent,
            long downloadedBytes,
            long? totalBytes,
            double currentBytesPerSecond,
            double averageBytesPerSecond,
            double? etaSeconds,
            DownloadResumeCapability resumeCapability,
            int activeConnections,
            IEnumerable<DownloadRangeTelemetry> ranges,
            string sanitizedSourceUrl,
            string retryState,
            string refreshSourceState,
            string lastError,
            string recoveryStatus,
            IEnumerable<DownloadSpeedSample> speedHistory,
            DateTime updatedUtc,
            string attemptId = "",
            string lifecycleState = "",
            long sourceGeneration = 0,
            long revision = 0)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) throw new ArgumentException("downloadId is required", nameof(downloadId));
            DownloadId = downloadId;
            AttemptId = attemptId ?? string.Empty;
            FileName = fileName ?? string.Empty;
            SourceHost = sourceHost ?? string.Empty;
            Status = status ?? string.Empty;
            LifecycleState = string.IsNullOrWhiteSpace(lifecycleState) ? Status : lifecycleState;
            Percent = percent;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            CurrentBytesPerSecond = currentBytesPerSecond;
            AverageBytesPerSecond = averageBytesPerSecond;
            EtaSeconds = etaSeconds;
            ResumeCapability = resumeCapability;
            ActiveConnections = activeConnections;
            Ranges = new List<DownloadRangeTelemetry>(ranges ?? Array.Empty<DownloadRangeTelemetry>()).AsReadOnly();
            SegmentSnapshots = Ranges;
            SanitizedSourceUrl = sanitizedSourceUrl ?? string.Empty;
            RetryState = retryState ?? string.Empty;
            RefreshSourceState = refreshSourceState ?? string.Empty;
            LastError = lastError ?? string.Empty;
            RecoveryStatus = recoveryStatus ?? string.Empty;
            SpeedHistory = new List<DownloadSpeedSample>(speedHistory ?? Array.Empty<DownloadSpeedSample>()).AsReadOnly();
            UpdatedUtc = updatedUtc;
            SourceGeneration = sourceGeneration;
            Revision = revision;
        }

        public string DownloadId { get; }
        public string AttemptId { get; }
        public string FileName { get; }
        public string SourceHost { get; }
        public string Status { get; }
        public string LifecycleState { get; }
        public double Percent { get; }
        public long DownloadedBytes { get; }
        public long? TotalBytes { get; }
        public double CurrentBytesPerSecond { get; }
        public double AverageBytesPerSecond { get; }
        public double? EtaSeconds { get; }
        public DownloadResumeCapability ResumeCapability { get; }
        public int ActiveConnections { get; }
        public IReadOnlyList<DownloadRangeTelemetry> Ranges { get; }
        public IReadOnlyList<DownloadRangeTelemetry> SegmentSnapshots { get; }
        public string SanitizedSourceUrl { get; }
        public string RetryState { get; }
        public string RefreshSourceState { get; }
        public string LastError { get; }
        public string RecoveryStatus { get; }
        public IReadOnlyList<DownloadSpeedSample> SpeedHistory { get; }
        public DateTime UpdatedUtc { get; }
        public long SourceGeneration { get; }
        public long Revision { get; }
    }

    public sealed class DownloadTelemetryChangedEventArgs : EventArgs
    {
        public DownloadTelemetryChangedEventArgs(DownloadTelemetrySnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public DownloadTelemetrySnapshot Snapshot { get; }
    }

    public sealed class DownloadTelemetryRemovedEventArgs : EventArgs
    {
        public DownloadTelemetryRemovedEventArgs(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) throw new ArgumentException("downloadId is required", nameof(downloadId));
            DownloadId = downloadId;
        }

        public string DownloadId { get; }
    }

    public interface IDownloadTelemetryService
    {
        event EventHandler<DownloadTelemetryChangedEventArgs> Changed;
        event EventHandler<DownloadTelemetryRemovedEventArgs> Removed;
        bool TryGet(string downloadId, out DownloadTelemetrySnapshot snapshot);
        IReadOnlyList<DownloadTelemetrySnapshot> GetActive();
    }

    public interface IDownloadTelemetrySink
    {
        void Publish(DownloadTelemetrySnapshot snapshot);
        void Remove(string downloadId);
    }

    public sealed class DownloadTelemetryService : IDownloadTelemetryService, IDownloadTelemetrySink
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, DownloadTelemetrySnapshot> active = new Dictionary<string, DownloadTelemetrySnapshot>(StringComparer.Ordinal);

        public event EventHandler<DownloadTelemetryChangedEventArgs> Changed;
        public event EventHandler<DownloadTelemetryRemovedEventArgs> Removed;

        public void Publish(DownloadTelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (gate)
            {
                if (active.TryGetValue(snapshot.DownloadId, out var current) &&
                    snapshot.Revision > 0 &&
                    current.Revision > 0 &&
                    snapshot.Revision <= current.Revision)
                {
                    return;
                }
                active[snapshot.DownloadId] = snapshot;
            }
            Changed?.Invoke(this, new DownloadTelemetryChangedEventArgs(snapshot));
        }

        public bool TryGet(string downloadId, out DownloadTelemetrySnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(downloadId))
            {
                snapshot = null!;
                return false;
            }
            lock (gate)
            {
                return active.TryGetValue(downloadId, out snapshot);
            }
        }

        public IReadOnlyList<DownloadTelemetrySnapshot> GetActive()
        {
            lock (gate)
            {
                return new List<DownloadTelemetrySnapshot>(active.Values).AsReadOnly();
            }
        }

        public void Remove(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) return;
            bool removed;
            lock (gate)
            {
                removed = active.Remove(downloadId);
            }
            if (removed)
            {
                Removed?.Invoke(this, new DownloadTelemetryRemovedEventArgs(downloadId));
            }
        }
    }
}
