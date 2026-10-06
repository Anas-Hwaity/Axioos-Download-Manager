using ADM.Core.MediaParser.Hls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using TraceLog;
using ADM.Core.Clients.Http;
using ADM.Core;
using ADM.Core.MediaProcessor;
using ADM.Core.Util;
using ADM.Core.IO;

namespace ADM.Core.Downloader.Adaptive.Hls
{
    public class MultiSourceHLSDownloader : MultiSourceDownloaderBase
    {
        private CountdownLatch? initLatch;
        private const string LiveMarkerName = "live-capture";
        private const int LiveRefreshFailureLimit = 5;
        private const int LiveIdleLimitMilliseconds = 30000;
        private volatile bool liveCapture;
        private volatile bool liveArmed;
        private double liveTargetDuration;
        private readonly Dictionary<int, long> lastMediaSequence = new Dictionary<int, long>();
        private readonly Dictionary<int, string> lastInitializationKey = new Dictionary<int, string>();
        private readonly ManualResetEvent liveWait = new ManualResetEvent(false);
        public override string Type => "Hls";
        public override Uri PrimaryUrl
        {
            get
            {
                var state = _state as MultiSourceHLSDownloadState;
                if (state == null)
                {
                    return null;
                }
                if (state.NonMuxedVideoPlaylistUrl != null) return state.NonMuxedVideoPlaylistUrl;
                if (state.MuxedPlaylistUrl != null) return state.MuxedPlaylistUrl;
                return null;
            }
        }
        public MultiSourceHLSDownloader(MultiSourceHLSDownloadInfo info, IHttpClient http = null,
            BaseMediaProcessor mediaProcessor = null,
            AuthenticationInfo? authentication = null, ProxyInfo? proxy = null) : base(info, http, mediaProcessor)
        {
            var state = new MultiSourceHLSDownloadState
            {
                Id = base.Id,
                Cookies = info.Cookies,
                Headers = info.Headers,
                Authentication = authentication,
                Proxy = proxy,
                TempDirectory = Path.Combine(Config.Instance.TempDir, Id)
            };

            if (state.Authentication == null)
            {
                state.Authentication = Helpers.GetAuthenticationInfoFromConfig(new Uri(info.VideoUri ?? info.AudioUri));
            }

            Log.Debug("Video playlist url: " + SensitiveDataRedactor.UrlForLog(info.VideoUri?.ToString()) +
                " Audio playlist url: " + SensitiveDataRedactor.UrlForLog(info.AudioUri?.ToString()));
            if (info.VideoUri != null && info.AudioUri != null)
            {
                state.NonMuxedVideoPlaylistUrl = new Uri(info.VideoUri);
                state.NonMuxedAudioPlaylistUrl = new Uri(info.AudioUri);
                state.Demuxed = true;
            }
            else
            {
                state.MuxedPlaylistUrl = new Uri(info.VideoUri);
                state.Demuxed = false;
            }

            this._state = state;
            this.TargetFileName = FileHelper.SanitizeFileName(info.File);
        }

        public MultiSourceHLSDownloader(string id, IHttpClient http = null, BaseMediaProcessor mediaProcessor = null) : base(id, http, mediaProcessor)
        {
        }

        public override void Stop()
        {
            this.initLatch?.Break();
            ReleaseLiveWait();
            base.Stop();
        }

        public bool IsLiveCapture => liveCapture && liveArmed;

        public bool IsFinishingLiveCapture => FinishRequested && !IsCancelled;

        protected override bool SkipsMissingChunks => liveArmed;

        public bool FinishLiveCapture()
        {
            if (!liveCapture || !liveArmed || IsCancelled || FinishRequested) return false;
            Log.Debug("Finishing the live recording with what was captured so far");
            RequestFinish();
            ReleaseLiveWait();
            return true;
        }

        private void ReleaseLiveWait()
        {
            try
            {
                liveWait.Set();
            }
            catch (ObjectDisposedException ex)
            {
                Log.Debug(ex, "The live refresh wait was already released");
            }
        }

        private static int StreamIndexOf(string playlistKey)
        {
            return playlistKey == "audio" ? 1 : 0;
        }

        private static string InitializationKey(HlsMediaSegment segment)
        {
            return segment.Url + "|" + segment.ByteRange.Key + "|" + segment.ByteRange.Value + "|" + segment.HasByteRange;
        }

