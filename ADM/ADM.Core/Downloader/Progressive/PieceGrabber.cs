using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.IO;
using ADM.Core;
using TraceLog;
using ADM.Core.Clients.Http;
using System.Linq;

namespace ADM.Core.Downloader.Progressive
{
    public class PieceGrabber : IDisposable
    {
        private Thread t;
        private ManualResetEvent sleepHandle = new ManualResetEvent(false);
        private Uri? redirectUri;
        private string? pieceId;
        private IPieceCallback? callback;
        private HttpRequest? request;
        private readonly CancelFlag cancellationTokenSource = new();
        private Stream? fileWriterStream;
        private int timesRetried = 0;
        private long maxByteRange = 0;
        private long actualHttpResponseSize = -1;
        private bool emptyRepresentation;
        private const int ResumeCheckBytes = 8192;
        private long resumeOverlap;
        private long resumeDownloadedAtRequest;
        private long responseBodyRemaining = -1;
        private bool representationConfirmed;
        private bool rewoundAfterMismatch;

        public CancelFlag CancellationToken => cancellationTokenSource;
        public PieceGrabber(string pieceId, IPieceCallback callback)
        {
            this.pieceId = pieceId;
            this.callback = callback;
        }

        private void OnComplete()
        {
            try
            {
                if (this.pieceId != null)
                {
                    this.callback?.PieceDownloadFinished(this.pieceId);
                }
            }
            catch (AssembleFailedException ex)
            {
                Log.Debug(ex, "Exception in OnComplete");
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception in OnComplete");
                if (ex is DownloadException exception)
                {
                    throw new AssembleFailedException(exception.ErrorCode, ex);
                }
                throw new AssembleFailedException(ErrorCode.Generic, ex);
            }
        }

        public void Download()
        {
            this.t = new Thread(Download2);
            this.t.Start();
        }

