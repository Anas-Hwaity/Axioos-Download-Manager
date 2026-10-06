using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraceLog;
using ADM.Core;
using ADM.Core.Util;
using ADM.Core.Clients.Http;
using ADM.Core.IO;
using ADM.Core.MediaProcessor;
using ADM.Core.Telemetry;
using System.Text;
using System.Threading;

#if !NET5_0_OR_GREATER
using ADM.Compatibility;
#endif

namespace ADM.Core.Downloader.Progressive
{
    public abstract class HTTPDownloaderBase : IPieceCallback, IBaseDownloader, IDownloadTelemetrySource
    {
        protected Dictionary<string, Piece> pieces = new();
        protected Dictionary<string, PieceGrabber> grabberDict = new();
        protected bool resumable;
        protected long totalSize;
        protected int MAX_COUNT = Config.Instance.MaxSegments;
        protected CancelFlag cancelFlag = new();
        protected FileNameFetchMode fileNameFetchMode = FileNameFetchMode.FileNameAndExtension;
        protected IHttpClient http;
        protected long lastStateSavedAt = Helpers.TickCount();
        protected long lastProgressUpdatedAt = Helpers.TickCount();
        protected readonly ProgressResultEventArgs progressResult = new();
        protected long totalDownloadedBytes = 0L;
        protected long lastDownloadedBytes = 0L;
        protected long ticksAtDownloadStartOrResume = 0L;
        protected SpeedLimiter speedLimiter = new();
        protected BaseMediaProcessor? mediaProcessor;
        protected long downloadedBytesSinceStartOrResume = 0L;
        protected ReaderWriterLockSlim rwLock = new(LockRecursionPolicy.SupportsRecursion);
        public ReaderWriterLockSlim Lock => this.rwLock;
        private bool stopRequested = false;

        public FileNameFetchMode FileNameFetchMode
        {
            get { return fileNameFetchMode; }
            set { fileNameFetchMode = value; }
        }

        public string? TargetDir { get; set; }
        public bool IsCancelled => cancelFlag.IsCancellationRequested;
        public string? Id { get; protected set; }
        public long FileSize
        {
            get
            {
                var state = this.GetState();
                return state != null && state.FileSize > 0 ? state.FileSize : totalSize;
            }
        }
        public string? TargetFile
        {
            get
            {
                if (TargetDir == null || TargetFileName == null)
                {
                    return null;
                }
                return Path.Combine(TargetDir,
                        TargetFileName);
            }
        }
        public string? TargetFileName { get; set; }
        public bool KeepProvidedFileName { get; set; }
        public virtual string Type => "Http";
        public virtual Uri? PrimaryUrl { get; }

        public virtual event EventHandler? Started;
        public virtual event EventHandler? Finished;
        public virtual event EventHandler? Cancelled;
        public virtual event EventHandler? Probed;
        public virtual event EventHandler<ProgressResultEventArgs>? ProgressChanged;
        public virtual event EventHandler<ProgressResultEventArgs>? AssembingProgressChanged;
        public virtual event EventHandler<DownloadFailedEventArgs>? Failed;

        public abstract void Start();

        public abstract void SaveForLater();

        public abstract void Resume();
        public abstract bool IsTextRedirectionAllowed();

        public abstract HeaderData?
            GetHeaderUrlAndCookies(string pieceId);

        public abstract void PieceConnected(string pieceId, ProbeResult? result);

        protected abstract void SaveChunkState();

        protected abstract void SaveState();

        protected abstract BaseHTTPDownloaderState GetState();

        protected abstract void AssemblePieces();

        public abstract bool IsFirstRequest(StreamType streamType);

        public abstract bool IsFileChangedOnServer(StreamType streamType, long streamSize, DateTime? lastModified);

        public abstract void RestoreState();

        public void ThrottleIfNeeded()
        {
            speedLimiter.ThrottleIfNeeded(this);
        }

