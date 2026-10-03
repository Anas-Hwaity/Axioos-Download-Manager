using System;
using System.Collections.Generic;
using ADM.Core.Telemetry;

namespace ADM.Wpf.UI.Dashboard
{
    public sealed class DownloadErrorHistoryBuffer
    {
        private readonly int capacity;
        private readonly Dictionary<string, Queue<string>> errors = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);

        public DownloadErrorHistoryBuffer(int capacity = 20)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public void Append(DownloadTelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(snapshot.LastError)) return;
            if (!errors.TryGetValue(snapshot.DownloadId, out var queue))
            {
                queue = new Queue<string>(capacity);
                errors[snapshot.DownloadId] = queue;
            }
            if (queue.Count > 0)
            {
                var last = string.Empty;
                foreach (var value in queue) last = value;
                if (string.Equals(last, snapshot.LastError, StringComparison.Ordinal)) return;
            }
            queue.Enqueue(snapshot.LastError);
            while (queue.Count > capacity) queue.Dequeue();
        }

        public IReadOnlyList<string> Get(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId) || !errors.TryGetValue(downloadId, out var queue))
                return Array.Empty<string>();
            return new List<string>(queue).AsReadOnly();
        }

        public void Remove(string downloadId)
        {
            if (!string.IsNullOrWhiteSpace(downloadId)) errors.Remove(downloadId);
        }
    }
}
