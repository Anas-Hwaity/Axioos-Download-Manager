using System;
using System.Collections.Generic;

namespace ADM.Core.Downloader.Adaptive.Dash
{
    public class MultiSourceDASHDownloadInfo : MultiSourceDownloadInfo
    {
        public List<Uri> AudioSegments { get; set; }
        public List<Uri> VideoSegments { get; set; }
        public long Duration { get; set; }
        public string Url { get; set; }
        public string VideoMimeType { get; set; }
        public string AudioMimeType { get; set; }
        public string? VideoFormat { get; set; }
        public string? AudioFormat { get; set; }
    }
}
