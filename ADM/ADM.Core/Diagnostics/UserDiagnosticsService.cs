using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TraceLog;
using ADM.Core.BrowserMonitoring;
using ADM.Core.DataAccess;
using ADM.Messaging;
using YDLWrapper;

namespace ADM.Core.Diagnostics
{
    public sealed class UserDiagnosticsSnapshot
    {
        public string AppVersion { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string RuntimeVersion { get; set; } = string.Empty;
        public string ProcessArchitecture { get; set; } = string.Empty;
        public int BrowserProtocolVersion { get; set; }
        public int RecoverySchemaVersion { get; set; }
        public string ExternalMediaAnalyzerVersion { get; set; } = string.Empty;
        public IDictionary<string, object> SafeConfiguration { get; set; } = new Dictionary<string, object>();
        public string DatabaseIntegrityStatus { get; set; } = string.Empty;
        public bool RecoverySafeModeRequired { get; set; }
        public string RecoverySafeModeCode { get; set; } = string.Empty;
        public IDictionary<string, int> RecoveryStatusCounts { get; set; } = new Dictionary<string, int>();
        public BrowserProtocolRuntimeSnapshot BrowserProtocolRuntime { get; set; } = new BrowserProtocolRuntimeSnapshot();
        public string[] RecentErrors { get; set; } = Array.Empty<string>();
        public string[] Limitations { get; set; } = Array.Empty<string>();
        public DateTime GeneratedAtUtc { get; set; }
    }

    public static class UserDiagnosticsService
    {
        public static UserDiagnosticsSnapshot Capture()
        {
            var config = Config.Instance;
            var db = AppDB.Instance;
            var safeMode = db.RecoverySafeMode ?? RecoverySafeModeState.Healthy();
            var recoveryCounts = db.RecoveryStatuses
                .GroupBy(status => string.IsNullOrWhiteSpace(status.Code) ? "Unknown" : status.Code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            return new UserDiagnosticsSnapshot
            {
                AppVersion = AppInfo.APP_VERSION,
                OperatingSystem = Environment.OSVersion.ToString(),
                RuntimeVersion = Environment.Version.ToString(),
                ProcessArchitecture = IntPtr.Size == 8 ? "x64" : "x86",
                BrowserProtocolVersion = BrowserProtocolV1.ProtocolVersion,
                RecoverySchemaVersion = RecoverySchema.CurrentVersion,
                ExternalMediaAnalyzerVersion = YDLProcess.TryGetVersion() ?? "Unavailable",
                SafeConfiguration = new Dictionary<string, object>
                {
                    ["browserMonitoringEnabled"] = config.IsBrowserMonitoringEnabled,
                    ["glassmorphismLevel"] = config.GlassmorphismLevel,
                    ["maxParallelDownloads"] = config.MaxParallelDownloads,
                    ["maxSegments"] = config.MaxSegments,
                    ["networkTimeoutSeconds"] = config.NetworkTimeout,
                    ["speedLimitEnabled"] = config.EnableSpeedLimit
                },
                DatabaseIntegrityStatus = DatabaseIntegrityStatus(db, safeMode),
                RecoverySafeModeRequired = safeMode.Required,
                RecoverySafeModeCode = safeMode.Code ?? string.Empty,
                RecoveryStatusCounts = recoveryCounts,
                BrowserProtocolRuntime = BrowserProtocolRuntimeDiagnostics.Snapshot(),
                RecentErrors = Log.GetRecentErrors(),
                Limitations = new[]
                {
                    "Extension version is not exposed at this diagnostics boundary and is therefore omitted.",
                    "Diagnostics intentionally omit database paths, recovery evidence paths, download IDs, URLs, cookies, credentials and authorization material."
                },
                GeneratedAtUtc = DateTime.UtcNow
            };
        }

        private static string DatabaseIntegrityStatus(AppDB db, RecoverySafeModeState safeMode)
        {
            if (safeMode.Required) return "SafeModeRequired:" + (safeMode.Code ?? "Unknown");
            return db.IsInitialized ? "StartupChecksPassed" : "NotInitialized";
        }

        public static string ToJson(UserDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return JsonConvert.SerializeObject(snapshot, Formatting.Indented);
        }

        public static string ToDisplayText(UserDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var b = new StringBuilder();
            b.AppendLine("Axioos diagnostics");
            b.AppendLine($"App: {snapshot.AppVersion}");
            b.AppendLine($"OS: {snapshot.OperatingSystem}");
            b.AppendLine($"Runtime: {snapshot.RuntimeVersion}");
            b.AppendLine($"Architecture: {snapshot.ProcessArchitecture}");
            b.AppendLine($"Browser protocol: v{snapshot.BrowserProtocolVersion}");
            b.AppendLine($"Recovery schema: v{snapshot.RecoverySchemaVersion}");
            b.AppendLine($"Database integrity: {snapshot.DatabaseIntegrityStatus}");
            b.AppendLine($"Recovery safe mode: {(snapshot.RecoverySafeModeRequired ? snapshot.RecoverySafeModeCode : "not required")}");
            b.AppendLine($"Browser endpoint: {snapshot.BrowserProtocolRuntime.ListenerState}; active clients: {snapshot.BrowserProtocolRuntime.ActiveConnections}; successful handshakes: {snapshot.BrowserProtocolRuntime.SuccessfulHandshakes}");
            b.AppendLine($"Recent retained errors: {snapshot.RecentErrors.Length}");
            b.AppendLine($"yt-dlp: {snapshot.ExternalMediaAnalyzerVersion}");
            return b.ToString();
        }

        public static void ExportZip(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Diagnostics export path is required.", nameof(path));
            var snapshot = Capture();
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
            var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(ToJson(snapshot));
        }
    }
}
