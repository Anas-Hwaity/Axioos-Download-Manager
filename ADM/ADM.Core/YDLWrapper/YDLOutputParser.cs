using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace YDLWrapper
{
    public static class YDLOutputParser
    {
        private const int MaxNestingDepth = 4;
        private const int MaxFormatsPerEntry = 400;

        private static readonly HashSet<string> NonMediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mhtml", "jpg", "jpeg", "png", "webp", "gif", "vtt", "srt", "ttml", "json"
        };

        private static readonly HashSet<string> UnusedSections = new HashSet<string>(StringComparer.Ordinal)
        {
            "automatic_captions", "subtitles", "requested_subtitles", "thumbnails", "heatmap", "chapters", "comments", "storyboards", "_format_sort_fields", "http_headers", "downloader_options"
        };

        private static readonly HashSet<string> NonMediaProtocols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mhtml", "websocket_frag"
        };

        public static List<YDLVideoEntry> Parse(string ydlJsonOutputFile)
        {
            var res = new List<YDLVideoEntry>();
            try
            {
                using var source = new StreamReader(ydlJsonOutputFile);
                foreach (var document in ReadDocuments(source))
                {
                    Collect(document, null, res, 0);
                }
            }
            catch (IOException)
            {
                return res;
            }
            catch (UnauthorizedAccessException)
            {
                return res;
            }
            return res;
        }

        public static List<YDLVideoEntry> ParseText(string json)
        {
            var res = new List<YDLVideoEntry>();
            if (string.IsNullOrWhiteSpace(json)) return res;
            foreach (var document in ReadDocuments(new StringReader(json)))
            {
                Collect(document, null, res, 0);
            }
            return res;
        }

        private static IEnumerable<JToken> ReadDocuments(TextReader source)
        {
            var documents = new List<JToken>();
            try
            {
                using (var reader = new JsonTextReader(source) { SupportMultipleContent = true, DateParseHandling = DateParseHandling.None })
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.StartObject)
                        {
                            documents.Add(ReadWithoutUnusedSections(reader));
                        }
                        else if (reader.TokenType == JsonToken.StartArray)
                        {
                            documents.Add(JToken.ReadFrom(reader));
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
            return documents;
        }

        private const int MaxFragmentsPerDocument = 200000;

        private static JObject ReadWithoutUnusedSections(JsonTextReader reader)
        {
            var fragmentBudget = MaxFragmentsPerDocument;
            return ReadTrimmedObject(reader, ref fragmentBudget);
        }

        private static JObject ReadTrimmedObject(JsonTextReader reader, ref int fragmentBudget)
        {
            var result = new JObject();
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                var name = reader.Value as string ?? string.Empty;
                if (!reader.Read()) break;
                if (UnusedSections.Contains(name))
                {
                    reader.Skip();
                    continue;
                }
                if (name == "fragments" && reader.TokenType == JsonToken.StartArray)
                {
                    result[name] = ReadFragmentList(reader, ref fragmentBudget);
                    continue;
                }
                result[name] = ReadTrimmedValue(reader, ref fragmentBudget);
            }
            return result;
        }

        private static JArray ReadFragmentList(JsonTextReader reader, ref int fragmentBudget)
        {
            var fragments = new JArray();
            var truncated = false;
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                if (fragmentBudget <= 0)
                {
                    truncated = true;
                    reader.Skip();
                    continue;
                }
                fragments.Add(ReadTrimmedValue(reader, ref fragmentBudget));
                fragmentBudget--;
            }
            return truncated ? new JArray() : fragments;
        }

        private static JToken ReadTrimmedValue(JsonTextReader reader, ref int fragmentBudget)
        {
            if (reader.TokenType == JsonToken.StartObject) return ReadTrimmedObject(reader, ref fragmentBudget);
            if (reader.TokenType == JsonToken.StartArray)
            {
                var array = new JArray();
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    array.Add(ReadTrimmedValue(reader, ref fragmentBudget));
                }
                return array;
            }
            return JToken.ReadFrom(reader);
        }

        private static void Collect(JToken? node, string? inheritedTitle, List<YDLVideoEntry> res, int depth)
        {
            if (node == null || depth > MaxNestingDepth) return;
            if (node is JArray array)
            {
                foreach (var child in array) Collect(child, inheritedTitle, res, depth + 1);
                return;
            }
            if (!(node is JObject obj)) return;

            var title = Text(obj["title"]) ?? Text(obj["fulltitle"]) ?? inheritedTitle;
            if (obj["entries"] is JArray entries)
            {
                foreach (var entry in entries) Collect(entry, title, res, depth + 1);
            }

            var formats = ReadFormats(obj, title);
            if (formats.Count == 0) return;
            var processed = ProcessFormatList(new YDLFormatList { Title = title!, Formats = formats.ToArray() });
            if (processed.Count == 0) return;
            res.Add(new YDLVideoEntry { Title = title!, Formats = processed });
        }

        private static List<YDLFormat> ReadFormats(JObject obj, string? title)
        {
            var formats = new List<YDLFormat>();
            if (obj["formats"] is JArray formatArray)
            {
                foreach (var token in formatArray)
                {
                    if (formats.Count >= MaxFormatsPerEntry) break;
                    if (token is JObject formatObject && TryReadFormat(formatObject, title, out var format))
                        formats.Add(format);
                }
            }
            if (formats.Count == 0 && obj["requested_formats"] is JArray requested)
            {
                foreach (var token in requested)
                {
                    if (token is JObject formatObject && TryReadFormat(formatObject, title, out var format))
                        formats.Add(format);
                }
            }
            if (formats.Count == 0 && Text(obj["url"]) != null && TryReadFormat(obj, title, out var single))
            {
                formats.Add(single);
            }
            return formats;
        }

        private static bool TryReadFormat(JObject token, string? title, out YDLFormat format)
        {
            format = new YDLFormat
            {
                Title = (Text(token["title"]) ?? title)!,
                Url = Text(token["url"])!,
                Format = (Text(token["format"]) ?? Text(token["format_id"]))!,
                Format_Note = Text(token["format_note"])!,
                Ext = Text(token["ext"])!,
                Protocol = Text(token["protocol"])!,
                Container = Text(token["container"])!,
                Vcodec = Text(token["vcodec"])!,
                Acodec = Text(token["acodec"])!,
                Width = Text(token["width"])!,
                Height = Text(token["height"])!,
                Abr = Text(token["abr"])!,
                Language_Preference = Text(token["language_preference"])!,
                Filesize = (Text(token["filesize"]) ?? Text(token["filesize_approx"]))!,
                Fragment_Base_Url = Text(token["fragment_base_url"])!,
                Manifest_Url = Text(token["manifest_url"])!,
                Fragments = ReadFragments(token["fragments"])!
            };

            var hasUrl = !string.IsNullOrEmpty(format.Url);
            var hasFragments = format.Fragments != null && format.Fragments.Count > 0;
            if (!hasUrl && !hasFragments) return false;
            if (format.Ext != null && NonMediaExtensions.Contains(format.Ext)) return false;
            if (format.Protocol != null && NonMediaProtocols.Contains(format.Protocol)) return false;
            if (IsExplicitNone(format.Vcodec) && IsExplicitNone(format.Acodec) && !LooksLikeMediaExtension(format.Ext)) return false;
            if (hasUrl)
            {
                if (!Uri.TryCreate(format.Url, UriKind.Absolute, out var uri)) return false;
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            }
            return true;
        }

        private static IList<Fragment>? ReadFragments(JToken? token)
        {
            if (!(token is JArray array) || array.Count == 0) return null;
            var fragments = new List<Fragment>();
            foreach (var item in array)
            {
                if (!(item is JObject fragment)) continue;
                var path = Text(fragment["path"]) ?? Text(fragment["url"]);
                if (string.IsNullOrEmpty(path)) continue;
                fragments.Add(new Fragment { Path = path!, Duration = Text(fragment["duration"])! });
            }
            return fragments.Count == 0 ? null : fragments;
        }

        private static string? Text(JToken? token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                case JTokenType.Object:
                case JTokenType.Array:
                    return null;
                case JTokenType.Integer:
                    var integer = (token as JValue)?.Value;
                    return integer == null ? null : Convert.ToString(integer, CultureInfo.InvariantCulture);
                case JTokenType.Float:
                    var number = (double)token;
                    if (double.IsNaN(number) || double.IsInfinity(number)) return null;
                    return Math.Abs(number - Math.Round(number)) < 0.0000001
                        ? ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture)
                        : number.ToString("0.###", CultureInfo.InvariantCulture);
                case JTokenType.Boolean:
                    return (bool)token ? "true" : "false";
                default:
                    var value = token.ToString();
                    return value.Length == 0 ? null : value;
            }
        }

        private static bool IsExplicitNone(string? codec)
        {
            return codec != null && GetStringValue(codec) == null;
        }

        private static bool LooksLikeMediaExtension(string? ext)
        {
            switch ((ext ?? string.Empty).ToLowerInvariant())
            {
                case "mp4":
                case "m4a":
                case "webm":
                case "mkv":
                case "mov":
                case "mp3":
                case "aac":
                case "ogg":
                case "opus":
                case "flv":
                case "ts":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAudioOnly(YDLFormat format)
        {
            var vcodec = GetStringValue(format.Vcodec);
            var acodec = GetStringValue(format.Acodec);
            if (vcodec != null) return false;
            if (acodec != null) return true;
            if (IsExplicitNone(format.Vcodec)) return true;
            var note = (format.Format_Note ?? string.Empty) + " " + (format.Format ?? string.Empty);
            return string.IsNullOrEmpty(format.Height) && note.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsVideoOnly(YDLFormat format)
        {
            var vcodec = GetStringValue(format.Vcodec);
            var acodec = GetStringValue(format.Acodec);
            if (acodec != null) return false;
            if (vcodec != null) return IsExplicitNone(format.Acodec);
            return IsExplicitNone(format.Acodec) && !IsExplicitNone(format.Vcodec);
        }

        private static List<YDLVideoFormatEntry> ProcessFormatList(YDLFormatList formatList)
        {
            var list = new List<YDLVideoFormatEntry>();
            var videoOnlyList = new List<YDLFormat>();
            var audioOnlyList = new List<YDLFormat>();

            foreach (var format in formatList.Formats ?? new YDLFormat[0])
            {
                if (IsAudioOnly(format))
                {
                    audioOnlyList.Add(format);
                }
                else if (IsVideoOnly(format))
                {
                    videoOnlyList.Add(format);
                }
                else
                {
                    list.Add(new YDLVideoFormatEntry
                    {
                        VideoUrl = format.Url,
                        VideoFragments = format.Fragments,
                        Title = formatList.Title,
                        YDLEntryType = GetEntryType(format),
                        VideoFormat = format.Format,
                        FileExt = format.Ext,
                        VideoCodec = format.Vcodec,
                        AudioCodec = format.Acodec,
                        Abr = format.Abr,
                        Width = format.Width,
                        Height = format.Height,
                        FragmentBaseUrl = format.Fragment_Base_Url
                    });
                }
            }

            foreach (var video in videoOnlyList)
            {
                var videoType = GetEntryType(video);
                var sameType = audioOnlyList.Where(candidate => GetEntryType(candidate) == videoType).ToList();
                var candidates = sameType.Count > 0 ? sameType : audioOnlyList;
                var audio = BestAudio(candidates);
                if (audio.HasValue)
                {
                    var paired = audio.Value;
                    var pairedType = GetEntryType(paired);
                    var entryType = pairedType == videoType ? videoType : PairedEntryType(videoType, pairedType);
                    list.Add(new YDLVideoFormatEntry
                    {
                        AudioFormat = paired.Format,
                        VideoFormat = video.Format,
                        AudioFragments = paired.Fragments,
                        VideoFragments = video.Fragments,
                        AudioUrl = paired.Url,
                        VideoUrl = video.Url,
                        Title = formatList.Title,
                        YDLEntryType = entryType,
                        FileExt = entryType == YDLEntryType.Hls ? "mp4" : "mkv",
                        VideoCodec = video.Vcodec,
                        AudioCodec = paired.Acodec,
                        Abr = paired.Abr,
                        Width = video.Width,
                        Height = video.Height,
                        FragmentBaseUrl = video.Fragment_Base_Url
                    });
                }
                else
                {
                    list.Add(new YDLVideoFormatEntry
                    {
                        VideoUrl = video.Url,
                        VideoFragments = video.Fragments,
                        Title = formatList.Title,
                        YDLEntryType = videoType,
                        VideoFormat = video.Format,
                        FileExt = video.Ext,
                        VideoCodec = video.Vcodec,
                        Width = video.Width,
                        Height = video.Height,
                        FragmentBaseUrl = video.Fragment_Base_Url
                    });
                }
            }

            if (list.Count == 0)
            {
                foreach (var audio in audioOnlyList)
                {
                    list.Add(new YDLVideoFormatEntry
                    {
                        AudioFormat = audio.Format,
                        AudioFragments = audio.Fragments,
                        AudioUrl = audio.Url,
                        VideoUrl = audio.Url,
                        Title = formatList.Title,
                        YDLEntryType = GetEntryType(audio),
                        FileExt = audio.Ext,
                        AudioCodec = audio.Acodec,
                        Abr = audio.Abr,
                        FragmentBaseUrl = audio.Fragment_Base_Url
                    });
                }
            }

            return list;
        }

        private static YDLEntryType PairedEntryType(YDLEntryType videoType, YDLEntryType audioType)
        {
            if (videoType == YDLEntryType.Hls || audioType == YDLEntryType.Hls) return YDLEntryType.Hls;
            if (videoType == YDLEntryType.MpegDash || audioType == YDLEntryType.MpegDash) return YDLEntryType.MpegDash;
            return YDLEntryType.Dash;
        }

        private static YDLFormat? BestAudio(List<YDLFormat> candidates)
        {
            if (candidates.Count == 0) return null;
            return candidates
                .OrderByDescending(audio => ParseNumber(audio.Language_Preference))
                .ThenByDescending(audio => (audio.Format_Note ?? string.Empty).IndexOf("original", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                .ThenByDescending(audio => ParseNumber(audio.Abr))
                .ThenBy(audio => (audio.Ext ?? string.Empty).Equals("m4a", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .First();
        }

        private static double ParseNumber(string? value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
        }

        private static YDLEntryType GetEntryType(YDLFormat format)
        {
            if (HasFragments(format)) return YDLEntryType.MpegDash;
            var protocol = format.Protocol?.ToLowerInvariant() ?? string.Empty;
            if (protocol.Contains("dash")) return YDLEntryType.Dash;
            if (protocol.Contains("m3u")) return YDLEntryType.Hls;
            var container = format.Container?.ToLowerInvariant() ?? string.Empty;
            if (container.Contains("dash")) return YDLEntryType.Dash;
            if (container.Contains("m3u")) return YDLEntryType.Hls;
            var url = format.Url ?? string.Empty;
            if (url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0) return YDLEntryType.Hls;
            return YDLEntryType.Http;
        }

        private static bool HasFragments(YDLFormat format)
        {
            return format.Fragments != null && format.Fragments.Count > 0;
        }

        private static string? GetStringValue(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if ("none".Equals(text)) return null;
            if ("'none'".Equals(text)) return null;
            if ("\"none\"".Equals(text)) return null;
            return text;
        }
    }
}
