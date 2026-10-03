using System;
using System.Collections.Generic;

namespace ADM.Core.SocialAnalysis
{
    public sealed class BrowserSessionMaterial
    {
        public Uri Origin { get; set; } = new Uri("https://invalid.local/");
        public string CookieHeader { get; set; } = string.Empty;
    }

    public interface IBrowserSessionProvider
    {
        BrowserSessionMaterial? TryTakeSession(string operationId, Uri origin);
    }

    public interface IBrowserSessionSink
    {
        bool StoreSession(string operationId, BrowserSessionMaterial material);
    }

    public sealed class NoBrowserSessionProvider : IBrowserSessionProvider
    {
        public BrowserSessionMaterial? TryTakeSession(string operationId, Uri origin)
        {
            if (operationId == null) throw new ArgumentNullException(nameof(operationId));
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            return null;
        }
    }

    public sealed class InMemoryBrowserSessionStore : IBrowserSessionProvider, IBrowserSessionSink
    {
        private sealed class Entry
        {
            public BrowserSessionMaterial Material { get; set; } = new BrowserSessionMaterial();
            public DateTime StoredAtUtc { get; set; }
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, Entry> sessions = new Dictionary<string, Entry>();
        private readonly TimeSpan lifetime;

        public InMemoryBrowserSessionStore() : this(TimeSpan.FromMinutes(2)) { }

        internal InMemoryBrowserSessionStore(TimeSpan lifetime)
        {
            this.lifetime = lifetime;
        }

        public bool StoreSession(string operationId, BrowserSessionMaterial material)
        {
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 512 || material == null || material.Origin == null ||
                string.IsNullOrEmpty(material.CookieHeader) || material.CookieHeader.Length > 65536 ||
                material.CookieHeader.IndexOf('\r') >= 0 || material.CookieHeader.IndexOf('\n') >= 0)
                return false;
            if (material.Origin.Scheme != Uri.UriSchemeHttp && material.Origin.Scheme != Uri.UriSchemeHttps) return false;
            var normalized = new BrowserSessionMaterial
            {
                Origin = new Uri(material.Origin.GetLeftPart(UriPartial.Authority)),
                CookieHeader = material.CookieHeader
            };
            lock (sync)
            {
                PurgeExpired(DateTime.UtcNow);
                sessions[operationId] = new Entry { Material = normalized, StoredAtUtc = DateTime.UtcNow };
            }
            return true;
        }

        public BrowserSessionMaterial? TryTakeSession(string operationId, Uri origin)
        {
            if (string.IsNullOrWhiteSpace(operationId)) return null;
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            lock (sync)
            {
                var now = DateTime.UtcNow;
                PurgeExpired(now);
                if (!sessions.TryGetValue(operationId, out var entry)) return null;
                sessions.Remove(operationId);
                if (now - entry.StoredAtUtc > lifetime || !SameOrigin(entry.Material.Origin, origin)) return null;
                return entry.Material;
            }
        }

        private void PurgeExpired(DateTime now)
        {
            var expired = new List<string>();
            foreach (var item in sessions)
                if (now - item.Value.StoredAtUtc > lifetime) expired.Add(item.Key);
            foreach (var key in expired) sessions.Remove(key);
        }

        private static bool SameOrigin(Uri left, Uri right)
        {
            return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
                   left.Port == right.Port;
        }
    }
}
