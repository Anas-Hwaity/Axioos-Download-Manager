using System;
using System.Collections.Generic;
using System.IO;
using ADM.Core.Settings;

namespace ADM.Core.Legacy
{
    public sealed class LegacyLanguageSettingsService : ILanguageSettingsService
    {
        public LanguageSettingsState Load()
        {
            var languages = new List<LanguageOption>();
            var indexFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Lang\index.txt");
            if (File.Exists(indexFile))
            {
                foreach (var line in File.ReadAllLines(indexFile))
                {
                    var index = line.IndexOf("=");
                    if (index > 0)
                    {
                        languages.Add(new LanguageOption
                        {
                            Name = line.Substring(0, index),
                            File = line.Substring(index + 1)
                        });
                    }
                }
            }
            return new LanguageSettingsState
            {
                Languages = languages,
                CurrentLanguage = Config.Instance.Language
            };
        }

        public void SaveLanguage(string language)
        {
            Config.Instance.Language = language;
            Config.SaveConfig();
        }
    }
}
