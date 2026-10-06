using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;

namespace YDLWrapper
{
    public class YDLProcess
    {
        public Uri? Uri { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? JsonOutputFile { get; set; }
        public string? BrowserName { get; set; }
        public string? CookieHeader { get; set; }

        private Process? ydlProc;
        private readonly StringBuilder errorBuffer = new();
        public const int MaxJsonOutputBytes = 256 * 1024 * 1024;
        public const int MaxErrorCaptureChars = 64 * 1024;
        public int TimeoutSeconds { get; set; } = 120;
        public string LastErrorDetail { get; private set; } = string.Empty;
        public const int MaxProviderVersionChars = 256;

        public static string? TryGetVersion(int timeoutMilliseconds = 3000)
        {
            try
            {
                var exec = FindYDLBinary();
                var arguments = new StringBuilder();
                ProcessArgumentEncoder.AppendArgument(arguments, "--version");
                var startInfo = new ProcessStartInfo
                {
                    FileName = exec.Path,
                    Arguments = arguments.ToString(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = PrepareWorkingDirectory()
                };
                using var process = Process.Start(startInfo);
                if (process == null) return null;
                if (!process.WaitForExit(Math.Max(250, timeoutMilliseconds)))
                {
                    try { process.Kill(); } catch (Exception ex) { Log.Debug(ex, "Unable to stop the yt-dlp version check"); }
                    return null;
                }
                var version = process.StandardOutput.ReadToEnd().Trim();
                if (version.Length == 0) return null;
                return version.Length <= MaxProviderVersionChars ? version : version.Substring(0, MaxProviderVersionChars);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "yt-dlp version could not be read");
                return null;
            }
        }

        public void Cancel()
        {
            if (ydlProc != null)
            {
                try
                {
                    ydlProc.Kill();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "yt-dlp was already stopped");
                }
            }
        }

