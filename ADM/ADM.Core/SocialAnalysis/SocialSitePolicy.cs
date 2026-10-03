using System;
using System.Collections.Generic;
using System.Linq;

namespace ADM.Core.SocialAnalysis
{
    [Flags]
    public enum SocialSiteCapability
    {
        None = 0,
        NativeMediaDetection = 1,
        ExternalAnalysis = 2,
        BrowserSessionAssistance = 4
    }

    public sealed class SocialSitePolicy
    {
        public SocialSitePolicy(string canonicalHost, SocialSiteCapability capabilities)
        {
            if (string.IsNullOrWhiteSpace(canonicalHost)) throw new ArgumentException("Canonical host is required.", nameof(canonicalHost));
            CanonicalHost = canonicalHost.Trim().Trim('.').ToLowerInvariant();
            Capabilities = capabilities;
        }

        public string CanonicalHost { get; }
        public SocialSiteCapability Capabilities { get; }

        public bool Matches(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            var host = uri.Host.ToLowerInvariant();
            return host == CanonicalHost || host.EndsWith("." + CanonicalHost, StringComparison.Ordinal);
        }

        public bool Has(SocialSiteCapability capability) => (Capabilities & capability) == capability;
    }

    public sealed class SocialSitePolicyRegistry
    {
        private const SocialSiteCapability Standard =
            SocialSiteCapability.NativeMediaDetection |
            SocialSiteCapability.ExternalAnalysis |
            SocialSiteCapability.BrowserSessionAssistance;

        private static readonly IReadOnlyList<SocialSitePolicy> DefaultPolicies = new[]
        {
            new SocialSitePolicy("youtube.com", Standard),
            new SocialSitePolicy("youtu.be", Standard),
            new SocialSitePolicy("x.com", Standard),
            new SocialSitePolicy("twitter.com", Standard),
            new SocialSitePolicy("facebook.com", Standard),
            new SocialSitePolicy("fb.watch", Standard),
            new SocialSitePolicy("instagram.com", Standard),
            new SocialSitePolicy("threads.net", Standard),
            new SocialSitePolicy("tiktok.com", Standard),
            new SocialSitePolicy("reddit.com", Standard),
            new SocialSitePolicy("redd.it", Standard),
            new SocialSitePolicy("vimeo.com", Standard),
            new SocialSitePolicy("twitch.tv", Standard),
            new SocialSitePolicy("dailymotion.com", Standard),
            new SocialSitePolicy("snapchat.com", Standard),
            new SocialSitePolicy("pinterest.com", Standard),
            new SocialSitePolicy("linkedin.com", Standard),
            new SocialSitePolicy("tumblr.com", Standard),
            new SocialSitePolicy("vk.com", Standard),
            new SocialSitePolicy("ok.ru", Standard)
        };

        private const string PublicWebHost = "public-web";
        private static SocialSitePolicy PublicWebPolicy => new SocialSitePolicy(PublicWebHost, Standard);

        private readonly IReadOnlyList<SocialSitePolicy> policies;

        public SocialSitePolicyRegistry() : this(DefaultPolicies) { }

        internal SocialSitePolicyRegistry(IReadOnlyList<SocialSitePolicy> policies)
        {
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        }

        public IReadOnlyList<SocialSitePolicy> Policies => policies;

        public bool TryMatch(string url, out SocialSitePolicy? policy)
        {
            policy = null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
            policy = policies.FirstOrDefault(candidate => candidate.Matches(uri));
            if (policy == null && !IsLoopbackHost(uri)) policy = PublicWebPolicy;
            return policy != null;
        }

        private static bool IsLoopbackHost(Uri uri)
        {
            var host = uri.Host.Trim('[', ']').ToLowerInvariant();
            return uri.IsLoopback || host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) ||
                   host == "0.0.0.0" || host.Length == 0;
        }
    }
}
