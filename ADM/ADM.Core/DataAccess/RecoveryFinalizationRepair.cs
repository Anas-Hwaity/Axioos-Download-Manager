using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace ADM.Core.DataAccess
{
    public static class RecoveryFinalizationRepair
    {
        public static int ApplyVerifiedPending(
            SQLiteConnection connection,
            IReadOnlyList<RecoveryRecord> records,
            IReadOnlyList<RecoveryReconciliationResult> statuses)
        {
            if (connection == null || records == null || statuses == null) return 0;
            var count = Math.Min(records.Count, statuses.Count);
            var updated = 0;
            using var transaction = connection.BeginTransaction();
            for (var i = 0; i < count; i++)
            {
                var record = records[i];
                var status = statuses[i];
                if (record == null || status == null
                    || status.Disposition != RecoveryStartupDisposition.FinalizationCommitPending
                    || !record.ExpectedLength.HasValue
                    || string.IsNullOrWhiteSpace(record.FinalFile)
                    || !File.Exists(record.FinalFile))
                {
                    continue;
                }

                var finalInfo = new FileInfo(record.FinalFile);
                if (finalInfo.Length != record.ExpectedLength.Value) continue;

                using var command = new SQLiteCommand(connection);
                command.Transaction = transaction;
                command.CommandText = @"UPDATE download_recovery
                                        SET finalization_state = 'finalized',
                                            checkpoint_revision = checkpoint_revision + 1,
                                            updated_utc = @updatedUtc
                                        WHERE download_id = @downloadId
                                          AND finalization_state <> 'finalized'
                                          AND expected_length = @expectedLength
                                          AND final_file = @finalFile";
                command.Parameters.AddWithValue("@downloadId", record.DownloadId);
                command.Parameters.AddWithValue("@expectedLength", record.ExpectedLength.Value);
                command.Parameters.AddWithValue("@finalFile", record.FinalFile);
                command.Parameters.AddWithValue("@updatedUtc", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
                updated += command.ExecuteNonQuery();
            }
            transaction.Commit();
            return updated;
        }
    }
}
