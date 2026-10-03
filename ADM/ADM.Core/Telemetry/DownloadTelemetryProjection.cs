using System;
using System.Collections.Generic;

namespace ADM.Core.Telemetry
{
    public sealed class DownloadTransferTelemetryProjection
    {
        public DownloadTransferTelemetryProjection(DownloadResumeCapability resumeCapability, int activeConnections, IEnumerable<DownloadRangeTelemetry> ranges)
        {
            ResumeCapability = resumeCapability;
            ActiveConnections = Math.Max(0, activeConnections);
            Ranges = new List<DownloadRangeTelemetry>(ranges ?? Array.Empty<DownloadRangeTelemetry>()).AsReadOnly();
        }

        public DownloadResumeCapability ResumeCapability { get; }
        public int ActiveConnections { get; }
        public IReadOnlyList<DownloadRangeTelemetry> Ranges { get; }
    }

    public interface IDownloadTelemetrySource
    {
        DownloadTransferTelemetryProjection CaptureTransferTelemetry();
    }
}
