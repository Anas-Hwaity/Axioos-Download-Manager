using System;
using System.Collections.Generic;
using System.Threading;

namespace ADM.Core.SocialAnalysis
{
    public sealed class SocialAnalysisService : ISocialAnalysisService
    {
        private readonly IExternalMediaAnalyzer externalAnalyzer;
        private readonly IBrowserSessionProvider sessionProvider;
        private readonly SocialSitePolicyRegistry sitePolicies;
        private readonly object sync = new object();
        private readonly Dictionary<string, CancellationTokenSource> activeOperations = new Dictionary<string, CancellationTokenSource>();

        public SocialAnalysisService(IExternalMediaAnalyzer externalAnalyzer)
            : this(externalAnalyzer, new NoBrowserSessionProvider(), new SocialSitePolicyRegistry()) { }

        public SocialAnalysisService(IExternalMediaAnalyzer externalAnalyzer, IBrowserSessionProvider sessionProvider)
            : this(externalAnalyzer, sessionProvider, new SocialSitePolicyRegistry()) { }

        internal SocialAnalysisService(IExternalMediaAnalyzer externalAnalyzer, IBrowserSessionProvider sessionProvider, SocialSitePolicyRegistry sitePolicies)
        {
            this.externalAnalyzer = externalAnalyzer ?? throw new ArgumentNullException(nameof(externalAnalyzer));
            this.sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            this.sitePolicies = sitePolicies ?? throw new ArgumentNullException(nameof(sitePolicies));
        }

        public bool IsSupportedSocialUrl(string url)
        {
            return sitePolicies.TryMatch(url, out var policy) && policy!.Has(SocialSiteCapability.ExternalAnalysis);
        }

        public SocialAnalysisResult Analyze(SocialAnalysisRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!IsSupportedSocialUrl(request.Url))
                return SocialAnalysisResult.Failure(externalAnalyzer.ProviderName, externalAnalyzer.ProviderVersion,
                    SocialAnalysisFailureKind.NotSupported, "UnsupportedSocialHost",
                    "yt-dlp analysis is available for public web pages only.");
            if (string.IsNullOrWhiteSpace(request.OperationId) || request.OperationId.Length > SocialAnalysisLimits.MaxOperationIdChars)
                return SocialAnalysisResult.Failure(externalAnalyzer.ProviderName, externalAnalyzer.ProviderVersion,
                    SocialAnalysisFailureKind.NotSupported, "InvalidOperationId", "A bounded social-analysis operation identifier is required.");
            if (string.IsNullOrWhiteSpace(request.Url) || request.Url.Length > SocialAnalysisLimits.MaxUrlChars)
                return SocialAnalysisResult.Failure(externalAnalyzer.ProviderName, externalAnalyzer.ProviderVersion,
                    SocialAnalysisFailureKind.NotSupported, "InvalidUrl", "The social-analysis URL is invalid or exceeds the supported limit.");

            var cancellation = new CancellationTokenSource();
            lock (sync)
            {
                if (activeOperations.ContainsKey(request.OperationId))
                    return SocialAnalysisResult.Failure(externalAnalyzer.ProviderName, externalAnalyzer.ProviderVersion,
                        SocialAnalysisFailureKind.ExtractorFailure, "AlreadyRunning", "This social-analysis operation is already running.");
                activeOperations[request.OperationId] = cancellation;
            }
            try
            {
                var result = externalAnalyzer.Analyze(request, cancellation.Token);
                if (result.IsSuccess || result.FailureKind != SocialAnalysisFailureKind.AuthenticationRequired ||
                    !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                    !sitePolicies.TryMatch(request.Url, out var policy) ||
                    !policy!.Has(SocialSiteCapability.BrowserSessionAssistance)) return result;
                var session = sessionProvider.TryTakeSession(request.OperationId, uri);
                if (session == null || string.IsNullOrEmpty(session.CookieHeader)) return result;
                var retryRequest = new SocialAnalysisRequest
                {
                    OperationId = request.OperationId,
                    Url = request.Url,
                    TimeoutSeconds = request.TimeoutSeconds,
                    SessionCookieHeader = session.CookieHeader,
                    Progress = request.Progress
                };
                return externalAnalyzer.Analyze(retryRequest, cancellation.Token);
            }
            finally
            {
                lock (sync)
                {
                    if (activeOperations.TryGetValue(request.OperationId, out var current) && ReferenceEquals(current, cancellation))
                        activeOperations.Remove(request.OperationId);
                }
                cancellation.Dispose();
            }
        }

        public void Cancel(string operationId)
        {
            if (string.IsNullOrEmpty(operationId)) return;
            CancellationTokenSource? cancellation = null;
            lock (sync) activeOperations.TryGetValue(operationId, out cancellation);
            if (cancellation != null)
            {
                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
    }
}
