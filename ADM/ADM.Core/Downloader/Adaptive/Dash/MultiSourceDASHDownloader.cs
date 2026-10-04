using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraceLog;
using ADM.Core.Clients.Http;
using ADM.Core;
using ADM.Core.MediaProcessor;
using ADM.Core.Util;
using ADM.Core.IO;

namespace ADM.Core.Downloader.Adaptive.Dash
{
    public class MultiSourceDASHDownloader : MultiSourceDownloaderBase
    {
        public override string Type => "Mpd-Dash";
        public override Uri PrimaryUrl
        {
            get
            {
                var state = _state as MultiSourceDASHDownloadState;
                return state == null ? null : new Uri(state.Url);
            }
        }
        public MultiSourceDASHDownloader(MultiSourceDASHDownloadInfo info, IHttpClient http = null,
            BaseMediaProcessor mediaProcessor = null,
            AuthenticationInfo? authentication = null, ProxyInfo? proxy = null) : base(info, http, mediaProcessor)
        {
            var state = new MultiSourceDASHDownloadState
            {
                Id = base.Id,
                Demuxed = info.VideoSegments != null && info.AudioSegments != null,
                Cookies = info.Cookies,
                Headers = info.Headers,
                Url = info.Url,
                Authentication = authentication,
                Proxy = proxy,
                TempDirectory = Path.Combine(Config.Instance.TempDir, Id)
            };

            if (state.Authentication == null)
            {
                state.Authentication = Helpers.GetAuthenticationInfoFromConfig(new Uri(info.Url));
            }

            this._state = state;
            this.TargetFileName = FileHelper.SanitizeFileName(info.File);

            state.FileSize = -1;
            var i = 0;

            if (state.Demuxed)
            {
                state.AudioChunkCount = info.AudioSegments.Count;
                state.VideoChunkCount = info.VideoSegments.Count;

                state.AudioSegments = info.AudioSegments;
                state.VideoSegments = info.VideoSegments;

                state.Duration = info.Duration;
                state.AudioContainerFormat = info.AudioFormat ?? FileExtensionHelper.GetExtensionFromMimeType(info.AudioMimeType)
                    ?? GuessContainerFormatFromPlaylist(info.AudioSegments);
                state.VideoContainerFormat = info.VideoFormat ?? FileExtensionHelper.GetExtensionFromMimeType(info.VideoMimeType)
                    ?? GuessContainerFormatFromPlaylist(info.VideoSegments);

                CreateChunks2(state, _chunks, _chunkStreamMap);



                var ext = FileExtensionHelper.GetExtensionFromMimeType(info.VideoMimeType) ??
                    FileExtensionHelper.GuessContainerFormatFromSegmentExtension(state.VideoContainerFormat);

                TargetFileName = Path.GetFileNameWithoutExtension(TargetFileName ?? "video")
                        + ext;
            }
            else
            {
                var segments = info.VideoSegments ?? info.AudioSegments;
                state.VideoChunkCount = segments.Count;
                state.VideoSegments = segments;
                state.Duration = info.Duration;

                CreateChunks1(state, _chunks, _chunkStreamMap);


                state.VideoContainerFormat = GuessContainerFormatFromPlaylist(segments);
                var ext = FileExtensionHelper.GuessContainerFormatFromSegmentExtension(
                            this._state.VideoContainerFormat.ToLowerInvariant());
                TargetFileName = Path.GetFileNameWithoutExtension(TargetFileName ?? "video")
                            + ext;
            }
        }

        public MultiSourceDASHDownloader(string id, IHttpClient http = null,
            BaseMediaProcessor mediaProcessor = null) : base(id, http, mediaProcessor)
        {

        }

        private static MultiSourceChunk CreateChunk(Uri mediaSegment, int streamIndex)
        {
            Log.Debug(streamIndex + "-Url: " + SensitiveDataRedactor.UrlForLog(mediaSegment?.ToString()));
            return new MultiSourceChunk
            {
                Uri = mediaSegment,
                ChunkState = ChunkState.Ready,
                Id = Guid.NewGuid().ToString(),
                Offset = 0,
                Size = -1,
                StreamIndex = streamIndex
            };
        }

        protected override void Init(string tempDir)
        {
        }

        protected override void OnContentTypeReceived(Chunk chunk, string contentType)
        {
        }

