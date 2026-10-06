using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;
using ADM.Core.Clients.Http;

namespace ADM.Core.Downloader.Adaptive
{
    public class HttpChunkDownloader
    {
        protected readonly Chunk _chunk;
        protected readonly IHttpClient _http;
        protected readonly CancelFlag _cancellationToken = new();
        protected readonly IChunkStreamMap _chunkStreamMap;
        protected readonly ICancelRequster _cancelRequster;
        protected readonly EventArgs EmptyArgs = EventArgs.Empty;
        protected long _lastUpdated;
        protected ChunkDownloadedEventArgs downloadedEventArgs;
        protected ManualResetEvent sleepHandle = new ManualResetEvent(false);
        protected Dictionary<string, List<string>> headers;
        protected string cookies;
        protected AuthenticationInfo? authentication;

        public event EventHandler<ChunkDownloadedEventArgs>? ChunkDataReceived;
        public event EventHandler<MimeTypeReceivedEventArgs>? MimeTypeReceived;

        public HttpChunkDownloader(
            Chunk chunk,
            IHttpClient http,
            Dictionary<string, List<string>> headers,
            string cookies,
            AuthenticationInfo? authentication,
            IChunkStreamMap chunkStreamMap,
            ICancelRequster cancelRequster)
        {
            _chunk = chunk;
            _http = http;
            _chunkStreamMap = chunkStreamMap;
            _cancelRequster = cancelRequster;
            _lastUpdated = Helpers.TickCount();
            downloadedEventArgs = new();

            this.cookies = cookies;
            this.headers = headers;
            this.authentication = authentication;
        }

        public bool TransientFailure { get; set; }

        public bool SkipWhenGone { get; set; }

        private const int GoneResponsesBeforeSkip = 3;

        protected virtual Stream PrepareOutStream()
        {
            var targetStream = new FileStream(_chunkStreamMap.GetStream(_chunk.Id),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, 1);

            targetStream.Seek(_chunk.Downloaded, SeekOrigin.Begin);
            return targetStream;
        }

        private Stream OpenOutStream()
        {
            try
            {
                return PrepareOutStream();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new StorageFailureException(ex);
            }
        }

        private sealed class StorageFailureException : Exception
        {
            public StorageFailureException(Exception inner) : base(inner.Message, inner) { }
        }

