using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using TraceLog;
using ADM.Core;
using ADM.Core.BrowserMonitoring;
using ADM.Core.Downloader;

namespace ADM.Core.DataAccess
{
    public class AppDB
    {
        private static object lockObj = new();
        private bool init = false;
        private SQLiteConnection db;
        private AppDB() { }
        private DownloadList downloadsDB;
        public DownloadList Downloads => downloadsDB;
        private IHistoryQueryService historyQuery;
        public IHistoryQueryService History => historyQuery;
        private IReadOnlyList<RecoveryReconciliationResult> recoveryStatuses = Array.Empty<RecoveryReconciliationResult>();
        public IReadOnlyList<RecoveryReconciliationResult> RecoveryStatuses => recoveryStatuses;
        public RecoverySafeModeState RecoverySafeMode { get; private set; } = RecoverySafeModeState.Healthy();
        public bool IsInitialized => init;
        private readonly BrowserProtocolRuntimeDiagnosticsState browserProtocolRuntimeDiagnostics = new BrowserProtocolRuntimeDiagnosticsState();
        public BrowserProtocolRuntimeDiagnosticsState BrowserProtocolRuntimeDiagnostics => browserProtocolRuntimeDiagnostics;
        private static AppDB instance;
        public static AppDB Instance
        {
            get
            {
                lock (lockObj)
                {
                    if (instance == null)
                    {
                        instance = new AppDB();
                    }
                }
                return instance;
            }
        }

        public bool Init(string file)
        {
            lock (this)
            {
                try
                {
                    string cs = $"URI=file:{file}";
                    if (!File.Exists(file))
                    {
                        SQLiteConnection.CreateFile(file);
                    }
                    db = new SQLiteConnection(cs);
                    db.Open();
                    RemoveOrphanTags(db);
                    this.RecoverySafeMode = RecoveryIntegrityGuard.CheckStartup(db, file);
                    if (this.RecoverySafeMode.Required)
                    {
                        db.Close();
                        return false;
                    }
                    SchemaInitializer.Init(db);
                    this.recoveryStatuses = RecoveryStartupReconciler.ClassifyAll(RecoverySchema.ReadAll(db));
                    RecoveryFinalizationRepair.ApplyVerifiedPending(db, RecoverySchema.ReadAll(db), this.recoveryStatuses);
                    this.recoveryStatuses = RecoveryStartupReconciler.ClassifyAll(RecoverySchema.ReadAll(db));
                    this.downloadsDB = new DownloadList(db);
                    this.historyQuery = new SqliteHistoryQueryService(db);
                    init = true;
                    return true;
                }
                catch (SQLiteException ex)
                {
                    this.RecoverySafeMode = RecoveryIntegrityGuard.PreserveEvidence(file, "DatabaseOpenOrIntegrityFailure", ex.Message);
                    Log.Debug(ex, ex.Message);
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                    return false;
                }
            }
        }

        private static void RemoveOrphanTags(SQLiteConnection connection)
        {
            try
            {
                using var health = new SQLiteCommand("PRAGMA quick_check(1)", connection);
                if (!string.Equals(Convert.ToString(health.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase)) return;
                using var probe = new SQLiteCommand(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('downloads','download_tags')", connection);
                if (Convert.ToInt64(probe.ExecuteScalar()) != 2) return;
                using var repair = new SQLiteCommand(
                    "DELETE FROM download_tags WHERE NOT EXISTS (SELECT 1 FROM downloads d WHERE d.id = download_tags.download_id)", connection);
                var removed = repair.ExecuteNonQuery();
                if (removed > 0) Log.Debug("Removed tags left behind by deleted downloads: " + removed);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Tags left behind by deleted downloads could not be removed");
            }
        }

        public bool Export(string file)
        {
            try
            {
                return DataImportExport.CopyToFile(db, file);
            }
            catch (Exception e)
            {
                Log.Debug(e, e.Message);
                return false;
            }
        }

        public bool Import(string file)
        {
            return Import(file, _ => true, () => { });
        }

        public bool Import(string file, Func<IReadOnlyList<string>, bool> placeFiles, Action removeFiles)
        {
            try
            {
                return DataImportExport.CopyFromFile(db, file, placeFiles, removeFiles);
            }
            catch (Exception e)
            {
                Log.Debug(e, e.Message);
                return false;
            }
        }
    }
}
