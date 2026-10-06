using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADM.Core.IO;
using TraceLog;

namespace ADM.Core.Downloader.Progressive
{
    public sealed class ResumeValidator
    {
        public const string StoreFileName = "validators.db";

        public ResumeValidator(string? etag, string? lastModified)
        {
            ETag = Clean(etag);
            LastModified = Clean(lastModified);
        }

        public string ETag { get; }
        public string LastModified { get; }

        public static string Clean(string? value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", " ").Trim();
        }

        public static bool IsStrong(string etag)
        {
            return etag.Length > 0 && !etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IndicatesChange(ResumeValidator? saved, string? etag, string? lastModified)
        {
            if (saved == null) return false;
            var currentTag = Clean(etag);
            if (!IsStrong(saved.ETag) || !IsStrong(currentTag)) return false;
            if (string.Equals(saved.ETag, currentTag, StringComparison.Ordinal)) return false;
            var currentDate = Clean(lastModified);
            var datesEqual = saved.LastModified.Length > 0 && string.Equals(saved.LastModified, currentDate, StringComparison.Ordinal);
            return !datesEqual;
        }

        public static bool Confirms(ResumeValidator? saved, string? etag)
        {
            if (saved == null) return false;
            var currentTag = Clean(etag);
            return IsStrong(saved.ETag) && IsStrong(currentTag) && string.Equals(saved.ETag, currentTag, StringComparison.Ordinal);
        }

        public static void Save(string? folder, Dictionary<StreamType, ResumeValidator> validators)
        {
            if (folder == null || folder.Length == 0) return;
            try
            {
                var text = new StringBuilder();
                foreach (var pair in validators)
                {
                    text.Append(((int)pair.Key).ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(pair.Value.ETag).Append('\t').Append(pair.Value.LastModified).Append('\n');
                }
                TransactedIO.Write(text.ToString(), StoreFileName, folder);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Resume validators could not be saved");
            }
        }

        public static Dictionary<StreamType, ResumeValidator> Load(string? folder)
        {
            var validators = new Dictionary<StreamType, ResumeValidator>();
            if (folder == null || folder.Length == 0) return validators;
            try
            {
                var text = TransactedIO.Read(StoreFileName, folder);
                if (text == null) return validators;
                foreach (var line in text.Split('\n'))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stream)) continue;
                    validators[(StreamType)stream] = new ResumeValidator(parts[1], parts[2]);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Resume validators could not be read");
            }
            return validators;
        }
    }
}
