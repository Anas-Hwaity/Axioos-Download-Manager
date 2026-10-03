using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using YDLWrapper;

namespace ADM.Core.SocialAnalysis
{
    public sealed class YtDlpAnalyzer : IExternalMediaAnalyzer
    {
        private readonly object providerVersionSync = new object();
        private bool providerVersionResolved;
        private string? providerVersion;
        private readonly Action? installMissingAnalyzer;
        private int installRequested;
        private long installRequestedAtTicks;
        public const int InstallWaitSeconds = 180;

        public YtDlpAnalyzer() { }

        public YtDlpAnalyzer(Action? installMissingAnalyzer)
        {
            this.installMissingAnalyzer = installMissingAnalyzer;
        }

        public string ProviderName => "yt-dlp";
        public string? ProviderVersion => providerVersion;

        private void EnsureProviderVersion()
        {
            if (providerVersionResolved) return;
            lock (providerVersionSync)
            {
                if (providerVersionResolved) return;
                providerVersion = YDLProcess.TryGetVersion();
                providerVersionResolved = true;
            }
        }

        private static bool AnalyzerPresent()
        {
            try
            {
                YDLProcess.FindYDLBinary();
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private void RequestInstallOnce()
        {
            var install = installMissingAnalyzer;
            if (install == null) return;
            Interlocked.CompareExchange(ref installRequestedAtTicks, DateTime.UtcNow.Ticks, 0);
            if (Interlocked.Exchange(ref installRequested, 1) != 0) return;
            install();
        }

        private DateTime InstallDeadline()
        {
            var requested = Interlocked.Read(ref installRequestedAtTicks);
            return requested == 0 ? DateTime.MinValue : new DateTime(requested, DateTimeKind.Utc).AddSeconds(InstallWaitSeconds);
        }

        private void StartWithInstallFallback(YDLProcess process, CancellationToken cancellationToken, Action<string>? progress)
        {
            if (!AnalyzerPresent())
            {
                if (installMissingAnalyzer == null)
                    throw new FileNotFoundException("yt-dlp is not installed. Open Axioos, choose Check for updates, then try again.");
                RequestInstallOnce();
                var deadline = InstallDeadline();
                while (!AnalyzerPresent())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline)
                        throw new FileNotFoundException("yt-dlp could not be downloaded. Check your connection, then restart Axioos to try once more.");
                    progress?.Invoke("Downloading yt-dlp for Axioos, this happens once");
                    Thread.Sleep(500);
                }
                providerVersionResolved = false;
            }
            progress?.Invoke("yt-dlp is reading this page");
            process.Start();
        }

        public SocialAnalysisResult Analyze(SocialAnalysisRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Url) || request.Url.Length > SocialAnalysisLimits.MaxUrlChars ||
                !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                    SocialAnalysisFailureKind.NotSupported, "InvalidUrl", "The social-analysis URL is invalid or unsafe.");

            try
            {
                RequestJsRuntimeIfMissing(uri, request.Progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                    SocialAnalysisFailureKind.Cancelled, "Cancelled", "Analysis was cancelled.");
            }
            EnsureProviderVersion();
            var process = new YDLProcess
            {
                Uri = uri,
                TimeoutSeconds = Math.Min(SocialAnalysisLimits.MaxTimeoutSeconds, Math.Max(1, request.TimeoutSeconds)),
                CookieHeader = request.SessionCookieHeader
            };

            try
            {
                using (cancellationToken.Register(process.Cancel))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    StartWithInstallFallback(process, cancellationToken, request.Progress);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var jsonOutputFile = process.JsonOutputFile;
                if (jsonOutputFile is null || jsonOutputFile.Length == 0 || !File.Exists(jsonOutputFile))
                    return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                        SocialAnalysisFailureKind.MalformedOutput, "MissingAnalyzerOutput", "yt-dlp did not produce analysis output.");

