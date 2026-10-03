using System;
using ADM.Core.Settings;

namespace ADM.Core.Legacy
{
    public sealed class LegacySettingsCommitService : ISettingsCommitService
    {
        private readonly IApplicationRuntimeContext runtimeContext;

        public LegacySettingsCommitService(IApplicationRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        }

        public void Commit()
        {
            Config.SaveConfig();
            runtimeContext.BroadcastConfigChange();
        }
    }
}
