using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ADM.Core.Util;

namespace TraceLog
{
    internal sealed class RecentErrorTraceListener : TraceListener
    {
        private const int MaxRecentErrors = 50;
        private readonly object sync = new object();
        private readonly Queue<string> recentErrors = new Queue<string>();

        public void RecordException(string message, string exceptionType)
        {
            var value = SensitiveDataRedactor.TextForLog($"{DateTime.UtcNow:o} {exceptionType}: {message}");
            lock (sync)
            {
                while (recentErrors.Count >= MaxRecentErrors) recentErrors.Dequeue();
                recentErrors.Enqueue(value);
            }
        }

        public string[] Snapshot()
        {
            lock (sync) return recentErrors.ToArray();
        }

        public override void Write(string message) { }
        public override void WriteLine(string message) { }
    }

    internal sealed class RollingLogTraceListener : TraceListener
    {
        internal const long MaxBytes = 4 * 1024 * 1024;
        internal const int Generations = 3;
        private readonly object sync = new object();
        private readonly string path;
        private readonly UTF8Encoding encoding = new UTF8Encoding(false);
        private const int FailuresBeforeGivingUp = 5;
        private StreamWriter? writer;
        private long written;
        private int failures;
        private bool disabled;

        public RollingLogTraceListener(string path)
        {
            this.path = path;
        }

        public override void Write(string? message)
        {
            Append(message, false);
        }

        public override void WriteLine(string? message)
        {
            Append(message, true);
        }

        internal static string GenerationPath(string path, int generation)
        {
            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            return Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "." + generation + Path.GetExtension(path));
        }

        private void Append(string? message, bool newLine)
        {
            lock (sync)
            {
                if (disabled) return;
                try
                {
                    var target = writer;
                    if (target == null || written >= MaxBytes) target = Open(written >= MaxBytes);
                    var text = message ?? string.Empty;
                    if (newLine) target.WriteLine(text);
                    else target.Write(text);
                    target.Flush();
                    written += encoding.GetByteCount(text) + 2;
                    failures = 0;
                }
                catch (Exception ex)
                {
                    failures++;
                    if (failures >= FailuresBeforeGivingUp) disabled = true;
                    var broken = writer;
                    writer = null;
                    CloseQuietly(broken);
                    System.Diagnostics.Debugger.Log(0, null, ex.Message + Environment.NewLine);
                }
            }
        }

        private StreamWriter Open(bool roll)
        {
            writer?.Dispose();
            writer = null;
            var folder = Path.GetDirectoryName(path);
            if (folder != null && folder.Length > 0) Directory.CreateDirectory(folder);
            if (roll || (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)) Roll();
            var stream = OpenStream(path);
            written = File.Exists(path) ? new FileInfo(path).Length : 0L;
            var opened = new StreamWriter(stream, encoding);
            writer = opened;
            return opened;
        }

        private static FileStream OpenStream(string path)
        {
#if !NET5_0_OR_GREATER
            try
            {
                return new FileStream(path, FileMode.Append, System.Security.AccessControl.FileSystemRights.AppendData, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                System.Diagnostics.Debugger.Log(0, null, ex.Message + Environment.NewLine);
            }
#endif
            return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }

        private static void CloseQuietly(StreamWriter? broken)
        {
            if (broken == null) return;
            try
            {
                broken.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debugger.Log(0, null, ex.Message + Environment.NewLine);
            }
        }

        private void Roll()
        {
            var oldest = GenerationPath(path, Generations);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var generation = Generations - 1; generation >= 1; generation--)
            {
                var from = GenerationPath(path, generation);
                if (File.Exists(from)) File.Move(from, GenerationPath(path, generation + 1));
            }
            if (File.Exists(path)) File.Move(path, GenerationPath(path, 1));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (sync)
                {
                    writer?.Dispose();
                    writer = null;
                    disabled = true;
                }
            }
            base.Dispose(disposing);
        }
    }

    public static class Log
    {
        public static void InitFileBasedTrace(string logfile)
        {
            try
            {
                Trace.WriteLine("Log init...");
                Trace.Listeners.Add(new RollingLogTraceListener(logfile) { Name = "myListener" });
                EnsureRecentErrorListener();
                Trace.AutoFlush = true;
                Trace.WriteLine("Log init...");
            }
            catch (Exception ex)
            {
                Trace.WriteLine(SensitiveDataRedactor.TextForLog(ex.ToString()));
            }
        }

        public static void Debug(object obj, string message)
        {
            var safeMessage = SensitiveDataRedactor.TextForLog(message);
            var safeObject = SensitiveDataRedactor.TextForLog(obj?.ToString());
            Trace.WriteLine($"[adm-{DateTime.Now.ToLongTimeString()}] {safeMessage} : {safeObject}");
            if (obj is Exception exception)
            {
                EnsureRecentErrorListener().RecordException(safeMessage, DescribeExceptionTypes(exception));
            }
        }

        public static void Debug(string message)
        {
            Trace.WriteLine($"[adm-{DateTime.Now.ToLongTimeString()}] {SensitiveDataRedactor.TextForLog(message)}");
        }

        internal static string DescribeExceptionTypes(Exception exception)
        {
            var name = exception.GetType().Name;
            var inner = exception.InnerException;
            var depth = 0;
            while (inner != null && depth < 4)
            {
                name += " caused by " + inner.GetType().Name;
                inner = inner.InnerException;
                depth++;
            }
            return name;
        }

        public static string[] GetRecentErrors() => EnsureRecentErrorListener().Snapshot();

        private static RecentErrorTraceListener EnsureRecentErrorListener()
        {
            var existing = Trace.Listeners.OfType<RecentErrorTraceListener>().FirstOrDefault();
            if (existing != null) return existing;
            var listener = new RecentErrorTraceListener { Name = "admRecentErrors" };
            Trace.Listeners.Add(listener);
            return listener;
        }
    }
}