        private void Download2()
        {
            try
            {
                if (this.pieceId == null || this.callback == null) return;
                var piece = this.callback.GetPiece(this.pieceId);
                while (!this.CancellationToken.IsCancellationRequested)
                {
                    var connectPhase = true;
                    try
                    {
                        if (piece.Length < 1 || piece.Downloaded < piece.Length)
                        {
                            using var response = Connect();
                            if (response == null)
                            {
                                throw new Exception("response is null");
                            }
                            connectPhase = false;
                            if (!emptyRepresentation) this.Download(response);
                            else using (new FileStream(this.callback.GetPieceFile(this.pieceId), FileMode.Create, FileAccess.Write)) { }
                        }

                        OnComplete();
                        return;
                    }
                    catch (TextRedirectException e)
                    {
                        this.redirectUri = e.RedirectUri;
                        continue;
                    }
                    catch (RangeEndedEarlyException e)
                    {
                        Log.Debug(e, "The server sent less than the requested range, asking for the rest");
                        continue;
                    }
                    catch (HttpException e)
                    {
                        var status = e.StatusCode;
                        if (HttpRetryPolicy.IsTransient(status))
                        {
                            Log.Debug("Transient HTTP status " + (int)status + ", retrying within the retry budget");
                        }
                        else if (Enum.IsDefined(typeof(HttpStatusCode), status))
                        {
                            throw new DownloadException(ErrorCode.InvalidResponse,
                                "Invalid response: " + e.Message, e);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is KeyNotFoundException || this.CancellationToken.IsCancellationRequested) return;
                        if (e is AssembleFailedException || e is NonRetriableException || e is OperationCanceledException) throw;
                        Log.Debug(e, "Error in PieceGrabber inner block - swallowing error - isCancelled: " + this.cancellationTokenSource.IsCancellationRequested);
                    }
                    timesRetried++;
                    if (timesRetried > Config.Instance.MaxRetry)
                    {
                        throw new DownloadException(ErrorCode.MaxRetryFailed, "Max retry exceeded");
                    }
                    if (connectPhase)
                    {
                        sleep(Config.Instance.RetryDelay * 1000);
                        CancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (Exception e)
            {
                if (e is KeyNotFoundException || this.CancellationToken.IsCancellationRequested)
                {
                    return;
                }
                Log.Debug(e, "Error in PieceGrabber outer block");
                try
                {
                    if (this.pieceId != null)
                    {
                        this.callback?.PieceDownloadFailed(this.pieceId,
                            e is DownloadException de ? de.ErrorCode : ErrorCode.Generic);
                    }
                }
                catch (Exception reportError)
                {
                    Log.Debug(reportError, "The failure of a piece could not be reported");
                }
            }
        }

        public void Stop()
        {
            Log.Debug("Stopping request");
            this.Dispose();
            try { this.request?.Abort(); } catch { }
        }

        public void Dispose()
        {
            this.cancellationTokenSource?.Cancel();
            this.sleepHandle.Set();
            this.sleepHandle.Close();
            this.pieceId = null;
            this.callback = null;
            try { this.fileWriterStream?.Dispose(); } catch { }
        }

        private HttpResponse Connect()
        {
            HttpResponse? response = null;
            var error = true;
            try
            {
                if (this.callback == null || this.pieceId == null) throw new OperationCanceledException();
                var piece = this.callback.GetPiece(this.pieceId);
                var firstRequest = this.callback.IsFirstRequest(piece.StreamType);

                if (piece.Length == -1 && !this.callback.IsFirstRequest(piece.StreamType))
                    throw new NonRetriableException(ErrorCode.NonResumable, "Resume not supported");

                var hc = this.callback.GetSharedHttpClient(this.pieceId);
                if (hc == null) throw new OperationCanceledException();
                request = CreateRequest(hc, piece);
                response = hc.Send(request);
                CancellationToken.ThrowIfCancellationRequested();
                if (!firstRequest)
                {
                    var requestedStart = piece.Offset + resumeDownloadedAtRequest - resumeOverlap;
                    var resumeDecision = HttpResumeDecisionEvaluator.Evaluate(
                        response.StatusCode, requestedStart, response.ContentRangeStart, response.ContentRangeLength, null);
                    if (resumeDecision == HttpResumeDecision.RangeIgnored)
                        throw new NonRetriableException(ErrorCode.InvalidResponse, "ResumeRangeIgnored :: " + piece.Id);
                    if (resumeDecision == HttpResumeDecision.OffsetMismatch)
                        throw new NonRetriableException(ErrorCode.InvalidResponse, "ResumeOffsetMismatch :: " + piece.Id);
                    if (resumeDecision == HttpResumeDecision.RepresentationChanged)
                        throw new NonRetriableException(ErrorCode.InvalidResponse, "ResumeRepresentationChanged :: " + piece.Id);
                    if (response.StatusCode == HttpStatusCode.PartialContent
                        && this.callback.IsRepresentationChanged(piece.StreamType, response.GetHeader("ETag"), response.GetHeader("Last-Modified")))
                        throw new NonRetriableException(ErrorCode.SourceChanged, "ResumeValidatorChanged :: " + piece.Id);
                    representationConfirmed = response.StatusCode == HttpStatusCode.PartialContent
                        && this.callback.IsRepresentationConfirmed(piece.StreamType, response.GetHeader("ETag"));
                }
                if (firstRequest && response.StatusCode == HttpStatusCode.PartialContent && response.ContentRangeStart > 0)
                {
                    throw new NonRetriableException(ErrorCode.InvalidResponse, "FirstRangeOffsetMismatch :: " + piece.Id);
                }
                if (firstRequest && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
                    && piece.Offset == 0 && piece.Downloaded == 0 && response.ContentRangeLength == 0)
                {
                    emptyRepresentation = true;
                    maxByteRange = 0;
                    actualHttpResponseSize = 0;
                    this.callback?.PieceConnected(this.pieceId, new ProbeResult
                    {
                        ResourceSize = 0,
                        Resumable = false,
                        FinalUri = redirectUri ?? response.ResponseUri,
                        AttachmentName = response.ContentDispositionFileName,
                        ContentType = response.ContentType,
                        LastModified = response.LastModified
                    });
                    error = false;
                    return response;
                }
                response.EnsureSuccessStatusCode();

                var status = response.StatusCode;
                var contentLength = response.ContentLength;
                if (response.Compressed)
                {
                    contentLength = -1;
                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        status = HttpStatusCode.OK;
                    }
                }

                if (firstRequest && response.ContentType == "text/plain" && this.callback.IsTextRedirectionAllowed())
                {
                    throw new TextRedirectException(new Uri(response.ReadAsString(CancellationToken).Trim()));
                }
                if (!firstRequest && status != HttpStatusCode.PartialContent)
                {
                    throw new NonRetriableException(ErrorCode.InvalidResponse, "Resume not supported :: " + piece.Id);
                }
                if (!firstRequest && contentLength > 0
                    && this.callback.IsFileChangedOnServer(piece.StreamType, response!.ContentRangeLength, null))
                {
                    throw new NonRetriableException(ErrorCode.InvalidResponse, "Content length mismatch :: " + piece.Id);
                }
                maxByteRange = contentLength <= 0 ? -1 : piece.Offset + contentLength;
                actualHttpResponseSize = maxByteRange;
                responseBodyRemaining = RangeBodyLength(response);
                this.callback?.PieceConnected(this.pieceId, firstRequest ? CreateProbeResult(response!) : null);
                error = false;
                return response!;
            }
            finally
            {
                if (error)
                {
                    try { request?.Abort(); } catch { }
                    try { response?.Close(); } catch { }
                }
            }
        }

        private void Download(HttpResponse response)
        {
            if (this.callback == null || this.pieceId == null) return;
            var piece = this.callback.GetPiece(this.pieceId);
            using var sourceStream = response.GetResponseStream();
            CancellationToken.ThrowIfCancellationRequested();
            VerifyResumeOverlap(piece, sourceStream);
            try
            {
                using var targetStream = new FileStream(this.callback.GetPieceFile(this.pieceId), 
                    FileMode.OpenOrCreate, FileAccess.Write);
                this.fileWriterStream = targetStream;
                targetStream.Seek(piece.Downloaded, SeekOrigin.Begin);
                if (piece.Length > 0)
                {
                    CopyWithFixedLength(piece, sourceStream, targetStream);
                }
                else
                {
                    CopyWithUnknownLength(piece, sourceStream, targetStream);
                }
                try
                {
                    targetStream.Close();
                }
                catch { }
            }
            finally
            {
                this.fileWriterStream = null;
            }
        }

        private void CopyWithFixedLength(Piece piece, Stream sourceStream, Stream targetStream)
        {
#if NET35
            var BUF = new byte[32 * 1024];
#else
            var BUF = System.Buffers.ArrayPool<byte>.Shared.Rent(32 * 1024);
#endif
            try
            {
                var count = 0L;
                while (!CancellationToken.IsCancellationRequested)
                {
                    if (this.pieceId == null || this.callback == null) break;
                    var remaining = piece.Length - piece.Downloaded;
                    if (remaining <= 0)
                    {
                        if (maxByteRange > 0 && this.callback.ContinueAdjacentPiece(this.pieceId, maxByteRange))
                        {
                            piece = this.callback.GetPiece(this.pieceId);
                            remaining = piece.Length - piece.Downloaded;
                        }
                        else
                        {
                            CloseUnfinishedRequest(count);
                            break;
                        }
                    }
                    var x = sourceStream.Read(BUF, 0,
                        (int)Math.Min(BUF.Length, remaining));
                    this.CancellationToken.ThrowIfCancellationRequested();
                    if (x == 0)
                    {
                        if (responseBodyRemaining == 0 && count > 0)
                        {
                            throw new RangeEndedEarlyException("Range ended before the piece was complete :: " + piece.Id);
                        }
                        throw new DownloadException(ErrorCode.Generic, "Unexpected EOF :: " + piece.Id);
                    }
                    if (responseBodyRemaining > 0) responseBodyRemaining = Math.Max(0, responseBodyRemaining - x);
                    try
                    {
                        targetStream.Write(BUF, 0, x);
                        count += x;
                    }
                    catch (IOException ioe)
                    {
                        Log.Debug(ioe, "Disk error");
                        throw new NonRetriableException(ErrorCode.DiskError, "Disk error :: " + piece.Id, ioe);
                    }
                    if (this.CancellationToken.IsCancellationRequested) return;
                    this.callback?.UpdateDownloadedBytesCount(this.pieceId, x);
                    this.callback?.ThrottleIfNeeded();
                }
            }
            finally
            {
#if !NET35
                System.Buffers.ArrayPool<byte>.Shared.Return(BUF, false);
#endif
            }
        }

        private void CloseUnfinishedRequest(long count)
        {
            if (actualHttpResponseSize - count != 0)
            {
                try
                {
                    Log.Debug("Disable connection reuse");
                    this.request?.Abort();
                }
                catch { }
            }
        }

        private void CopyWithUnknownLength(Piece piece, Stream sourceStream, Stream targetStream)
        {
#if NET35
            var BUF = new byte[32 * 1024];
#else
            var BUF = System.Buffers.ArrayPool<byte>.Shared.Rent(32 * 1024);
#endif

            try
            {
                while (!CancellationToken.IsCancellationRequested)
                {
                    if (this.pieceId == null || this.callback == null) break;
                    var x = sourceStream.Read(BUF, 0, BUF.Length);
                    this.CancellationToken.ThrowIfCancellationRequested();
                    if (x == 0)
                    {
                        break;
                    }
                    try
                    {
                        targetStream.Write(BUF, 0, x);
                    }
                    catch (IOException ioe)
                    {
                        Log.Debug(ioe, "Disk error");
                        throw new NonRetriableException(ErrorCode.DiskError, "Disk error :: " + piece.Id, ioe);
                    }
                    if (this.CancellationToken.IsCancellationRequested) return;
                    this.callback?.UpdateDownloadedBytesCount(this.pieceId, x);
                    this.callback?.ThrottleIfNeeded();
                }
            }
            finally
            {
#if !NET35
                System.Buffers.ArrayPool<byte>.Shared.Return(BUF, false);
#endif
            }
        }

        private HttpRequest CreateRequest(IHttpClient hc, Piece piece)
        {
            if (this.callback == null || this.pieceId == null) throw new OperationCanceledException();
            var headerCookieUrl = this.callback.GetHeaderUrlAndCookies(this.pieceId);
            if (headerCookieUrl == null) throw new OperationCanceledException();
            var req = hc.CreateGetRequest(this.redirectUri ?? headerCookieUrl.Value.Url,
                headerCookieUrl.Value.Headers,
                headerCookieUrl.Value.Cookies,
                headerCookieUrl.Value.Authentication);
            if (this.callback.IsFirstRequest(piece.StreamType))
            {
                resumeOverlap = 0;
                resumeDownloadedAtRequest = 0;
                req.AddRange(0);
            }
            else
            {
                resumeDownloadedAtRequest = piece.Downloaded;
                resumeOverlap = Math.Max(0, Math.Min(piece.Downloaded, ResumeCheckBytes));
                var rangeStart = piece.Offset + resumeDownloadedAtRequest - resumeOverlap;
                Log.Debug("Range: " + rangeStart + "-" + (piece.Offset + piece.Length - 1));
                req.AddRange(rangeStart, piece.Offset + piece.Length - 1);
            }
            return req;
        }

        private static long RangeBodyLength(HttpResponse response)
        {
            if (response.Compressed || response.StatusCode != HttpStatusCode.PartialContent) return -1;
            if (WebRequestExtensions.TryParseContentRange(response.GetHeader("Content-Range"), out long start, out long end, out _)
                && start >= 0 && end >= start)
            {
                return end - start + 1;
            }
            return -1;
        }

        private static long CompleteLength(HttpResponse response)
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var total = response.ContentRangeLength;
                if (total > 0) return total;
            }
            return response.ContentLength;
        }

