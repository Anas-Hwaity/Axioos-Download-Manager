using System.Collections.Generic;
using System.Threading;
using ADM.Core;

namespace ADM.Core.Downloader.Adaptive
{
    class CancelRequestor : ICancelRequster
    {
        private readonly List<HttpChunkDownloader> downloaders = new();
        private readonly CancelFlag _cancellationToken;
        private readonly ManualResetEvent quiesced = new(true);
        public ErrorCode Error { get; private set; } = ErrorCode.None;

        public CancelRequestor(CancelFlag cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public void CancelWithFatal(ErrorCode error)
        {
            this.Error = error;
            if (!_cancellationToken.IsCancellationRequested) _cancellationToken.Cancel();
            CancelAll();
        }

        public void NotifyTransientFailure()
        {
            lock (this)
            {
                foreach (var downloader in downloaders)
                {
                    if (!downloader.TransientFailure) return;
                }
                CancelWithFatal(ErrorCode.Generic);
            }
        }

        public bool RegisterThread(HttpChunkDownloader chunkDownloader)
        {
            lock (this)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    chunkDownloader.Cancel();
                    return false;
                }
                downloaders.Add(chunkDownloader);
                quiesced.Reset();
                return true;
            }
        }

        public void UnRegisterThread(HttpChunkDownloader chunkDownloader)
        {
            lock (this)
            {
                downloaders.Remove(chunkDownloader);
                if (downloaders.Count == 0) quiesced.Set();
            }
        }

        public void CancelAll()
        {
            lock (this)
            {
                var list = new List<HttpChunkDownloader>(downloaders);
                foreach (var downloader in list) downloader.Cancel();
            }
        }

        public bool WaitForQuiescence(int timeoutMilliseconds)
        {
            return quiesced.WaitOne(timeoutMilliseconds);
        }
    }
}
