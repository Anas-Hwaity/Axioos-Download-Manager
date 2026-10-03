using System;
using System.Collections.Generic;
using System.Linq;
using ADM.Core.Settings;

namespace ADM.Core.Legacy
{
    public sealed class LegacyCredentialSettingsService : ICredentialSettingsService
    {
        public IReadOnlyList<PasswordEntry> Load()
        {
            return Config.Instance.UserCredentials.ToArray();
        }

        public void Save(IReadOnlyList<PasswordEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            Config.Instance.UserCredentials = new List<PasswordEntry>(entries);
        }
    }
}
