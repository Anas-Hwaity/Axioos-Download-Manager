using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace ADM.Core.DataAccess
{
    public static class RecoverySchema
    {
        public const int CurrentVersion = 1;

        public static IReadOnlyList<RecoveryRecord> ReadAll(SQLiteConnection connection)
        {
            var records = new List<RecoveryRecord>();
            using var command = new SQLiteCommand(connection);
            command.CommandText = "SELECT download_id, schema_version, checkpoint_revision, state_kind, state_file, source_identity, expected_length, finalization_state, final_file, updated_utc FROM download_recovery";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new RecoveryRecord
                {
                    DownloadId = reader.GetString(0),
                    SchemaVersion = reader.GetInt32(1),
                    CheckpointRevision = reader.GetInt64(2),
                    StateKind = reader.GetString(3),
                    StateFile = reader.GetString(4),
                    SourceIdentity = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    ExpectedLength = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                    FinalizationState = reader.GetString(7),
                    FinalFile = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    UpdatedUtc = reader.GetInt64(9)
                });
            }
            return records;
        }

        public static void Ensure(SQLiteConnection connection)
        {
            using var command = new SQLiteCommand(connection);
            command.CommandText = @"CREATE TABLE IF NOT EXISTS recovery_schema_meta(
                                        singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
                                        schema_version INTEGER NOT NULL
                                    );
                                    CREATE TABLE IF NOT EXISTS download_recovery(
                                        download_id TEXT PRIMARY KEY,
                                        schema_version INTEGER NOT NULL,
                                        checkpoint_revision INTEGER NOT NULL,
                                        state_kind TEXT NOT NULL,
                                        state_file TEXT NOT NULL,
                                        source_identity TEXT,
                                        expected_length INTEGER,
                                        finalization_state TEXT NOT NULL,
                                        final_file TEXT,
                                        updated_utc INTEGER NOT NULL
                                    ) WITHOUT ROWID;";
            command.ExecuteNonQuery();

            command.CommandText = "SELECT schema_version FROM recovery_schema_meta WHERE singleton = 1";
            var existing = command.ExecuteScalar();
            if (existing == null || existing == DBNull.Value)
            {
                command.CommandText = "INSERT INTO recovery_schema_meta(singleton, schema_version) VALUES (1, @version)";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@version", CurrentVersion);
                command.ExecuteNonQuery();
                return;
            }

            var version = Convert.ToInt32(existing);
            if (version != CurrentVersion)
            {
                throw new InvalidOperationException($"RecoverySchemaVersionUnsupported:{version}");
            }
        }
    }
}
