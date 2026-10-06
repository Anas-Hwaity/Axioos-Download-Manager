using ADM.Core.MediaParser.Util;
using System;
using System.Collections.Generic;
using System.Globalization;

#if !NET5_0_OR_GREATER
using ADM.Compatibility;
#endif

namespace ADM.Core.MediaParser.Hls
{
    public static class HlsParser
    {
        public static readonly string AUDIO = "AUDIO";
        public static readonly string VIDEO = "VIDEO";
        public static readonly string EXT_X_STREAM_INF = "#EXT-X-STREAM-INF:";
        public static readonly string EXT_X_MEDIA = "#EXT-X-MEDIA:";
        public static readonly string EXT_X_BYTERANGE = "#EXT-X-BYTERANGE:";
        public static readonly string EXTINF = "#EXTINF:";
        public static readonly string EXT_X_MEDIA_SEQUENCE = "#EXT-X-MEDIA-SEQUENCE:";
        public static readonly string EXT_X_KEY = "#EXT-X-KEY:";
        public static readonly string EXT_X_MAP = "#EXT-X-MAP:";
        public static readonly string EXT_X_I_FRAMES_ONLY = "#EXT-X-I-FRAMES-ONLY:";
        public const string ExtXEndList = "#EXT-X-ENDLIST";
        public const string ExtXTargetDuration = "#EXT-X-TARGETDURATION:";
        public const string ExtXPlaylistType = "#EXT-X-PLAYLIST-TYPE:";