        private void VerifyResumeOverlap(Piece piece, Stream sourceStream)
        {
            var overlap = (int)resumeOverlap;
            resumeOverlap = 0;
            if (overlap <= 0 || this.callback == null || this.pieceId == null) return;
            if (piece.Downloaded != resumeDownloadedAtRequest)
            {
                throw new DownloadException(ErrorCode.Generic, "Saved part moved while reconnecting :: " + piece.Id);
            }
            var fromServer = new byte[overlap];
            var received = 0;
            while (received < overlap)
            {
                var x = sourceStream.Read(fromServer, received, overlap - received);
                this.CancellationToken.ThrowIfCancellationRequested();
                if (x == 0)
                {
                    throw new DownloadException(ErrorCode.Generic, "Unexpected EOF while checking the saved part :: " + piece.Id);
                }
                received += x;
            }
            if (responseBodyRemaining > 0) responseBodyRemaining = Math.Max(0, responseBodyRemaining - overlap);
            var onDisk = new byte[overlap];
            var available = 0;
            try
            {
                using var saved = new FileStream(this.callback.GetPieceFile(this.pieceId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                saved.Seek(piece.Downloaded - overlap, SeekOrigin.Begin);
                while (available < overlap)
                {
                    var y = saved.Read(onDisk, available, overlap - available);
                    if (y == 0) break;
                    available += y;
                }
            }
            catch (FileNotFoundException ex)
            {
                Log.Debug(ex, "The saved part is missing");
                available = -1;
            }
            if (available < overlap)
            {
                RejectSavedPart(piece, available < 0 ? "SavedPartMissing" : "SavedPartTooShort");
            }
            for (var i = 0; i < overlap; i++)
            {
                if (fromServer[i] != onDisk[i])
                {
                    RejectSavedPart(piece, "ResumeContentMismatch");
                }
            }
        }

        private void RejectSavedPart(Piece piece, string reason)
        {
            if (representationConfirmed && !rewoundAfterMismatch && this.callback != null && this.pieceId != null)
            {
                rewoundAfterMismatch = true;
                Log.Debug("The saved part of a piece does not match an unchanged file on the server, fetching that piece again :: " + reason);
                this.callback.UpdateDownloadedBytesCount(this.pieceId, -piece.Downloaded);
                throw new DownloadException(ErrorCode.Generic, reason + " :: " + piece.Id);
            }
            throw new NonRetriableException(ErrorCode.SourceChanged, reason + " :: " + piece.Id);
        }




        private ProbeResult CreateProbeResult(HttpResponse response)
        {
            return new ProbeResult
            {
                ResourceSize = response.Compressed ? -1 : CompleteLength(response),
                Resumable = response.Compressed ? false : response.StatusCode == HttpStatusCode.PartialContent,
                FinalUri = redirectUri ?? response.ResponseUri,
                AttachmentName = response.ContentDispositionFileName,
                ContentType = response.ContentType,
                LastModified = response.LastModified,
                ETag = response.GetHeader("ETag"),
                LastModifiedHeader = response.GetHeader("Last-Modified")
            };
        }

        private void sleep(int interval)
        {
            sleepHandle.WaitOne(interval);
        }

    }
}
