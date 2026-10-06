using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Axioos.Setup
{
    internal enum SetupMode
    {
        Branded,
        Classic,
        Quiet
    }

    internal enum SetupStep
    {
        Unpacking,
        Closing,
        Installing,
        Approval,
        Protecting,
        Replacing,
        Checking
    }

    internal sealed class InstallResult
    {
        internal int Code;
        internal bool Success;
        internal bool Cancelled;
        internal bool RestartNeeded;
        internal string Message;
        internal string DataNote;
        internal bool DataProblem;

        internal static InstallResult FromExitCode(int code, string logFile)
        {
            var result = new InstallResult();
            result.Code = code;
            if (code == 0)
            {
                result.Success = true;
            }
            else if (code == 3010 || code == 1641)
            {
                result.Success = true;
                result.RestartNeeded = true;
            }
            else if (code == 1602)
            {
                result.Cancelled = true;
            }
            else if (code == 1618)
            {
                result.Message = "Another installation is running. Wait for it to finish, then try again.";
            }
            else
            {
                result.Message = "Windows Installer stopped with code " + code + ".";
                if (logFile != null && File.Exists(logFile)) result.Message += " Details: " + logFile;
            }
            return result;
        }

        internal static InstallResult Failure(string message)
        {
            var result = new InstallResult();
            result.Code = 1603;
            result.Message = message;
            return result;
        }

        internal static InstallResult Cancel()
        {
            var result = new InstallResult();
            result.Code = 1602;
            result.Cancelled = true;
            return result;
        }
    }

    internal static class Installer
    {
        internal const string AppExecutable = "adm-app.exe";
        private const int ElevationCancelled = 1223;

        internal static string DefaultFolder()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (string.IsNullOrEmpty(root)) root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(root, "Axioos");
        }

        internal static bool AppIsRunning()
        {
            try
            {
                Process[] running = Process.GetProcessesByName("adm-app");
                bool any = running.Length > 0;
                foreach (Process process in running) process.Dispose();
                return any;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CloseRunningApp()
        {
            foreach (string name in new[] { "adm-app", "adm-app-host" })
            {
                Process[] running;
                try
                {
                    running = Process.GetProcessesByName(name);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (Process process in running)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(50);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        internal static InstallResult Run(string customFolder, IList<string> passThrough, SetupMode mode, Action<SetupStep, int> report)
        {
            Action<int> unpackProgress = null;
            if (report != null) unpackProgress = percent => report(SetupStep.Unpacking, percent);
            SetupPayload payload = SetupPayload.Unpack(unpackProgress);
            string folder = Path.Combine(Path.GetTempPath(), "Axioos-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string installer = Path.Combine(folder, payload.FileName);
                File.WriteAllBytes(installer, payload.Content);
                if (report != null) report(SetupStep.Approval, 0);
                string logFile = Path.Combine(Path.GetTempPath(), "Axioos-Setup.log");
                bool replacing = mode != SetupMode.Classic && DataGuard.PreviousVersionPresent(customFolder);
                var arguments = new StringBuilder("/i \"" + installer + "\"");
                if (mode != SetupMode.Classic) arguments.Append(" /qn /norestart");
                if (mode == SetupMode.Branded) arguments.Append(" /l*v \"" + logFile + "\"");
                if (!string.IsNullOrEmpty(customFolder)) arguments.Append(" INSTALLFOLDER=\"" + customFolder.TrimEnd('\\') + "\"");
                foreach (string extra in passThrough) arguments.Append(' ').Append(Quote(extra));
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), arguments.ToString());
                if (mode == SetupMode.Branded || (mode == SetupMode.Quiet && !IsAdministrator()))
                {
                    start.UseShellExecute = true;
                    start.Verb = "runas";
                }
                else
                {
                    start.UseShellExecute = false;
                }
                try
                {
                    using (Process process = Process.Start(start))
                    {
                        if (process == null) throw new IOException("Windows Installer could not be started.");
                        if (mode == SetupMode.Classic)
                        {
                            process.WaitForExit();
                            return InstallResult.FromExitCode(process.ExitCode, null);
                        }
                        var journal = new SetupJournal();
                        journal.Note("Setup started for Axioos Download Manager");
                        if (report != null) report(SetupStep.Closing, 0);
                        CloseRunningApp();
                        journal.Note("Axioos was closed");
                        if (report != null) report(SetupStep.Protecting, 0);
                        List<string> dataFolders = DataGuard.KnownFolders();
                        List<DataSnapshot> snapshots = DataGuard.Protect(dataFolders, journal);
                        if (report != null) report(replacing ? SetupStep.Replacing : SetupStep.Installing, 0);
                        process.WaitForExit();
                        journal.Note("Windows Installer ended with code " + process.ExitCode);
                        InstallResult result = InstallResult.FromExitCode(process.ExitCode, mode == SetupMode.Branded ? logFile : null);
                        if (report != null) report(SetupStep.Checking, 0);
                        bool dataProblem;
                        result.DataNote = DataGuard.Verify(snapshots, journal, out dataProblem);
                        result.DataProblem = dataProblem;
                        if (result.DataNote.Length > 0) journal.Note(result.DataNote);
                        DataGuard.KeepSetupLogs(dataFolders, mode == SetupMode.Branded ? logFile : null, journal);
                        return result;
                    }
                }
                catch (Win32Exception error)
                {
                    if (error.NativeErrorCode == ElevationCancelled) return InstallResult.Cancel();
                    throw;
                }
            }
            finally
            {
                TryDelete(folder);
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Quote(string value)
        {
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return value;
            int equals = value.IndexOf('=');
            if (equals > 0 && value.IndexOf('"') < 0 && IsPropertyName(value.Substring(0, equals)))
            {
                return value.Substring(0, equals + 1) + "\"" + value.Substring(equals + 1) + "\"";
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static bool IsPropertyName(string name)
        {
            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '.') return false;
            }
            return name.Length > 0;
        }

        private static void TryDelete(string folder)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(400);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(400);
                }
            }
        }
    }
}
