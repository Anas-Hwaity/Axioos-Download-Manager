using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ADM.Core.Util;
using TraceLog;

namespace ADM.Core.Clients.Http
{
    public static class HeaderFileNameDecoder
    {
        private const int Latin1CodePage = 28591;
        private const string EncodedWordPattern = @"=\?([^?\s]+)\?([bBqQ])\?([^?\s]*)\?=";

        public static string? Decode(string? contentDisposition)
        {
            if (contentDisposition == null || contentDisposition.Trim().Length == 0) return null;
            var header = RepairHeaderText(contentDisposition);
            string? extended = null;
            string? plain = null;
            foreach (var parameter in SplitParameters(header))
            {
                var separator = parameter.IndexOf('=');
                if (separator <= 0) continue;
                var name = parameter.Substring(0, separator).Trim();
                var value = parameter.Substring(separator + 1).Trim();
                if (extended == null && name.Equals("filename*", StringComparison.OrdinalIgnoreCase))
                {
                    extended = DecodeExtendedValue(value);
                }
                else if (plain == null && name.Equals("filename", StringComparison.OrdinalIgnoreCase))
                {
                    plain = DecodePlainValue(value);
                }
            }
            var chosen = extended != null && extended.Trim().Length > 0 ? extended : plain;
            if (chosen == null || chosen.Trim().Length == 0) return null;
            return FileHelper.SanitizeFileName(chosen);
        }