        public void Start()
        {
            errorBuffer.Clear();
            var exec = FindYDLBinary();
            var workFolder = PrepareWorkingDirectory();
            var pb = new ProcessStartInfo
            {
                FileName = exec.Path,
                WorkingDirectory = workFolder,
            };

            var sb = new StringBuilder();
            foreach (var arg in new string[] {
                "--no-warnings", "-q", "-i", "-J", "--no-playlist", "--ignore-config",
                "--socket-timeout", "15", "--retries", "2", "--extractor-retries", "2" })
            {
                ProcessArgumentEncoder.AppendArgument(sb, arg);
            }

            ProcessArgumentEncoder.AppendArgument(sb, "--encoding");
            ProcessArgumentEncoder.AppendArgument(sb, "utf-8");

            if (exec.BinaryType == YtBinaryType.YtDlp)
            {
                ProcessArgumentEncoder.AppendArgument(sb, "--paths");
                ProcessArgumentEncoder.AppendArgument(sb, "temp:" + workFolder);
                ProcessArgumentEncoder.AppendArgument(sb, "--paths");
                ProcessArgumentEncoder.AppendArgument(sb, "home:" + workFolder);
            }

            if (exec.BinaryType == YtBinaryType.YtDlp && FindBundledJsRuntime(exec.Path) is string jsRuntime)
            {
                ProcessArgumentEncoder.AppendArgument(sb, "--js-runtimes");
                ProcessArgumentEncoder.AppendArgument(sb, "deno:" + jsRuntime);
            }

            if (exec.BinaryType == YtBinaryType.YtDlp && BrowserName is string browserName && browserName.Length > 0)
            {
                ProcessArgumentEncoder.AppendArgument(sb, "--cookies-from-browser");
                ProcessArgumentEncoder.AppendArgument(sb, browserName);
            }
            if (!string.IsNullOrEmpty(CookieHeader))
            {
                ProcessArgumentEncoder.AppendArgument(sb, "--add-header");
                ProcessArgumentEncoder.AppendArgument(sb, "Cookie: " + CookieHeader);
            }

            ProcessArgumentEncoder.AppendArgument(sb, Uri!.AbsoluteUri);

            if (UserName is string userName && userName.Length > 0)
            {
                ProcessArgumentEncoder.AppendArgument(sb, "--username");
                ProcessArgumentEncoder.AppendArgument(sb, userName);
                if (Password is string password && password.Length > 0)
                {
                    ProcessArgumentEncoder.AppendArgument(sb, "--password");
                    ProcessArgumentEncoder.AppendArgument(sb, password);
                }
            }

            pb.Arguments = sb.ToString();

            Log.Debug("Starting external media analyzer: " + Path.GetFileName(exec.Path));

            pb.RedirectStandardOutput = true;
            pb.CreateNoWindow = true;
            pb.UseShellExecute = false;
            pb.RedirectStandardError = true;
            pb.RedirectStandardInput = false;
            pb.StandardOutputEncoding = Encoding.UTF8;
            pb.StandardErrorEncoding = Encoding.UTF8;
            JsonOutputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            Log.Debug("Opening temporary yt-dlp metadata file");
            using var fs = new FileStream(JsonOutputFile,
                FileMode.Create, FileAccess.ReadWrite);
            long outputBytes = 0;
            var outputLimitExceeded = false;
            System.Threading.Tasks.Task? outputCopy = null;

            try
            {
                var process = Process.Start(pb) ?? throw new InvalidOperationException("yt-dlp could not be started.");
                ydlProc = process;
                outputCopy = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var source = process.StandardOutput.BaseStream;
                        var chunk = new byte[81920];
                        int read;
                        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            if (outputBytes + read > MaxJsonOutputBytes)
                            {
                                outputLimitExceeded = true;
                                try { process.Kill(); } catch (Exception ex) { Log.Debug(ex, "Unable to stop yt-dlp after its output grew too large"); }
                                return;
                            }
                            fs.Write(chunk, 0, read);
                            outputBytes += read;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "yt-dlp output copy stopped");
                    }
                });

                ydlProc.ErrorDataReceived += (a, b) =>
                {
                    if (b.Data != null)
                    {
                        lock (errorBuffer)
                        {
                            var remaining = MaxErrorCaptureChars - errorBuffer.Length;
                            if (remaining > 0)
                            {
                                var line = b.Data.Length <= remaining ? b.Data : b.Data.Substring(0, remaining);
                                errorBuffer.Append(line);
                                if (errorBuffer.Length < MaxErrorCaptureChars) errorBuffer.AppendLine();
                            }
                        }
                    }
                };

                ydlProc.BeginErrorReadLine();

                if (!ydlProc.WaitForExit(Math.Max(1, TimeoutSeconds) * 1000))
                {
                    try { ydlProc.Kill(); }
                    catch (Exception ex) { Log.Debug(ex, "Unable to terminate timed-out yt-dlp process"); }
                    throw new TimeoutException("yt-dlp analysis timed out after " + TimeoutSeconds + " seconds.");
                }
                ydlProc.WaitForExit();
                if (!outputCopy.Wait(TimeSpan.FromSeconds(30)))
                {
                    try { ydlProc.Kill(); } catch (Exception ex) { Log.Debug(ex, "Unable to stop yt-dlp after its output stalled"); }
                    throw new TimeoutException("yt-dlp output did not finish.");
                }
                fs.Close();
                lock (errorBuffer)
                {
                    var captured = errorBuffer.ToString().Trim();
                    LastErrorDetail = captured.Length > 700 ? captured.Substring(captured.Length - 700) : captured;
                }
                if (outputLimitExceeded)
                    throw new InvalidDataException("yt-dlp output exceeded the configured limit.");

                if (ydlProc.ExitCode != 0)
                {
                    Log.Debug("Non-zero error code from youtube-dl: " + ydlProc.ExitCode);
                    var detail = errorBuffer.ToString().Trim();
                    if (detail.Length > 700) detail = detail.Substring(detail.Length - 700);
                    throw new Exception("yt-dlp exited with code " + ydlProc.ExitCode +
                        (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
                }
            }
            finally
            {
                try { outputCopy?.Wait(5000); } catch (Exception ex) { Log.Debug(ex, "yt-dlp output copy did not stop"); }
                ydlProc?.Dispose();
                ydlProc = null;
            }
        }

        internal static string PrepareWorkingDirectory()
        {
            foreach (var root in new string[] { Path.GetTempPath(), Config.AppDir })
            {
                try
                {
                    var folder = Path.Combine(root, "Axioos-analysis").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    Directory.CreateDirectory(folder);
                    return folder;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    Log.Debug(ex, "yt-dlp working folder could not be prepared");
                }
            }
            return Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static string? FindBundledJsRuntime(string ytDlpPath)
        {
            var name = Environment.OSVersion.Platform == PlatformID.Win32NT ? "deno.exe" : "deno";
            var folders = new List<string>();
            var ytDlpFolder = string.IsNullOrEmpty(ytDlpPath) ? null : Path.GetDirectoryName(ytDlpPath);
            if (!string.IsNullOrEmpty(ytDlpFolder)) folders.Add(ytDlpFolder!);
            folders.Add(Config.AppDir);
            folders.Add(AppDomain.CurrentDomain.BaseDirectory);
            foreach (var folder in folders)
            {
                try
                {
                    var candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        private static YtBinaryType GetYtBinaryType(string executableName)
        {
            if (executableName.StartsWith("yt-dlp"))
            {
                return YtBinaryType.YtDlp;
            }
            return YtBinaryType.Yt;
        }

        public static YtBinary FindYDLBinary()
        {
            var executableNames = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? new string[] { "yt-dlp.exe", "yt-dlp_x86.exe", "youtube-dl.exe" }
                : new string[] { "yt-dlp", "yt-dlp_linux", "youtube-dl" };
            string? binPath = null;
            string? execName = null;
            var found = false;
            foreach (var executableName in executableNames)
            {
                execName = executableName;
                var path = Path.Combine(Config.AppDir, executableName);
                if (File.Exists(path))
                {
                    found = true;
                    binPath = path;
                    break;
                }
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, executableName);
                if (File.Exists(path))
                {
                    found = true;
                    binPath = path;
                    break;
                }
                var ydlPathEnvVar = Environment.GetEnvironmentVariable("YOUTUBEDL_HOME");
                if (ydlPathEnvVar != null)
                {
                    path = Path.Combine(ydlPathEnvVar, executableName);
                    if (File.Exists(path))
                    {
                        found = true;
                        binPath = path;
                        break;
                    }
                }
                path = PlatformHelper.FindExecutableFromSystemPath(executableName);
                if (path != null)
                {
                    found = true;
                    binPath = path;
                    break;
                }
            }
            if (found)
            {
                return new YtBinary { BinaryType = GetYtBinaryType(execName!), Path = binPath! };
            }
            throw new FileNotFoundException("YoutubeDL executable not found");
        }


    }
}