        private void TrackLiveState(Dictionary<string, HlsPlaylist> playlists)
        {
            var live = false;
            liveTargetDuration = 0.0;
            foreach (var pair in playlists)
            {
                var streamIndex = StreamIndexOf(pair.Key);
                var playlist = pair.Value;
                var segments = playlist.MediaSegments;
                if (segments == null) continue;
                if (playlist.IsEndless) live = true;
                if (playlist.TargetDuration > liveTargetDuration) liveTargetDuration = playlist.TargetDuration;
                foreach (var segment in segments)
                {
                    if (segment.IsInitialization) lastInitializationKey[streamIndex] = InitializationKey(segment);
                    else lastMediaSequence[streamIndex] = segment.MediaSequence;
                }
            }
            liveCapture = live;
        }

        private void ArmLiveCapture()
        {
            liveArmed = true;
            try
            {
                File.WriteAllText(Path.Combine(this._state.TempDirectory, LiveMarkerName), "live");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The live recording marker could not be written");
            }
        }

        private void RemoveLiveMarker()
        {
            try
            {
                File.Delete(Path.Combine(this._state.TempDirectory, LiveMarkerName));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "The live recording marker could not be removed");
            }
        }

        private string? FetchPlaylistText(Uri uri)
        {
            try
            {
                var request = _http.CreateGetRequest(uri, this._state.Headers, this._state.Cookies, this._state.Authentication);
                using var response = _http.Send(request);
                response.EnsureSuccessStatusCode();
                return response.ReadAsString(this._cancellationTokenSource);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The live playlist could not be refreshed");
                return null;
            }
        }