        public static HlsPlaylist ParseMediaSegments(IEnumerable<string> manifestLines, string playlistUrl)
        {
            var mediaSegments = new List<HlsMediaSegment>();
            var mediaSequence = 0L;
            long? pendingRangeLength = null;
            long? pendingRangeOffset = null;
            Uri? previousRangeUrl = null;
            var previousRangeEnd = 0L;
            var endListSeen = false;
            var targetDuration = 0.0;
            var duration = 0.0;
            var totalDuration = 0.0;
            var hasByteRange = false;
            var isEncrypted = false;
            var baseUrl = new Uri(playlistUrl);
            var keyFrameOnly = false;

            Uri? keyUrl = null;
            string? iv = null;

            var sigFound = false;

            foreach (var lineText in manifestLines)
            {
                var line = lineText.Trim(' ', '\r');
                if (line.Length < 1) continue;
                if (!sigFound)
                {
                    if (line.StartsWith("#EXTM3U"))
                    {
                        sigFound = true;
                        continue;
                    }
                    else
                    {
                        return null;
                    }
                }
                if (line[0] != '#')
                {
                    var url = UrlResolver.Resolve(baseUrl, line);
                    var mediaSegment = new HlsMediaSegment(url)
                    {
                        ByteRange = new KeyValuePair<long, long>(0, 0),
                        Duration = duration,
                        KeyUrl = keyUrl,
                        IV = iv,
                        MediaSequence = mediaSequence
                    };
                    if (pendingRangeLength.HasValue)
                    {
                        var rangeStart = ResolveRangeStart(pendingRangeOffset, previousRangeUrl, previousRangeEnd, url);
                        mediaSegment.ByteRange = new KeyValuePair<long, long>(rangeStart, pendingRangeLength.Value);
                        mediaSegment.HasByteRange = true;
                        previousRangeUrl = url;
                        previousRangeEnd = checked(rangeStart + pendingRangeLength.Value);
                    }
                    else
                    {
                        previousRangeUrl = null;
                        previousRangeEnd = 0L;
                    }
                    pendingRangeLength = null;
                    pendingRangeOffset = null;
                    mediaSegments.Add(mediaSegment);
                    mediaSequence++;
                    totalDuration += duration;
                }
                else if (line.StartsWith(EXT_X_I_FRAMES_ONLY))
                {
                    keyFrameOnly = true;
                }
                else if (line.StartsWith(EXT_X_BYTERANGE))
                {
                    hasByteRange = true;
                    var attrList = line.Substring(EXT_X_BYTERANGE.Length).Trim();
                    ParseByteRange(attrList, out var rangeLength, out var rangeOffset);
                    pendingRangeLength = rangeLength;
                    pendingRangeOffset = rangeOffset;
                }
                else if (line.StartsWith(ExtXEndList))
                {
                    endListSeen = true;
                }
                else if (line.StartsWith(ExtXTargetDuration))
                {
                    double.TryParse(line.Substring(ExtXTargetDuration.Length).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out targetDuration);
                }
                else if (line.StartsWith(ExtXPlaylistType))
                {
                    if (line.Substring(ExtXPlaylistType.Length).Trim().Equals("VOD", StringComparison.OrdinalIgnoreCase)) endListSeen = true;
                }
                else if (line.StartsWith(EXTINF))
                {
                    var attrs = line.Substring(EXTINF.Length).Trim();
                    if (attrs.Length > 0)
                    {
                        if (!double.TryParse(attrs.Split(',')[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration)) duration = 0.0;
                    }
                }
                else if (line.StartsWith(EXT_X_MEDIA_SEQUENCE))
                {
                    mediaSequence = long.Parse(line.Substring(EXT_X_MEDIA_SEQUENCE.Length).Trim(), CultureInfo.InvariantCulture);
                }
                else if (line.StartsWith(EXT_X_KEY))
                {
                    var encAttrs = HlsHelper.ParseAttributes(line.Substring(EXT_X_KEY.Length));
                    if (encAttrs.ContainsKey("METHOD"))
                    {
                        if (encAttrs["METHOD"] == "AES-128" && encAttrs.GetValueOrDefault("KEYFORMAT", "identity") == "identity")
                        {
                            isEncrypted = true;
                            keyUrl = UrlResolver.Resolve(baseUrl, encAttrs["URI"]);
                            iv = encAttrs.ContainsKey("IV") ? encAttrs["IV"] : mediaSequence.ToString("X");
                        }
                        else if (encAttrs["METHOD"] != "NONE")
                        {
                            return null;
                        }
                    }
                }
                else if (line.StartsWith(EXT_X_MAP))
                {
                    var encAttrs = HlsHelper.ParseAttributes(line.Substring(EXT_X_MAP.Length));
                    if (encAttrs.ContainsKey("URI"))
                    {
                        var mapSegment = new HlsMediaSegment(UrlResolver.Resolve(baseUrl, encAttrs["URI"]))
                        {
                            ByteRange = new KeyValuePair<long, long>(0, 0),
                            Duration = 0,
                            KeyUrl = keyUrl,
                            IV = iv,
                            IsInitialization = true
                        };
                        if (encAttrs.ContainsKey("BYTERANGE"))
                        {
                            ParseByteRange(encAttrs["BYTERANGE"], out var mapLength, out var mapOffset);
                            mapSegment.ByteRange = new KeyValuePair<long, long>(mapOffset ?? 0L, mapLength);
                            mapSegment.HasByteRange = true;
                        }
                        mediaSegments.Add(mapSegment);
                    }
                }
            }

            if (mediaSegments.Count > 0)
            {
                return new HlsPlaylist
                {
                    MediaSegments = mediaSegments,
                    HasByteRange = hasByteRange,
                    IsEncrypted = isEncrypted,
                    TotalDuration = totalDuration,
                    IsKeyIFrameOnly = keyFrameOnly,
                    IsEndless = !endListSeen,
                    TargetDuration = targetDuration
                };
            }

            return null;
        }

        public static List<HlsPlaylistContainer> ParseMasterPlaylist(IEnumerable<string> manifestLines, string playlistUrl)
        {
            var containers = new List<HlsPlaylistContainer>();
            var baseUrl = new Uri(playlistUrl);
            var sigFound = false;
            var mapExtStreamInf = new List<Dictionary<string, string>>();
            var mapExtMedia = new List<Dictionary<string, string>>();
            var urls = new List<Uri>();
            foreach (var lineText in manifestLines)
            {
                var line = lineText.Trim();
                if (line.Length < 1) continue;
                if (!sigFound)
                {
                    if (line.StartsWith("#EXTM3U"))
                    {
                        sigFound = true;
                        continue;
                    }
                    else
                    {
                        return null;
                    }
                }

                if (line[0] != '#')
                {
                    urls.Add(UrlResolver.Resolve(baseUrl, line));
                }
                else if (line.StartsWith(EXT_X_STREAM_INF))
                {
                    mapExtStreamInf.Add(HlsHelper.ParseAttributes(line.Substring(EXT_X_STREAM_INF.Length)));
                }
                else if (line.StartsWith(EXT_X_MEDIA))
                {
                    mapExtMedia.Add(HlsHelper.ParseAttributes(line.Substring(EXT_X_MEDIA.Length)));
                }
            }

            if (mapExtStreamInf.Count < 1) return null;

            for (var i = 0; i < urls.Count; i++)
            {
                var extStreamInf = mapExtStreamInf[i];
                if (extStreamInf.ContainsKey(AUDIO))
                {
                    var groupId = extStreamInf[AUDIO];
                    foreach (var media in mapExtMedia)
                    {
                        if (media["GROUP-ID"] == groupId && media["TYPE"] == AUDIO)
                        {
                            containers.Add(new HlsPlaylistContainer
                            {
                                VideoPlaylist = urls[i],
                                AudioPlaylist = media.ContainsKey("URI") ? UrlResolver.Resolve(baseUrl, media["URI"]) : null,
                                Attributes = MergeDict(extStreamInf, media)
                            });
                        }
                    }
                }
                else if (extStreamInf.ContainsKey(VIDEO))
                {
                    var groupId = extStreamInf[VIDEO];
                    foreach (var media in mapExtMedia)
                    {
                        if (media["GROUP-ID"] == groupId && media["TYPE"] == VIDEO)
                        {
                            containers.Add(new HlsPlaylistContainer
                            {
                                VideoPlaylist = media.ContainsKey("URI") ? UrlResolver.Resolve(baseUrl, media["URI"]) : null,
                                AudioPlaylist = urls[i],
                                Attributes = MergeDict(extStreamInf, media)
                            });
                        }
                    }
                }
                else
                {
                    containers.Add(new HlsPlaylistContainer
                    {
                        VideoPlaylist = urls[i],
                        Attributes = extStreamInf
                    });
                }
            }

            return containers;
        }

        private static Dictionary<string, string> MergeDict(Dictionary<string, string> d1, Dictionary<string, string> d2)
        {
            var dict = new Dictionary<string, string>();
            foreach (var ent in d1)
            {
                dict[ent.Key] = ent.Value;
            }
            foreach (var ent in d2)
            {
                dict[ent.Key] = ent.Value;
            }
            return dict;
        }

        public static long ResolveRangeStart(long? explicitOffset, Uri? previousRangeUrl, long previousRangeEnd, Uri url)
        {
            if (explicitOffset.HasValue) return explicitOffset.Value;
            if (previousRangeUrl != null && previousRangeUrl.Equals(url)) return previousRangeEnd;
            return 0L;
        }

        public static void ParseByteRange(string str, out long length, out long? offset)
        {
            var attrs = str.Trim().Trim('"').Split('@');
            length = long.Parse(attrs[0].Trim(), CultureInfo.InvariantCulture);
            offset = null;
            if (attrs.Length >= 2)
            {
                offset = long.Parse(attrs[1].Trim(), CultureInfo.InvariantCulture);
            }
            if (length < 0 || (offset.HasValue && offset.Value < 0))
            {
                throw new FormatException("Byte range values cannot be negative");
            }
        }
    }
}
