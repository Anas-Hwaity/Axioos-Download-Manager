using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using TraceLog;
using ADM.Core;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.Updater;
using ADM.Core.Util;

namespace ADM.Core.UI
{
    public class ComponentUpdaterUIController
    {
        private UpdateMode updateMode;
        private IUpdaterUI updaterUI;
        private IList<UpdateInfo>? updates;
        private int count = 0;
        private SingleSourceHTTPDownloader? http;
        private readonly IList<string> files = new List<string>();
        private readonly IList<string> installNames = new List<string>();
        private long size;
        private long downloaded;
        private readonly Version appVersion;

        public ComponentUpdaterUIController(IUpdaterUI updaterUI, UpdateMode updateMode, Version appVersion)
        {
            this.updaterUI = updaterUI;
            this.updateMode = updateMode;
            this.appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
            try
            {
                this.updaterUI.Cancelled += (s, e) =>
                {
                    if (this.http != null)
                    {
                        this.http.Stop();
                    }
                    this.http = null;
                };
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ComponentUpdaterUI");
            }
        }

        public void StartUpdate()
        {
            new Thread(() =>
            {
                try
                {
                    updaterUI.Inderminate = true;
                    var complete = UpdateChecker.GetAppUpdates(appVersion, out updates, out _, this.updateMode);
                    if (!complete && updates.Count == 0)
                    {
                        updaterUI.DownloadFailed(this, new DownloadFailedEventArgs(ErrorCode.Generic));
                        return;
                    }
                    if (updates.Count == 0)
                    {
                        updaterUI.ShowNoUpdateMessage();
                        return;
                    }
                    foreach (var update in updates)
                    {
                        size += update.Size;
                    }
                    updaterUI.Inderminate = false;
                    StartUpdate(updates[0]);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                    updaterUI.DownloadFailed(this, new DownloadFailedEventArgs(ErrorCode.Generic));
                }
            }).Start();

        }

        private void StartUpdate(UpdateInfo update)
        {
            try
            {
                Log.Debug("Downloading " + update.Name);
                updaterUI.Label = "Downloading " + update.Name;
                http = new SingleSourceHTTPDownloader(new SingleSourceHTTPDownloadInfo
                {
                    Uri = update.Url,
                    Headers = new Dictionary<string, List<string>>
                    {
                        ["User-Agent"] = new List<string>{
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36" }
                    }
                });
                http.SetTargetDirectory(Path.GetTempPath());
                http.Started += updaterUI.DownloadStarted;
                http.Finished += Finished;
                http.ProgressChanged += ProgressChanged;
                http.Cancelled += updaterUI.DownloadCancelled;
                http.Failed += updaterUI.DownloadFailed;
                http.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "StartUpdate");
                updaterUI.DownloadFailed(this, new DownloadFailedEventArgs(ErrorCode.Generic));
            }
        }

        private static void InstallComponentFile(string file, string installName)
        {
            var name = Path.GetFileName(installName);
            var staging = Path.Combine(Config.AppDir, name + "." + Guid.NewGuid().ToString("N") + ".new");
            var target = Path.Combine(Config.AppDir, name);
            Directory.CreateDirectory(Config.AppDir);
            File.Copy(file, staging, true);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(target)) File.Move(target, target + "." + Guid.NewGuid().ToString("N") + ".old");
                    File.Move(staging, target);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
            try { File.Delete(file); } catch (Exception ex) { Log.Debug(ex, "Component download cleanup failed"); }
            foreach (var leftover in Directory.GetFiles(Config.AppDir, name + ".*.old"))
            {
                try { File.Delete(leftover); } catch (Exception ex) { Log.Debug(ex, "Old component is still in use"); }
            }
        }

        private static void InstallJsRuntime(string archive)
        {
            var extracted = Path.Combine(Path.GetTempPath(), "deno." + Guid.NewGuid().ToString("N") + ".extract");
            try
            {
                var found = false;
                using (var archiveStream = new FileStream(archive, FileMode.Open, FileAccess.Read))
                using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (!string.Equals(entry.Name, "deno.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        using (var source = entry.Open())
                        using (var output = File.Create(extracted))
                        {
                            source.CopyTo(output);
                        }
                        found = true;
                        break;
                    }
                }
                if (!found || new FileInfo(extracted).Length == 0)
                {
                    throw new InvalidDataException("The JavaScript runtime archive holds no usable deno.exe.");
                }
                InstallComponentFile(extracted, "deno.exe");
                var installed = new FileInfo(Path.Combine(Config.AppDir, "deno.exe"));
                if (!installed.Exists || installed.Length == 0)
                {
                    throw new IOException("The JavaScript runtime was not installed.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "JavaScript runtime install failed");
                throw;
            }
            finally
            {
                try { if (File.Exists(extracted)) File.Delete(extracted); } catch (Exception ex) { Log.Debug(ex, "JavaScript runtime staging cleanup failed"); }
                try { File.Delete(archive); } catch (Exception ex) { Log.Debug(ex, "JavaScript runtime archive cleanup failed"); }
            }
        }

        private static void VerifyDownload(string file, UpdateInfo update)
        {
            var problem = FindDownloadProblem(file, update);
            if (problem == null) return;
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Rejected component download could not be removed");
            }
            throw new InvalidDataException(problem);
        }

        private static string? FindDownloadProblem(string file, UpdateInfo update)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length == 0) return "The downloaded component is missing or empty.";
            if (update.Size > 0 && info.Length != update.Size) return "The downloaded component has the wrong size.";
            const string prefix = "sha256:";
            var digest = update.Digest;
            if (digest == null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("No published digest for " + update.Name + ", size check only");
                return null;
            }
            string actual;
            using (var stream = File.OpenRead(file))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
            return string.Equals(actual, digest.Substring(prefix.Length).Trim(), StringComparison.OrdinalIgnoreCase)
                ? null
                : "The downloaded component does not match its published digest.";
        }

        private void ProgressChanged(object? sender, ProgressResultEventArgs e)
        {
            try
            {
                var totalProgress = (int)(((downloaded + e.Downloaded) * 100) / size);
                this.updaterUI.DownloadProgressChanged(this, new ProgressResultEventArgs { Progress = totalProgress });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ProgressChanged");
            }
        }

        private void Finished(object? sender, EventArgs e)
        {
            try
            {
                Log.Debug("Finished " + updates[count].Name);
                VerifyDownload(http!.TargetFile!, updates[count]);
                downloaded += updates[count].Size;
                installNames.Add(updates[count].Name);
                count++;
                files.Add(http!.TargetFile!);

#if NET5_0_OR_GREATER
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    PlatformHelper.SetExecutable(http!.TargetFile!);
                }
#endif
                if (count == updates.Count)
                {
                    for (var index = 0; index < files.Count; index++)
                    {
                        var file = files[index];
                        var installName = index < installNames.Count ? installNames[index] : Path.GetFileName(file);
                        if (installName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            InstallJsRuntime(file);
                            continue;
                        }
                        InstallComponentFile(file, installName);
                    }

                    File.WriteAllText(Path.Combine(Config.AppDir, "ytdlp-update.json"),
                        JsonConvert.SerializeObject(new UpdateHistory
                        {
                            YoutubeDLUpdateDate = DateTime.Now
                        }));

                    updaterUI.DownloadFinished(sender, e);
                    return;
                }
                StartUpdate(updates[count]);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Finished");
                updaterUI.DownloadFailed(this, new DownloadFailedEventArgs(ErrorCode.Generic));
            }
        }
    }
}
