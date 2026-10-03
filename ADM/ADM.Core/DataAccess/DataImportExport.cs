using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using TraceLog;

namespace ADM.Core.DataAccess
{
    public static class DataImportExport
    {
        public static bool CopyToFile(SQLiteConnection sql, string file)
        {
            try
            {
                var cs = $"URI=file:{file}";
                if (!File.Exists(file))
                {
                    SQLiteConnection.CreateFile(file);
                }
                using var dest = new SQLiteConnection(cs);
                dest.Open();
                lock (sql)
                {
                    sql.BackupDatabase(dest, "main", "main", -1, null, 0);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
                return false;
            }
        }

        public static bool CopyFromFile(SQLiteConnection sql, string file)
        {
            return CopyFromFile(sql, file, _ => true, () => { });
        }

        public static bool CopyFromFile(SQLiteConnection sql, string file, Func<IReadOnlyList<string>, bool> placeFiles, Action removeFiles)
        {
            if (!File.Exists(file)) return false;
            lock (sql)
            {
                var attached = false;
                try
                {
                    using (var attachCmd = new SQLiteCommand("ATTACH DATABASE @file AS imported", sql))
                    {
                        attachCmd.Parameters.AddWithValue("@file", file);
                        attachCmd.ExecuteNonQuery();
                    }
                    attached = true;
                    var source = Columns(sql, "imported", "downloads");
                    if (!source.Contains("id", StringComparer.OrdinalIgnoreCase))
                    {
                        Log.Debug("Import stopped: the file holds no download list");
                        return false;
                    }
                    var shared = Columns(sql, "main", "downloads")
                        .Where(column => source.Contains(column, StringComparer.OrdinalIgnoreCase)).ToList();
                    var columnList = string.Join(", ", shared.Select(column => "\"" + column + "\"").ToArray());
                    var newIds = new List<string>();
                    using (var idCmd = new SQLiteCommand("SELECT id FROM imported.downloads WHERE id IS NOT NULL AND id NOT IN (SELECT id FROM main.downloads)", sql))
                    using (var reader = idCmd.ExecuteReader())
                    {
                        while (reader.Read()) newIds.Add(Convert.ToString(reader.GetValue(0)) ?? string.Empty);
                    }
                    var hasTags = Columns(sql, "imported", "download_tags").Count > 0 && Columns(sql, "main", "download_tags").Count > 0;
                    var placed = false;
                    var transaction = sql.BeginTransaction();
                    try
                    {
                        if (hasTags)
                        {
                            Execute(sql, transaction, "CREATE TEMP TABLE IF NOT EXISTS import_new_ids(id TEXT PRIMARY KEY)");
                            Execute(sql, transaction, "DELETE FROM temp.import_new_ids");
                            Execute(sql, transaction, "INSERT OR IGNORE INTO temp.import_new_ids SELECT id FROM imported.downloads WHERE id IS NOT NULL AND id NOT IN (SELECT id FROM main.downloads)");
                        }
                        Execute(sql, transaction, "INSERT OR IGNORE INTO main.downloads (" + columnList + ") SELECT " + columnList + " FROM imported.downloads WHERE id IS NOT NULL");
                        if (hasTags)
                        {
                            Execute(sql, transaction, "INSERT OR IGNORE INTO main.download_tags (download_id, tag) SELECT download_id, tag FROM imported.download_tags WHERE download_id IN (SELECT id FROM temp.import_new_ids)");
                            Execute(sql, transaction, "DROP TABLE IF EXISTS temp.import_new_ids");
                        }
                        placed = true;
                        if (!placeFiles(newIds))
                        {
                            transaction.Rollback();
                            return false;
                        }
                        transaction.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Import could not be merged, nothing was changed");
                        try
                        {
                            transaction.Rollback();
                        }
                        catch (Exception rollbackError)
                        {
                            Log.Debug(rollbackError, "Import rollback failed");
                        }
                        if (placed) removeFiles();
                        return false;
                    }
                    finally
                    {
                        transaction.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                    return false;
                }
                finally
                {
                    if (attached) Detach(sql);
                }
            }
        }

        private static void Execute(SQLiteConnection sql, SQLiteTransaction transaction, string text)
        {
            using var command = new SQLiteCommand(text, sql, transaction);
            command.ExecuteNonQuery();
        }

        private static void Detach(SQLiteConnection sql)
        {
            try
            {
                using var detachCmd = new SQLiteCommand("DETACH DATABASE imported", sql);
                detachCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Imported database could not be detached");
            }
        }

        private static List<string> Columns(SQLiteConnection sql, string schema, string table)
        {
            var columns = new List<string>();
            using var command = new SQLiteCommand("PRAGMA " + schema + ".table_info(" + table + ")", sql);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = Convert.ToString(reader["name"]) ?? string.Empty;
                if (name.Length > 0 && name.All(character => char.IsLetterOrDigit(character) || character == '_')) columns.Add(name);
            }
            return columns;
        }
    }
}
