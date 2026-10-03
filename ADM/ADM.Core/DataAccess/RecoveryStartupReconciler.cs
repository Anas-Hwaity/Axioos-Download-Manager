using System;
using System.Collections.Generic;
using System.IO;

namespace ADM.Core.DataAccess
{
    public enum RecoveryStartupDisposition
    {
        Recoverable,
        Finalized,
        FinalizationCommitPending,
        NeedsAttention
    }

    public sealed class RecoveryRecord
    {
        public string DownloadId { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public long CheckpointRevision { get; set; }
        public string StateKind { get; set; } = string.Empty;
        public string StateFile { get; set; } = string.Empty;
        public string SourceIdentity { get; set; } = string.Empty;
        public long? ExpectedLength { get; set; }
        public string FinalizationState { get; set; } = string.Empty;
        public string FinalFile { get; set; } = string.Empty;
        public long UpdatedUtc { get; set; }
    }

    public sealed class RecoveryReconciliationResult
    {
        public RecoveryReconciliationResult(string downloadId, RecoveryStartupDisposition disposition, string code)
        {
            DownloadId = downloadId ?? string.Empty;
            Disposition = disposition;
            Code = code ?? string.Empty;
        }

        public string DownloadId { get; }
        public RecoveryStartupDisposition Disposition { get; }
        public string Code { get; }
    }

    public static class RecoveryStartupReconciler
    {
        public static IReadOnlyList<RecoveryReconciliationResult> ClassifyAll(IReadOnlyList<RecoveryRecord> records)
        {
            var results = new List<RecoveryReconciliationResult>();
            if (records == null)
            {
                return results;
            }

            foreach (var record in records)
            {
                results.Add(Classify(record));
            }
            return results;
        }

        public static RecoveryReconciliationResult Classify(RecoveryRecord? record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.DownloadId))
            {
                return NeedsAttention(record, "MetadataInvalid");
            }
            if (record.SchemaVersion != RecoverySchema.CurrentVersion)
            {
                return NeedsAttention(record, "RecoverySchemaVersionUnsupported");
            }
            if (string.IsNullOrWhiteSpace(record.StateKind) || string.IsNullOrWhiteSpace(record.StateFile))
            {
                return NeedsAttention(record, "MetadataInvalid");
            }
            if (record.ExpectedLength.HasValue && record.ExpectedLength.Value < 0)
            {
                return NeedsAttention(record, "ExpectedLengthInvalid");
            }

            var finalExists = !string.IsNullOrWhiteSpace(record.FinalFile) && File.Exists(record.FinalFile);
            if (string.Equals(record.FinalizationState, "finalized", StringComparison.OrdinalIgnoreCase))
            {
                if (!finalExists)
                {
                    return NeedsAttention(record, "FinalFileMissing");
                }
                if (record.ExpectedLength.HasValue && new FileInfo(record.FinalFile).Length != record.ExpectedLength.Value)
                {
                    return NeedsAttention(record, "FinalLengthMismatch");
                }
                return new RecoveryReconciliationResult(record.DownloadId, RecoveryStartupDisposition.Finalized, "FinalizedVerified");
            }

            if (finalExists)
            {
                if (!record.ExpectedLength.HasValue)
                {
                    return NeedsAttention(record, "FinalLengthUnknown");
                }
                if (new FileInfo(record.FinalFile).Length != record.ExpectedLength.Value)
                {
                    return NeedsAttention(record, "FinalLengthMismatch");
                }
                return new RecoveryReconciliationResult(record.DownloadId, RecoveryStartupDisposition.FinalizationCommitPending, "FinalizationCommitPending");
            }

            if (!File.Exists(record.StateFile))
            {
                return NeedsAttention(record, "StateFileMissing");
            }

            return new RecoveryReconciliationResult(record.DownloadId, RecoveryStartupDisposition.Recoverable, "StateFileVerified");
        }

        private static RecoveryReconciliationResult NeedsAttention(RecoveryRecord? record, string code)
        {
            return new RecoveryReconciliationResult(record?.DownloadId ?? string.Empty, RecoveryStartupDisposition.NeedsAttention, code);
        }
    }
}
