using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ADM.Core.Settings;

namespace ADM.Wpf.UI.Dialogs.Settings.ViewModels
{
    public sealed class AdvancedSettingsViewModel : INotifyPropertyChanged
    {
        private readonly IAdvancedSettingsService settingsService;
        private bool shutdownAfterAllFinished;
        private bool keepPCAwake;
        private bool runCommandAfterCompletion;
        private bool scanWithAntiVirus;
        private bool autoStartEnabled;
        private string afterCompletionCommand = string.Empty;
        private string antiVirusExecutable = string.Empty;
        private string antiVirusArgs = string.Empty;
        private string fallbackUserAgent = string.Empty;

        public AdvancedSettingsViewModel(IAdvancedSettingsService settingsService, bool isAutoStartAvailable)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            IsAutoStartAvailable = isAutoStartAvailable;
            ResetUserAgentCommand = new ActionCommand(() => FallbackUserAgent = settingsService.DefaultFallbackUserAgent);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ICommand ResetUserAgentCommand { get; }
        public bool IsAutoStartAvailable { get; }
        public bool ShutdownAfterAllFinished { get => shutdownAfterAllFinished; set => Set(ref shutdownAfterAllFinished, value); }
        public bool KeepPCAwake { get => keepPCAwake; set => Set(ref keepPCAwake, value); }
        public bool RunCommandAfterCompletion { get => runCommandAfterCompletion; set => Set(ref runCommandAfterCompletion, value); }
        public bool ScanWithAntiVirus { get => scanWithAntiVirus; set => Set(ref scanWithAntiVirus, value); }
        public bool AutoStartEnabled { get => autoStartEnabled; set => Set(ref autoStartEnabled, value); }
        public string AfterCompletionCommand { get => afterCompletionCommand; set => Set(ref afterCompletionCommand, value ?? string.Empty); }
        public string AntiVirusExecutable { get => antiVirusExecutable; set => Set(ref antiVirusExecutable, value ?? string.Empty); }
        public string AntiVirusArgs { get => antiVirusArgs; set => Set(ref antiVirusArgs, value ?? string.Empty); }
        public string FallbackUserAgent { get => fallbackUserAgent; set => Set(ref fallbackUserAgent, value ?? string.Empty); }

        public void Reload()
        {
            var state = settingsService.Load();
            ShutdownAfterAllFinished = state.ShutdownAfterAllFinished;
            KeepPCAwake = state.KeepPCAwake;
            RunCommandAfterCompletion = state.RunCommandAfterCompletion;
            ScanWithAntiVirus = state.ScanWithAntiVirus;
            AutoStartEnabled = state.AutoStartEnabled;
            AfterCompletionCommand = state.AfterCompletionCommand;
            AntiVirusExecutable = state.AntiVirusExecutable;
            AntiVirusArgs = state.AntiVirusArgs;
            FallbackUserAgent = state.FallbackUserAgent;
        }

        public void Save()
        {
            settingsService.Save(new AdvancedSettingsState
            {
                ShutdownAfterAllFinished = ShutdownAfterAllFinished,
                KeepPCAwake = KeepPCAwake,
                RunCommandAfterCompletion = RunCommandAfterCompletion,
                ScanWithAntiVirus = ScanWithAntiVirus,
                AutoStartEnabled = AutoStartEnabled,
                AfterCompletionCommand = AfterCompletionCommand,
                AntiVirusExecutable = AntiVirusExecutable,
                AntiVirusArgs = AntiVirusArgs,
                FallbackUserAgent = FallbackUserAgent
            });
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
