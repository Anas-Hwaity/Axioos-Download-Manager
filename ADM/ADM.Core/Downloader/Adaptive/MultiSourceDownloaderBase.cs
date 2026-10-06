using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TraceLog;
using ADM.Core;
using ADM.Core.MediaProcessor;
using ADM.Core.Util;
using ADM.Core.Clients.Http;
using ADM.Core.IO;
using ADM.Core.Telemetry;
#if NET35
using ADM.Compatibility;
#endif
using System.Text;

namespace ADM.Core.Downloader.Adaptive
{
    public abstract class MultiSourceDownloaderBase : IBaseDownloader, IDownloadTelemetrySource
    {
        protected IHttpClient _http;
        protected MultiSourceDownloadState _state;
        protected List<MultiSourceChunk> _chunks;
        protected CancelFlag _cancellationTokenSource;
        protected CancelFlag _cancellationTokenSourceStateSaver;
        protected SimpleStreamMap _chunkStreamMap;
        protected ICancelRequster _cancelRequestor;
        protected FileNameFetchMode _fileNameFetchMode = FileNameFetchMode.FileNameAndExtension;
        protected long lastUpdated = Helpers.TickCount();
        protected readonly ProgressResultEventArgs progressResult;
        protected SpeedLimiter speedLimiter = new();
        protected CountdownLatch? countdownLatch;
        public bool IsCancelled => _cancellationTokenSource.IsCancellationRequested;
        public string Id { get; private set; }
        public virtual long FileSize => this._state.FileSize;
        public virtual double Duration => this._state.Duration;
        protected ReaderWriterLockSlim rwLock = new(LockRecursionPolicy.SupportsRecursion);
        public ReaderWriterLockSlim Lock => this.rwLock;
        public FileNameFetchMode FileNameFetchMode
        {
            get { return _fileNameFetchMode; }
            set { _fileNameFetchMode = value; }
        }
        public virtual string TargetFile => Path.Combine(TargetDir, TargetFileName);
        public virtual string TargetFileName { get; set; }
        public virtual string TargetDir { get; set; }
        public virtual string Type => "N/A";
        public virtual Uri PrimaryUrl => null;

        public int SpeedLimit => _state?.SpeedLimit ?? 0;

        public bool EnableSpeedLimit => _state?.SpeedLimit > 0;

        public virtual event EventHandler Probed;
        public virtual event EventHandler Finished;
        public virtual event EventHandler Started;
        public virtual event EventHandler<ProgressResultEventArgs> ProgressChanged;
        public virtual event EventHandler Cancelled;
        public virtual event EventHandler<DownloadFailedEventArgs> Failed;
        public virtual event EventHandler<ProgressResultEventArgs> AssembingProgressChanged;
        protected BaseMediaProcessor mediaProcessor;
        protected long totalDownloadedBytes = 0L;
        protected long downloadedBytesSinceStartOrResume = 0L;
        protected int lastProgress = 0;
        protected long lastDownloaded = 0;
        protected long ticksAtDownloadStartOrResume = 0L;
        private bool stopRequested = false;
        private bool assembled;
        private string? partialOutput;
        private string? reservedOutput;
        private volatile bool finishRequested;

        protected bool FinishRequested => finishRequested;

        protected virtual bool ExtendChunks()
        {
            return false;
        }

        protected virtual bool SkipsMissingChunks => false;

        protected static List<MultiSourceChunk> LeadingFinishedChunks(List<MultiSourceChunk> chunks, List<MultiSourceChunk> dropped)
        {
            var kept = new List<MultiSourceChunk>(chunks.Count);
            var closedStreams = new HashSet<int>();
            foreach (var chunk in chunks)
            {
                if (closedStreams.Contains(chunk.StreamIndex))
                {
                    dropped.Add(chunk);
                }
                else if (chunk.ChunkState != ChunkState.Finished)
                {
                    closedStreams.Add(chunk.StreamIndex);
                    dropped.Add(chunk);
                }
                else
                {
                    kept.Add(chunk);
                }
            }
            return kept;
        }

        protected void RequestFinish()
        {
            finishRequested = true;
            _cancelRequestor.CancelAll();
            try
            {
                this.countdownLatch?.Break();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The chunk wait could not be released for finishing");
            }
        }

