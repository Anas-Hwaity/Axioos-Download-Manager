using System;
using System.Runtime.InteropServices;

using SafeWinHttpHandle = Interop.WinHttp.SafeWinHttpHandle;

namespace System.Net.Http
{
    internal sealed class WinInetProxyHelper
    {
        private const int RecentAutoDetectionInterval = 120_000;
        private readonly string? _autoConfigUrl, _proxy, _proxyBypass;
        private readonly bool _autoDetect;
        private readonly bool _useProxy;
        private bool _autoDetectionFailed;
        private int _lastTimeAutoDetectionFailed;

        public WinInetProxyHelper()
        {
            Interop.WinHttp.WINHTTP_CURRENT_USER_IE_PROXY_CONFIG proxyConfig = default;

            try
            {
                if (Interop.WinHttp.WinHttpGetIEProxyConfigForCurrentUser(out proxyConfig))
                {
                    _autoConfigUrl = Marshal.PtrToStringUni(proxyConfig.AutoConfigUrl)!;
                    _autoDetect = proxyConfig.AutoDetect;
                    _proxy = Marshal.PtrToStringUni(proxyConfig.Proxy)!;
                    _proxyBypass = Marshal.PtrToStringUni(proxyConfig.ProxyBypass)!;

                    _useProxy = true;
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                }
            }

            finally
            {
                Marshal.FreeHGlobal(proxyConfig.AutoConfigUrl);
                Marshal.FreeHGlobal(proxyConfig.Proxy);
                Marshal.FreeHGlobal(proxyConfig.ProxyBypass);
            }
        }

        public string? AutoConfigUrl => _autoConfigUrl;

        public bool AutoDetect => _autoDetect;

        public bool AutoSettingsUsed => AutoDetect || !string.IsNullOrEmpty(AutoConfigUrl);

        public bool ManualSettingsUsed => !string.IsNullOrEmpty(Proxy);

        public bool ManualSettingsOnly => !AutoSettingsUsed && ManualSettingsUsed;

        public string? Proxy => _proxy;

        public string? ProxyBypass => _proxyBypass;

        public bool RecentAutoDetectionFailure =>
            _autoDetectionFailed &&
            Environment.TickCount - _lastTimeAutoDetectionFailed <= RecentAutoDetectionInterval;

        public bool GetProxyForUrl(
            SafeWinHttpHandle? sessionHandle,
            Uri uri,
            out Interop.WinHttp.WINHTTP_PROXY_INFO proxyInfo)
        {
            proxyInfo.AccessType = Interop.WinHttp.WINHTTP_ACCESS_TYPE_NO_PROXY;
            proxyInfo.Proxy = IntPtr.Zero;
            proxyInfo.ProxyBypass = IntPtr.Zero;

            if (!_useProxy)
            {
                return false;
            }

            bool useProxy = false;

            Interop.WinHttp.WINHTTP_AUTOPROXY_OPTIONS autoProxyOptions;
            autoProxyOptions.AutoConfigUrl = AutoConfigUrl;
            autoProxyOptions.AutoDetectFlags = AutoDetect ?
                (Interop.WinHttp.WINHTTP_AUTO_DETECT_TYPE_DHCP | Interop.WinHttp.WINHTTP_AUTO_DETECT_TYPE_DNS_A) : 0;
            autoProxyOptions.AutoLoginIfChallenged = false;
            autoProxyOptions.Flags =
                (AutoDetect ? Interop.WinHttp.WINHTTP_AUTOPROXY_AUTO_DETECT : 0) |
                (!string.IsNullOrEmpty(AutoConfigUrl) ? Interop.WinHttp.WINHTTP_AUTOPROXY_CONFIG_URL : 0);
            autoProxyOptions.Reserved1 = IntPtr.Zero;
            autoProxyOptions.Reserved2 = 0;


#pragma warning disable CA1845
            string destination = uri.AbsoluteUri;
            if (uri.Scheme == "wss")
            {
                destination = "https" + destination.Substring("wss".Length);
            }
            else if (uri.Scheme == "ws")
            {
                destination = "http" + destination.Substring("ws".Length);
            }
#pragma warning restore CA1845

            var repeat = false;
            do
            {
                _autoDetectionFailed = false;
                if (Interop.WinHttp.WinHttpGetProxyForUrl(
                    sessionHandle!,
                    destination,
                    ref autoProxyOptions,
                    out proxyInfo))
                {
                    useProxy = true;

                    break;
                }
                else
                {
                    var lastError = Marshal.GetLastWin32Error();

                    if (lastError == Interop.WinHttp.ERROR_WINHTTP_LOGIN_FAILURE)
                    {
                        if (repeat)
                        {
                            break;
                        }
                        else
                        {
                            repeat = true;
                            autoProxyOptions.AutoLoginIfChallenged = true;
                        }
                    }
                    else
                    {
                        if (lastError == Interop.WinHttp.ERROR_WINHTTP_AUTODETECTION_FAILED)
                        {
                            _autoDetectionFailed = true;
                            _lastTimeAutoDetectionFailed = Environment.TickCount;
                        }

                        break;
                    }
                }
            } while (repeat);

            if (!useProxy && !string.IsNullOrEmpty(Proxy))
            {
                proxyInfo.AccessType = Interop.WinHttp.WINHTTP_ACCESS_TYPE_NAMED_PROXY;
                proxyInfo.Proxy = Marshal.StringToHGlobalUni(Proxy);
                proxyInfo.ProxyBypass = string.IsNullOrEmpty(ProxyBypass) ?
                    IntPtr.Zero : Marshal.StringToHGlobalUni(ProxyBypass);

                useProxy = true;
            }

            return useProxy;
        }
    }
}