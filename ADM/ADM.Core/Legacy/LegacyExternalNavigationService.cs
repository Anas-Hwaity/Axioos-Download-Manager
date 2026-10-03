using ADM.Core.Navigation;
using ADM.Core.Util;
using System;

namespace ADM.Core.Legacy
{
    public sealed class LegacyExternalNavigationService : IExternalNavigationService
    {
        public void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("External navigation only permits absolute HTTP(S) URLs.", nameof(url));
            }

            PlatformHelper.OpenBrowser(uri.AbsoluteUri);
        }
    }
}
