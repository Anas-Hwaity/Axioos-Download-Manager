using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    public static class Log
    {
        public static void InitFileBasedTrace(string logfile)
        {
            try
            {
                Trace.WriteLine("Log init...");
                Trace.Listeners.Add(new TextWriterTraceListener(logfile, "myListener"));
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
                EnsureRecentErrorListener().RecordException(safeMessage, exception.GetType().Name);
            }
        }

        public static void Debug(string message)
        {
            Trace.WriteLine($"[adm-{DateTime.Now.ToLongTimeString()}] {SensitiveDataRedactor.TextForLog(message)}");
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
