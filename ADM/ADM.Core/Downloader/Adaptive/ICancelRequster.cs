using ADM.Core;

namespace ADM.Core.Downloader.Adaptive
{
    public interface ICancelRequster
    {
        public ErrorCode Error { get; }

        void CancelWithFatal(ErrorCode error);
        void NotifyTransientFailure();
        bool RegisterThread(HttpChunkDownloader chunkDownloader);
        void UnRegisterThread(HttpChunkDownloader chunkDownloader);
        void CancelAll();
        bool WaitForQuiescence(int timeoutMilliseconds);
    }
}
