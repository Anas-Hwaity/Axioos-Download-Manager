using System;
using ADM.Core.Settings;
using ADM.Core.Util;

namespace ADM.Core.Legacy
{
    public sealed class LegacyAdvancedSettingsService : IAdvancedSettingsService
    {
        public AdvancedSettingsState Load()
        {
            var config = Config.Instance;
            return new AdvancedSettingsState
            {
                ShutdownAfterAllFinished = config.ShutdownAfterAllFinished,
                KeepPCAwake = config.KeepPCAwake,
                RunCommandAfterCompletion = config.RunCommandAfterCompletion,
                ScanWithAntiVirus = config.ScanWithAntiVirus,
                AutoStartEnabled = PlatformHelper.IsAutoStartEnabled(),
                AfterCompletionCommand = config.AfterCompletionCommand,
                AntiVirusExecutable = config.AntiVirusExecutable,
                AntiVirusArgs = config.AntiVirusArgs,
                FallbackUserAgent = config.FallbackUserAgent
            };
        }

        public void Save(AdvancedSettingsState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var config = Config.Instance;
            config.ShutdownAfterAllFinished = state.ShutdownAfterAllFinished;
            config.KeepPCAwake = state.KeepPCAwake;
            config.RunCommandAfterCompletion = state.RunCommandAfterCompletion;
            config.ScanWithAntiVirus = state.ScanWithAntiVirus;
            PlatformHelper.EnableAutoStart(state.AutoStartEnabled);
            config.AfterCompletionCommand = state.AfterCompletionCommand;
            config.AntiVirusExecutable = state.AntiVirusExecutable;
            config.AntiVirusArgs = state.AntiVirusArgs;
            config.FallbackUserAgent = state.FallbackUserAgent;
        }

        public void ResetFallbackUserAgent()
        {
            Config.Instance.FallbackUserAgent = Config.DefaultFallbackUserAgent;
        }
    }
}
