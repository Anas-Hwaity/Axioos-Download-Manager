using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ADM.Core.HttpServer
{
    internal static class LineReader
    {
        internal const int DefaultMaxLineChars = 8192;
        internal const int DefaultMaxLines = 128;

        internal static IEnumerable<string> ReadLines(Stream stream, int maxLineChars = DefaultMaxLineChars, int maxLines = DefaultMaxLines)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (maxLineChars <= 0) throw new ArgumentOutOfRangeException(nameof(maxLineChars));
            if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));

            var buffer = new StringBuilder();
            var lineCount = 0;
            while (true)
            {
                var x = stream.ReadByte();
                if (x == -1) throw new IOException("Unexpected EOF while reading HTTP headers.");
                if (x == '\n')
                {
                    if (buffer.Length == 0) yield break;
                    lineCount++;
                    if (lineCount > maxLines) throw new IOException("HTTP request contains too many header lines.");
                    var line = buffer.ToString();
                    buffer.Clear();
                    yield return line;
                    continue;
                }
                if (x == '\r') continue;
                if (buffer.Length >= maxLineChars) throw new IOException("HTTP header line exceeds the configured limit.");
                buffer.Append((char)x);
            }
        }
    }
}
