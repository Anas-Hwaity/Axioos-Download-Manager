using System;
using System.IO;
using System.Threading;
using TraceLog;

namespace ADM.Core.IO
{
    public static class OutputFileStaging
    {
        public const string StagingPrefix = "axioos-";
        public const string StagingMarker = ".part";
        public const string ReservationFileName = "reserved-name.txt";
        private const int MaxNameAttempts = 100000;

        public static string ReserveUniqueFileName(string file, string folder, string? stateFolder)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var extension = Path.GetExtension(file);
            var earlier = ReadReservation(stateFolder);
            if (earlier != null && BelongsTo(earlier, file, name, extension) && IsEmptyFile(Path.Combine(folder, earlier)))
            {
                return earlier;
            }
            for (var count = 0; count < MaxNameAttempts; count++)
            {
                var candidate = count == 0 ? file : name + "_" + count + extension;
                var path = Path.Combine(folder, candidate);
                if (File.Exists(path) || Directory.Exists(path)) continue;
                try
                {
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                    }
                    WriteReservation(stateFolder, candidate);
                    return candidate;
                }
                catch (IOException ex)
                {
                    if (!File.Exists(path)) throw;
                    Log.Debug(ex, "Another download took this file name first, trying the next one");
                }
            }
            throw new IOException("No free file name was found for " + file);
        }

        public static string StagingPath(string targetFile, string? id, bool keepExtension)
        {
            var folder = Path.GetDirectoryName(targetFile) ?? string.Empty;
            var extension = keepExtension ? Path.GetExtension(targetFile) : string.Empty;
            return Path.Combine(folder, StagingPrefix + Token(id) + StagingMarker + extension);
        }

        public static void ReleaseReservation(string? stateFolder, string? targetFolder)
        {
            if (targetFolder == null || targetFolder.Length == 0) return;
            try
            {
                var reserved = ReadReservation(stateFolder);
                if (reserved == null) return;
                var path = Path.Combine(targetFolder, reserved);
                if (IsEmptyFile(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The reserved file name of a removed download could not be released");
            }
        }

        public static void ForgetReservation(string? stateFolder)
        {
            if (stateFolder == null || stateFolder.Length == 0) return;
            try
            {
                File.Delete(Path.Combine(stateFolder, ReservationFileName));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The reserved file name could not be forgotten");
            }
        }

        private static bool BelongsTo(string reserved, string file, string name, string extension)
        {
            if (string.Equals(reserved, file, StringComparison.OrdinalIgnoreCase)) return true;
            return reserved.Length > name.Length + extension.Length
                && reserved.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                && reserved.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmptyFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length == 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Log.Debug(ex, "A reserved file name could not be checked");
                return false;
            }
        }

        private static string? ReadReservation(string? stateFolder)
        {
            if (stateFolder == null || stateFolder.Length == 0) return null;
            try
            {
                var marker = Path.Combine(stateFolder, ReservationFileName);
                if (!File.Exists(marker)) return null;
                var reserved = File.ReadAllText(marker).Trim();
                if (reserved.Length == 0 || reserved.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                return reserved;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The reserved file name could not be read");
                return null;
            }
        }

        private static void WriteReservation(string? stateFolder, string reserved)
        {
            if (stateFolder == null || stateFolder.Length == 0) return;
            try
            {
                if (!Directory.Exists(stateFolder)) return;
                File.WriteAllText(Path.Combine(stateFolder, ReservationFileName), reserved);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The reserved file name could not be recorded");
            }
        }

        public static void Commit(string stagingFile, string targetFile)
        {
            if (!File.Exists(targetFile))
            {
                File.Move(stagingFile, targetFile);
                return;
            }
            try
            {
                File.Replace(stagingFile, targetFile, null);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The existing file could not be swapped in one step, replacing it in two steps");
            }
            if (!File.Exists(targetFile))
            {
                File.Move(stagingFile, targetFile);
                return;
            }
            var folder = Path.GetDirectoryName(targetFile) ?? string.Empty;
            var aside = Path.Combine(folder, StagingPrefix + Guid.NewGuid().ToString("N").Substring(0, 16) + ".old");
            File.Move(targetFile, aside);
            try
            {
                File.Move(stagingFile, targetFile);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The new file could not be moved into place, putting the previous file back");
                File.Move(aside, targetFile);
                throw;
            }
            try
            {
                File.Delete(aside);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The replaced file could not be removed: " + aside);
            }
        }

        public static void Discard(string? stagingFile, string? reservedTarget)
        {
            if (stagingFile != null && stagingFile.Length > 0)
            {
                try
                {
                    File.Delete(stagingFile);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "The unfinished output file could not be removed");
                }
            }
            if (reservedTarget != null && reservedTarget.Length > 0)
            {
                try
                {
                    var placeholder = new FileInfo(reservedTarget);
                    if (placeholder.Exists && placeholder.Length == 0) placeholder.Delete();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "The reserved file name could not be released");
                }
            }
        }

        public static void DiscardLeftovers(string? folder, string? id)
        {
            if (folder == null || folder.Length == 0 || id == null || id.Length == 0) return;
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var leftover in Directory.GetFiles(folder, StagingPrefix + Token(id) + StagingMarker + "*"))
                {
                    File.Delete(leftover);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Unfinished output files of a removed download could not be cleared");
            }
        }

        public static void DeleteFolder(string? folder)
        {
            if (folder == null || folder.Length == 0) return;
            if (TryDeleteFolder(folder)) return;
            string target = folder;
            var worker = new Thread(() =>
            {
                foreach (var wait in new int[] { 250, 500, 1000, 2000, 4000, 8000, 15000, 30000 })
                {
                    Thread.Sleep(wait);
                    if (TryDeleteFolder(target)) return;
                }
                Log.Debug("Temporary download data is still in use and was left in place: " + target);
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static bool TryDeleteFolder(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return true;
                Directory.Delete(folder, true);
                return !Directory.Exists(folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "Temporary download data is still in use, it will be removed shortly");
                return false;
            }
        }

        private static string Token(string? id)
        {
            var token = id == null || id.Length == 0 ? Guid.NewGuid().ToString("N") : id.Replace("-", string.Empty);
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                token = token.Replace(invalid, '_');
            }
            return token.Length > 16 ? token.Substring(0, 16) : token;
        }
    }
}