        private const int SettingLockWaitMilliseconds = 250;

        public int SpeedLimitSetting => speedLimiter.Setting;

        public void SetMaxConnections(int? maxConnections)
        {
            if (maxConnections.HasValue && maxConnections.Value > 0) MAX_COUNT = maxConnections.Value;
        }

        public void ConfigureTransferPolicy(int? speedLimitKiB, int? maxConnections)
        {
            SetMaxConnections(maxConnections);
            speedLimiter.SetExplicitLimit(speedLimitKiB);
            var state = GetState();
            if (state != null) state.SpeedLimit = speedLimiter.Setting;
        }

        private volatile bool speedLimitUnsaved;

        public void SetSpeedLimit(int setting)
        {
            speedLimiter.SetExplicitLimit(setting);
            var state = GetState();
            if (state == null) return;
            state.SpeedLimit = speedLimiter.Setting;
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

        protected void RestoreTransferPolicy(BaseHTTPDownloaderState state)
        {
            speedLimiter.SetExplicitLimit(state.SpeedLimit);
        }

        protected ProxyInfo? TransferProxy(BaseHTTPDownloaderState? state)
        {
            return state?.Proxy ?? Config.Instance.Proxy;
        }

        protected bool assemblyCompleted;
        protected string? assemblyOutput;

        protected string? reservedOutput;

        private readonly Dictionary<StreamType, ResumeValidator> resumeValidators = new Dictionary<StreamType, ResumeValidator>();
        private bool resumeValidatorsLoaded;

        protected void RememberResumeValidator(StreamType streamType, ProbeResult result)
        {
            lock (resumeValidators)
            {
                if (!resumeValidatorsLoaded) LoadResumeValidators();
                resumeValidators[streamType] = new ResumeValidator(result.ETag, result.LastModifiedHeader);
                ResumeValidator.Save(GetState()?.TempDir, resumeValidators);
            }
        }

        public bool IsRepresentationChanged(StreamType streamType, string? etag, string? lastModified)
        {
            ResumeValidator? saved;
            lock (resumeValidators)
            {
                if (!resumeValidatorsLoaded) LoadResumeValidators();
                resumeValidators.TryGetValue(streamType, out saved);
            }
            return ResumeValidator.IndicatesChange(saved, etag, lastModified);
        }

        public bool IsRepresentationConfirmed(StreamType streamType, string? etag)
        {
            ResumeValidator? saved;
            lock (resumeValidators)
            {
                if (!resumeValidatorsLoaded) LoadResumeValidators();
                resumeValidators.TryGetValue(streamType, out saved);
            }
            return ResumeValidator.Confirms(saved, etag);
        }

        private void LoadResumeValidators()
        {
            resumeValidatorsLoaded = true;
            foreach (var pair in ResumeValidator.Load(GetState()?.TempDir))
            {
                resumeValidators[pair.Key] = pair.Value;
            }
        }

        protected bool AssemblyWasInterrupted()
        {
            if (assemblyCompleted || !this.cancelFlag.IsCancellationRequested) return false;
            DiscardAssemblyOutput();
            return true;
        }

        protected void DiscardAssemblyOutput()
        {
            var staging = assemblyOutput;
            var reserved = reservedOutput;
            assemblyOutput = null;
            reservedOutput = null;
            OutputFileStaging.Discard(staging, reserved);
            if (reserved != null) OutputFileStaging.ForgetReservation(GetState()?.TempDir);
        }

        protected void ReserveOutputNameIfRenaming()
        {
            if (Config.Instance.FileConflictResolution != FileConflictResolution.AutoRename) return;
            if (this.TargetDir == null || this.TargetFileName == null) return;
            this.TargetFileName = OutputFileStaging.ReserveUniqueFileName(this.TargetFileName, this.TargetDir, GetState()?.TempDir);
            reservedOutput = this.TargetFile;
        }

        private AuthenticationInfo? resumeAuthentication;

        public void UseCredentials(AuthenticationInfo? authentication)
        {
            resumeAuthentication = authentication;
        }

        protected AuthenticationInfo? RestoredCredentials()
        {
            if (resumeAuthentication != null) return resumeAuthentication;
            try
            {
                var url = PrimaryUrl;
                return url == null ? null : Helpers.GetAuthenticationInfoFromConfig(url);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Saved credentials could not be looked up");
                return null;
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
                var ranges = pieces.Values.Select(piece => new DownloadRangeTelemetry(
                    piece.Offset,
                    piece.Length > 0 ? piece.Offset + piece.Length - 1 : piece.Offset,
                    piece.Downloaded,
                    piece.State.ToString())).ToList();
                var active = pieces.Values.Count(piece => piece.State == SegmentState.Downloading);
                var capability = pieces.Count == 0 ? DownloadResumeCapability.Unknown : (resumable ? DownloadResumeCapability.Yes : DownloadResumeCapability.No);
                var projection = new DownloadTransferTelemetryProjection(capability, active, ranges);
                lastTransferTelemetry = projection;
                return projection;
            }
            finally { rwLock.ExitReadLock(); }
        }

        public virtual void Stop()
        {
            if (stopRequested)
            {
                return;
            }
            stopRequested = true;
            try
            {
                this.cancelFlag.Cancel();
                this.speedLimiter.WakeIfSleeping();
                SaveSpeedLimitWhenFree();
                foreach (var pc in grabberDict.Keys)
                {
                    grabberDict[pc].Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error while Stop");
            }
            OnCancelled();
        }

        public virtual void SetTargetDirectory(string? folder)
        {
            this.TargetDir = folder;
        }

        public virtual void SetUserSelectedFile(string file)
        {
            var folder = Path.GetDirectoryName(file);
            var name = Path.GetFileName(file);
            this.TargetFileName = FileHelper.SanitizeFileName(name);
            this.TargetDir = folder;
            this.KeepProvidedFileName = true;
        }

        public virtual void SetFileName(string name, FileNameFetchMode fileNameFetchMode)
        {
            this.TargetFileName = FileHelper.SanitizeFileName(name);
            this.fileNameFetchMode = fileNameFetchMode;
        }

        public virtual bool ContinueAdjacentPiece(string pieceId, long maxByteRange)
        {
            try
            {
                rwLock.EnterWriteLock();
                var chunk = pieces[pieceId];
                var position = chunk.Offset + chunk.Length;
                foreach (var key in this.pieces.Keys)
                {
                    var val = this.pieces[key];
                    if (val.Downloaded == 0 && val.Offset == position &&
                        val.StreamType == chunk.StreamType &&
                        !this.cancelFlag.IsCancellationRequested)
                    {
                        if ((val.Offset + val.Length) <= maxByteRange)
                        {
                            var grabber = this.grabberDict.GetValueOrDefault(val.Id, null);
                            if (grabber != null)
                            {
                                grabber.Stop();
                                this.grabberDict.Remove(val.Id);
                            }
                            this.pieces.Remove(key);
                            var len = val.Length;
                            chunk.Length += len;
                            this.CreatePiece();
                            this.SaveChunkState();
                            return true;
                        }
                        Log.Debug("ContinueAdjacentPiece fail bcs of " + maxByteRange + "<" + ((val.Offset + val.Length)));
                    }
                }
                return false;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public virtual Piece GetPiece(string pieceId)
        {
            try
            {
                rwLock.EnterReadLock();
                return this.pieces[pieceId];
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        public virtual string GetPieceFile(string pieceId)
        {
            return Path.Combine(this.GetState().TempDir, pieceId);
        }

        public virtual IHttpClient? GetSharedHttpClient(string pieceId)
        {
            if (this.grabberDict.ContainsKey(pieceId))
            {
                return this.http;
            }
            return null;
        }

        public void PieceDownloadFailed(string pieceId, ErrorCode error)
        {
            if (this.cancelFlag.IsCancellationRequested) return;
            try
            {
                rwLock.EnterWriteLock();
                grabberDict.Remove(pieceId);
                this.SaveChunkState();
                if (grabberDict.Count == 0)
                {
                    OnFailed(error);
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public virtual void PieceDownloadFinished(string pieceId)
        {
            if (this.cancelFlag.IsCancellationRequested) return;
            try
            {
                rwLock.EnterWriteLock();
                var piece = this.pieces[pieceId];
                piece.State = SegmentState.Finished;
                grabberDict.Remove(pieceId);
                SaveChunkState();
                if (this.AllFinished())
                {
                    SaveChunkState();
                    this.AssemblePieces();
                    if (AssemblyWasInterrupted()) return;
                    OnFinished();
                    return;
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
            this.CreatePiece();
        }

        public virtual void UpdateDownloadedBytesCount(string pieceId, long bytes)
        {
            try
            {
                rwLock.EnterWriteLock();
                SaveSpeedLimitIfChanged();
                totalDownloadedBytes += bytes;
                downloadedBytesSinceStartOrResume += bytes;
                if (totalDownloadedBytes < 0) totalDownloadedBytes = 0;
                if (downloadedBytesSinceStartOrResume < 0) downloadedBytesSinceStartOrResume = 0;

                var pc = pieces[pieceId];
                pc.Downloaded += bytes;

                var ticks = Helpers.TickCount();

                if (ticks - lastStateSavedAt > 2000)
                {
                    SaveChunkState();
                    lastStateSavedAt = ticks;
                }

                var ticksElapsed = ticks - ticksAtDownloadStartOrResume;
                if (ticks - lastProgressUpdatedAt > 500 && ticksElapsed > 0)
                {
                    var instantSpeed = ((totalDownloadedBytes - lastDownloadedBytes) * 1000) / (ticks - lastProgressUpdatedAt);
                    var avgSpeed = (downloadedBytesSinceStartOrResume * 1000.0) / ticksElapsed;
                    lastProgressUpdatedAt = ticks;
                    lastDownloadedBytes = totalDownloadedBytes;
                    progressResult.DownloadSpeed = instantSpeed;
                    progressResult.Downloaded = totalDownloadedBytes;
                    progressResult.Progress = FileSize > 0 ? (int)(totalDownloadedBytes * 100 / FileSize) : 0;
                    progressResult.Eta = FileSize > 0 ? (long)Math.Ceiling((FileSize - totalDownloadedBytes) / avgSpeed) : 0;
                    ProgressChanged?.Invoke(this, progressResult);
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        protected virtual void CreatePiece()
        {
            try
            {
                rwLock.EnterWriteLock();
                if (this.cancelFlag.IsCancellationRequested) return;
                if (this.grabberDict.Count == MAX_COUNT) return;
                var rem = MAX_COUNT - this.grabberDict.Count;
                rem -= RetryFailedPieces(rem);
                if (rem > 0 && !this.cancelFlag.IsCancellationRequested)
                {
                    var maxChunkId = FindMaxChunk();
                    var newchunkId = SplitPiece(maxChunkId);
                    if (newchunkId != null && !this.cancelFlag.IsCancellationRequested)
                    {
                        this.grabberDict[newchunkId] = new PieceGrabber(newchunkId, this);
                        grabberDict[newchunkId].Download();
                    }
                }
                SaveChunkState();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        protected virtual bool AllFinished()
        {
            try
            {
                rwLock.EnterReadLock();
                foreach (var pi in this.pieces.Keys)
                {
                    if (this.pieces[pi].State != SegmentState.Finished) return false;
                }
                return true;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        protected int RetryFailedPieces(int max)
        {
            try
            {
                rwLock.EnterWriteLock();
                var count = 0;
                for (var chunkToRetry = Math.Min(GetInactiveChunkCount(), max); chunkToRetry > 0; chunkToRetry--)
                {
                    var chunkId = GetInactivePiece();
                    if (chunkId == null || this.cancelFlag.IsCancellationRequested) break;
                    grabberDict[chunkId] = new PieceGrabber(chunkId, this);
                    grabberDict[chunkId].Download();
                    count++;
                }
                return count;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        protected int GetInactiveChunkCount()
        {
            try
            {
                rwLock.EnterReadLock();
                return pieces.Keys.Where(chunkId => !(grabberDict.ContainsKey(chunkId)
                 || pieces[chunkId].State == SegmentState.Finished)).Count();
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        protected string? GetInactivePiece()
        {
            try
            {
                rwLock.EnterReadLock();
                foreach (var chunkId in pieces.Keys)
                {
                    if (!(grabberDict.ContainsKey(chunkId) || pieces[chunkId].State == SegmentState.Finished
                        || pieces[chunkId].Downloaded == pieces[chunkId].Length)) return chunkId;
                }
                return null;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        protected string? FindMaxChunk()
        {
            try
            {
                rwLock.EnterReadLock();
                var max = -1L;
                string? pid = null;
                foreach (var pieceId in this.pieces.Keys)
                {
                    if (this.cancelFlag.IsCancellationRequested) break;
                    var rem = this.pieces[pieceId].Length - this.pieces[pieceId].Downloaded;
                    if (rem > max)
                    {
                        pid = pieceId;
                        max = rem;
                    }
                }
                return max > 256 * 1024 ? pid : null;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        protected string? SplitPiece(string chunkId)
        {
            try
            {
                rwLock.EnterWriteLock();
                if (chunkId != null && !this.cancelFlag.IsCancellationRequested)
                {
                    var chunk = pieces[chunkId];
                    var rem = chunk.Length - chunk.Downloaded;
                    if (rem < 256 * 1024)
                    {
                        return null;
                    }
                    var len = rem / 2;
                    var offset = chunk.Offset + chunk.Length - len;
                    chunk.Length -= len;
                    var newPieceId = Guid.NewGuid().ToString();
                    var newPiece = new Piece
                    {
                        Offset = offset,
                        Length = len,
                        Downloaded = 0,
                        State = SegmentState.NotStarted,
                        Id = newPieceId,
                        StreamType = chunk.StreamType
                    };
                    pieces[newPieceId] = newPiece;
                    SaveChunkState();
                    return newPieceId;
                }
                return null;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        protected virtual void OnStarted()
        {
            this.Started?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnFinished()
        {
            this.Finished?.Invoke(this, EventArgs.Empty);
            Cleanup();
        }

        protected virtual void OnProgressChanged(int progress)
        {
            this.progressResult.Progress = progress;
            this.ProgressChanged?.Invoke(this, progressResult);
        }

        protected virtual void OnCancelled()
        {
            this.Cancelled?.Invoke(this, EventArgs.Empty);
            Cleanup();
        }

        protected virtual void OnProbed()
        {
            this.Probed?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnFailed(ErrorCode error)
        {
            try
            {
                if (error == ErrorCode.InvalidResponse && totalDownloadedBytes > 0)
                {
                    this.Failed?.Invoke(this, new DownloadFailedEventArgs(ErrorCode.SessionExpired));
                }
                else
                {
                    this.Failed?.Invoke(this, new DownloadFailedEventArgs(error));
                }
            }
            finally
            {
                Cleanup();
            }
        }

        protected void ReportFailure(ErrorCode error)
        {
            try
            {
                OnFailed(error);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The failure of the download could not be reported");
            }
        }

        protected virtual void OnAssembleProgressChanged(int progress)
        {
            this.progressResult.Progress = progress;
            this.AssembingProgressChanged?.Invoke(this, progressResult);
        }

        protected virtual void TicksAndSizeAtResume()
        {
            if (pieces == null) return;
            foreach (var piece in pieces.Values)
            {
                try
                {
                    var pieceFile = GetPieceFile(piece.Id);
                    var onDisk = File.Exists(pieceFile) ? new FileInfo(pieceFile).Length : 0;
                    if (piece.Downloaded > onDisk)
                    {
                        Log.Debug("Piece " + piece.Id + " was ahead of its file on resume and was corrected");
                        piece.Downloaded = onDisk;
                        if (piece.State == SegmentState.Finished) piece.State = SegmentState.NotStarted;
                    }
                    else if (piece.State != SegmentState.Finished && piece.Length > 0 && piece.Downloaded == piece.Length)
                    {
                        Log.Debug("Piece " + piece.Id + " was complete on resume and was marked as finished");
                        piece.State = SegmentState.Finished;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Unable to verify piece length on resume");
                }
            }
            totalDownloadedBytes = 0;

            ticksAtDownloadStartOrResume = Helpers.TickCount();

            foreach (var pk in pieces.Keys)
            {
                totalDownloadedBytes += pieces[pk].Downloaded;
            }
        }

        private void Cleanup()
        {
            try
            {
                this.http?.Dispose();
            }
            catch (Exception e)
            {
                Log.Debug(e, "Exception while disposing http client");
            }
        }

        public long GetTotalDownloaded() => this.totalDownloadedBytes;

        public long GetDownloaded() => this.downloadedBytesSinceStartOrResume;

        protected virtual void DeleteFileParts()
        {
            try
            {
                Log.Debug("Deleting temporary download files");
                OutputFileStaging.DeleteFolder(this.GetState().TempDir);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "DeleteFileParts");
            }
        }

        protected static void WriteChunkState(Dictionary<string, Piece> chunks, BinaryWriter w)
        {
            w.Write(chunks.Count);
            foreach (var key in chunks.Keys)
            {
                w.Write(key);
                var chunk = chunks[key];
                w.Write(chunk.Id);
                w.Write(chunk.Downloaded);
                w.Write(chunk.Length);
                w.Write(chunk.Offset);
                w.Write((int)chunk.State);
                w.Write((int)chunk.StreamType);
            }
        }

        protected static void ReadChunkState(BinaryReader r, out Dictionary<string, Piece> chunks)
        {
            var count = r.ReadInt32();
            chunks = new();
            for (var i = 0; i < count; i++)
            {
                var key = r.ReadString();
                chunks[key] = new Piece
                {
                    Id = r.ReadString(),
                    Downloaded = r.ReadInt64(),
                    Length = r.ReadInt64(),
                    Offset = r.ReadInt64(),
                    State = (SegmentState)r.ReadInt32(),
                    StreamType = (StreamType)r.ReadInt32()
                };
            }
        }

        protected Dictionary<string, Piece> ChunkStateFromBytes(Stream stream)
        {
#if NET35
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var r = new BinaryReader(ms);
            ReadChunkState(r, out Dictionary<string, Piece> chunks);
            return chunks;
#else
            using var r = new BinaryReader(stream, Encoding.UTF8, true);
            ReadChunkState(r, out Dictionary<string, Piece> chunks);
            return chunks;
#endif
        }

        protected void ChunkStateToBytes(Stream stream)
        {
#if NET35
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            WriteChunkState(pieces, w);
            ms.CopyTo(stream);
#else
            using var w = new BinaryWriter(stream, Encoding.UTF8, true);
            WriteChunkState(pieces, w);
#endif
        }
    }

    public class BaseHTTPDownloaderState
    {
        public string? TempDir;
        public string? Id;
        public long FileSize = -1;
        public AuthenticationInfo? Authentication;
        public ProxyInfo? Proxy;
        public DateTime LastModified;
        public int SpeedLimit;
    }

    public enum DownloadState
    {
        Created, Downloading, Stopped, Failed, Finished, Assembling
    }
}
