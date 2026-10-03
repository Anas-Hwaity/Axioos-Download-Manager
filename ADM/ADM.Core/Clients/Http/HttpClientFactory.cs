using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ADM.Core;

namespace ADM.Core.Clients.Http
{
    public static class HttpClientFactory
    {
        public static IHttpClient NewHttpClient(ProxyInfo? proxyInfo)
        {
            ProxyInfo? proxy = null;
            if (proxyInfo.HasValue)
            {
                if (proxyInfo.Value.ProxyType != ProxyType.Custom)
                {
                    proxy = proxyInfo;
                }
                else if (!string.IsNullOrWhiteSpace(proxyInfo.Value.Host)
                    && proxyInfo.Value.Port > 0 && proxyInfo.Value.Port <= 65535)
                {
                    proxy = proxyInfo;
                }
                else
                {
                    throw new ArgumentException("The custom proxy host and port must be valid.", nameof(proxyInfo));
                }
            }

            if (Environment.Version.Major == 2)
            {
                return new WinHttpClient(proxy);
            }
            else
            {
#if NET5_0_OR_GREATER
                return new DotNetHttpClient(proxy);
#else
                return new NetFxHttpClient(proxy);
#endif
            }
        }
    }
}
