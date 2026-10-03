using System;

namespace ADM.Core.Settings
{
    public sealed class AdvancedSettingsState
    {
        public bool ShutdownAfterAllFinished { get; set; }
        public bool KeepPCAwake { get; set; }
        public bool RunCommandAfterCompletion { get; set; }
        public bool ScanWithAntiVirus { get; set; }
        public bool AutoStartEnabled { get; set; }
        public string AfterCompletionCommand { get; set; } = string.Empty;
        public string AntiVirusExecutable { get; set; } = string.Empty;
        public string AntiVirusArgs { get; set; } = string.Empty;
        public string FallbackUserAgent { get; set; } = string.Empty;
    }

    public interface IAdvancedSettingsService
    {
        AdvancedSettingsState Load();
        void Save(AdvancedSettingsState state);
        void ResetFallbackUserAgent();
    }
}
