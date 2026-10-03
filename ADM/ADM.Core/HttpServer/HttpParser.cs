using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using TraceLog;
#if !NET5_0_OR_GREATER
using ADM.Compatibility;
#endif

namespace ADM.Core.HttpServer
{
    internal static class HttpParser
    {
        internal const int MaxRequestBodyBytes = 512 * 1024;
        internal const int MaxHeaderLineChars = 8192;
        internal const int MaxHeaderLines = 128;

        public static string ParseRequestStatusLine(string statusLine)
        {
            if (string.IsNullOrWhiteSpace(statusLine) || statusLine.Length > MaxHeaderLineChars)
                throw new IOException("Invalid HTTP request line.");

            var parts = statusLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new IOException("Invalid HTTP request line.");
            var method = parts[0];
            var path = parts[1];
            var version = parts[2];
            if (!string.Equals(method, "GET", StringComparison.Ordinal) &&
                !string.Equals(method, "POST", StringComparison.Ordinal))
                throw new IOException("Unsupported HTTP method for local control endpoint.");
            if (path.Length == 0 || path.Length > 2048 || path[0] != '/' ||
                path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0 || path.IndexOf('\0') >= 0)
                throw new IOException("Invalid HTTP request path.");
            if (!string.Equals(version, "HTTP/1.0", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(version, "HTTP/1.1", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unsupported HTTP version for local control endpoint.");
            return path;
        }

        internal static void ParseHeader(string headerLine, out string key, out string value)
        {
            var index = headerLine.IndexOf(":", StringComparison.Ordinal);
            if (index > 0)
            {
                key = headerLine.Substring(0, index).Trim();
                value = headerLine.Substring(index + 1).Trim();
                if (key.Length == 0 || key.Length > 256 || value.Length > MaxHeaderLineChars)
                    throw new IOException("Invalid HTTP header size.");
                return;
            }
            throw new IOException("Invalid header");
        }

        internal static long ParseContentLength(Dictionary<string, List<string>> headers)
        {
            if (!headers.TryGetValue("Content-Length", out var values)) return -1;
            if (values == null || values.Count != 1) throw new IOException("HTTP request must contain at most one Content-Length header.");
            if (!long.TryParse(values[0], out var length) || length < 0 || length > MaxRequestBodyBytes)
                throw new IOException("HTTP request body length is invalid or exceeds the configured limit.");
            return length;
        }

        private static bool ShouldKeepAlive(Dictionary<string, List<string>> headers)
        {
            var value = headers.GetValueOrDefault("Connection")?[0] ?? "close";
            return value.Equals("keep-alive", StringComparison.InvariantCultureIgnoreCase);
        }

        internal static RequestContext ParseContext(TcpClient tcp)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));
            string path = "/";
            Dictionary<string, List<string>> headers = new(StringComparer.OrdinalIgnoreCase);
            byte[]? body = null;
            var io = tcp.GetStream();
            var first = true;
            foreach (var line in LineReader.ReadLines(io, MaxHeaderLineChars, MaxHeaderLines))
            {
                if (first)
                {
                    path = ParseRequestStatusLine(line);
                    first = false;
                    continue;
                }
                ParseHeader(line, out string headerName, out string headerValue);
                var values = headers.GetValueOrDefault(headerName, new List<string>());
                values.Add(headerValue);
                headers[headerName] = values;
            }
            if (first) throw new IOException("HTTP request status line is missing.");
            if (headers.ContainsKey("Transfer-Encoding"))
                throw new IOException("Transfer-Encoding is not supported by the local browser control endpoint.");

            var contentLength = ParseContentLength(headers);
            if (contentLength > 0)
            {
                body = new byte[(int)contentLength];
                using var ms = new MemoryStream(body);
                CopyExactlyTo(io, ms, contentLength);
            }
            return new RequestContext(path, headers, body, tcp, ShouldKeepAlive(headers));
        }

        internal static void CopyExactlyTo(Stream stream, Stream destination, long count)
        {
            if (count < 0 || count > MaxRequestBodyBytes) throw new IOException("HTTP body copy length exceeds the configured limit.");
            var buffer = new byte[8192];
            var remaining = count;
            while (remaining > 0)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0) throw new IOException("Unexpected EOF while reading HTTP request body.");
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }
    }
}