        public static string RepairHeaderText(string? value)
        {
            if (value == null || value.Length == 0) return string.Empty;
            var plain = true;
            var singleByte = true;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c > 0x7F) plain = false;
                if (c > 0xFF) singleByte = false;
            }
            if (plain) return value;
            if (!singleByte) return RepairSystemCodePageText(value);
            var bytes = new byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                bytes[i] = (byte)value[i];
            }
            var utf8 = TryDecodeStrict(new UTF8Encoding(false, true), bytes);
            if (utf8 != null) return utf8;
            var legacy = TryDecodeStrict(SystemCodePage(), bytes);
            return legacy ?? value;
        }

        public static List<string> SplitParameters(string header)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < header.Length; i++)
            {
                var c = header[i];
                if (quoted && c == '\\' && i + 1 < header.Length && (header[i + 1] == '"' || header[i + 1] == '\\'))
                {
                    current.Append(c).Append(header[i + 1]);
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    quoted = !quoted;
                    current.Append(c);
                    continue;
                }
                if (c == ';' && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }
                current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }

        public static string DecodeExtendedValue(string value)
        {
            var text = Unquote(value);
            var first = text.IndexOf('\'');
            var second = first < 0 ? -1 : text.IndexOf('\'', first + 1);
            var charset = second < 0 ? string.Empty : text.Substring(0, first).Trim();
            var data = second < 0 ? text : text.Substring(second + 1);
            var encoding = ResolveEncoding(charset);
            var bytes = PercentDecode(data, encoding ?? new UTF8Encoding(false, false));
            return DecodeBytes(bytes, encoding);
        }

        public static string DecodePlainValue(string value)
        {
            var text = Unquote(value);
            if (text.IndexOf("=?", StringComparison.Ordinal) >= 0 && text.IndexOf("?=", StringComparison.Ordinal) >= 0)
            {
                var decodedWords = DecodeEncodedWords(text);
                if (!string.Equals(decodedWords, text, StringComparison.Ordinal)) return decodedWords;
            }
            if (text.IndexOf('%') < 0) return text;
            var bytes = PercentDecode(text, new UTF8Encoding(false, false));
            var decoded = TryDecodeStrict(new UTF8Encoding(false, true), bytes);
            return decoded ?? text;
        }

        public static string DecodeEncodedWords(string text)
        {
            var joined = Regex.Replace(text, @"(\?=)\s+(=\?)", "$1$2");
            return Regex.Replace(joined, EncodedWordPattern, match =>
            {
                var encoding = ResolveEncoding(match.Groups[1].Value);
                var kind = match.Groups[2].Value;
                var payload = match.Groups[3].Value;
                byte[]? bytes;
                if (kind == "b" || kind == "B")
                {
                    bytes = TryFromBase64(payload);
                }
                else
                {
                    bytes = FromQuotedPrintableWord(payload);
                }
                return bytes == null ? match.Value : DecodeBytes(bytes, encoding);
            });
        }

        public static string? DecodeUrlSegment(string? escapedSegment)
        {
            if (escapedSegment == null || escapedSegment.Length == 0) return null;
            var bytes = PercentDecode(escapedSegment, new UTF8Encoding(false, false));
            var utf8 = TryDecodeStrict(new UTF8Encoding(false, true), bytes);
            if (utf8 != null) return utf8;
            return TryDecodeStrict(SystemCodePage(), bytes);
        }

        private static string Unquote(string value)
        {
            var text = value.Trim();
            if (text.Length < 2 || text[0] != '"') return text;
            var builder = new StringBuilder(text.Length);
            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '\\'))
                {
                    builder.Append(text[i + 1]);
                    i++;
                    continue;
                }
                if (c == '"') break;
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static byte[] PercentDecode(string data, Encoding literalEncoding)
        {
            var bytes = new List<byte>(data.Length);
            var literal = new StringBuilder();
            for (var i = 0; i < data.Length; i++)
            {
                var c = data[i];
                if (c == '%' && i + 2 < data.Length && IsHex(data[i + 1]) && IsHex(data[i + 2]))
                {
                    FlushLiteral(literal, bytes, literalEncoding);
                    bytes.Add((byte)((HexValue(data[i + 1]) << 4) | HexValue(data[i + 2])));
                    i += 2;
                    continue;
                }
                literal.Append(c);
            }
            FlushLiteral(literal, bytes, literalEncoding);
            return bytes.ToArray();
        }

        private static void FlushLiteral(StringBuilder literal, List<byte> bytes, Encoding encoding)
        {
            if (literal.Length == 0) return;
            bytes.AddRange(encoding.GetBytes(literal.ToString()));
            literal.Length = 0;
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10;
        }

        private static byte[]? TryFromBase64(string payload)
        {
            try
            {
                var padded = payload.Replace('-', '+').Replace('_', '/');
                while (padded.Length % 4 != 0) padded += "=";
                return Convert.FromBase64String(padded);
            }
            catch (FormatException ex)
            {
                Log.Debug("Encoded file name is not valid base64: " + ex.GetType().Name);
                return null;
            }
        }

        private static byte[] FromQuotedPrintableWord(string payload)
        {
            var bytes = new List<byte>(payload.Length);
            for (var i = 0; i < payload.Length; i++)
            {
                var c = payload[i];
                if (c == '_')
                {
                    bytes.Add(0x20);
                }
                else if (c == '=' && i + 2 < payload.Length && IsHex(payload[i + 1]) && IsHex(payload[i + 2]))
                {
                    bytes.Add((byte)((HexValue(payload[i + 1]) << 4) | HexValue(payload[i + 2])));
                    i += 2;
                }
                else if (c <= 0xFF)
                {
                    bytes.Add((byte)c);
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                }
            }
            return bytes.ToArray();
        }

        private static Encoding? ResolveEncoding(string charset)
        {
            var name = charset.Trim();
            if (name.Length == 0 || name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf8", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                if (name.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) || name.Equals("latin1", StringComparison.OrdinalIgnoreCase))
                {
                    return Encoding.GetEncoding(Latin1CodePage);
                }
                return Encoding.GetEncoding(name);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                Log.Debug("File name charset is not available, reading it as UTF-8: " + ex.GetType().Name);
                return null;
            }
        }

        private static string DecodeBytes(byte[] bytes, Encoding? declared)
        {
            if (declared != null) return declared.GetString(bytes);
            var utf8 = TryDecodeStrict(new UTF8Encoding(false, true), bytes);
            if (utf8 != null) return utf8;
            var legacy = TryDecodeStrict(SystemCodePage(), bytes);
            if (legacy != null) return legacy;
            return new UTF8Encoding(false, false).GetString(bytes);
        }

        private static string RepairSystemCodePageText(string value)
        {
            var system = SystemCodePage();
            if (system == null || system.CodePage == Encoding.UTF8.CodePage) return value;
            try
            {
                var bytes = system.GetBytes(value);
                var utf8 = TryDecodeStrict(new UTF8Encoding(false, true), bytes);
                return utf8 ?? value;
            }
            catch (EncoderFallbackException ex)
            {
                Log.Debug("Header text does not fit the system code page, leaving it unchanged: " + ex.GetType().Name);
                return value;
            }
        }

        private static Encoding? SystemCodePage()
        {
            try
            {
                return Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                Log.Debug("System code page is not available: " + ex.GetType().Name);
                return null;
            }
        }

        private static string? TryDecodeStrict(Encoding? encoding, byte[] bytes)
        {
            if (encoding == null) return null;
            try
            {
                return encoding.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                Log.Debug("Header bytes are not valid " + encoding.WebName + ": " + ex.GetType().Name);
                return null;
            }
        }
    }
}