                var parsed = YDLOutputParser.Parse(jsonOutputFile);
                var variants = new List<SocialMediaVariant>();
                foreach (var entry in parsed ?? new List<YDLVideoEntry>())
                {
                    if (entry.Formats == null) continue;
                    foreach (var item in entry.Formats)
                    {
                        if (variants.Count >= SocialAnalysisLimits.MaxVariants) break;
                        if ((item.VideoFragments?.Count ?? 0) > SocialAnalysisLimits.MaxFragmentsPerVariant ||
                            (item.AudioFragments?.Count ?? 0) > SocialAnalysisLimits.MaxFragmentsPerVariant) continue;
                        variants.Add(Normalize(entry.Title, item));
                    }
                    if (variants.Count >= SocialAnalysisLimits.MaxVariants) break;
                }
                if (variants.Count == 0)
                {
                    var detail = SafeDetail(process.LastErrorDetail, request.SessionCookieHeader);
                    var failureKind = detail.Length == 0 ? SocialAnalysisFailureKind.MalformedOutput : ClassifyFailure(detail);
                    var failureCode = failureKind == SocialAnalysisFailureKind.MalformedOutput || failureKind == SocialAnalysisFailureKind.ExtractorFailure
                        ? "NoUsableFormats" : FailureCode(failureKind);
                    var message = "yt-dlp returned no usable media formats." + (detail.Length == 0 ? string.Empty : " " + detail);
                    return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                        failureKind == SocialAnalysisFailureKind.ExtractorFailure ? SocialAnalysisFailureKind.MalformedOutput : failureKind,
                        failureCode, message);
                }

                return SocialAnalysisResult.Success(ProviderName, ProviderVersion, variants);
            }
            catch (OperationCanceledException)
            {
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                    SocialAnalysisFailureKind.Cancelled, "Cancelled", "Analysis was cancelled.");
            }
            catch (TimeoutException ex)
            {
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                    SocialAnalysisFailureKind.Timeout, "Timeout", ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                    SocialAnalysisFailureKind.NotSupported, "AnalyzerUnavailable", ex.Message);
            }
            catch (InvalidDataException ex)
            {
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion,
                    SocialAnalysisFailureKind.MalformedOutput, "MalformedOutput", ex.Message);
            }
            catch (Exception ex)
            {
                var kind = ClassifyFailure(ex.Message);
                return SocialAnalysisResult.Failure(ProviderName, ProviderVersion, kind, FailureCode(kind), SafeDetail(ex.Message, request.SessionCookieHeader));
            }
            finally
            {
                TryDelete(process.JsonOutputFile);
            }
        }

        private void RequestJsRuntimeIfMissing(Uri uri, Action<string>? progress, CancellationToken cancellationToken)
        {
            if (installMissingAnalyzer == null || Updater.UpdateChecker.GetDenoAssetForCurrentOS() == null) return;
            var host = uri.Host.ToLowerInvariant();
            if (!(host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com") || host == "m.youtube.com")) return;
            try
            {
                if (YDLProcess.FindBundledJsRuntime(string.Empty) != null) return;
                RequestInstallOnce();
                var deadline = InstallDeadline();
                while (YDLProcess.FindBundledJsRuntime(string.Empty) == null && DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke("Downloading the YouTube helper for yt-dlp, this happens once");
                    Thread.Sleep(500);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TraceLog.Log.Debug(ex, "JavaScript runtime install request failed");
            }
        }

        private static SocialMediaVariant Normalize(string title, YDLVideoFormatEntry item)
        {
            return new SocialMediaVariant
            {
                Kind = MapKind(item.YDLEntryType),
                Title = BoundText(title ?? item.Title ?? string.Empty, SocialAnalysisLimits.MaxTitleChars),
                VideoUrl = SafeHttpUrl(item.VideoUrl),
                AudioUrl = SafeHttpUrl(item.AudioUrl),
                VideoFragments = MapFragments(item.VideoFragments),
                AudioFragments = MapFragments(item.AudioFragments),
                VideoFormat = item.VideoFormat,
                AudioFormat = item.AudioFormat,
                FileExtension = item.FileExt,
                VideoCodec = item.VideoCodec,
                AudioCodec = item.AudioCodec,
                AudioBitrateKbps = item.Abr,
                Width = item.Width,
                Height = item.Height,
                FragmentBaseUrl = SafeHttpUrl(item.FragmentBaseUrl)
            };
        }

        private static IList<SocialMediaFragment> MapFragments(IList<Fragment> fragments)
        {
            if (fragments == null || fragments.Count > SocialAnalysisLimits.MaxFragmentsPerVariant) return new List<SocialMediaFragment>();
            return fragments
                .Select(x => new SocialMediaFragment
                {
                    Path = BoundText(x.Path ?? string.Empty, SocialAnalysisLimits.MaxUrlChars),
                    Duration = BoundText(x.Duration ?? string.Empty, 64)
                }).ToList();
        }

        private static string? SafeHttpUrl(string? value)
        {
            if (value == null || value.Trim().Length == 0 || value.Length > SocialAnalysisLimits.MaxUrlChars) return null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? value : null;
        }

        private static string BoundText(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static SocialMediaVariantKind MapKind(YDLEntryType kind)
        {
            switch (kind)
            {
                case YDLEntryType.Dash: return SocialMediaVariantKind.Dash;
                case YDLEntryType.Hls: return SocialMediaVariantKind.Hls;
                case YDLEntryType.MpegDash: return SocialMediaVariantKind.MpegDash;
                default: return SocialMediaVariantKind.Http;
            }
        }

        private static SocialAnalysisFailureKind ClassifyFailure(string message)
        {
            var text = (message ?? string.Empty).ToLowerInvariant();
            if (text.Contains("429") || text.Contains("rate limit") || text.Contains("too many requests"))
                return SocialAnalysisFailureKind.RateLimited;
            if (text.Contains("login") || text.Contains("sign in") || text.Contains("cookie") || text.Contains("authentication"))
                return SocialAnalysisFailureKind.AuthenticationRequired;
            if (text.Contains("network") || text.Contains("connection") || text.Contains("dns") || text.Contains("socket"))
                return SocialAnalysisFailureKind.Network;
            return SocialAnalysisFailureKind.ExtractorFailure;
        }

        private static string FailureCode(SocialAnalysisFailureKind kind)
        {
            switch (kind)
            {
                case SocialAnalysisFailureKind.AuthenticationRequired: return "AuthenticationRequired";
                case SocialAnalysisFailureKind.RateLimited: return "RateLimited";
                case SocialAnalysisFailureKind.Network: return "Network";
                default: return "ExtractorFailure";
            }
        }

        private static string SafeDetail(string message, string? secret)
        {
            var text = message ?? string.Empty;
            if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[redacted]");
            text = Regex.Replace(text, @"(?i)\b(authorization|proxy-authorization|cookie)\s*[:=]\s*[^,\r\n]+", "$1: [redacted]");
            text = Regex.Replace(text, @"https?://[^\s""'<>]+", match => RedactUrlQuery(match.Value), RegexOptions.IgnoreCase);
            return text.Length <= 420 ? text : text.Substring(0, 420);
        }

        private static string RedactUrlQuery(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "[redacted-url]";
            var safe = uri.GetLeftPart(UriPartial.Path);
            return string.IsNullOrEmpty(uri.Query) ? safe : safe + "?[redacted]";
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { TraceLog.Log.Debug(ex, "Temporary yt-dlp output could not be deleted"); }
        }
    }
}
