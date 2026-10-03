using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using TraceLog;
using ADM.Core;

namespace ADM.Core.Clients.Http
{
    internal class NetFxHttpClient : IHttpClient
    {
        private readonly string connectionGroupName = Guid.NewGuid().ToString();
        private HashSet<ServicePoint> servicePoints = new();
        private bool disposed;
        private ProxyInfo? proxy;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

        internal NetFxHttpClient(ProxyInfo? proxy)
        {
            this.proxy = proxy;
        }

        private HttpWebRequest CreateRequest(Uri uri)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("HttpWebRequestClient");
            }

            var http = (HttpWebRequest)WebRequest.Create(uri);
            var p = ProxyHelper.GetProxy(this.proxy);
            if (p != null)
            {
                http.Proxy = p;
            }
            http.Timeout = http.ReadWriteTimeout = (int)Timeout.TotalMilliseconds;
            http.UseDefaultCredentials = true;
            http.AutomaticDecompression =  DecompressionMethods.GZip | DecompressionMethods.Deflate;
            http.AllowAutoRedirect = true;

            http.ConnectionGroupName = this.connectionGroupName;
            var sp = http.ServicePoint;
            lock (servicePoints)
            {
                if (sp != null)
                {
                    servicePoints.Add(sp);
                }
            }
            return http;
        }

        public HttpRequest CreateGetRequest(Uri uri,
            Dictionary<string, List<string>>? headers = null,
            string? cookies = null,
            AuthenticationInfo? authentication = null)
        {
            var req = this.CreateRequest(uri);
            req.AllowAutoRedirect = false;
            if (headers != null)
            {
                foreach (var e in headers)
                {
                    SetHeader(req, e.Key, e.Value);
                }
            }
            if (cookies != null)
            {
                SetHeader(req, "Cookie", cookies);
            }
            if (authentication != null && !string.IsNullOrEmpty(authentication.Value.UserName))
            {
                req.Credentials = new NetworkCredential(authentication.Value.UserName, authentication.Value.Password);
            }

            return new HttpRequest { Session = new NetFxHttpSession { Request = req } };
        }

        public HttpRequest CreatePostRequest(Uri uri,
            Dictionary<string, List<string>>? headers = null,
            string? cookies = null,
            AuthenticationInfo? authentication = null,
            byte[]? body = null)
        {
            var req = this.CreateRequest(uri);
            req.Method = "POST";
            if (headers != null)
            {
                foreach (var e in headers)
                {
                    SetHeader(req, e.Key, e.Value);
                }
            }
            if (cookies != null)
            {
                SetHeader(req, "Cookie", cookies);
            }
            if (authentication != null && !string.IsNullOrEmpty(authentication.Value.UserName))
            {
                req.Credentials = new NetworkCredential(authentication.Value.UserName, authentication.Value.Password);
            }
            if (body != null)
            {
                req.ContentLength = body.Length;
                using var rs = req.GetRequestStream();
                rs.Write(body, 0, body.Length);
                rs.Close();
            }
            return new HttpRequest { Session = new NetFxHttpSession { Request = req } };
        }

        public HttpResponse Send(HttpRequest request)
        {
            HttpWebRequest r;
            HttpWebResponse response;
            if (request.Session == null)
            {
                throw new ArgumentNullException(nameof(request.Session));
            }
            if (request.Session is not NetFxHttpSession session)
            {
                throw new ArgumentNullException(nameof(request.Session));
            }
            if (session.Request == null)
            {
                throw new ArgumentNullException(nameof(session.Request));
            }
            r = session.Request;
            response = GetResponse(r);
            var hops = 0;
            while (IsUnfollowedRedirect(response) && hops < MaxManualRedirects && r.Method == "GET")
            {
                var location = response.Headers["Location"];
                if (string.IsNullOrEmpty(location)) break;
                if (!Uri.TryCreate(response.ResponseUri ?? r.RequestUri, location, out var target)) break;
                if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) break;
                HttpWebRequest next;
                try
                {
                    next = CreateRedirectRequest(r, target);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Redirect request could not be created");
                    response.Close();
                    throw;
                }
                response.Close();
                r = next;
                session.Request = r;
                response = GetResponse(r);
                hops++;
            }
            session.Request = r;

            var servicePoint = r.ServicePoint;
            if (servicePoint != null)
            {
                servicePoints.Add(servicePoint);
            }
            session.Response = response;
            return new HttpResponse { Session = session };
        }

        private int MaxManualRedirects => 10;

        private static bool IsUnfollowedRedirect(HttpWebResponse response)
        {
            var code = (int)response.StatusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        private static HttpWebResponse GetResponse(HttpWebRequest request)
        {
            try
            {
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException we)
            {
                Log.Debug(we, we.Message);
                if (we.Response == null)
                {
                    throw new Exception("Connectivity error");
                }
                var response = (HttpWebResponse)we.Response;
                if (!IsUnfollowedRedirect(response))
                {
                    response.Discard();
                    response.Close();
                }
                return response;
            }
        }

        private HttpWebRequest CreateRedirectRequest(HttpWebRequest previous, Uri target)
        {
            var next = CreateRequest(target);
            next.AllowAutoRedirect = false;
            var answered = previous.Address ?? previous.RequestUri;
            var sameAuthority = string.Equals(answered.Authority, target.Authority, StringComparison.OrdinalIgnoreCase)
                && answered.Scheme == target.Scheme;
            var sameSite = sameAuthority || (IsSameSite(answered.Host, target.Host)
                && !(answered.Scheme == Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttps));
            foreach (var key in previous.Headers.AllKeys)
            {
                var lower = key.ToLowerInvariant();
                var value = previous.Headers[key];
                if (value == null || lower == "host" || lower == "connection" || lower == "proxy-connection") continue;
                if (!sameAuthority && lower == "authorization") continue;
                if (!sameSite && lower == "cookie") continue;
                if (lower == "range")
                {
                    ApplyRange(next, value);
                    continue;
                }
                try
                {
                    SetHeader(next, key, value);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Redirect header copy skipped for " + key);
                }
            }
            if (sameAuthority)
            {
                next.Credentials = previous.Credentials;
            }
            return next;
        }

        private static bool IsSameSite(string first, string second)
        {
            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
            if (Uri.CheckHostName(first) != UriHostNameType.Dns || Uri.CheckHostName(second) != UriHostNameType.Dns) return false;
            var site = SiteOf(first);
            return site.Length > 0 && string.Equals(site, SiteOf(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string SiteOf(string host)
        {
            var labels = host.TrimEnd('.').Split('.');
            if (labels.Length < 2) return string.Empty;
            var count = 2;
            if (labels[labels.Length - 1].Length == 2 && IsSharedSecondLevel(labels[labels.Length - 2])) count = 3;
            if (labels.Length < count) return string.Empty;
            return string.Join(".", labels, labels.Length - count, count);
        }

        private static bool IsSharedSecondLevel(string label)
        {
            switch (label.ToLowerInvariant())
            {
                case "co":
                case "com":
                case "net":
                case "org":
                case "gov":
                case "edu":
                case "ac":
                case "or":
                case "ne":
                case "go":
                case "mil":
                case "nom":
                case "sch":
                case "ltd":
                case "plc":
                case "gob":
                case "gouv":
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyRange(HttpWebRequest request, string value)
        {
            var spec = value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ? value.Substring(6) : value;
            var parts = spec.Split('-');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var start)) return;
            if (long.TryParse(parts[1], out var end))
            {
                request.AddRange(start, end);
            }
            else
            {
                request.AddRange(start);
            }
        }

        public void Dispose()
        {
            lock (this)
            {
                try
                {
                    disposed = true;
                    foreach (var servicePoint in servicePoints)
                    {
                        Log.Debug("Disposing service point");
                        Log.Debug("ConnectionName: " + servicePoint.ConnectionName +
                            "\nCurrentConnections: " + servicePoint.CurrentConnections +
                            "\nAddress: " + servicePoint.Address +
                            "\nGetHashCode(): " + servicePoint.GetHashCode());
                        servicePoint.CloseConnectionGroup(this.connectionGroupName);
                    }
                }
                catch { }
            }
        }

        public void Close()
        {
            this.Dispose();
        }

        private static void SetHeader(HttpWebRequest request, string key, IEnumerable<string> values)
        {
            try
            {
                foreach (var value in values)
                {
                    SetHeader(request, key, value);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error setting header value");
            }
        }

        private static void SetHeader(HttpWebRequest request, string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "accept":
                    request.Accept = value;
                    break;
                case "connection":
                    request.Connection = value;
                    break;
                case "content-type":
                    request.ContentType = value;
                    break;
                case "expect":
                    request.Expect = value;
                    break;
#if !NET35
                case "date":
                    request.Date = DateTime.Parse(value);
                    break;
                case "host":
                    request.Host = value;
                    break;
#endif
                case "if-modified-since":
                    request.IfModifiedSince = DateTime.Parse(value);
                    break;
                case "referer":
                    request.Referer = value;
                    break;
                case "user-agent":
                    request.UserAgent = value;
                    break;
                case "transfer-encoding":
                    request.TransferEncoding = value;
                    break;
                case "range":
                case "content-length":
                    break;
                default:
                    request.Headers.Add(key, value);
                    break;
            }
        }
    }
}
