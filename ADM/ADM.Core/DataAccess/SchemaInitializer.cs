using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;

namespace ADM.Core.DataAccess
{
    public static class SchemaInitializer
    {
        private static void CreateTablesIfNotExists(SQLiteConnection c)
        {
            var query = @"CREATE TABLE IF NOT EXISTS downloads(
                                            id TEXT PRIMARY KEY,
                                            completed INT,
                                            name TEXT,
                                            date_added INT,
                                            size INT,
                                            status INT,
                                            progress INT,
                                            download_type TEXT,
                                            filenamefetchmode INT,
                                            maxspeedlimitinkib INT,
                                            targetdir TEXT,
                                            primary_url TEXT,
                                            referer_url TEXT,
                                            auth INT,
                                            user TEXT,
                                            pass TEXT,
                                            proxy INT,
                                            proxy_host TEXT,
                                            proxy_port INT,
                                            proxy_user TEXT,
                                            proxy_pass TEXT,
                                            proxy_type INT
                                        ) WITHOUT ROWID";
            using var cmd = new SQLiteCommand(c);
            cmd.CommandText = query;
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS download_tags(
                                    download_id TEXT NOT NULL,
                                    tag TEXT NOT NULL COLLATE NOCASE,
                                    PRIMARY KEY(download_id, tag),
                                    FOREIGN KEY(download_id) REFERENCES downloads(id) ON DELETE CASCADE
                                ) WITHOUT ROWID;
                                CREATE INDEX IF NOT EXISTS idx_download_tags_tag ON download_tags(tag, download_id);
                                CREATE INDEX IF NOT EXISTS idx_downloads_history_date ON downloads(completed, date_added DESC, id);
                                CREATE INDEX IF NOT EXISTS idx_downloads_history_name ON downloads(completed, name COLLATE NOCASE, id);
                                CREATE INDEX IF NOT EXISTS idx_downloads_history_size ON downloads(completed, size, id);
                                CREATE INDEX IF NOT EXISTS idx_downloads_history_type ON downloads(completed, download_type COLLATE NOCASE, date_added DESC, id);
                                CREATE INDEX IF NOT EXISTS idx_downloads_history_url ON downloads(primary_url COLLATE NOCASE);";
            cmd.ExecuteNonQuery();
        }

        public static void Init(SQLiteConnection c)
        {
            CreateTablesIfNotExists(c);
            RecoverySchema.Ensure(c);
        }
    }
}
