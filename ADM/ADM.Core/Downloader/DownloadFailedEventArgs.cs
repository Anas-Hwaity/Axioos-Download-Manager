using System;
using ADM.Core;

namespace ADM.Core.Downloader
{
    public class DownloadFailedEventArgs : EventArgs
    {
        public DownloadFailedEventArgs(ErrorCode errorCode)
        {
            ErrorCode = errorCode;
        }
        public ErrorCode ErrorCode { get; }
    }
}
