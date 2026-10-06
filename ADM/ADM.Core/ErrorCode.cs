using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace ADM.Core
{
    public enum ErrorCode : int
    {
        None, Generic, NonResumable, AssemblingFailed,
        MaxRetryFailed, InvalidResponse, FFmpegNotFound,
        FFmpegError, DiskError, SessionExpired, SourceChanged
    }
}
