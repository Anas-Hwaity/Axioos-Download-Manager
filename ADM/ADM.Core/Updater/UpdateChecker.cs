using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TraceLog;
using ADM.Core;
using ADM.Core.Clients.Http;

namespace ADM.Core.Updater
{
    public class UpdateChecker
    {
        private static readonly string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36";

        public static bool GetAppUpdates(
            Version appVersion,
            out IList<UpdateInfo> updates,
            out bool firstUpdate,
            UpdateMode updateMode = UpdateMode.All)
        {
            updates = new List<UpdateInfo>();

            firstUpdate = !File.Exists(Path.Combine(Config.AppDir, "ytdlp-update.json"));
            var failed = false;
            try
            {

                using var hc = HttpClientFactory.NewHttpClient(null);
                hc.Timeout = TimeSpan.FromSeconds(Config.Instance.NetworkTimeout);

                if ((updateMode & UpdateMode.AppUpdateOnly) == UpdateMode.AppUpdateOnly)
                {
                    var appUpdate = FindNewAppVersion(hc, appVersion, ref failed);
                    if (appUpdate != null)
                    {
                        var au = appUpdate.Value;
                        au.IsExternal = false;
                        updates.Add(au);
                    }
                }

                if ((updateMode & UpdateMode.YoutubeDLUpdateOnly) == UpdateMode.YoutubeDLUpdateOnly)
                {
                    if (!YtDlpInstalled())
                    {
                        var youtubeDLUpdate = FindNewYoutubeDLVersion(hc, DateTime.MinValue, ref failed);
                        if (youtubeDLUpdate != null)
                        {
                            updates.Add(youtubeDLUpdate.Value);
                        }
                    }
                    if (YDLWrapper.YDLProcess.FindBundledJsRuntime(string.Empty) == null && GetDenoAssetForCurrentOS() is AssetPattern denoAsset)
                    {
                        var deno = FindNewRelease(hc, Links.DenoReleaseGH, r => true, denoAsset, ref failed);
                        if (deno != null)
                        {
                            updates.Add(deno.Value);
                        }
                    }
                }


                return !failed;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GetAppUpdates");
            }
            return false;
        }

        private static UpdateInfo? FindNewRelease(IHttpClient hc,
            string url,
            Predicate<GitHubRelease> condition,
            AssetPattern? assetPattern,
            ref bool failed)
        {
            try
            {
                var request = hc.CreateGetRequest(new Uri(url), new Dictionary<string, List<string>>
                {
                    ["User-Agent"] = new List<string> { UserAgent }
                });
                using var response = hc.Send(request);
                using var stream = response.GetResponseStream();
                using var streamReader = new StreamReader(stream);
                using var r = new JsonTextReader(streamReader);
                var serializer = new JsonSerializer();
                var release = serializer.Deserialize<GitHubRelease?>(r);
                if (!release.HasValue) return null;
                if (condition.Invoke(release.Value))
                {
                    if (release.Value.Assets == null) return null;
                    foreach (var asset in release.Value.Assets)
                    {
                        if (assetPattern != null && asset.Name.StartsWith(assetPattern.Value.Prefix))
                        {
                            var found = false;
                            if (assetPattern.Value.Extensions == null || assetPattern.Value.Extensions.Length == 0)
                            {
                                found = true;
                            }
                            else
                            {
                                foreach (var ext in assetPattern.Value.Extensions!)
                                {
                                    if (string.IsNullOrEmpty(ext) || asset.Name.EndsWith(ext)) { found = true; break; }
                                }
                            }
                            if (found)
                            {
                                return new UpdateInfo
                                {
                                    Url = asset.Url,
                                    Name = asset.Name,
                                    Size = asset.Size,
                                    Digest = asset.Digest,
                                    TagName = release.Value.TagName,
                                    IsExternal = true
                                };
                            }
                        }
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Debug(ex, "Error in FindNewRelease");
            }
            return null;
        }

        private static Version ParseGitHubTag(string tag)
        {
            try
            {
                return new Version((tag ?? string.Empty).Trim().TrimStart('v', 'V'));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ParseGitHubTag");
                return new Version(0, 0, 0);
            }
        }

        private static AssetPattern GetYoutubeDLExecutableNameForCurrentOS() =>
            Environment.OSVersion.Platform == PlatformID.Win32NT ?
            new AssetPattern
            {
                Prefix = "yt-dlp_x86",
                Extensions = new string[] { ".exe" }
            } : new AssetPattern
            {
                Prefix = "yt-dlp",
                Extensions = new string[] { }
            };


        internal static AssetPattern? GetDenoAssetForCurrentOS()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Environment.Is64BitOperatingSystem) return null;
            return new AssetPattern { Prefix = "deno-x86_64-pc-windows-msvc", Extensions = new string[] { ".zip" } };
        }

        private static AssetPattern? GetAppInstallerNameForCurrentOS()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return new AssetPattern { Prefix = "admsetup", Extensions = new string[] { ".msi", ".exe" } };
            }
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pkgNameFile = Path.Combine(baseDir, "source_pkg");
            if (File.Exists(pkgNameFile))
            {
                var pkgNamePatterns = File.ReadAllText(pkgNameFile);
                var arr = pkgNamePatterns.Split('|');
                if (arr.Length == 2)
                {
                    return new AssetPattern { Prefix = arr[0], Extensions = arr[1].Split(';') };
                }
            }
            return null;
        }

        private static bool YtDlpInstalled()
        {
            try
            {
                YDLWrapper.YDLProcess.FindYDLBinary();
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private static UpdateInfo? FindNewYoutubeDLVersion(IHttpClient hc, DateTime lastUpdated, ref bool failed) =>
            FindNewRelease(hc, Links.YtDlpReleaseGH, r => r.PublishedAt > lastUpdated,
                GetYoutubeDLExecutableNameForCurrentOS(), ref failed);


        private static UpdateInfo? FindNewAppVersion(IHttpClient hc, Version appVersion, ref bool failed) =>
            FindNewRelease(hc, Links.AppLatestReleaseGH, r => ParseGitHubTag(r.TagName) > appVersion,
                GetAppInstallerNameForCurrentOS(), ref failed);
    }

    internal struct GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        [JsonProperty("published_at")]
        public DateTime PublishedAt { get; set; }
        public Assets[] Assets { get; set; }
        public string Body { get; set; }
    }

    internal struct Assets
    {
        public string Name { get; set; }
        [JsonProperty("browser_download_url")]
        public string Url { get; set; }
        public long Size { get; set; }
        public string? Digest { get; set; }
    }

    public struct UpdateInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string? Digest { get; set; }
        public string TagName { get; set; }
        public bool IsExternal { get; set; }
    }

    public struct UpdateHistory
    {
        public DateTime YoutubeDLUpdateDate { get; set; }
    }

    public struct AssetPattern
    {
        public string Prefix { get; set; }
        public string[] Extensions { get; set; }
    }

    [Flags]
    public enum UpdateMode
    {
        AppUpdateOnly = 4,
        YoutubeDLUpdateOnly = 2,
        All = AppUpdateOnly
    }
}
