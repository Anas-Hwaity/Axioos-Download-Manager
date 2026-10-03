using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ADM.Core;
using ADM.Core.Settings;

namespace ADM.Wpf.UI.Dialogs.Settings.ViewModels
{
    public sealed class NetworkSettingsViewModel : INotifyPropertyChanged
    {
        private readonly INetworkSettingsService settingsService;
        private int networkTimeout;
        private int maxSegments;
        private int maxRetry;
        private string maxSpeedLimit = "0";
        private bool enableSpeedLimit;
        private int proxyTypeIndex;
        private string proxyHost = string.Empty;
        private string proxyPort = "0";
        private string proxyUser = string.Empty;
        private string proxyPassword = string.Empty;

        public NetworkSettingsViewModel(INetworkSettingsService settingsService)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            TimeoutOptions = Enumerable.Range(1, 300).ToArray();
            MaxSegmentOptions = Enumerable.Range(1, 64).ToArray();
            MaxRetryOptions = Enumerable.Range(1, 100).ToArray();
            OpenSystemProxySettingsCommand = new ActionCommand(settingsService.OpenSystemProxySettings);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<int> TimeoutOptions { get; }
        public IReadOnlyList<int> MaxSegmentOptions { get; }
        public IReadOnlyList<int> MaxRetryOptions { get; }
        public ICommand OpenSystemProxySettingsCommand { get; }
        public int NetworkTimeout { get => networkTimeout; set => Set(ref networkTimeout, value); }
        public int MaxSegments { get => maxSegments; set => Set(ref maxSegments, value); }
        public int MaxRetry { get => maxRetry; set => Set(ref maxRetry, value); }
        public string MaxSpeedLimit { get => maxSpeedLimit; set => Set(ref maxSpeedLimit, value ?? string.Empty); }
        public bool EnableSpeedLimit { get => enableSpeedLimit; set => Set(ref enableSpeedLimit, value); }
        public int ProxyTypeIndex
        {
            get => proxyTypeIndex;
            set
            {
                if (Set(ref proxyTypeIndex, value))
                {
                    OnPropertyChanged(nameof(IsManualProxy));
                }
            }
        }
        public bool IsManualProxy => ProxyTypeIndex == (int)ProxyType.Custom;
        public string ProxyHost { get => proxyHost; set => Set(ref proxyHost, value ?? string.Empty); }
        public string ProxyPort { get => proxyPort; set => Set(ref proxyPort, value ?? string.Empty); }
        public string ProxyUser { get => proxyUser; set => Set(ref proxyUser, value ?? string.Empty); }
        public string ProxyPassword { get => proxyPassword; set => Set(ref proxyPassword, value ?? string.Empty); }

        public void Reload()
        {
            var state = settingsService.Load();
            NetworkTimeout = state.NetworkTimeout;
            MaxSegments = state.MaxSegments;
            MaxRetry = state.MaxRetry;
            MaxSpeedLimit = state.MaxSpeedLimit;
            EnableSpeedLimit = state.EnableSpeedLimit;
            ProxyTypeIndex = (int)state.ProxyType;
            ProxyHost = state.ProxyHost;
            ProxyPort = state.ProxyPort;
            ProxyUser = state.ProxyUser;
            ProxyPassword = state.ProxyPassword;
        }

        public void Save()
        {
            settingsService.Save(new NetworkSettingsState
            {
                NetworkTimeout = NetworkTimeout,
                MaxSegments = MaxSegments,
                MaxRetry = MaxRetry,
                MaxSpeedLimit = MaxSpeedLimit,
                EnableSpeedLimit = EnableSpeedLimit,
                ProxyType = (ProxyType)ProxyTypeIndex,
                ProxyHost = ProxyHost,
                ProxyPort = ProxyPort,
                ProxyUser = ProxyUser,
                ProxyPassword = ProxyPassword
            });
        }


        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string? propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal sealed class ActionCommand : ICommand
    {
        private readonly Action execute;

        internal ActionCommand(Action execute)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