        private bool RangeMatchesRequest(HttpResponse response, long requestedStart)
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var start = response.ContentRangeStart;
                if (start < 0) return requestedStart == 0;
                return start == requestedStart;
            }
            return _chunk.Offset == 0;
        }

        private bool IsIncomplete(long completeLength, long declaredLength, long receivedNow)
        {
            if (_chunk.Size > 0) return _chunk.Downloaded < _chunk.Size;
            if (completeLength > 0) return _chunk.Offset + _chunk.Downloaded < completeLength;
            if (declaredLength > 0) return receivedNow < declaredLength;
            return false;
        }

        private void TrimOutStream(Stream targetStream)
        {
            try
            {
                if (targetStream.CanSeek && targetStream.Length > _chunk.Downloaded)
                {
                    targetStream.SetLength(_chunk.Downloaded);
                    targetStream.Seek(_chunk.Downloaded, SeekOrigin.Begin);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new StorageFailureException(ex);
            }
        }

        private void FlushOutStream(Stream targetStream)
        {
            try
            {
                targetStream.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new StorageFailureException(ex);
            }
        }

        private void ReconcileWithSavedFile()
        {
            try
            {
                var saved = new FileInfo(_chunkStreamMap.GetStream(_chunk.Id));
                var length = saved.Exists ? saved.Length : 0L;
                if (_chunk.Downloaded > length) _chunk.Downloaded = length;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The saved part could not be measured before continuing");
            }
        }

        private bool SkipMissingChunk()
        {
            try
            {
                using (new FileStream(_chunkStreamMap.GetStream(_chunk.Id), FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "An empty file could not be written for a skipped part");
                return false;
            }
            Log.Debug("A part of the live stream is no longer on the server and was skipped");
            _chunk.Size = -1;
            _chunk.Downloaded = 0;
            TransientFailure = false;
            _chunk.ChunkState = ChunkState.Finished;
            return true;
        }

        public void Download()
        {
            if (!_cancelRequster.RegisterThread(this)) return;
            var retryCount = 0;
            var rangeRejections = 0;
            var stalledRounds = 0;
            var goneResponses = 0;
            ReconcileWithSavedFile();
            var progressMark = _chunk.Downloaded;
#if NET35
            var buffer = new byte[32 * 1024];
#else
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(32 * 1024);
#endif
            try
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (_chunk.Size > 0 && _chunk.Downloaded >= _chunk.Size)
                        {
                            TransientFailure = false;
                            _chunk.ChunkState = ChunkState.Finished;
                            return;
                        }
                        Log.Debug("Creating request");
                        var requestedStart = _chunk.Offset + _chunk.Downloaded;
                        var request = this._http.CreateGetRequest(_chunk.Uri, this.headers, this.cookies, this.authentication);
                        if (_chunk.Size > 0)
                        {
                            request.AddRange(_chunk.Offset + _chunk.Downloaded, _chunk.Offset + _chunk.Size - 1);
                        }
                        else
                        {
                            request.AddRange(_chunk.Offset + _chunk.Downloaded);
                        }

                        Log.Debug("Sending request");
                        using var response = this._http.Send(request);
                        Log.Debug("Sent request");
                        _cancellationToken.ThrowIfCancellationRequested();
                        response.EnsureSuccessStatusCode();

                        if (response.StatusCode != HttpStatusCode.PartialContent && (response.StatusCode != HttpStatusCode.OK && _chunk.Downloaded + _chunk.Offset > 0))
                        {
                            _cancelRequster.CancelWithFatal(ErrorCode.InvalidResponse);
                            return;
                        }

                        if (!RangeMatchesRequest(response, requestedStart))
                        {
                            rangeRejections++;
                            if (rangeRejections > Config.Instance.MaxRetry)
                            {
                                _cancelRequster.CancelWithFatal(ErrorCode.InvalidResponse);
                                return;
                            }
                            throw new InvalidDataException("The server answered with a different range than the one requested");
                        }
                        rangeRejections = 0;

                        if (_chunk.Downloaded > 0 && response.StatusCode == HttpStatusCode.OK)
                        {
                            Log.Debug("Partial content non supported, discarding partially downloaded parts");
                            _chunk.Downloaded = 0;
                        }

                        var completeLength = response.StatusCode == HttpStatusCode.PartialContent && !response.Compressed ? response.ContentRangeLength : -1;
                        var declaredLength = response.StatusCode == HttpStatusCode.OK && !response.Compressed ? response.ContentLength : -1;

                        if (response.ContentType != null)
                        {
                            MimeTypeReceived?.Invoke(this, new MimeTypeReceivedEventArgs
                            {
                                MimeType = response.ContentType,
                                Chunk = _chunk
                            });
                        }
                        Log.Debug("Init download request");
                        var stream = response.GetResponseStream();
                        _cancellationToken.ThrowIfCancellationRequested();
                        using var sourceStream = stream;
                        using var targetStream = OpenOutStream();
                        TrimOutStream(targetStream);
                        var receivedNow = 0L;

                        while (!_cancellationToken.IsCancellationRequested)
                        {
                            var wanted = buffer.Length;
                            if (_chunk.Size > 0)
                            {
                                var missing = _chunk.Size - _chunk.Downloaded;
                                if (missing <= 0)
                                {
                                    FlushOutStream(targetStream);
                                    TransientFailure = false;
                                    _chunk.ChunkState = ChunkState.Finished;
                                    return;
                                }
                                if (missing < wanted) wanted = (int)missing;
                            }
                            int x = sourceStream.Read(buffer, 0, wanted);
                            _cancellationToken.ThrowIfCancellationRequested();
                            if (x == 0)
                            {
                                FlushOutStream(targetStream);
                                if (_chunk.Size > 0 && completeLength > 0 && _chunk.Offset + _chunk.Downloaded >= completeLength)
                                {
                                    Log.Debug("The resource ended before the declared range did, keeping what the server has");
                                    _chunk.Size = _chunk.Downloaded > 0 ? _chunk.Downloaded : -1;
                                }
                                else if (IsIncomplete(completeLength, declaredLength, receivedNow))
                                {
                                    if (_chunk.Downloaded <= progressMark)
                                    {
                                        stalledRounds++;
                                        if (stalledRounds > Config.Instance.MaxRetry)
                                        {
                                            _cancelRequster.CancelWithFatal(ErrorCode.MaxRetryFailed);
                                            return;
                                        }
                                        throw new EndOfStreamException("The response ended before the chunk was complete");
                                    }
                                    progressMark = _chunk.Downloaded;
                                    Log.Debug("The response ended before the chunk was complete, asking for the rest");
                                    break;
                                }
                                TransientFailure = false;
                                _chunk.ChunkState = ChunkState.Finished;
                                return;
                            }

                            try
                            {
                                targetStream.Write(buffer, 0, x);
                            }
                            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                            {
                                throw new StorageFailureException(ex);
                            }

                            _chunk.Downloaded += x;
                            receivedNow += x;
                            TransientFailure = false;
                            retryCount = 0;
                            downloadedEventArgs.Downloaded = x;
                            ChunkDataReceived?.Invoke(this, downloadedEventArgs);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Debug(e, "Error in DownloadAsync");
                        if (e is StorageFailureException)
                        {
                            _cancelRequster.CancelWithFatal(ErrorCode.DiskError);
                            return;
                        }
                        if (SkipWhenGone && e is HttpException gone && (gone.StatusCode == HttpStatusCode.NotFound || gone.StatusCode == HttpStatusCode.Gone))
                        {
                            goneResponses++;
                            if (goneResponses >= GoneResponsesBeforeSkip && SkipMissingChunk()) return;
                        }
                        TransientFailure = true;
                        retryCount++;
                        if (retryCount > Config.Instance.MaxRetry)
                        {
                            retryCount = 0;
                            _cancelRequster.NotifyTransientFailure();
                        }
                        else
                        {
                            if (!(e is OperationCanceledException))
                            {
                                sleep(Config.Instance.RetryDelay * 1000);
                            }
                        }
                    }
                }
            }
            finally
            {
#if !NET35
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
#endif
                Log.Debug("Finished request");
                _cancelRequster.UnRegisterThread(this);
            }
        }

        private void sleep(int interval)
        {
            sleepHandle.WaitOne(interval);
        }

        public void Cancel()
        {
            _cancellationToken.Cancel();
            sleepHandle.Set();
        }
    }

    public class ChunkDownloadedEventArgs : EventArgs
    {
        public long Downloaded
        {
            get; set;
        }
    }

    public class MimeTypeReceivedEventArgs : EventArgs
    {
        public string? MimeType
        {
            get; set;
        }

        public Chunk? Chunk { get; set; }
    }
}
