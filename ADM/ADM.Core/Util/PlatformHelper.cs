using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TraceLog;

namespace ADM.Core.Util
{
    public static class PlatformHelper
    {
        public static bool IsFirstRun()
        {
            if (AcceptanceTestEnvironment.SkipFirstRun)
            {
                return false;
            }

            var firstRunFile = Path.Combine(Config.AppDir, "adm-" + AppInfo.APP_VERSION + ".first-run");
            if (!File.Exists(firstRunFile))
            {
                try
                {
                    File.Create(firstRunFile).Close();
                }
                catch { }
                return true;
            }
            return false;
        }

        public static bool KillAll(string processName, out string? processExecutable)
        {
            processExecutable = null;
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes != null)
                {
                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (processExecutable == null)
                            {
                                processExecutable = proc.MainModule?.FileName;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, ex.Message);
                        }

                        try
                        {
                            proc.Kill();
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, ex.Message);
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
            return false;
        }

        public static string GetOsDefaultDownloadFolder()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Win32NativeMethods.GetDownloadDirectoryPath();
            }
            else
            {
#if NET5_0_OR_GREATER
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
#else
                throw new PlatformNotSupportedException("Please use program which was compiled for dotnet5");
#endif
            }
        }

        public static void OpenBrowser(string url)
        {
            var os = Environment.OSVersion.Platform;
            switch (os)
            {
                case PlatformID.Win32NT:
                    var psiShellEx = new ProcessStartInfo
                    {
                        FileName = url,
                    };
                    psiShellEx.UseShellExecute = true;
                    Log.Debug($"Shell execute: {SensitiveDataRedactor.UrlForLog(url)}");
                    Process.Start(psiShellEx);
                    break;
#if NET5_0_OR_GREATER
                case PlatformID.Unix:
                    var psi = ProcessInvocation.Create(
                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open",
                        new string[] { url });
                    Log.Debug("Starting subprocess: " + Path.GetFileName(psi.FileName));
                    Process.Start(psi);
                    break;
#endif
            }
        }

        private static void StartWithoutBlockingCaller(ProcessStartInfo startInfo, string description)
        {
            var launcher = new Thread(() =>
            {
                try
                {
                    var process = Process.Start(startInfo);
                    process?.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, description);
                }
            })
            {
                IsBackground = true,
                Name = "ADM shell launch"
            };
            launcher.TrySetApartmentState(ApartmentState.STA);
            launcher.Start();
        }

        public static bool OpenFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                var os = Environment.OSVersion.Platform;
                switch (os)
                {
                    case PlatformID.Win32NT:
                        var psiShellEx = new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true,
                            ErrorDialog = true
                        };
                        Log.Debug("Opening local file with shell");
                        StartWithoutBlockingCaller(psiShellEx, "OpenFile");
                        return true;
#if NET5_0_OR_GREATER
                    case PlatformID.Unix:
                        var psi = ProcessInvocation.Create(
                            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open",
                            new string[] { path });
                        Log.Debug("Starting subprocess: " + Path.GetFileName(psi.FileName));
                        Process.Start(psi);
                        return true;
#endif
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "OpenFile");
            }
            return false;
        }

        public static bool OpenFolder(string path, string? file = null)
        {
            if (file != null)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var os = Environment.OSVersion.Platform;
                        switch (os)
                        {
                            case PlatformID.Win32NT:
                                {
                                    var psi = ProcessInvocation.Create("explorer",
                                        new string[] { "/select,", Path.Combine(path, file) });
                                    Log.Debug("Starting subprocess: " + Path.GetFileName(psi.FileName));
                                    StartWithoutBlockingCaller(psi, "OpenFolder");
                                    return true;
                                }
#if NET5_0_OR_GREATER
                            case PlatformID.Unix:
                                {
                                    var psi = ProcessInvocation.Create(
                                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open",
                                        new string[] { path });
                                    Log.Debug("Starting subprocess: " + Path.GetFileName(psi.FileName));
                                    Process.Start(psi);
                                    return true;
                                }
#endif
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "OpenFolder");
                    }
                }
                return false;
            }
            return OpenFile(path);
        }

        private static string FindChromeExecutableFromRegistry()
        {
            try
            {
                using var regKey = Registry.ClassesRoot.OpenSubKey(@"ChromeHTML\shell\open\command");
                return FileHelper.GetFileNameFromQuote((string)regKey.GetValue(null));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error fetching chrome location from registry");
            }
            return null;
        }

        public static string GetChromeExecutable()
        {
            var os = Environment.OSVersion.Platform;
            switch (os)
            {
                case PlatformID.Win32NT:
                    var chromeExe = FindChromeExecutableFromRegistry();
                    if (!string.IsNullOrEmpty(chromeExe))
                    {
                        return chromeExe;
                    }
                    var suffix = "Google\\Chrome\\Application\\chrome.exe";
                    foreach (var path in new[] {
                        Path.Combine(Environment.GetEnvironmentVariable("PROGRAMFILES",
                            EnvironmentVariableTarget.Machine),suffix),
                        Path.Combine(Environment.GetEnvironmentVariable("PROGRAMFILES(X86)",
                            EnvironmentVariableTarget.Machine),suffix),
                        Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA",
                            EnvironmentVariableTarget.User),suffix),
                    })
                    {
                        if (File.Exists(path)) return path;
                    }
                    return null;
