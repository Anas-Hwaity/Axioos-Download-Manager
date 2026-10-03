using System.Collections.Generic;

namespace ADM.Core.Settings
{
    public sealed class LanguageOption
    {
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    public sealed class LanguageSettingsState
    {
        public IReadOnlyList<LanguageOption> Languages { get; set; } = new LanguageOption[0];
        public string CurrentLanguage { get; set; } = string.Empty;
    }

    public interface ILanguageSettingsService
    {
        LanguageSettingsState Load();
        void SaveLanguage(string language);
    }
}
