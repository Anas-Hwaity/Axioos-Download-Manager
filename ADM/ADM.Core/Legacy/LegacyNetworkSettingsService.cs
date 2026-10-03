using System;
using ADM.Core.Settings;
using ADM.Core.Util;

namespace ADM.Core.Legacy
{
    public sealed class LegacyNetworkSettingsService : INetworkSettingsService
    {
        public NetworkSettingsState Load()
        {
            var config = Config.Instance;
            var proxy = config.Proxy;
            return new NetworkSettingsState
            {
                NetworkTimeout = config.NetworkTimeout,
                MaxSegments = config.MaxSegments,
                MaxRetry = config.MaxRetry,
                MaxSpeedLimit = config.DefaltDownloadSpeed.ToString(),
                EnableSpeedLimit = config.EnableSpeedLimit,
                ProxyType = proxy?.ProxyType ?? ProxyType.System,
                ProxyHost = proxy?.Host ?? string.Empty,
                ProxyPort = (proxy?.Port ?? 0).ToString(),
                ProxyUser = proxy?.UserName ?? string.Empty,
                ProxyPassword = proxy?.Password ?? string.Empty
            };
        }

        public void Save(NetworkSettingsState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var config = Config.Instance;
            config.NetworkTimeout = state.NetworkTimeout;
            config.MaxSegments = state.MaxSegments;
            config.MaxRetry = state.MaxRetry;
            if (Int32.TryParse(state.MaxSpeedLimit, out int speed))
            {
                config.DefaltDownloadSpeed = speed;
            }
            config.EnableSpeedLimit = state.EnableSpeedLimit;
            Int32.TryParse(state.ProxyPort, out int port);
            config.Proxy = new ProxyInfo
            {
                ProxyType = state.ProxyType,
                Host = state.ProxyHost,
                UserName = state.ProxyUser,
                Password = state.ProxyPassword,
                Port = port
            };
        }

        public void OpenSystemProxySettings()
        {
            PlatformHelper.OpenWindowsProxySettings();
        }
    }
}