        private Dictionary<string, HlsPlaylist>? RefreshPlaylists()
        {
            var state = this._state as MultiSourceHLSDownloadState;
            if (state == null) return null;
            var sources = new List<KeyValuePair<string, Uri>>();
            if (state.Demuxed)
            {
                sources.Add(new KeyValuePair<string, Uri>("video", state.NonMuxedVideoPlaylistUrl));
                sources.Add(new KeyValuePair<string, Uri>("audio", state.NonMuxedAudioPlaylistUrl));
            }
            else
            {
                sources.Add(new KeyValuePair<string, Uri>("muxed", state.MuxedPlaylistUrl));
            }
            var playlists = new Dictionary<string, HlsPlaylist>();
            foreach (var source in sources)
            {
                var text = FetchPlaylistText(source.Value);
                if (text == null) return null;
                HlsPlaylist? playlist;
                try
                {
                    playlist = HlsParser.ParseMediaSegments(text.Split('\n'), source.Value.ToString());
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "The refreshed live playlist could not be read");
                    return null;
                }
                if (playlist == null) return null;
                playlists[source.Key] = playlist;
            }
            return playlists;
        }

        private int AppendNewSegments(Dictionary<string, HlsPlaylist> playlists)
        {
            var added = 0;
            try
            {
                rwLock.EnterWriteLock();
                foreach (var pair in playlists)
                {
                    var streamIndex = StreamIndexOf(pair.Key);
                    var segments = pair.Value.MediaSegments;
                    if (segments == null) continue;
                    var prefix = streamIndex == 0 ? "1_" : "2_";
                    if (!lastMediaSequence.TryGetValue(streamIndex, out var lastSequence)) lastSequence = -1;
                    lastInitializationKey.TryGetValue(streamIndex, out string? lastKey);
                    HlsMediaSegment? pendingInitialization = null;
                    foreach (var segment in segments)
                    {
                        if (segment.IsInitialization)
                        {
                            pendingInitialization = InitializationKey(segment) == lastKey ? null : segment;
                            continue;
                        }
                        if (segment.MediaSequence <= lastSequence) continue;
                        if (pendingInitialization != null)
                        {
                            AddLiveChunk(pendingInitialization, streamIndex, prefix);
                            lastKey = InitializationKey(pendingInitialization);
                            pendingInitialization = null;
                        }
                        AddLiveChunk(segment, streamIndex, prefix);
                        lastSequence = segment.MediaSequence;
                        added++;
                    }
                    lastMediaSequence[streamIndex] = lastSequence;
                    if (lastKey != null) lastInitializationKey[streamIndex] = lastKey;
                    if (streamIndex == 0) this._state.VideoChunkCount = _chunks.Count(chunk => chunk.StreamIndex == 0);
                    else this._state.AudioChunkCount = _chunks.Count(chunk => chunk.StreamIndex == 1);
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
            return added;
        }

        private void AddLiveChunk(HlsMediaSegment segment, int streamIndex, string prefix)
        {
            var chunk = CreateChunk(segment, streamIndex);
            _chunks.Add(chunk);
            _chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(this._state.TempDirectory, prefix + chunk.Id + FileHelper.GetFileName(chunk.Uri));
        }

        protected override bool ExtendChunks()
        {
            if (!liveCapture) return false;
            if (!liveArmed) ArmLiveCapture();
            var idleSince = Helpers.TickCount();
            var failures = 0;
            var waitMilliseconds = (int)Math.Max(1000.0, Math.Min(15000.0, liveTargetDuration > 0 ? liveTargetDuration * 1000.0 : 5000.0));
            var idleLimit = Math.Max(LiveIdleLimitMilliseconds, 4 * waitMilliseconds);
            while (!this._cancellationTokenSource.IsCancellationRequested && !FinishRequested)
            {
                liveWait.WaitOne(waitMilliseconds);
                if (this._cancellationTokenSource.IsCancellationRequested || FinishRequested) break;
                var playlists = RefreshPlaylists();
                if (this._cancellationTokenSource.IsCancellationRequested || FinishRequested) break;
                if (playlists == null)
                {
                    failures++;
                    if (failures >= LiveRefreshFailureLimit)
                    {
                        Log.Debug("The live playlist stopped answering, saving what was recorded");
                        liveCapture = false;
                        return false;
                    }
                    continue;
                }
                failures = 0;
                var added = AppendNewSegments(playlists);
                var ended = playlists.Values.All(playlist => !playlist.IsEndless);
                if (ended)
                {
                    liveCapture = false;
                    RemoveLiveMarker();
                }
                if (added > 0) return true;
                if (ended) return false;
                if (Helpers.TickCount() - idleSince > idleLimit)
                {
                    Log.Debug("The live playlist stopped growing, saving what was recorded");
                    liveCapture = false;
                    return false;
                }
            }
            return false;
        }

        private Dictionary<string, HlsPlaylist> ProbeTarget()
        {
            bool GetHlsManifest(Uri uri, out HttpStatusCode statusCode, out string? text)
            {
                statusCode = HttpStatusCode.OK;
                text = null;

                try
                {
                    var request = _http.CreateGetRequest(uri, this._state.Headers, this._state.Cookies, this._state.Authentication);
                    using var response = _http.Send(request);
                    this._cancellationTokenSource.ThrowIfCancellationRequested();
                    statusCode = response.StatusCode;
                    response.EnsureSuccessStatusCode();
                    text = response.ReadAsString(this._cancellationTokenSource);
                    this._cancellationTokenSource.ThrowIfCancellationRequested();
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                    return false;
                }
            }

            try
            {
                var state = this._state as MultiSourceHLSDownloadState;
                var results = new string?[state.Demuxed ? 2 : 1];
                var status = new HttpStatusCode[state.Demuxed ? 2 : 1];
                var success = true;
                if (state.Demuxed)
                {
                    initLatch = new CountdownLatch(2);
                    var t1 = new Thread(() =>
                      {
                          var res = GetHlsManifest(state.NonMuxedVideoPlaylistUrl,
                              out HttpStatusCode statusCode,
                              out string? text);
                          results[0] = text;
                          status[0] = statusCode;
                          if (!res)
                          {
                              success = false;
                          }
                          initLatch.CountDown();
                      });
                    t1.Start();

                    var t2 = new Thread(() =>
                      {
                          var res = GetHlsManifest(state.NonMuxedAudioPlaylistUrl,
                              out HttpStatusCode statusCode,
                              out string? text);
                          results[1] = text;
                          status[1] = statusCode;
                          if (!res)
                          {
                              success = false;
                          }
                          initLatch.CountDown();
                      });
                    t2.Start();
                }
                else
                {
                    initLatch = new CountdownLatch(1);

                    new Thread(() =>
                    {
                        var res = GetHlsManifest(state.MuxedPlaylistUrl,
                            out HttpStatusCode statusCode,
                            out string? text);
                        results[0] = text;
                        status[0] = statusCode;
                        if (!res)
                        {
                            success = false;
                        }
                        initLatch.CountDown();
                    }).Start();
                }

                initLatch.Wait();
                this._cancellationTokenSource.ThrowIfCancellationRequested();
                HttpStatusCode? FindErrorStatus()
                {
                    if (status.Length == 2)
                    {
                        if (status[0] != HttpStatusCode.OK) return status[0];
                        if (status[1] != HttpStatusCode.OK) return status[1];
                        return null;
                    }
                    else
                    {
                        if (status[0] != HttpStatusCode.OK) return status[0];
                        return null;
                    }
                }

                if (!success)
                {
                    var statusCode = FindErrorStatus();
                    if (statusCode.HasValue)
                    {
                        throw new Exception($"Invalid response code: {statusCode.Value}",
                            new HttpException(statusCode.Value.ToString(), null, statusCode.Value));
                    }
                    throw new Exception("Unable to download HLS manifest");
                }

                var playlists = new Dictionary<string, HlsPlaylist>();
                if (state.Demuxed)
                {
                    playlists["video"] = HlsParser.ParseMediaSegments(results[0]!.Split('\n'), state.NonMuxedVideoPlaylistUrl.ToString());
                    playlists["audio"] = HlsParser.ParseMediaSegments(results[1]!.Split('\n'), state.NonMuxedAudioPlaylistUrl.ToString());

                    this._state.VideoContainerFormat = GuessContainerFormatFromPlaylist(playlists["video"]);
                    this._state.AudioContainerFormat = GuessContainerFormatFromPlaylist(playlists["audio"]);

                    var ext = FileExtensionHelper.GuessContainerFormatFromSegmentExtension(
                            this._state.VideoContainerFormat, this._state.AudioContainerFormat);
                    TargetFileName = Path.GetFileNameWithoutExtension(TargetFileName ?? "video")
                            + ext;

                    Log.Debug($"Guessed Demuxed formats - VideoContainerFormat: {this._state.VideoContainerFormat} AudioContainerFormat: {this._state.AudioContainerFormat}");
                    Log.Debug($"Guessed media extension: {ext}");
                }
                else
                {
                    playlists["muxed"] = HlsParser.ParseMediaSegments(results[0]!.Split('\n'),
                        state.MuxedPlaylistUrl.ToString());
                    _state.Duration = playlists["muxed"].TotalDuration;
                    this._state.VideoContainerFormat = GuessContainerFormatFromPlaylist(playlists["muxed"]);
                    var ext = FileExtensionHelper.GuessContainerFormatFromSegmentExtension(
                            this._state.VideoContainerFormat.ToLowerInvariant());
                    TargetFileName = Path.GetFileNameWithoutExtension(TargetFileName ?? "video")
                                + ext;

                    Log.Debug($"Guessed Muxed format - VideoContainerFormat: {this._state.VideoContainerFormat}");
                    Log.Debug($"Guessed media extension: {ext}");
                }

                if (string.IsNullOrEmpty(this.TargetDir))
                {
                    this.TargetDir = FileHelper.GetDownloadFolderByFileName(this.TargetFileName);
                }

                return playlists;
            }
            catch { throw; }
        }

        protected override void Init(string tempDir)
        {
            var playlists = ProbeTarget();
            _state.FileSize = -1;
            var i = 0;

            if (this._state.Demuxed)
            {
                var video = playlists["video"];
                var audio = playlists["audio"];
                _chunks = new List<MultiSourceChunk>(video.MediaSegments.Count + audio.MediaSegments.Count);

                this._state.AudioChunkCount = audio.MediaSegments.Count;
                this._state.VideoChunkCount = video.MediaSegments.Count;
                this._state.Duration = Math.Max(playlists["video"].TotalDuration, playlists["audio"].TotalDuration);
                this._state.AudioContainerFormat = Path.GetExtension(FileHelper.GetFileName(audio.MediaSegments.Last().Url));
                this._state.VideoContainerFormat = Path.GetExtension(FileHelper.GetFileName(video.MediaSegments.Last().Url));

                for (; i < Math.Min(this._state.AudioChunkCount, this._state.VideoChunkCount); i++)
                {
                    var chunk1 = CreateChunk(video.MediaSegments[i], 0);
                    _chunks.Add(chunk1);
                    _chunkStreamMap.StreamMap[chunk1.Id] = Path.Combine(tempDir, "1_" + chunk1.Id + FileHelper.GetFileName(chunk1.Uri));

                    var chunk2 = CreateChunk(audio.MediaSegments[i], 1);
                    _chunks.Add(chunk2);
                    _chunkStreamMap.StreamMap[chunk2.Id] = Path.Combine(tempDir, "2_" + chunk2.Id + FileHelper.GetFileName(chunk2.Uri));
                }
                for (; i < this._state.VideoChunkCount; i++)
                {
                    var chunk = CreateChunk(video.MediaSegments[i], 0);
                    _chunks.Add(chunk);
                    _chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(tempDir, "1_" + chunk.Id + FileHelper.GetFileName(chunk.Uri));
                }
                for (; i < this._state.AudioChunkCount; i++)
                {
                    var chunk = CreateChunk(audio.MediaSegments[i], 1);
                    _chunks.Add(chunk);
                    _chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(tempDir, "2_" + chunk.Id + FileHelper.GetFileName(chunk.Uri));
                }
            }
            else
            {
                var playlist = playlists["muxed"];
                _chunks = new List<MultiSourceChunk>(playlist.MediaSegments.Count);
                this._state.VideoChunkCount = playlist.MediaSegments.Count;
                this._state.Duration = playlist.TotalDuration;

                for (; i < this._state.VideoChunkCount; i++)
                {
                    var chunk = CreateChunk(playlist.MediaSegments[i], 0);
                    _chunks.Add(chunk);
                    _chunkStreamMap.StreamMap[chunk.Id] = Path.Combine(tempDir, "1_" + chunk.Id + FileHelper.GetFileName(chunk.Uri));
                }
            }
            TrackLiveState(playlists);
        }

        private MultiSourceChunk CreateChunk(HlsMediaSegment mediaSegment, int streamIndex)
        {
            Log.Debug(streamIndex + "-Url: " + SensitiveDataRedactor.UrlForLog(mediaSegment.Url?.ToString()));
            return new MultiSourceChunk
            {
                Uri = mediaSegment.Url,
                ChunkState = ChunkState.Ready,
                Id = Guid.NewGuid().ToString(),
                Offset = mediaSegment.HasByteRange ? mediaSegment.ByteRange.Key : 0,
                Size = mediaSegment.HasByteRange && mediaSegment.ByteRange.Value > 0 ? mediaSegment.ByteRange.Value : -1,
                Duration = mediaSegment.Duration,
                StreamIndex = streamIndex
            };
        }

        protected override void RestoreState()
        {
            var state = DownloadStateIO.LoadMultiSourceHLSDownloadState(Id!);
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

                var hlsDir = state.TempDirectory;

                var streamMap = _chunks.Select(c => new
                {
                    c.Id,
                    TempFilePath = Path.Combine(hlsDir, (c.StreamIndex == 0 ? "1_" : "2_") + c.Id + FileHelper.GetFileName(c.Uri))
                }).ToDictionary(e => e.Id, e => e.TempFilePath);
                _chunkStreamMap = new SimpleStreamMap { StreamMap = streamMap };

                if (File.Exists(Path.Combine(hlsDir, LiveMarkerName)))
                {
                    var captured = LeadingFinishedChunks(_chunks, new List<MultiSourceChunk>());
                    if (captured.Count > 0 && captured.Count < _chunks.Count)
                    {
                        Log.Debug("Resuming a live recording, saving the " + captured.Count + " parts that were captured");
                        _chunks = captured;
                    }
                }

                var count = 0;
                totalDownloadedBytes = 0;
                _chunks.ForEach(c =>
                {
                    if (c.ChunkState == ChunkState.Finished) count++;
                    if (c.Downloaded > 0) totalDownloadedBytes += c.Downloaded;
                });
                ticksAtDownloadStartOrResume = Helpers.TickCount();
                this.lastProgress = (count * 100) / _chunks.Count;
                Log.Debug("Already downloaded: " + count + " Total: " + _chunks.Count);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Chunk restore failed");
                _chunks = null!;
            }
        }

        protected override void SaveState()
        {
            DownloadStateIO.Save((MultiSourceHLSDownloadState)this._state);
        }

        protected override void OnContentTypeReceived(Chunk chunk, string contentType)
        {
        }

        private static string GuessContainerFormatFromPlaylist(HlsPlaylist playlist)
        {
            var file = FileHelper.GetFileName(playlist.MediaSegments.Last().Url);
            return Path.GetExtension(file).ToLowerInvariant();
        }
    }

    public class MultiSourceHLSDownloadState : MultiSourceDownloadState
    {
        public Uri MuxedPlaylistUrl, NonMuxedAudioPlaylistUrl, NonMuxedVideoPlaylistUrl;
    }
}
