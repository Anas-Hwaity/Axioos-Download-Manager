using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace ADM.Core.DataAccess
{
    public enum HistorySortField
    {
        DateAdded,
        Name,
        Size,
        DownloadType,
        Status
    }

    public enum HistorySortDirection
    {
        Ascending,
        Descending
    }

    public sealed class HistoryQueryRequest
    {
        public const int MaxPageSize = 200;

        public string? SearchText { get; set; }
        public bool? Completed { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? DownloadType { get; set; }
        public string? Domain { get; set; }
        public long? MinSize { get; set; }
        public long? MaxSize { get; set; }
        public string? Tag { get; set; }
        public HistorySortField SortField { get; set; } = HistorySortField.DateAdded;
        public HistorySortDirection SortDirection { get; set; } = HistorySortDirection.Descending;
        public int PageSize { get; set; } = 100;
        public int Offset { get; set; }

        public static string? NormalizeDomain(string? value)
        {
            if (value == null) return null;
            var text = value.Trim();
            if (text.Length == 0) return null;
            var scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) text = text.Substring(scheme + 3);
            var end = text.IndexOfAny(new[] { '/', '?', '#' });
            if (end >= 0) text = text.Substring(0, end);
            var user = text.LastIndexOf('@');
            if (user >= 0) text = text.Substring(user + 1);
            var port = text.LastIndexOf(':');
            if (port >= 0 && text.IndexOf(']') < 0) text = text.Substring(0, port);
            text = text.Trim().Trim('.').ToLowerInvariant();
            if (text.StartsWith("www.", StringComparison.Ordinal) && text.Length > 4) text = text.Substring(4);
            return text.Length == 0 ? null : text;
        }
    }

    public sealed class HistoryRecord
    {
        public string Id { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; }
        public long Size { get; set; }
        public int StatusCode { get; set; }
        public string DownloadType { get; set; } = string.Empty;
        public string TargetDir { get; set; } = string.Empty;
        public string PrimaryUrl { get; set; } = string.Empty;
        public string SafeUrlDisplay { get; set; } = string.Empty;
        public bool FileExists { get; set; }
    }

    public sealed class HistoryPage
    {
        public HistoryPage(IReadOnlyList<HistoryRecord> items, int totalCount, int offset, int pageSize)
        {
            Items = items;
            TotalCount = totalCount;
            Offset = offset;
            PageSize = pageSize;
        }

        public IReadOnlyList<HistoryRecord> Items { get; }
        public int TotalCount { get; }
        public int Offset { get; }
        public int PageSize { get; }
    }

    public interface IHistoryQueryService
    {
        HistoryPage Query(HistoryQueryRequest request);
        IReadOnlyList<string> ListTags();
    }

    public sealed class SqliteHistoryQueryService : IHistoryQueryService
    {
        public const int MaxListedTags = 500;
        private const string UrlRestSql = "substr(primary_url, instr(primary_url, '://') + 3)";
        private const string AuthoritySql = "lower(CASE WHEN instr(" + UrlRestSql + ", '/') > 0 THEN substr(" + UrlRestSql + ", 1, instr(" + UrlRestSql + ", '/') - 1) ELSE " + UrlRestSql + " END)";
        private readonly SQLiteConnection db;

        public SqliteHistoryQueryService(SQLiteConnection db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public IReadOnlyList<string> ListTags()
        {
            var tags = new List<string>();
            lock (db)
            {
                using var command = new SQLiteCommand(
                    "SELECT DISTINCT t.tag FROM download_tags t JOIN downloads d ON d.id=t.download_id WHERE d.completed=1 ORDER BY t.tag COLLATE NOCASE LIMIT @limit", db);
                command.Parameters.AddWithValue("@limit", MaxListedTags);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0)) tags.Add(reader.GetString(0));
                }
            }
            return tags;
        }

        public HistoryPage Query(HistoryQueryRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var pageSize = Math.Max(1, Math.Min(HistoryQueryRequest.MaxPageSize, request.PageSize));
            var offset = Math.Max(0, request.Offset);
            var where = new List<string>();
            var values = new Dictionary<string, object?>();
            BuildFilters(request, where, values);
            var whereSql = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);

            lock (db)
            {
                var total = Count(whereSql, values);
                var items = ReadPage(request, whereSql, values, pageSize, offset);
                return new HistoryPage(items, total, offset, pageSize);
            }
        }

        private int Count(string whereSql, IReadOnlyDictionary<string, object?> values)
        {
            using var command = new SQLiteCommand("SELECT COUNT(*) FROM downloads" + whereSql, db);
            AddParameters(command, values);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private IReadOnlyList<HistoryRecord> ReadPage(
            HistoryQueryRequest request,
            string whereSql,
            IReadOnlyDictionary<string, object?> values,
            int pageSize,
            int offset)
        {
            var sql = new StringBuilder();
            sql.Append("SELECT id, completed, name, date_added, size, status, download_type, targetdir, primary_url FROM downloads");
            sql.Append(whereSql);
            sql.Append(" ORDER BY ");
            sql.Append(GetSortColumn(request.SortField));
            sql.Append(request.SortDirection == HistorySortDirection.Ascending ? " ASC" : " DESC");
            sql.Append(", id ASC LIMIT @limit OFFSET @offset");

            using var command = new SQLiteCommand(sql.ToString(), db);
            AddParameters(command, values);
            command.Parameters.AddWithValue("@limit", pageSize);
            command.Parameters.AddWithValue("@offset", offset);

            var items = new List<HistoryRecord>(pageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var targetDir = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
                var name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var primaryUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                var completed = reader.GetInt32(1) != 0;
                items.Add(new HistoryRecord
                {
                    Id = reader.GetString(0),
                    Completed = completed,
                    Name = name,
                    DateAdded = DateTime.FromBinary(reader.GetInt64(3)),
                    Size = reader.GetInt64(4),
                    StatusCode = reader.GetInt32(5),
                    DownloadType = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    TargetDir = targetDir,
                    PrimaryUrl = primaryUrl,
                    SafeUrlDisplay = CreateSafeUrlDisplay(primaryUrl),
                    FileExists = completed && FileExists(targetDir, name)
                });
            }
            return items;
        }

        private static void BuildFilters(
            HistoryQueryRequest request,
            ICollection<string> where,
            IDictionary<string, object?> values)
        {
            if (request.Completed.HasValue)
            {
                where.Add("completed=@completed");
                values["@completed"] = request.Completed.Value ? 1 : 0;
            }
            if (request.DateFrom.HasValue)
            {
                where.Add("date_added>=@dateFrom");
                values["@dateFrom"] = request.DateFrom.Value.ToBinary();
            }
            if (request.DateTo.HasValue)
            {
                where.Add("date_added<=@dateTo");
                values["@dateTo"] = request.DateTo.Value.ToBinary();
            }
            if (request.DownloadType is string downloadType && !string.IsNullOrWhiteSpace(downloadType))
            {
                where.Add("download_type=@downloadType COLLATE NOCASE");
                values["@downloadType"] = downloadType.Trim();
            }
            if (request.MinSize.HasValue)
            {
                where.Add("size>=@minSize");
                values["@minSize"] = request.MinSize.Value;
            }
            if (request.MaxSize.HasValue)
            {
                where.Add("size<=@maxSize");
                values["@maxSize"] = request.MaxSize.Value;
            }
            if (request.SearchText is string searchText && !string.IsNullOrWhiteSpace(searchText))
            {
                where.Add("(name LIKE @search ESCAPE '\\' COLLATE NOCASE OR primary_url LIKE @search ESCAPE '\\' COLLATE NOCASE)");
                values["@search"] = "%" + EscapeLike(searchText.Trim()) + "%";
            }
            if (HistoryQueryRequest.NormalizeDomain(request.Domain) is string domain)
            {
                var pattern = EscapeLike(domain);
                where.Add("(" + AuthoritySql + "=@domain OR " + AuthoritySql + " LIKE @domainPort ESCAPE '\\' OR " +
                          AuthoritySql + " LIKE @subdomain ESCAPE '\\' OR " + AuthoritySql + " LIKE @subdomainPort ESCAPE '\\')");
                values["@domain"] = domain;
                values["@domainPort"] = pattern + ":%";
                values["@subdomain"] = "%." + pattern;
                values["@subdomainPort"] = "%." + pattern + ":%";
            }
            if (request.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                where.Add("EXISTS (SELECT 1 FROM download_tags t WHERE t.download_id=downloads.id AND t.tag=@tag COLLATE NOCASE)");
                values["@tag"] = tag.Trim();
            }
        }

        private static string GetSortColumn(HistorySortField field)
        {
            return field switch
            {
                HistorySortField.Name => "name COLLATE NOCASE",
                HistorySortField.Size => "size",
                HistorySortField.DownloadType => "download_type COLLATE NOCASE",
                HistorySortField.Status => "completed",
                _ => "date_added"
            };
        }

        private static void AddParameters(SQLiteCommand command, IReadOnlyDictionary<string, object?> values)
        {
            foreach (var item in values)
            {
                command.Parameters.AddWithValue(item.Key, item.Value ?? DBNull.Value);
            }
        }

        private static string EscapeLike(string value)
        {
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static bool FileExists(string targetDir, string name)
        {
            if (string.IsNullOrWhiteSpace(targetDir) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                return File.Exists(Path.Combine(targetDir, name));
            }
            catch
            {
                return false;
            }
        }

        private static string CreateSafeUrlDisplay(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return string.Empty;

            var authority = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
            return uri.Scheme + "://" + authority + uri.AbsolutePath;
        }
    }
}