        private void DropUnfinishedChunks()
        {
            var dropped = new List<MultiSourceChunk>();
            try
            {
                rwLock.EnterWriteLock();
                var kept = LeadingFinishedChunks(_chunks, dropped);
                _chunks.Clear();
                _chunks.AddRange(kept);
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
            foreach (var chunk in dropped)
            {
                try
                {
                    File.Delete(_chunkStreamMap.GetStream(chunk.Id));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "An unfinished part of a stopped recording could not be removed");
                }
            }
            if (_chunks.Count == 0) throw new OperationCanceledException();
            SaveChunkState();
        }

        private void RefetchDamagedChunks()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (_cancellationTokenSource.IsCancellationRequested || finishRequested) return;
                if (!MarkDamagedChunks()) return;
                DownloadChunks();
            }
        }

        private bool MarkDamagedChunks()
        {
            var damaged = false;
            foreach (var chunk in _chunks)
            {
                if (chunk.Size <= 0) continue;
                var file = new FileInfo(_chunkStreamMap.GetStream(chunk.Id));
                var length = file.Exists ? file.Length : 0L;
                if (length == chunk.Size) continue;
                Log.Debug("A saved part has " + length + " bytes but should have " + chunk.Size + ", it will be fetched again");
                chunk.Downloaded = length < chunk.Size ? length : 0L;
                chunk.ChunkState = ChunkState.Ready;
                damaged = true;
            }
            if (damaged) SaveChunkState();
            return damaged;
        }
        private int? configuredMaxConnections;

        private AuthenticationInfo? resumeAuthentication;

        public void UseCredentials(AuthenticationInfo? authentication)
        {
            resumeAuthentication = authentication;
        }

        protected void RestoreCredentials()
        {
            if (_state == null || _state.Authentication != null) return;
            if (resumeAuthentication != null)
            {
                _state.Authentication = resumeAuthentication;
                return;
            }
            try
            {
                var url = PrimaryUrl;
                if (url != null) _state.Authentication = Helpers.GetAuthenticationInfoFromConfig(url);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Saved credentials could not be looked up");
            }
        }

        private const int TelemetryLockWaitMilliseconds = 40;
        private volatile DownloadTransferTelemetryProjection? lastTransferTelemetry;

        public DownloadTransferTelemetryProjection CaptureTransferTelemetry()
        {
            if (!rwLock.TryEnterReadLock(TelemetryLockWaitMilliseconds))
            {
                return lastTransferTelemetry ??
                    new DownloadTransferTelemetryProjection(DownloadResumeCapability.Unknown, 0, Array.Empty<DownloadRangeTelemetry>());
            }
            try
            {
                var ranges = _chunks.Select(chunk => new DownloadRangeTelemetry(
                    chunk.Offset,
                    chunk.Size > 0 ? chunk.Offset + chunk.Size - 1 : chunk.Offset,
                    chunk.Downloaded,
                    chunk.ChunkState.ToString())).ToList();
                var active = _chunks.Count(chunk => chunk.ChunkState == ChunkState.InProgress);
                var capability = _chunks.Count == 0 ? DownloadResumeCapability.Unknown : DownloadResumeCapability.Yes;
                var projection = new DownloadTransferTelemetryProjection(capability, active, ranges);
                lastTransferTelemetry = projection;
                return projection;
            }
            finally { rwLock.ExitReadLock(); }
        }

        public MultiSourceDownloaderBase(MultiSourceDownloadInfo info,
            IHttpClient? http = null,
            BaseMediaProcessor? mediaProcessor = null)
        {
            Id = Guid.NewGuid().ToString();

            _cancellationTokenSource = new();
            _cancellationTokenSourceStateSaver = new();

            progressResult = new ProgressResultEventArgs();

            this.mediaProcessor = mediaProcessor;
            this._http = http;

            _cancelRequestor = new CancelRequestor(_cancellationTokenSource);
            _chunks = new List<MultiSourceChunk>();
            _chunkStreamMap = new SimpleStreamMap
            {
                StreamMap = new Dictionary<string, string>()
            };
        }

        public MultiSourceDownloaderBase(string id,
            IHttpClient? http = null,
            BaseMediaProcessor? mediaProcessor = null)
        {
            Id = id;

            _cancellationTokenSource = new();
            _cancellationTokenSourceStateSaver = new();
            progressResult = new ProgressResultEventArgs();
            this.mediaProcessor = mediaProcessor;
            this._http = http;
            _cancelRequestor = new CancelRequestor(_cancellationTokenSource);
            _chunks = new List<MultiSourceChunk>();
            _chunkStreamMap = new SimpleStreamMap
            {
                StreamMap = new Dictionary<string, string>()
            };
        }

        protected abstract void SaveState();
        protected abstract void RestoreState();
        protected abstract void Init(string tempDir);
        protected abstract void OnContentTypeReceived(Chunk chunk, string contentType);

        public virtual void Start()
        {
            Start(true);
        }

        private void Start(bool start)
        {
            new Thread(() =>
            {
                Directory.CreateDirectory(_state.TempDirectory);
                ticksAtDownloadStartOrResume = Helpers.TickCount();
                SaveState();
                if (start)
                {
                    Started?.Invoke(this, EventArgs.Empty);
                    Download();
                }
            }).Start();
        }

        public void SaveForLater()
        {
            Start(false);
        }

        public virtual void Stop()
        {
            if (stopRequested) return;
            stopRequested = true;
            _cancellationTokenSourceStateSaver.Cancel();
            _cancellationTokenSource.Cancel();
            _cancelRequestor.CancelAll();
            speedLimiter.WakeIfSleeping();
            try { this.countdownLatch?.Break(); } catch { }
            try
            {
                _http?.Dispose();
                if (_cancelRequestor.WaitForQuiescence(5000))
                {
                    SaveChunkState();
                }
                else
                {
                    Log.Debug("Adaptive workers did not quiesce before stop checkpoint deadline");
                }
                SaveSpeedLimitWhenFree();
                Log.Debug("Stopped");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error while stopping adaptive download");
            }
        }

        public virtual void Resume()
        {
            new Thread(() =>
            {
                try
                {
                    Started?.Invoke(this, EventArgs.Empty);
                    RestoreState();
                    Directory.CreateDirectory(_state.TempDirectory);

                    if (_chunks == null)
                    {
                        Log.Debug("Chunk restore failed");
                        Download();
                        return;
                    }
                    this._http ??= HttpClientFactory.NewHttpClient(_state.Proxy ?? Config.Instance.Proxy);
                    this._http.Timeout = TimeSpan.FromSeconds(Config.Instance.NetworkTimeout);

                    DownloadChunks();
                    RefetchDamagedChunks();

                    this._cancellationTokenSource.ThrowIfCancellationRequested();

                    Assemble();
                    EnsureAssembled();
                    OnComplete();
                }
                catch (OperationCanceledException ex)
                {
                    Log.Debug(ex, ex.Message);
                    ReportStopped();
                }
                catch (FileNotFoundException ex)
                {
                    Log.Debug(ex, ex.Message);
                    OnFailed(new DownloadFailedEventArgs(ErrorCode.FFmpegNotFound));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                    if (ex.InnerException is HttpException he)
                    {
                        OnFailed(new DownloadFailedEventArgs(ErrorCode.InvalidResponse));
                    }
                    else
                    {
                        OnFailed(new DownloadFailedEventArgs(
                            ex is DownloadException de ? de.ErrorCode : ErrorCode.Generic));
                    }
                }
            }).Start();
        }

        protected virtual void SaveChunkState()
        {
            if (_chunks == null) return;
            try
            {
                rwLock.EnterWriteLock();
                TransactedIO.WriteStream("chunks.db", _state.TempDirectory, ChunkStateToBytes);
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        private void Download()
        {
            try
            {
                this._http ??= HttpClientFactory.NewHttpClient(_state.Proxy ?? Config.Instance.Proxy);
                this._http.Timeout = TimeSpan.FromSeconds(Config.Instance.NetworkTimeout);

                Directory.CreateDirectory(_state.TempDirectory);

                Init(_state.TempDirectory);
                SaveState();
                OnProbe();
                DownloadChunks();
                while (!_cancellationTokenSource.IsCancellationRequested && !finishRequested && ExtendChunks())
                {
                    DownloadChunks();
                }

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (finishRequested) DropUnfinishedChunks();
                RefetchDamagedChunks();

                Assemble();
                EnsureAssembled();
                OnComplete();
            }
            catch (OperationCanceledException ex)
            {
                Log.Debug(ex, ex.Message);
                ReportStopped();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
                if (ex.InnerException is HttpException)
                {
                    var he = ex.InnerException as HttpException;
                    OnFailed(new DownloadFailedEventArgs(ErrorCode.InvalidResponse));
                }
                else
                {
                    OnFailed(new DownloadFailedEventArgs(
                        ex is DownloadException de ? de.ErrorCode : ErrorCode.Generic));
                }
            }
        }

        private void ReportStopped()
        {
            var error = this._cancelRequestor.Error;
            if (error != ErrorCode.None && !stopRequested)
            {
                OnFailed(new DownloadFailedEventArgs(error));
                return;
            }
            OnCancelled();
        }

        private void EnsureAssembled()
        {
            if (assembled) return;
            DiscardPartialOutput();
            throw new OperationCanceledException();
        }

        private void DiscardPartialOutput()
        {
            var staging = partialOutput;
            var reserved = reservedOutput;
            partialOutput = null;
            reservedOutput = null;
            OutputFileStaging.Discard(staging, reserved);
            if (reserved != null) OutputFileStaging.ForgetReservation(_state?.TempDirectory);
        }

        private void ReserveOutputNameIfRenaming()
        {
            if (Config.Instance.FileConflictResolution != FileConflictResolution.AutoRename) return;
            this.TargetFileName = OutputFileStaging.ReserveUniqueFileName(this.TargetFileName, this.TargetDir, _state?.TempDirectory);
            reservedOutput = TargetFile;
        }

        private void CommitOutput(string staging)
        {
            OutputFileStaging.Commit(staging, TargetFile);
            partialOutput = null;
            reservedOutput = null;
        }

        protected void OnProbe()
        {
            var probeEventHandler = Probed;
            probeEventHandler?.Invoke(this, EventArgs.Empty);
        }

        private void DownloadChunkRange(int startIndex, int endIndex, CountdownLatch latch)
        {
            Log.Debug("Starting thread for range: " + startIndex + " -> " + endIndex);

            var count = 0;
            new Thread(() =>
            {
                Log.Debug("Inside thread");
                try
                {
                    for (var i = startIndex; i <= endIndex; i++)
                    {
                        if (this._cancellationTokenSource.IsCancellationRequested || finishRequested)
                        {
                            break;
                        }
                        if (i >= _chunks.Count) break;
                        var chunk = _chunks[i];
                        if (chunk.ChunkState == ChunkState.Finished)
                        {
                            continue;
                        }
                        DownloadChunk(chunk, latch);
                        count++;
                    }
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    Log.Debug(ex, "The chunk list changed while a worker was starting its next chunk");
                }
                finally
                {
                    if (this._cancellationTokenSource.IsCancellationRequested || finishRequested) latch.Break();
                }
                Log.Debug("Finished chunk-count: " + count);
            }).Start();
        }

        private void DownloadChunks()
        {
            var unfinishedPieceCount = _chunks.Where(chunk => chunk.ChunkState != ChunkState.Finished).Count();
            if (unfinishedPieceCount < 1) return;
            SaveChunkState();
            this.countdownLatch = new(unfinishedPieceCount);

            Log.Debug("Downloading chunks: " + unfinishedPieceCount);

            var threadCount = Math.Min(unfinishedPieceCount, configuredMaxConnections ?? Config.Instance.MaxSegments);
            var piecePerThread = (int)Math.Ceiling((float)unfinishedPieceCount / threadCount);
            var startIndex = 0;
            var endIndex = 0;

            var m = 0;
            for (var i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                if (chunk.ChunkState != ChunkState.Finished) m++;
                if (m == piecePerThread)
                {
                    endIndex = i;
                    DownloadChunkRange(startIndex, endIndex, countdownLatch);
                    startIndex = i + 1;
                    m = 0;
                }
            }
            if (m != 0)
            {
                endIndex = _chunks.Count - 1;
                DownloadChunkRange(startIndex, endIndex, countdownLatch);
            }

            Log.Debug("Waiting for downloading all chunks");
            this.countdownLatch.Wait();
            if (finishRequested) _cancelRequestor.CancelAll();
            if ((_cancellationTokenSource.IsCancellationRequested || finishRequested) && !_cancelRequestor.WaitForQuiescence(5000))
            {
                Log.Debug("Adaptive workers did not quiesce after cancellation");
            }
            SaveChunkState();
            _cancellationTokenSourceStateSaver.Cancel();
            Log.Debug("Countdown latch exited");
        }

        private void DownloadChunk(Chunk chunk, CountdownLatch latch)
        {
            var chunkDownloader = new HttpChunkDownloader(chunk, _http, this._state.Headers,
                this._state.Cookies, this._state.Authentication,
                _chunkStreamMap, _cancelRequestor);
            chunkDownloader.SkipWhenGone = SkipsMissingChunks;

            try
            {
                chunkDownloader.ChunkDataReceived += ChunkDataReceived;
                chunkDownloader.MimeTypeReceived += MimeTypeReceived;
                chunkDownloader.Download();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
            finally
            {
                chunkDownloader.ChunkDataReceived -= ChunkDataReceived;
                chunkDownloader.MimeTypeReceived -= MimeTypeReceived;
                latch.CountDown();
            }
        }

        private void ChunkDataReceived(object sender, ChunkDownloadedEventArgs args)
        {
            try
            {
                rwLock.EnterWriteLock();
                SaveSpeedLimitIfChanged();
                long tick = Helpers.TickCount();
                totalDownloadedBytes += args.Downloaded;
                downloadedBytesSinceStartOrResume += args.Downloaded;
                var ticksElapsed = tick - lastUpdated;
                if (ticksElapsed >= 2000)
                {
                    var downloadedCount = _chunks.FindAll(c => c.ChunkState == ChunkState.Finished).Count;
                    progressResult.Progress = (int)(downloadedCount * 100 / this._chunks.Count);
                    progressResult.Downloaded = totalDownloadedBytes;
                    var prgDiff = progressResult.Progress - lastProgress;
                    lastProgress = progressResult.Progress;
                    if (prgDiff > 0)
                    {
                        var eta = (ticksElapsed * (100 - progressResult.Progress) / 1000 * prgDiff);
                        progressResult.Eta = eta;
                    }
                    var timeDiff = tick - ticksAtDownloadStartOrResume;
                    if (timeDiff > 0)
                    {
                        progressResult.DownloadSpeed = (downloadedBytesSinceStartOrResume * 1000.0) / timeDiff;
                    }
                    lastUpdated = tick;
                    ProgressChanged?.Invoke(this, progressResult);
                    SaveChunkState();
                }
                this.ThrottleIfNeeded();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        private void MimeTypeReceived(object sender, MimeTypeReceivedEventArgs args)
        {
            try
            {
                rwLock.EnterWriteLock();
                this.OnContentTypeReceived(args.Chunk, args.MimeType);
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }


        private void ConcatSegments(IEnumerable<string> files, string target)
        {
#if NET35
            var buf = new byte[5 * 1024 * 1024];
#else
            var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(5 * 1024 * 1024);
#endif

            try
            {
                var totalSize = 0L;
                using var fsout = new FileStream(target, FileMode.Create, FileAccess.ReadWrite);
                foreach (string file in files)
                {
                    using var infs = new FileStream(file, FileMode.Open, FileAccess.Read);
                    while (!this._cancellationTokenSource.IsCancellationRequested)
                    {
                        var x = infs.Read(buf, 0, buf.Length);
                        if (x == 0)
                        {
                            break;
                        }
                        try
                        {
                            fsout.Write(buf, 0, x);
                        }
                        catch (IOException ioe)
                        {
                            throw new AssembleFailedException(ErrorCode.DiskError, ioe);
                        }
                        totalSize += x;
                    }
                }
                this._state.FileSize = totalSize;
            }
            finally
            {
#if !NET35
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
#endif
            }
        }

        protected virtual void Assemble()
        {
            try
            {
                AssembleOutput();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Assembly did not finish, removing the unfinished output");
                DiscardPartialOutput();
                throw;
            }
        }

        private void AssembleOutput()
        {
            SaveChunkState();
            if (this._cancellationTokenSource.IsCancellationRequested) return;
            if (string.IsNullOrEmpty(this.TargetDir))
            {
                this.TargetDir = FileHelper.GetDownloadFolderByFileName(this.TargetFileName);
            }

            if (!Directory.Exists(this.TargetDir))
            {
                Directory.CreateDirectory(this.TargetDir);
            }

            if (MarkDamagedChunks()) throw new AssembleFailedException(ErrorCode.Generic);
            ReserveOutputNameIfRenaming();

            if (!_state.Demuxed)
            {
                var plainStaging = OutputFileStaging.StagingPath(TargetFile, Id, false);
                partialOutput = plainStaging;
                ConcatSegments(this._chunks.Select(c => this._chunkStreamMap.GetStream(c.Id)), plainStaging);
                if (this._cancellationTokenSource.IsCancellationRequested) return;
                CommitOutput(plainStaging);
                DeleteFileParts();
                assembled = true;
                return;
            }

            if (mediaProcessor == null)
            {
                throw new AssembleFailedException(ErrorCode.Generic);
            }

            mediaProcessor.ProgressChanged += (s, e) => this.AssembingProgressChanged?.Invoke(this, e);

            var videoFile = Path.Combine(_state.TempDirectory, "1_video" + _state.VideoContainerFormat);
            var audioFile = Path.Combine(_state.TempDirectory, "2_audio" + _state.AudioContainerFormat);

            ConcatSegments(this._chunks.Where(c => c.StreamIndex == 0).Select(c => this._chunkStreamMap.GetStream(c.Id)),
                            videoFile);
            ConcatSegments(this._chunks.Where(c => c.StreamIndex == 1).Select(c => this._chunkStreamMap.GetStream(c.Id)),
                audioFile);
            if (this._cancellationTokenSource.IsCancellationRequested) return;

            var staging = OutputFileStaging.StagingPath(TargetFile, Id, true);
            partialOutput = staging;
            var res = mediaProcessor.MergeAudioVideStream(videoFile, audioFile, staging,
                this._cancellationTokenSource, out long totalSize);
            if (this._cancellationTokenSource.IsCancellationRequested) return;
            if (res != MediaProcessingResult.Success)
            {
                DiscardPartialOutput();
                var name = Path.GetFileNameWithoutExtension(TargetFileName);
                TargetFileName = name + ".mkv";
                ReserveOutputNameIfRenaming();
                staging = OutputFileStaging.StagingPath(TargetFile, Id, true);
                partialOutput = staging;
                var fallback = mediaProcessor.MergeAudioVideStream(videoFile, audioFile, staging,
                    this._cancellationTokenSource, out totalSize);
                if (this._cancellationTokenSource.IsCancellationRequested) return;
                if (fallback != MediaProcessingResult.Success)
                {
                    throw new AssembleFailedException(
                        res == MediaProcessingResult.AppNotFound ? ErrorCode.FFmpegNotFound :
                                ErrorCode.FFmpegError);
                }
            }

            if (this._cancellationTokenSource.IsCancellationRequested) return;
            CommitOutput(staging);
            DeleteFileParts();

            this._state.FileSize = totalSize;
            assembled = true;
        }

        private void DeleteFileParts()
        {
            Log.Debug("DeleteFileParts...");
            try
            {
                OutputFileStaging.DeleteFolder(_state.TempDirectory);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "DeleteFileParts");
            }
        }

        public void SetFileName(string name, FileNameFetchMode fileNameFetchMode)
        {
            this.TargetFileName = FileHelper.SanitizeFileName(name);
        }

        public void SetTargetDirectory(string folder)
        {
            this.TargetDir = folder;
        }




        protected void OnComplete()
        {
            Log.Debug("OnComplete");
            Finished?.Invoke(this, EventArgs.Empty);
            Cleanup();
        }

        protected void OnFailed(DownloadFailedEventArgs args)
        {
            if (args.ErrorCode == ErrorCode.InvalidResponse && totalDownloadedBytes > 0)
            {
                Failed?.Invoke(this, new DownloadFailedEventArgs(ErrorCode.SessionExpired));
            }
            else
            {
                Failed?.Invoke(this, args);
            }
            Cleanup();
        }

        protected void OnCancelled()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Cleanup();
        }

        private void Cleanup()
        {
            try
            {
                this._http?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception while disposing http client");
            }
        }

        public long GetTotalDownloaded() => this.totalDownloadedBytes;

        public long GetDownloaded() => this.downloadedBytesSinceStartOrResume;

        protected void ThrottleIfNeeded()
        {
            speedLimiter.ThrottleIfNeeded(this);
        }

        protected static void WriteChunkState(List<MultiSourceChunk> chunks, BinaryWriter w)
        {
            w.Write(chunks.Count);
            foreach (var chunk in chunks)
            {
                w.Write(chunk.Id);
                w.Write(chunk.Downloaded);
                w.Write(chunk.Size);
                w.Write(chunk.Offset);
                w.Write(chunk.StreamIndex);
                w.Write(chunk.Duration);
                w.Write((int)chunk.ChunkState);
                w.Write(chunk.Uri.ToString());
            }
        }

        protected static void ReadChunkState(BinaryReader r, out List<MultiSourceChunk> chunks)
        {
            var count = r.ReadInt32();
            chunks = new(count);
            for (var i = 0; i < count; i++)
            {
                chunks.Add(new MultiSourceChunk
                {
                    Id = r.ReadString(),
                    Downloaded = r.ReadInt64(),
                    Size = r.ReadInt64(),
                    Offset = r.ReadInt64(),
                    StreamIndex = r.ReadInt32(),
                    Duration = r.ReadDouble(),
                    ChunkState = (ChunkState)r.ReadInt32(),
                    Uri = new Uri(r.ReadString())
                });
            }
        }

        protected List<MultiSourceChunk> ChunkStateFromBytes(Stream stream)
        {
#if NET35
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var r = new BinaryReader(ms);
#else
            using var r = new BinaryReader(stream, Encoding.UTF8, true);
#endif
            ReadChunkState(r, out List<MultiSourceChunk> chunks);
            return chunks;
        }

        protected void ChunkStateToBytes(Stream stream)
        {
#if NET35
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            WriteChunkState(_chunks, w);
            ms.CopyTo(stream);
#else
            using var w = new BinaryWriter(stream, Encoding.UTF8, true);
            WriteChunkState(_chunks, w);
#endif
        }

        private const int SettingLockWaitMilliseconds = 250;

        public int SpeedLimitSetting => speedLimiter.Setting;

        public void SetMaxConnections(int? maxConnections)
        {
            configuredMaxConnections = maxConnections.HasValue && maxConnections.Value > 0 ? maxConnections : null;
        }

        public void ConfigureTransferPolicy(int? speedLimitKiB, int? maxConnections)
        {
            SetMaxConnections(maxConnections);
            speedLimiter.SetExplicitLimit(speedLimitKiB);
            if (_state != null) _state.SpeedLimit = speedLimiter.Setting;
        }

        private volatile bool speedLimitUnsaved;

        public void SetSpeedLimit(int setting)
        {
            speedLimiter.SetExplicitLimit(setting);
            if (_state == null) return;
            _state.SpeedLimit = speedLimiter.Setting;
            speedLimitUnsaved = true;
            SaveSpeedLimitWhenFree();
        }

        private void SaveSpeedLimitWhenFree()
        {
            if (!speedLimitUnsaved) return;
            if (!rwLock.TryEnterWriteLock(SettingLockWaitMilliseconds)) return;
            try
            {
                SaveSpeedLimitIfChanged();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        private void SaveSpeedLimitIfChanged()
        {
            if (!speedLimitUnsaved) return;
            speedLimitUnsaved = false;
            try
            {
                SaveState();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The speed limit could not be saved with the download");
            }
        }

        protected void RestoreTransferPolicy()
        {
            if (_state != null) speedLimiter.SetExplicitLimit(_state.SpeedLimit);
        }
    }

    public class MultiSourceChunk : Chunk
    {
        public int StreamIndex { get; set; }
        public double Duration { get; set; }
    }

    public abstract class MultiSourceDownloadInfo : IRequestData
    {
        public string? Cookies { get; set; }
        public Dictionary<string, List<string>> Headers { get; set; }
        public string File { get; set; }
        public string ContentType { get; set; }
    }

    public abstract class MultiSourceDownloadState
    {
        public string Id;
        public Dictionary<string, List<string>> Headers;
        public string? Cookies;
        public long FileSize = -1;
        public double Duration;
        public string TempDirectory;
        public bool Demuxed;
        public int AudioChunkCount = 0;
        public int VideoChunkCount = 0;
        public string VideoContainerFormat = "";
        public string AudioContainerFormat = "";


        public AuthenticationInfo? Authentication;
        public ProxyInfo? Proxy;
        public int SpeedLimit;
    }
}