#if NET5_0_OR_GREATER
                case PlatformID.Unix:
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        const string macChromeExe = "/Applications/Google Chrome.app";
                        if (File.Exists(macChromeExe))
                        {
                            return macChromeExe;
                        }
                    }
                    else
                    {
                        const string linuxChromeExe = "/usr/bin/google-chrome";
                        if (File.Exists(linuxChromeExe))
                        {
                            return linuxChromeExe;
                        }
                    }
                    break;
#endif
            }
            Log.Debug("Chrome executable not found!");
            return null;
        }

        public static string GetFireFoxExecutable()
        {
            var os = Environment.OSVersion.Platform;
            switch (os)
            {
                case PlatformID.Win32NT:
                    var suffix = "Mozilla Firefox\\firefox.exe";
                    foreach (var path in new[] {
                        Path.Combine(Environment.GetEnvironmentVariable("PROGRAMFILES",
                            EnvironmentVariableTarget.Machine),suffix),
                        Path.Combine(Environment.GetEnvironmentVariable("PROGRAMFILES(X86)",
                            EnvironmentVariableTarget.Machine),suffix),
                        Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA",
                            EnvironmentVariableTarget.User),suffix),
                    })
                    {
                        if (File.Exists(path)) return path;
                    }
                    return null;
#if NET5_0_OR_GREATER
                case PlatformID.Unix:
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        const string macFireFoxExe = "/Applications/Firefox.app";
                        if (File.Exists(macFireFoxExe))
                        {
                            return macFireFoxExe;
                        }
                    }
                    else
                    {
                        const string linuxFireFoxExe = "/usr/bin/firefox";
                        if (File.Exists(linuxFireFoxExe))
                        {
                            return linuxFireFoxExe;
                        }
                    }
                    break;
#endif
            }
            Log.Debug("Firefox executable not found!");
            return null;
        }

        public static string GetEdgeExecutable()
        {
            var os = Environment.OSVersion.Platform;
            switch (os)
            {
                case PlatformID.Win32NT:
                    var suffix = @"Edge\Application\msedge.exe";
                    foreach (var path in new[] {
                        Path.Combine(Environment.GetEnvironmentVariable("PROGRAMFILES",
                            EnvironmentVariableTarget.Machine),suffix),
                        Path.Combine(Environment.GetEnvironmentVariable("PROGRAMFILES(X86)",
                            EnvironmentVariableTarget.Machine),suffix),
                        Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA",
                            EnvironmentVariableTarget.User),suffix),
                    })
                    {
                        if (File.Exists(path)) return path;
                    }
                    break;
            }

            return null;
        }

        public static bool EnableAutoStart(bool enable)
        {
            try
            {
                var os = Environment.OSVersion.Platform;
                if (os == PlatformID.Win32NT)
                {
                    using var hkcuRun = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (hkcuRun != null)
                    {
                        if (enable)
                        {
                            var admExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adm-app.exe");
                            hkcuRun.SetValue("ADM", $"\"{admExe}\" --background");
                        }
                        else
                        {
                            hkcuRun.DeleteValue("ADM", false);
                        }
                    }
                    return true;
                }
#if NET5_0_OR_GREATER
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return true;
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var autoStartDir = GetLinuxDesktopAutoStartDir();
                    if (!Directory.Exists(autoStartDir))
                    {
                        Directory.CreateDirectory(autoStartDir);
                    }
                    var desktopFile = Path.Combine(autoStartDir, "adm-app.desktop");
                    File.WriteAllText(desktopFile, GetLinuxDesktopFile());
                    SetExecutable(desktopFile);
                    return true;
                }
#endif
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
            return false;
        }
#if NET5_0_OR_GREATER
        public static string GetLinuxDesktopAutoStartDir()
        {
            var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(configDir))
            {
                configDir = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "~", ".config");
            }
            return Path.Combine(configDir, "autostart");
        }

        public static string GetLinuxDesktopFile()
        {
            var appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adm-app");
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adm-logo.svg");

            return "[Desktop Entry]\r\n" +
                "Encoding=UTF-8\r\n" +
                "Version=1.0\r\n" +
                "Type=Application\r\n" +
                "Terminal=false\r\n" +
                $"Exec=env GTK_USE_PORTAL=1 \"{appPath}\" --background\r\n" +
                "Name=Axioos Download Manager\r\n" +
                "Comment=Axioos Download Manager\r\n" +
                "Categories=Network;\r\n" +
                $"Icon={iconPath}";
        }




#endif

        public static string GetAppPlatform()
        {
            var os = Environment.OSVersion.Platform;
            if (os == PlatformID.Win32NT)
            {
                return "Windows";
            }
#if NET5_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "MacOSX";
            }
