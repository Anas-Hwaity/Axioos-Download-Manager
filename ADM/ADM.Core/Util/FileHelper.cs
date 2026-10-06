using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TraceLog;

namespace ADM.Core.Util
{
    public static class FileHelper
    {
        public static readonly Regex RxFileWithinQuote = new Regex("\\\"(.*)\\\"");
        public const int MaxSafeFileNameLength = 240;

        public const int MaxTitleLength = 150;

        public static string SanitizeTitle(string? title, string fallback)
        {
            var text = title == null || title.Trim().Length == 0 ? fallback : title.Replace('/', '_').Replace('\\', '_');
            var clean = SanitizeFileName(text) ?? fallback;
            if (clean.Length > MaxTitleLength)
            {
                clean = CutAtTextElement(clean, MaxTitleLength).TrimEnd('.', ' ');
            }
            return clean.Length == 0 ? fallback : clean;
        }

        public static string? SanitizeFileName(string fileName)
        {
            if (fileName == null) return null;

            var lastSlash = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
            var leaf = lastSlash >= 0 ? fileName.Substring(lastSlash + 1) : fileName;

            var builder = new StringBuilder(leaf.Length);
            foreach (var c in leaf)
            {
                if (IsDirectionControl(c)) continue;
                if (c < 32 || (c >= 0x7F && c <= 0x9F) || c == '<' || c == '>' || c == ':' || c == '"' ||
                    c == '/' || c == '\\' || c == '|' || c == '?' || c == '*')
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(c);
                }
            }

            var sanitized = builder.ToString().Trim().TrimEnd('.', ' ');
            if (string.IsNullOrEmpty(sanitized) || sanitized == "." || sanitized == "..")
                sanitized = "download";

            var firstDot = sanitized.IndexOf('.');
            var stem = firstDot >= 0 ? sanitized.Substring(0, firstDot) : sanitized;
            if (IsReservedWindowsDeviceName(stem)) sanitized = "_" + sanitized;

            if (sanitized.Length > MaxSafeFileNameLength)
                sanitized = FitLength(sanitized, MaxSafeFileNameLength);
            if (sanitized.Length == 0) sanitized = "download";
            return sanitized;
        }

        public static bool IsDirectionControl(char c)
        {
            return (c >= 0x202A && c <= 0x202E) || (c >= 0x2066 && c <= 0x2069);
        }

        public static string CutAtTextElement(string text, int maxLength)
        {
            if (maxLength <= 0) return string.Empty;
            if (text.Length <= maxLength) return text;
            var starts = StringInfo.ParseCombiningCharacters(text);
            var cut = 0;
            foreach (var start in starts)
            {
                if (start > maxLength) break;
                cut = start;
            }
            return text.Substring(0, cut);
        }

        public static string FitLength(string name, int maxLength)
        {
            if (name.Length <= maxLength) return name;
            var dot = name.LastIndexOf('.');
            var extension = dot > 0 && name.Length - dot <= 16 && name.IndexOf(' ', dot) < 0 ? name.Substring(dot) : string.Empty;
            var stem = extension.Length == 0 ? name : name.Substring(0, dot);
            var fitted = CutAtTextElement(stem, maxLength - extension.Length).TrimEnd('.', ' ');
            if (fitted.Length == 0) return CutAtTextElement(name, maxLength).TrimEnd('.', ' ');
            return fitted + extension;
        }

        private static bool IsReservedWindowsDeviceName(string stem)
        {
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;

            if (stem.Length == 4 && char.IsDigit(stem[3]) && stem[3] >= '1' && stem[3] <= '9')
            {
                return stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                       stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public static string GetDownloadFolderByFileName(string file)
        {
            try
            {
                var ext = Path.GetExtension(file)?.ToUpperInvariant();
                foreach (var category in Config.Instance.Categories)
                {
                    if (ext != null && category.FileExtensions.Contains(ext))
                    {
                        return category.DefaultFolder;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error");
            }
            return Config.Instance.DefaultDownloadFolder;
        }



        public static bool AddFileExtension(string name, string contentType, out string nameWithExt)
        {
            name = SanitizeFileName(name);
            if (name.EndsWith("."))
            {
                name = name.TrimEnd('.');
            }
            if (string.IsNullOrEmpty(contentType))
            {
                nameWithExt = name;
                return false;
            }
            if (contentType == "text/html")
            {
                nameWithExt = name + ".html";
                return true;
            }
            else
            {
                try
                {
                    var ext = MimeTypes.Get(contentType.ToLowerInvariant());
                    if (!string.IsNullOrEmpty(ext))
                    {
                        var prevExt = Path.GetExtension(name);
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(name);
                        if (!("." + ext).Equals(prevExt, StringComparison.InvariantCultureIgnoreCase))
                        {
                            nameWithExt = nameWithoutExt + "." + ext;
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error in AddFileExtension");
                }

                nameWithExt = name;
                return true;
            }
        }

        public static string GetFileName(Uri uri, string contentType = null)
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrEmpty(name) && (name.IndexOf('\uFFFD') >= 0 || name.IndexOf('%') >= 0))
            {
                var escaped = uri.AbsolutePath;
                var repaired = ADM.Core.Clients.Http.HeaderFileNameDecoder.DecodeUrlSegment(escaped.Substring(escaped.LastIndexOf('/') + 1));
                if (repaired != null && repaired.Length > 0) name = repaired;
            }
            if (string.IsNullOrEmpty(name))
            {
                name = uri.Host.Replace('.', '_');
            }
            name = SanitizeFileName(name);
            if (string.IsNullOrEmpty(contentType))
            {
                return name;
            }

            if (contentType == "text/html")
            {
                return Path.ChangeExtension(name, ".html");
            }
            else
            {
                if (!Path.HasExtension(name))
                {
                    var ext = MimeTypes.Get(contentType.ToLowerInvariant());
                    if (!string.IsNullOrEmpty(ext))
                    {
                        name += "." + ext;
                    }
                }
                return name;
            }
        }

        public static string GetUniqueFileName(string file, string folder)
        {
            var path = Path.Combine(folder, file);
            var name = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            var count = 0;
            while (File.Exists(path))
            {
                count++;
                path = Path.Combine(folder, name + "_" + count + ext);
            }
            return count == 0 ? file : name + "_" + count + ext;
        }

        public static string GetFileNameFromQuote(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            var matcher = RxFileWithinQuote.Match(text);
            if (matcher.Success)
            {
                return matcher.Groups[1].Value;
            }
            return null;
        }

        public static string QuoteFilePathIfNeeded(string file)
        {
            if (file.Contains(" "))
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT ? $"\"{file}\"" : $"\"{file}\"";
            }
            return file;
        }
    }
}
