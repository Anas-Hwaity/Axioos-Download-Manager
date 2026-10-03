using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace ADM.Core.DataAccess
{
    public sealed class RecoveryBackupManifest
    {
        public string BackupFile { get; set; }
        public string ManifestFile { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public int SchemaVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public static class RecoveryBackupManager
    {
        public static RecoveryBackupManifest CreateVerifiedBackup(SQLiteConnection source, string backupDirectory, int schemaVersion)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(backupDirectory)) throw new ArgumentException("BackupDirectoryRequired", nameof(backupDirectory));
            Directory.CreateDirectory(backupDirectory);

            var createdUtc = DateTime.UtcNow;
            var backupFile = Path.Combine(backupDirectory,
                "recovery-" + createdUtc.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".db");
            SQLiteConnection.CreateFile(backupFile);
            using (var destination = new SQLiteConnection("URI=file:" + backupFile))
            {
                destination.Open();
                source.BackupDatabase(destination, "main", "main", -1, null, 0);
            }

            VerifyIndependently(backupFile);
            var info = new FileInfo(backupFile);
            var sha256 = ComputeSha256(backupFile);
            var manifest = backupFile + ".manifest";
            File.WriteAllLines(manifest, new[]
            {
                "sha256=" + sha256,
                "size=" + info.Length.ToString(CultureInfo.InvariantCulture),
                "schema_version=" + schemaVersion.ToString(CultureInfo.InvariantCulture),
                "created_utc=" + createdUtc.ToString("O", CultureInfo.InvariantCulture)
            });

            return new RecoveryBackupManifest
            {
                BackupFile = backupFile,
                ManifestFile = manifest,
                Sha256 = sha256,
                Size = info.Length,
                SchemaVersion = schemaVersion,
                CreatedUtc = createdUtc
            };
        }

        private static void VerifyIndependently(string backupFile)
        {
            using var verification = new SQLiteConnection("URI=file:" + backupFile);
            verification.Open();
            if (!CheckReturnsOnlyOk(verification, "PRAGMA quick_check"))
                throw new InvalidOperationException("RecoveryBackupVerificationFailed:quick_check");
            if (HasAnyRow(verification, "PRAGMA foreign_key_check"))
                throw new InvalidOperationException("RecoveryBackupVerificationFailed:foreign_key_check");
            if (!CheckReturnsOnlyOk(verification, "PRAGMA integrity_check"))
                throw new InvalidOperationException("RecoveryBackupVerificationFailed:integrity_check");
        }

        private static bool CheckReturnsOnlyOk(SQLiteConnection connection, string sql)
        {
            using var command = new SQLiteCommand(sql, connection);
            using var reader = command.ExecuteReader();
            var sawRow = false;
            while (reader.Read())
            {
                sawRow = true;
                if (!string.Equals(Convert.ToString(reader.GetValue(0)), "ok", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return sawRow;
        }

        private static bool HasAnyRow(SQLiteConnection connection, string sql)
        {
            using var command = new SQLiteCommand(sql, connection);
            using var reader = command.ExecuteReader();
            return reader.Read();
        }

        private static string ComputeSha256(string file)
        {
            using var sha = SHA256.Create();
            using var input = File.OpenRead(file);
            var digest = sha.ComputeHash(input);
            return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
