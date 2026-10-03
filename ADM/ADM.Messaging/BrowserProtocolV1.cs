using System;

namespace ADM.Core.BrowserMonitoring
{
    public static class BrowserProtocolV1
    {
        public const int ProtocolVersion = 1;
        public const int MaxApplicationMessageBytes = 512 * 1024;
        public const int MaxJsonDepth = 32;
        public const int MaxArrayItems = 256;
        public const int MaxStringChars = 65536;
        public const int MaxUrlChars = 16384;
        public const int MaxHeaderCount = 128;
        public const int MaxHeaderChars = 8192;
        public const string DesktopPipeName = ProductIdentity.BrowserProtocolPipeName;
        public const string BrowserHostProductIdentity = ProductIdentity.BrowserHostProductIdentity;

        public static readonly TimeSpan NativeHostConnectionTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan PassiveDesktopProbeTimeout = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan TakeoverDurableAckTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan CommandResponseTimeout = TimeSpan.FromSeconds(10);

        public static readonly int[] ReconnectBackoffMilliseconds = new[] { 250, 500, 1000, 2000, 5000 };
    }
}
