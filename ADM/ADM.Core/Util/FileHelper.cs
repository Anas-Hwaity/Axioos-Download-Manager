using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                var cut = char.IsHighSurrogate(clean[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
                clean = clean.Substring(0, cut).TrimEnd('.', ' ');
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
                if (c < 32 || c == '<' || c == '>' || c == ':' || c == '"' ||
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
                sanitized = sanitized.Substring(0, MaxSafeFileNameLength).TrimEnd('.', ' ');
            if (sanitized.Length == 0) sanitized = "download";
            return sanitized;
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
