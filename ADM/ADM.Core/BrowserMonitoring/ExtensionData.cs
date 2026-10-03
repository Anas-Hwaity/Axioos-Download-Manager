using System;
using System.Collections.Generic;
using System.Text;

namespace ADM.Core.BrowserMonitoring
{
    public class ExtensionData
    {
        public string Url { get; set; }
        public string Cookie { get; set; }
        public Dictionary<string, List<string>> RequestHeaders { get; set; }
        public Dictionary<string, List<string>> ResponseHeaders { get; set; }
        public string File { get; set; }
        public string Method { get; set; }
        public string UserAgent { get; set; }
        public string TabUrl { get; set; }
        public string TabId { get; set; }
        public string PageSessionId { get; set; }
        public double? ObservedAtMonotonicMs { get; set; }
        public string SessionReason { get; set; }
        public string TabTitle { get; set; }
        public string Referer { get; set; }
        public long? FileSize { get; set; }
        public string MimeType { get; set; }
        public string Vid { get; set; }
        public string Browser { get; set; }
    }
}
