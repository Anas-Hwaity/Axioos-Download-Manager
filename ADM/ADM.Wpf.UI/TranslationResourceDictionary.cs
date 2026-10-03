using System;
using System.Linq;
using System.Windows;
using Translations;
using ADM.Core.Settings;

namespace ADM.Wpf.UI
{
    internal class TranslationResourceDictionary : ResourceDictionary
    {
        internal TranslationResourceDictionary(ILanguageSettingsService languageSettingsService)
        {
            if (languageSettingsService == null) throw new ArgumentNullException(nameof(languageSettingsService));
            var state = languageSettingsService.Load();
            var language = state.Languages.FirstOrDefault(item => item.Name == state.CurrentLanguage);
            if (language == null) return;
            TextResource.Load(language.File);
            foreach (var key in TextResource.GetKeys())
            {
                Add(key, TextResource.GetText(key));
            }
        }
    }
}
