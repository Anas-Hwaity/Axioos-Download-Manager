using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ADM.Core.Settings;

namespace ADM.Wpf.UI.Dialogs.LanguageSettings
{
    public sealed class LanguageSettingsViewModel : INotifyPropertyChanged
    {
        private readonly ILanguageSettingsService settingsService;
        private LanguageOption? selectedLanguage;

        public LanguageSettingsViewModel(ILanguageSettingsService settingsService)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<LanguageOption> Languages { get; } = new ObservableCollection<LanguageOption>();
        public LanguageOption? SelectedLanguage { get => selectedLanguage; set => Set(ref selectedLanguage, value); }

        public void Reload()
        {
            var state = settingsService.Load();
            Languages.Clear();
            foreach (var language in state.Languages) Languages.Add(language);
            SelectedLanguage = Languages.FirstOrDefault(x => x.Name == state.CurrentLanguage);
        }

        public bool SaveSelectedLanguage()
        {
            if (SelectedLanguage == null) return false;
            settingsService.SaveLanguage(SelectedLanguage.Name);
            return true;
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
