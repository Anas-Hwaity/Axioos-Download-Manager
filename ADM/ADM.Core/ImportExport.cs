using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TraceLog;
using ADM.Core;
using ADM.Core.DataAccess;

namespace ADM.Core
{
    internal class ImportExport
    {
        private const string DatabaseEntryName = "downloads-export.db";
        private const string StagedSuffix = ".importing";
        private const long MaxImportBytes = 2L * 1024 * 1024 * 1024;
        private static readonly string[] DownloadFileExtensions = { ".state.1", ".state.2", ".state", ".info" };

        internal static bool Import(string path)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                if (!ExtractKnownEntries(path, tempDir))
                {
                    Log.Debug("Import stopped: the archive is not an Axioos download list");
                    return false;
                }
                var placed = new List<string>();
                return AppDB.Instance.Import(
                    Path.Combine(tempDir, DatabaseEntryName),
                    newIds => PlaceDownloadFiles(tempDir, newIds, placed),
                    () => RemoveFiles(placed));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Import failed");
                return false;
            }
            finally
            {
                TryDeleteTemporaryDirectory(tempDir);
            }
        }

        private static bool ExtractKnownEntries(string path, string tempDir)
        {
            using var zipToOpen = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
            var total = 0L;
            var hasDatabase = false;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
                var name = Path.GetFileName(entry.FullName);
                if (name == null || name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                var isDatabase = string.Equals(name, DatabaseEntryName, StringComparison.OrdinalIgnoreCase);
                if (!isDatabase && !IsDownloadFile(name)) continue;
                total += entry.Length;
                if (total > MaxImportBytes) return false;
                entry.ExtractToFile(Path.Combine(tempDir, isDatabase ? DatabaseEntryName : name), true);
                hasDatabase |= isDatabase;
            }
            return hasDatabase;
        }

        private static bool IsDownloadFile(string name)
        {
            return DownloadFileExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool PlaceDownloadFiles(string tempDir, IReadOnlyList<string> newIds, List<string> placed)
        {
            var staged = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var id in newIds)
                {
                    if (id == null || id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                    foreach (var extension in DownloadFileExtensions)
                    {
                        var source = Path.Combine(tempDir, id + extension);
                        if (!File.Exists(source)) continue;
                        var target = Path.Combine(Config.DataDir, id + extension);
                        var stagedFile = target + StagedSuffix;
                        File.Copy(source, stagedFile, true);
                        staged.Add(new KeyValuePair<string, string>(stagedFile, target));
                    }
                }
                foreach (var pair in staged)
                {
                    if (File.Exists(pair.Value)) File.Delete(pair.Value);
                    File.Move(pair.Key, pair.Value);
                    placed.Add(pair.Value);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Imported download files could not be placed");
                RemoveFiles(staged.Select(pair => pair.Key).ToList());
                RemoveFiles(placed);
                return false;
            }
        }

        private static void RemoveFiles(List<string> files)
        {
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Import cleanup could not remove a file");
                }
            }
            files.Clear();
        }

        internal static bool Export(string path)
        {
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                path = $"{path}.zip";
            }
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var created = false;
            try
            {
                Directory.CreateDirectory(tempDir);
                var dir = new DirectoryInfo(Config.DataDir);
                var filesToAdd = new List<string>();
                var dbFile = Path.Combine(tempDir, DatabaseEntryName);
                if (!AppDB.Instance.Export(dbFile))
                {
                    Log.Debug("Export stopped: the download list could not be copied");
                    return false;
                }
                filesToAdd.Add(dbFile);
                foreach (var extension in DownloadFileExtensions)
                {
                    filesToAdd.AddRange(dir.GetFiles("*" + extension).Select(x => x.FullName));
                }

                var partial = path + ".partial";
                created = true;
                using (var zipToCreate = new FileStream(partial, FileMode.Create))
                using (var archive = new ZipArchive(zipToCreate, ZipArchiveMode.Create))
                {
                    foreach (var file in filesToAdd)
                    {
                        if (!File.Exists(file)) continue;
                        archive.CreateEntryFromFile(file, Path.GetFileName(file));
                    }
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
                created = false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Export failed");
                return false;
            }
            finally
            {
                if (created) RemoveFiles(new List<string> { path + ".partial" });
                TryDeleteTemporaryDirectory(tempDir);
            }
        }

        private static void TryDeleteTemporaryDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException ex)
            {
                Log.Debug(ex, "Temporary import and export folder could not be removed");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug(ex, "Temporary import and export folder could not be removed");
            }
        }
    }
}
