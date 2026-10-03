using System.Collections.Generic;

namespace ADM.Core.Settings
{
    public interface ICredentialSettingsService
    {
        IReadOnlyList<PasswordEntry> Load();
        void Save(IReadOnlyList<PasswordEntry> entries);
    }
}
