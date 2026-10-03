using System;

namespace ADM.Core.Settings
{
    public sealed class NetworkSettingsState
    {
        public int NetworkTimeout { get; set; }
        public int MaxSegments { get; set; }
        public int MaxRetry { get; set; }
        public string MaxSpeedLimit { get; set; } = "0";
        public bool EnableSpeedLimit { get; set; }
        public ProxyType ProxyType { get; set; }
        public string ProxyHost { get; set; } = string.Empty;
        public string ProxyPort { get; set; } = "0";
        public string ProxyUser { get; set; } = string.Empty;
        public string ProxyPassword { get; set; } = string.Empty;
    }

    public interface INetworkSettingsService
    {
        NetworkSettingsState Load();
        void Save(NetworkSettingsState state);
        void OpenSystemProxySettings();
    }
}
