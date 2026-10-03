using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using ADM.Core.IO;

namespace ADM.Core.BrowserMonitoring
{
    internal sealed class BrowserTakeoverOwnershipStore
    {
        internal const int MinimumRetentionHours = 24;
        private const string FileName = "takeover-ownership-v1.json";
        private readonly object sync = new object();
        private readonly string directory;

        internal sealed class OwnershipRecord
        {
            public string IdempotencyKey { get; set; } = string.Empty;
            public string State { get; set; } = "pending";
            public string? DesktopDownloadId { get; set; }
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        }

        internal BrowserTakeoverOwnershipStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Browser protocol state directory is required.", nameof(directory));
            this.directory = directory;
            Directory.CreateDirectory(directory);
        }

        internal OwnershipRecord? Get(string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
            lock (sync)
            {
                var records = ReadRecords();
                Prune(records, DateTime.UtcNow);
                return records.TryGetValue(idempotencyKey, out var record) ? record : null;
            }
        }

        internal bool TryReserve(string idempotencyKey, out OwnershipRecord record)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
            lock (sync)
            {
                var records = ReadRecords();
                Prune(records, DateTime.UtcNow);
                if (records.TryGetValue(idempotencyKey, out record!)) return false;
                record = new OwnershipRecord { IdempotencyKey = idempotencyKey, State = "pending", UpdatedAtUtc = DateTime.UtcNow };
                records[idempotencyKey] = record;
                WriteRecords(records);
                return true;
            }
        }

        internal void MarkAccepted(string idempotencyKey, string desktopDownloadId)
        {
            if (string.IsNullOrWhiteSpace(desktopDownloadId)) throw new ArgumentException("Desktop download id is required.", nameof(desktopDownloadId));
            if (IsPromptId(desktopDownloadId)) throw new ArgumentException("A confirmation prompt is not a desktop download.", nameof(desktopDownloadId));
            lock (sync)
            {
                var records = ReadRecords();
                if (!records.TryGetValue(idempotencyKey, out var record))
                {
                    record = new OwnershipRecord { IdempotencyKey = idempotencyKey };
                    records[idempotencyKey] = record;
                }
                record.State = "accepted";
                record.DesktopDownloadId = desktopDownloadId;
                record.UpdatedAtUtc = DateTime.UtcNow;
                WriteRecords(records);
            }
        }

        internal bool MarkDeclined(string idempotencyKey)
        {
            lock (sync)
            {
                var records = ReadRecords();
                if (!records.TryGetValue(idempotencyKey, out var record) || record.State != "pending") return false;
                record.State = "declined";
                record.UpdatedAtUtc = DateTime.UtcNow;
                WriteRecords(records);
                return true;
            }
        }

        internal int ReleaseUnconfirmed()
        {
            lock (sync)
            {
                var records = ReadRecords();
                var remove = new List<string>();
                foreach (var pair in records)
                {
                    var promptOnly = pair.Value.State == "accepted" && IsPromptId(pair.Value.DesktopDownloadId);
                    if (pair.Value.State == "pending" || promptOnly) remove.Add(pair.Key);
                }
                foreach (var key in remove) records.Remove(key);
                if (remove.Count > 0) WriteRecords(records);
                return remove.Count;
            }
        }

        internal static bool IsPromptId(string? desktopDownloadId)
        {
            return desktopDownloadId != null && desktopDownloadId.StartsWith("prompt-", StringComparison.Ordinal);
        }

        internal void ClearPending(string idempotencyKey)
        {
            lock (sync)
            {
                var records = ReadRecords();
                if (records.TryGetValue(idempotencyKey, out var record) && record.State == "pending")
                {
                    records.Remove(idempotencyKey);
                    WriteRecords(records);
                }
            }
        }

        private Dictionary<string, OwnershipRecord> ReadRecords()
        {
            var json = TransactedIO.Read(FileName, directory);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, OwnershipRecord>(StringComparer.Ordinal);
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, OwnershipRecord>>(json!) ??
                    new Dictionary<string, OwnershipRecord>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new Dictionary<string, OwnershipRecord>(StringComparer.Ordinal);
            }
        }

        private void WriteRecords(Dictionary<string, OwnershipRecord> records)
        {
            Directory.CreateDirectory(directory);
            if (!TransactedIO.Write(JsonConvert.SerializeObject(records, Formatting.None), FileName, directory))
                throw new IOException("Failed to persist browser takeover ownership state.");
        }

        internal void MarkBrowserTerminal(string idempotencyKey)
        {
            lock (sync)
            {
                var records = ReadRecords();
                if (!records.TryGetValue(idempotencyKey, out var record)) return;
                record.State = "terminal";
                record.UpdatedAtUtc = DateTime.UtcNow;
                WriteRecords(records);
            }
        }

        private static void Prune(Dictionary<string, OwnershipRecord> records, DateTime now)
        {
            var remove = new List<string>();
            foreach (var pair in records)
            {
                var expired = now - pair.Value.UpdatedAtUtc >= TimeSpan.FromHours(MinimumRetentionHours);
                if (expired && (pair.Value.State == "pending" || pair.Value.State == "terminal" || pair.Value.State == "declined")) remove.Add(pair.Key);
            }
            foreach (var key in remove) records.Remove(key);
        }
    }
}