        protected override void RestoreState()
        {
            var state = DownloadStateIO.LoadMultiSourceDASHDownloadState(Id!);
            this._state = state;
            RestoreCredentials();
            RestoreTransferPolicy();




            try
            {
                Log.Debug("Restoring adaptive download chunks");

                if (!TransactedIO.ReadStream("chunks.db", state.TempDirectory, s =>
                {
                    _chunks = ChunkStateFromBytes(s);
                }))
                {
                    throw new FileNotFoundException(Path.Combine(state.TempDirectory, "chunks.db"));
                }



                var dashDir = _state.TempDirectory;
                var streamMap = _chunks.Select(c => new
                {
                    c.Id,
                    TempFilePath = Path.Combine(dashDir, (c.StreamIndex == 0 ? "1_" : "2_") + c.Id + FileHelper.GetFileName(c.Uri))
                }).ToDictionary(e => e.Id, e =>
                        e.TempFilePath);
                _chunkStreamMap = new SimpleStreamMap { StreamMap = streamMap };

            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error loading chunks");

                if (state.Demuxed)
                {
                    CreateChunks2(state, _chunks, _chunkStreamMap);
                }
                else
                {
                    CreateChunks1(state, _chunks, _chunkStreamMap);
                }
            }


            var count = 0;
            totalDownloadedBytes = 0;
            _chunks.ForEach(c =>
            {
                if (c.ChunkState == ChunkState.Finished) count++;
                if (c.Downloaded > 0) totalDownloadedBytes += c.Downloaded;
            });
            this.lastProgress = (count * 100) / _chunks.Count;
            ticksAtDownloadStartOrResume = Helpers.TickCount();
            Log.Debug("Already downloaded: " + count);
        }

        protected override void SaveState()
        {
            DownloadStateIO.Save((MultiSourceDASHDownloadState)_state);

            SaveChunkState();
        }


        private static string GuessContainerFormatFromPlaylist(List<Uri> segments)
        {
            var file = FileHelper.GetFileName(segments.Last());
            return Path.GetExtension(file);
        }

        private static void CreateChunks2(MultiSourceDASHDownloadState state, List<MultiSourceChunk> chunks, SimpleStreamMap chunkStreamMap)
        {
            var i = 0;
            if (state.Demuxed && state.VideoSegments != null && state.AudioSegments != null)
            {
                for (; i < Math.Min(state.AudioChunkCount, state.VideoChunkCount); i++)
                {
                    var chunk1 = CreateChunk(state.VideoSegments[i], 0);
                    chunks.Add(chunk1);
                    chunkStreamMap.StreamMap[chunk1.Id] = Path.Combine(state.TempDirectory, "1_" + chunk1.Id + FileHelper.GetFileName(chunk1.Uri));

                    var chunk2 = CreateChunk(state.AudioSegments[i], 1);
                    chunks.Add(chunk2);
                    chunkStreamMap.StreamMap[chunk2.Id] = Path.Combine(state.TempDirectory, "2_" + chunk2.Id + FileHelper.GetFileName(chunk2.Uri));
                }
                for (; i < state.VideoChunkCount; i++)
                {
                    var chunk = CreateChunk(state.VideoSegments[i], 0);
                    chunks.Add(chunk);
                    chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(state.TempDirectory, "1_" + chunk.Id + FileHelper.GetFileName(chunk.Uri));
                }
                for (; i < state.AudioChunkCount; i++)
                {
                    var chunk = CreateChunk(state.AudioSegments[i], 1);
                    chunks.Add(chunk);
                    chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(state.TempDirectory, "2_" + chunk.Id + FileHelper.GetFileName(chunk.Uri));
                }
            }
        }

        private static void CreateChunks1(MultiSourceDASHDownloadState state, List<MultiSourceChunk> chunks, SimpleStreamMap chunkStreamMap)
        {
            var i = 0;
            var segments = state.VideoSegments ?? state.AudioSegments;
            if (segments != null)
            {
                for (; i < state.VideoChunkCount; i++)
                {
                    var chunk = CreateChunk(segments[i], 0);
                    chunks.Add(chunk);
                    chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(state.TempDirectory, "1_" + chunk.Id + FileHelper.GetFileName(chunk.Uri));
                }
            }
        }
    }

    public class MultiSourceDASHDownloadState : MultiSourceDownloadState
    {
        public string? Url { get; set; }
        public List<Uri>? AudioSegments { get; set; }
        public List<Uri>? VideoSegments { get; set; }
    }
}
