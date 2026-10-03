using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ADM.Core.Util;

namespace ADM.Wpf.UI.Diagnostics
{
    internal static class AcceptanceDiagnostics
    {
        public static void RecordStage(string stage, string? detail = null)
        {
            Append("stage", stage, detail ?? string.Empty);
        }

        public static void RecordException(string stage, Exception exception)
        {
            Append("exception", stage, exception.ToString());
        }

        private static void Append(string kind, string stage, string detail)
        {
            if (!AcceptanceTestEnvironment.IsEnabled)
            {
                return;
            }

            var path = AcceptanceTestEnvironment.DiagnosticsPath;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                Trace.WriteLine("Acceptance diagnostics path is missing or not absolute.");
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = string.Join("\t", new[]
                {
                    DateTime.UtcNow.ToString("O"),
                    Process.GetCurrentProcess().Id.ToString(),
                    Thread.CurrentThread.ManagedThreadId.ToString(),
                    Sanitize(AcceptanceTestEnvironment.CorrelationId ?? string.Empty),
                    Sanitize(kind),
                    Sanitize(stage),
                    Sanitize(detail)
                });
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Trace.WriteLine("Acceptance diagnostic write failed: " + exception);
            }
        }

        private static string Sanitize(string value)
        {
            return value.Replace("\t", " ").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