#endif
            return "UnsupportedOS";
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                var os = Environment.OSVersion.Platform;
                if (os == PlatformID.Win32NT)
                {
                    using var hkcuRun = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (hkcuRun != null)
                    {
                        var command = (string)hkcuRun.GetValue("ADM");
                        var path = FileHelper.GetFileNameFromQuote(command);
                        return !string.IsNullOrEmpty(path) &&
                            path == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adm-app.exe");
                    }
                }
#if NET5_0_OR_GREATER
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var autoStartDir = GetLinuxDesktopAutoStartDir();
                    if (!Directory.Exists(autoStartDir))
                    {
                        return false;
                    }
                    var file = Path.Combine(autoStartDir, "adm-app.desktop");
                    if (!File.Exists(file))
                    {
                        return false;
                    }
                    var text = File.ReadAllText(file);
                    return text.Contains(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adm-app"));
                }
#endif
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }

            return false;
        }

        public static void SpawnSubProcess(string executable,
            string[]? args = null,
            bool useShellExecute = false,
            bool createNoWindow = true)
        {
            var psi = ProcessInvocation.Create(executable, args, useShellExecute, createNoWindow);
            Log.Debug("Starting subprocess: " + Path.GetFileName(psi.FileName));
            Process.Start(psi);
        }

        public static void ShutDownPC()
        {
            Log.Debug("Issuing shutdown command...");
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    SpawnSubProcess("shutdown", new string[] { "/t", "30", "/s" });
                    break;
#if NET5_0_OR_GREATER
                case PlatformID.Unix:
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        var cmd = "org.freedesktop.login1.Manager.PowerOff";
                        SpawnSubProcess("dbus-send", new string[] { "--system", "--print-reply",
                            "--dest=org.freedesktop.login1", "/org/freedesktop/login1", cmd, "boolean:true"});
                        return;
                    }
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        var cmd = "tell app \"System Events\" to shut down";
                        SpawnSubProcess("osascript", new string[] { "-e", cmd });
                    }
                    break;
#endif
                default:
                    Log.Debug("Operating system not supported");
                    break;
            }
        }

        public static void SendKeepAlivePing()
        {
            Log.Debug("Keep alive ping...");
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    SetThreadExecutionState(EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                    break;
#if NET5_0_OR_GREATER
                case PlatformID.Unix:
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        SpawnSubProcess("dbus-send", new string[] { "--print-reply", "--type=method_call",
                            "--dest=org.freedesktop.ScreenSaver", "/ScreenSaver", "org.freedesktop.ScreenSaver.SimulateUserActivity" });
                        return;
                    }
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        SpawnSubProcess("caffeinate", new string[] { "-i", "-t", "3" });
                    }
                    break;
#endif
                default:
                    Log.Debug("Operating system not supported");
                    break;
            }
        }

        public static void RunCommand(string cmd)
        {
            var parts = ProcessInvocation.ParseLegacyCommandLine(cmd);
            if (parts.Count == 0) return;
            var args = new string[Math.Max(0, parts.Count - 1)];
            for (var i = 1; i < parts.Count; i++) args[i - 1] = parts[i];
            Log.Debug("Running configured completion program: " + SensitiveDataRedactor.TextForLog(parts[0]));
            SpawnSubProcess(parts[0], args);
        }

        public static void RunAntivirus(string cmd, string options, string file)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            var optionParts = ProcessInvocation.ParseLegacyCommandLine(options ?? string.Empty);
            var args = new string[optionParts.Count + 1];
            for (var i = 0; i < optionParts.Count; i++) args[i] = optionParts[i];
            args[args.Length - 1] = file;
            Log.Debug("Running configured antivirus: " + SensitiveDataRedactor.TextForLog(cmd));
            SpawnSubProcess(cmd, args);
        }

        public static string? FindExecutableFromSystemPath(string executableName)
        {
            var values = Environment.GetEnvironmentVariable("PATH");
            foreach (var spath in values?.Split(Path.PathSeparator) ?? new string[] { string.Empty })
            {
                var fullPath = Path.Combine(spath, executableName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        }

        public static void OpenWindowsProxySettings()
        {
            if (Environment.OSVersion.Version.Major == 10)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ms-settings:network-proxy",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "inetcpl.cpl,LaunchConnectionDialog",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }

        public static bool SetExecutable(string path)
        {
            const int _0755 =
            S_IRUSR | S_IXUSR | S_IWUSR
            | S_IRGRP | S_IXGRP
            | S_IROTH | S_IXOTH;
            return (chmod(Path.GetFullPath(path), (int)_0755) == 0);
        }

        [FlagsAttribute]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        const int S_IRUSR = 0x100;
        const int S_IWUSR = 0x80;
        const int S_IXUSR = 0x40;

        const int S_IRGRP = 0x20;
        const int S_IWGRP = 0x10;
        const int S_IXGRP = 0x8;

        const int S_IROTH = 0x4;
        const int S_IWOTH = 0x2;
        const int S_IXOTH = 0x1;

        [DllImport("libc", SetLastError = true)]
        public static extern int chmod(string pathname, int mode);

    }
}
