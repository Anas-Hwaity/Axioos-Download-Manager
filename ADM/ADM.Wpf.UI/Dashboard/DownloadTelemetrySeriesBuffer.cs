using System;
using System.Collections.Generic;
using ADM.Core.Telemetry;

namespace ADM.Wpf.UI.Dashboard
{
    public sealed class DownloadTelemetrySeriesBuffer
    {
        private readonly int capacity;
        private readonly Dictionary<string, Queue<DownloadSpeedSample>> series = new Dictionary<string, Queue<DownloadSpeedSample>>(StringComparer.Ordinal);

        public DownloadTelemetrySeriesBuffer(int capacity = 120)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public void Append(DownloadTelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!series.TryGetValue(snapshot.DownloadId, out var samples))
            {
                samples = new Queue<DownloadSpeedSample>(capacity);
                series[snapshot.DownloadId] = samples;
            }
            samples.Enqueue(new DownloadSpeedSample(snapshot.UpdatedUtc, snapshot.CurrentBytesPerSecond));
            while (samples.Count > capacity)
            {
                samples.Dequeue();
            }
        }

        public IReadOnlyList<DownloadSpeedSample> Get(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) return Array.Empty<DownloadSpeedSample>();
            if (!series.TryGetValue(downloadId, out var samples)) return Array.Empty<DownloadSpeedSample>();
            return new List<DownloadSpeedSample>(samples).AsReadOnly();
        }

        public void Remove(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId)) return;
            series.Remove(downloadId);
        }

        public void Clear()
        {
            series.Clear();
        }
    }
}
