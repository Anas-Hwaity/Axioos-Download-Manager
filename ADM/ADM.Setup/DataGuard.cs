using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Axioos.Setup
{
    internal sealed class DataFile
    {
        internal string Path = string.Empty;
        internal long Length;
    }

    internal sealed class DataSnapshot
    {
        internal string Folder = string.Empty;
        internal string Database = string.Empty;
        internal string Backup = string.Empty;
        internal int SavedDownloads;
        internal readonly List<DataFile> Files = new List<DataFile>();
    }

    internal sealed class SetupJournal
    {
        private readonly StringBuilder text = new StringBuilder();

        internal void Note(string description)
        {
            string clean = (description ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            lock (text)
            {
                text.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + clean);
            }
        }

        internal string Text()
        {
            lock (text)
            {
                return text.ToString();
            }
        }
    }

    internal static class DataGuard
    {
        internal const string DataFolderName = ".adm-app-data";
        internal const string DatabaseName = "downloads.db";
        internal const string SettingsName = "settings.dat";
        private const int BackupsKept = 5;
        private const int SetupLogsKept = 6;
        private const long LargestKeptLog = 33554432;

        internal static List<string> KnownFolders()
        {
            var found = new List<string>();
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(profile)) return found;
                string current = Path.Combine(profile, DataFolderName);
                if (Directory.Exists(current)) found.Add(current);
            }
            catch (Exception)
            {
                found.Clear();
            }
            return found;
        }

        internal static List<DataSnapshot> Protect(IList<string> folders, SetupJournal journal)
        {
            var snapshots = new List<DataSnapshot>();
            foreach (string folder in folders)
            {
                var snapshot = new DataSnapshot();
                snapshot.Folder = folder;
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string name in new[] { DatabaseName, SettingsName })
                    {
                        string file = Path.Combine(folder, name);
                        if (!File.Exists(file)) continue;
                        var described = new DataFile();
                        described.Path = file;
                        described.Length = new FileInfo(file).Length;
                        snapshot.Files.Add(described);
                    }
                    snapshot.SavedDownloads = CountSavedDownloads(folder);
                    snapshots.Add(snapshot);
                    journal.Note("Found " + snapshot.Files.Count + " data files and " + snapshot.SavedDownloads + " saved download records in " + folder);
                }
                catch (Exception error)
                {
                    journal.Note("The data folder could not be read: " + error.Message);
                    continue;
                }
                try
                {
                    string database = Path.Combine(folder, DatabaseName);
                    if (File.Exists(database))
                    {
                        snapshot.Database = database;
                        snapshot.Backup = Backup(folder, database);
                        journal.Note("Saved a copy of the download list as " + snapshot.Backup);
                    }
                }
                catch (Exception error)
                {
                    journal.Note("A copy of the download list could not be saved: " + error.Message);
                }
            }
            return snapshots;
        }

        internal static string Verify(IList<DataSnapshot> snapshots, SetupJournal journal, out bool problem)
        {
            problem = false;
            if (snapshots == null || snapshots.Count == 0) return string.Empty;
            bool restored = false;
            foreach (DataSnapshot snapshot in snapshots)
            {
                try
                {
                    foreach (DataFile before in snapshot.Files)
                    {
                        if (StillThere(before)) continue;
                        bool isDatabase = string.Equals(before.Path, snapshot.Database, StringComparison.OrdinalIgnoreCase);
                        if (isDatabase && snapshot.Backup.Length > 0 && File.Exists(snapshot.Backup) && !Installer.AppIsRunning())
                        {
                            File.Copy(snapshot.Backup, snapshot.Database, true);
                            restored = true;
                            journal.Note("The download list was missing after setup and was restored from " + snapshot.Backup);
                        }
                        else
                        {
                            problem = true;
                            journal.Note("A data file is missing after setup: " + before.Path);
                        }
                    }
                    int savedDownloads = CountSavedDownloads(snapshot.Folder);
                    if (savedDownloads < snapshot.SavedDownloads && !Installer.AppIsRunning())
                    {
                        problem = true;
                        journal.Note("Saved download records went from " + snapshot.SavedDownloads + " to " + savedDownloads + " during setup");
                    }
                }
                catch (Exception error)
                {
                    journal.Note("The data folder could not be checked: " + error.Message);
                    return "Setup could not check your data folder. Setup never changes it, so your downloads stay where they are.";
                }
            }
            if (problem) return "Some data files are missing after setup. A copy of your download list is in the backups folder of your data folder.";
            if (restored) return "Your download list was restored from the copy made before setup.";
            return "Your downloads, settings and logs were kept.";
        }

        internal static void KeepSetupLogs(IList<string> folders, string installerLog, SetupJournal journal)
        {
            foreach (string folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    string logs = Path.Combine(folder, "setup-logs");
                    Directory.CreateDirectory(logs);
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(installerLog) && File.Exists(installerLog) && new FileInfo(installerLog).Length <= LargestKeptLog)
                    {
                        File.Copy(installerLog, Path.Combine(logs, "setup-" + stamp + "-installer.log"), true);
                    }
                    File.WriteAllText(Path.Combine(logs, "setup-" + stamp + "-steps.txt"), journal.Text(), new UTF8Encoding(false));
                    Prune(logs, "setup-*", SetupLogsKept);
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        internal static bool PreviousVersionPresent(string customFolder)
        {
            try
            {
                var folders = new List<string>();
                folders.Add(Installer.DefaultFolder());
                if (!string.IsNullOrEmpty(customFolder)) folders.Add(customFolder);
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                {
                    string command = run == null ? null : run.GetValue("ADM") as string;
                    if (!string.IsNullOrEmpty(command) && command.StartsWith("\"", StringComparison.Ordinal))
                    {
                        int end = command.IndexOf('"', 1);
                        if (end > 1) folders.Add(Path.GetDirectoryName(command.Substring(1, end - 1)));
                    }
                }
                foreach (string folder in folders)
                {
                    if (!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, Installer.AppExecutable))) return true;
                }
            }
            catch (Exception)
            {
                return Installer.AppIsRunning();
            }
            return Installer.AppIsRunning();
        }

        private static bool StillThere(DataFile before)
        {
            if (!File.Exists(before.Path)) return false;
            return before.Length == 0 || new FileInfo(before.Path).Length > 0;
        }

        private static int CountSavedDownloads(string folder)
        {
            string data = Path.Combine(folder, "Data");
            if (!Directory.Exists(data)) return 0;
            return Directory.GetFiles(data, "*.state*").Length;
        }

        private static string Backup(string folder, string database)
        {
            string backups = Path.Combine(folder, "backups");
            Directory.CreateDirectory(backups);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = Path.Combine(backups, "downloads-" + stamp + ".db");
            using (var source = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var target = new FileStream(backup, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }
            Prune(backups, "downloads-*.db", BackupsKept);
            return backup;
        }

        private static void Prune(string folder, string pattern, int keep)
        {
            string[] files = Directory.GetFiles(folder, pattern);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < files.Length - keep; index++)
            {
                try
                {
                    File.Delete(files[index]);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }
        }
    }
}
