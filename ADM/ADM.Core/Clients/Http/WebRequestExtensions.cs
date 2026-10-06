using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;

namespace ADM.Core.Clients.Http
{
    public static class WebRequestExtensions
    {
        public static void EnsureSuccessStatusCode(this HttpWebResponse response)
        {
            if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new HttpException(response.StatusDescription, null, response.StatusCode);
            }
        }

        public static void EnsureSuccessStatusCode(HttpStatusCode statusCode, string? statusDescription)
        {
            if (statusCode != HttpStatusCode.OK && statusCode != HttpStatusCode.PartialContent)
            {
                throw new HttpException(statusDescription ?? "Invalid response", null, statusCode);
            }
        }

        public static void Discard(this HttpWebResponse response)
        {
#if NET35
            var bytes = new byte[8192];
#else
            var bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(8192);
#endif
            try
            {
                using var stream = response.GetResponseStream();
                while (true)
                {
                    int x = stream.Read(bytes, 0, bytes.Length);
                    if (x == 0) break;
                }
            }
            finally
            {
#if !NET35
                System.Buffers.ArrayPool<byte>.Shared.Return(bytes);
#endif
            }
        }

        public static long GetContentLength(this HttpWebResponse response)
        {
            try
            {
                if (response.ContentLength > 0) return response.ContentLength;
                if (response.ContentEncoding != null &&
                    response.ContentEncoding != "none" && response.ContentEncoding != "identity")
                {
                    return -1;
                }
                var contentRange = response.Headers.GetValues("Content-Range")?.First();
                return ContentLengthFromContentRange(contentRange);
            }
            catch { }
            return -1;
        }

        public static long ContentLengthFromContentRange(string? contentRange)
        {
            return TryParseContentRange(contentRange, out _, out _, out long total) ? total : -1;
        }

        public static bool TryParseContentRange(string? contentRange, out long start, out long end, out long total)
        {
            start = end = total = -1;
            if (string.IsNullOrWhiteSpace(contentRange)) return false;
            try
            {
                var value = contentRange.Trim();
                if (!value.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase)) return false;
                var payload = value.Substring(6).Trim();
                var slash = payload.IndexOf('/');
                if (slash <= 0) return false;
                var range = payload.Substring(0, slash).Trim();
                var totalText = payload.Substring(slash + 1).Trim();
                if (totalText != "*" && !Int64.TryParse(totalText, out total)) return false;
                if (range == "*") return total >= 0;
                var dash = range.IndexOf('-');
                if (dash <= 0) return false;
                if (!Int64.TryParse(range.Substring(0, dash).Trim(), out start)) return false;
                if (!Int64.TryParse(range.Substring(dash + 1).Trim(), out end)) return false;
                return start >= 0 && end >= start;
            }
            catch
            {
                start = end = total = -1;
                return false;
            }
        }

        public static string? GetContentDispositionFileName(this HttpWebResponse response)
        {
            return GetContentDispositionFileName(response.Headers.Get("Content-Disposition"));
        }

        public static string? GetContentDispositionFileName(string? contentDisposition)
        {
            try
            {
                if (contentDisposition == null) return null;
                Log.Debug("Trying to derive filename from Content-Disposition");
                return HeaderFileNameDecoder.Decode(contentDisposition);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }

            return null;
        }

        public static string ReadAsString(this HttpWebResponse response, CancelFlag cancellationToken)
        {
#if NET35
            var buf = new byte[8192];
#else
            var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(8192);
#endif
            try
            {
                var sourceStream = response.GetResponseStream();
                var ms = new MemoryStream();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var x = sourceStream.Read(buf, 0, buf.Length);
                    if (x == 0)
                    {
                        break;
                    }
                    ms.Write(buf, 0, x);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            finally
            {
#if !NET35
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
#endif
            }

        }

#if NET35

        public static void AddRange(this HttpWebRequest request, long start, long end)
        {
            var method = typeof(WebHeaderCollection).GetMethod
                        ("AddWithoutValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            string key = "Range";
            string val = string.Format("bytes={0}-{1}", start, end);

            method.Invoke(request.Headers, new object[] { key, val });
        }

        public static void AddRange(this HttpWebRequest request, long start)
        {
            var method = typeof(WebHeaderCollection).GetMethod
                        ("AddWithoutValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            string key = "Range";
            string val = string.Format("bytes={0}-", start);

            method.Invoke(request.Headers, new object[] { key, val });
        }
#endif
    }
}
