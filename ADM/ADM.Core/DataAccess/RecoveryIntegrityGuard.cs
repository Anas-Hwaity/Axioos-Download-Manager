using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace ADM.Core.DataAccess
{
    public sealed class RecoverySafeModeState
    {
        public bool Required { get; set; }
        public string Code { get; set; }
        public string EvidenceDirectory { get; set; }
        public IReadOnlyList<string> IntegrityErrors { get; set; }

        public static RecoverySafeModeState Healthy()
        {
            return new RecoverySafeModeState
            {
                Required = false,
                Code = "Healthy",
                EvidenceDirectory = string.Empty,
                IntegrityErrors = Array.Empty<string>()
            };
        }
    }

    public static class RecoveryIntegrityGuard
    {
        public static RecoverySafeModeState CheckStartup(SQLiteConnection connection, string databaseFile)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            var errors = new List<string>();
            CollectCheck(connection, "PRAGMA quick_check", "quick_check", errors, expectOkRow: true);
            CollectCheck(connection, "PRAGMA foreign_key_check", "foreign_key_check", errors, expectOkRow: false);
            if (errors.Count == 0) return RecoverySafeModeState.Healthy();

            CollectCheck(connection, "PRAGMA integrity_check", "integrity_check", errors, expectOkRow: true);
            return PreserveEvidence(databaseFile, "IntegrityCheckFailed", errors);
        }

        public static RecoverySafeModeState PreserveEvidence(string databaseFile, string code, string detail)
        {
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(detail)) errors.Add(detail);
            return PreserveEvidence(databaseFile, code, errors);
        }

        private static RecoverySafeModeState PreserveEvidence(string databaseFile, string code, IReadOnlyList<string> errors)
        {
            var evidenceDirectory = string.Empty;
            try
            {
                var fullPath = Path.GetFullPath(databaseFile);
                var root = fullPath + ".recovery-evidence";
                evidenceDirectory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(evidenceDirectory);
                CopyEvidenceIfPresent(fullPath, evidenceDirectory);
                CopyEvidenceIfPresent(fullPath + "-wal", evidenceDirectory);
                CopyEvidenceIfPresent(fullPath + "-shm", evidenceDirectory);
            }
            catch (Exception ex)
            {
                var expanded = new List<string>(errors ?? Array.Empty<string>()) { "EvidencePreservationFailed:" + ex.GetType().Name };
                errors = expanded;
            }
            return new RecoverySafeModeState
            {
                Required = true,
                Code = code ?? "RecoverySafeModeRequired",
                EvidenceDirectory = evidenceDirectory,
                IntegrityErrors = errors ?? Array.Empty<string>()
            };
        }

        private static void CopyEvidenceIfPresent(string source, string evidenceDirectory)
        {
            if (!File.Exists(source)) return;
            var destination = Path.Combine(evidenceDirectory, Path.GetFileName(source));
            File.Copy(source, destination, false);
            File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
        }

        private static void CollectCheck(SQLiteConnection connection, string sql, string name, List<string> errors, bool expectOkRow)
        {
            using var command = new SQLiteCommand(sql, connection);
            using var reader = command.ExecuteReader();
            var sawRow = false;
            while (reader.Read())
            {
                sawRow = true;
                var value = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0));
                if (expectOkRow && string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase)) continue;
                errors.Add(name + ":" + value);
            }
            if (expectOkRow && !sawRow) errors.Add(name + ":NoResult");
        }
    }
}
