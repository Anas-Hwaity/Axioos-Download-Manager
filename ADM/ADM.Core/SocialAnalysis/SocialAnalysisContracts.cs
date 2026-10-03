using System;
using System.Collections.Generic;
using System.Threading;

namespace ADM.Core.SocialAnalysis
{
    public static class SocialAnalysisLimits
    {
        public const int MaxOperationIdChars = 512;
        public const int MaxUrlChars = 8192;
        public const int MaxTimeoutSeconds = 120;
        public const int MaxVariants = 512;
        public const int MaxFragmentsPerVariant = 30000;
        public const int MaxTitleChars = 512;
    }

    public enum SocialAnalysisFailureKind
    {
        None = 0,
        NotSupported,
        AuthenticationRequired,
        RateLimited,
        Network,
        ExtractorFailure,
        Timeout,
        Cancelled,
        MalformedOutput
    }

    public enum SocialMediaVariantKind
    {
        Http,
        Dash,
        Hls,
        MpegDash
    }

    public sealed class SocialMediaFragment
    {
        public string Path { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
    }

    public sealed class SocialMediaVariant
    {
        public SocialMediaVariantKind Kind { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? VideoUrl { get; set; }
        public string? AudioUrl { get; set; }
        public IList<SocialMediaFragment> VideoFragments { get; set; } = new List<SocialMediaFragment>();
        public IList<SocialMediaFragment> AudioFragments { get; set; } = new List<SocialMediaFragment>();
        public string? VideoFormat { get; set; }
        public string? AudioFormat { get; set; }
        public string? FileExtension { get; set; }
        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public string? AudioBitrateKbps { get; set; }
        public string? Width { get; set; }
        public string? Height { get; set; }
        public string? FragmentBaseUrl { get; set; }
    }

    public sealed class SocialAnalysisRequest
    {
        public string OperationId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 110;
        internal string? SessionCookieHeader { get; set; }
        public Action<string>? Progress { get; set; }
    }

    public sealed class SocialAnalysisResult
    {
        public bool IsSuccess { get; private set; }
        public string ProviderName { get; private set; } = string.Empty;
        public string? ProviderVersion { get; private set; }
        public IList<SocialMediaVariant> Variants { get; private set; } = new List<SocialMediaVariant>();
        public SocialAnalysisFailureKind FailureKind { get; private set; }
        public string ErrorCode { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;

        public static SocialAnalysisResult Success(string providerName, string? providerVersion, IList<SocialMediaVariant> variants)
        {
            return new SocialAnalysisResult
            {
                IsSuccess = true,
                ProviderName = providerName ?? string.Empty,
                ProviderVersion = providerVersion,
                Variants = variants ?? new List<SocialMediaVariant>(),
                FailureKind = SocialAnalysisFailureKind.None
            };
        }

        public static SocialAnalysisResult Failure(string providerName, string? providerVersion,
            SocialAnalysisFailureKind failureKind, string errorCode, string message)
        {
            return new SocialAnalysisResult
            {
                IsSuccess = false,
                ProviderName = providerName ?? string.Empty,
                ProviderVersion = providerVersion,
                FailureKind = failureKind,
                ErrorCode = errorCode ?? string.Empty,
                Message = message ?? string.Empty
            };
        }
    }

    public interface IExternalMediaAnalyzer
    {
        string ProviderName { get; }
        string? ProviderVersion { get; }
        SocialAnalysisResult Analyze(SocialAnalysisRequest request, CancellationToken cancellationToken);
    }

    public interface ISocialAnalysisService
    {
        bool IsSupportedSocialUrl(string url);
        SocialAnalysisResult Analyze(SocialAnalysisRequest request);
        void Cancel(string operationId);
    }
}
