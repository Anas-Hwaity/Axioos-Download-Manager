using System;
using System.Text.RegularExpressions;

namespace ADM.Core.Util
{
    public static class SensitiveDataRedactor
    {
        private const string UrlPattern = @"https?://[^\s""'<>]+";
        private const string HeaderSecretPattern = @"(?i)\b(authorization|proxy-authorization|cookie)\s*[:=]\s*[^,\r\n]+";

        public static string UrlForLog(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "[non-http-url]";

            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Fragment = string.Empty,
                Query = string.Empty
            };
            var safe = builder.Uri.GetLeftPart(UriPartial.Path);
            if (!string.IsNullOrEmpty(uri.Query)) safe += "?[redacted]";
            return safe;
        }

        public static string TextForLog(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var redacted = Regex.Replace(value, HeaderSecretPattern, "$1: [redacted]");
            return Regex.Replace(redacted, UrlPattern, match => UrlForLog(match.Value), RegexOptions.IgnoreCase);
        }
    }
}
