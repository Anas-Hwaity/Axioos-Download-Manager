using System;
using System.IO;
using ADM.Core.Util;

namespace ADM.Core.DataAccess
{
    public interface IHistoryActionService
    {
        bool OpenFile(HistoryRecord record);
        bool OpenFolder(HistoryRecord record);
        bool CanDownloadAgain(HistoryRecord record);
        void DownloadAgain(HistoryRecord record);
    }

    public sealed class HistoryActionService : IHistoryActionService
    {
        private readonly ApplicationCore core;

        public HistoryActionService(ApplicationCore core)
        {
            this.core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public bool OpenFile(HistoryRecord record)
        {
            if (record == null || !record.FileExists) return false;
            var path = Combine(record);
            return path != null && PlatformHelper.OpenFile(path);
        }

        public bool OpenFolder(HistoryRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TargetDir)) return false;
            return PlatformHelper.OpenFolder(record.TargetDir, record.Name);
        }

        public bool CanDownloadAgain(HistoryRecord record)
        {
            return record != null && IsHttpUrl(record.PrimaryUrl);
        }

        public void DownloadAgain(HistoryRecord record)
        {
            if (!CanDownloadAgain(record)) throw new InvalidOperationException("History record does not contain a downloadable HTTP(S) source.");
            core.AddDownload(new Message { Url = record.PrimaryUrl, File = record.Name });
        }

        private static string? Combine(HistoryRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.TargetDir) || string.IsNullOrWhiteSpace(record.Name)) return null;
            try { return Path.Combine(record.TargetDir, record.Name); }
            catch { return null; }
        }

        private static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
