using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using TraceLog;
using ADM.Core;

namespace ADM.Core.Clients.Http
{
    internal static class ProxyHelper
    {
        internal static IWebProxy? GetProxy(ProxyInfo? proxy)
        {
            if (proxy.HasValue)
            {
                Log.Debug("Proxy type: " + proxy.Value.ProxyType);
                if (proxy.Value.ProxyType == ProxyType.Direct)
                {
                    return new WebProxy();
                }
                else if (proxy.Value.ProxyType == ProxyType.Custom)
                {
                    var endpoint = new UriBuilder(Uri.UriSchemeHttp, proxy.Value.Host, proxy.Value.Port).Uri;
                    var p = new ExplicitWebProxy(endpoint);
                    if (!string.IsNullOrEmpty(proxy.Value.UserName))
                    {
                        p.Credentials = new NetworkCredential(proxy.Value.UserName, proxy.Value.Password);
                    }
                    return p;
                }
            }
            return null;
        }

        private sealed class ExplicitWebProxy : IWebProxy
        {
            private readonly Uri endpoint;

            internal ExplicitWebProxy(Uri endpoint)
            {
                this.endpoint = endpoint;
            }

            public ICredentials? Credentials { get; set; }

            public Uri GetProxy(Uri destination) => endpoint;

            public bool IsBypassed(Uri host) => false;
        }
    }
}
