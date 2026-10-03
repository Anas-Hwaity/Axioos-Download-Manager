using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ADM.Core;
using ADM.Core.Settings;

namespace ADM.Wpf.UI.Dialogs.Settings.ViewModels
{
    public sealed class GeneralSettingsViewModel : INotifyPropertyChanged
    {
        private readonly IGeneralSettingsService settingsService;
        private bool showProgressWindow;
        private bool showDownloadCompleteWindow;
        private bool startDownloadAutomatically;
        private bool overwriteExistingFiles;
        private bool allowSystemDarkTheme;
        private int glassmorphismLevel = 100;
        private string appearanceTheme = AxioosThemes.DefaultTheme;
        private string appearanceBackdrop = AxioosThemes.DefaultBackdrop;
        private string appearanceAccent = AxioosThemes.DefaultAccent;
        private string tempFolder = string.Empty;
        private int maxParallelDownloads;
        private bool automaticCategorySelection;
        private string defaultDownloadFolder = string.Empty;
        private int doubleClickActionIndex;

        public GeneralSettingsViewModel(IGeneralSettingsService settingsService)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            MaxParallelDownloadOptions = Enumerable.Range(1, 50).ToArray();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<int> MaxParallelDownloadOptions { get; }
        public ObservableCollection<Category> Categories { get; } = new ObservableCollection<Category>();
        public bool ShowProgressWindow { get => showProgressWindow; set => Set(ref showProgressWindow, value); }
        public bool ShowDownloadCompleteWindow { get => showDownloadCompleteWindow; set => Set(ref showDownloadCompleteWindow, value); }
        public bool StartDownloadAutomatically { get => startDownloadAutomatically; set => Set(ref startDownloadAutomatically, value); }
        public bool OverwriteExistingFiles { get => overwriteExistingFiles; set => Set(ref overwriteExistingFiles, value); }
        public bool AllowSystemDarkTheme { get => allowSystemDarkTheme; set => Set(ref allowSystemDarkTheme, value); }
        public int GlassmorphismLevel { get => glassmorphismLevel; set => Set(ref glassmorphismLevel, Math.Max(0, Math.Min(100, value))); }
        public IReadOnlyList<AxioosTheme> ThemeOptions => AxioosThemes.Themes;
        public IReadOnlyList<AxioosChoice> BackdropOptions => AxioosThemes.Backdrops;
        public IReadOnlyList<AxioosChoice> AccentOptions => AxioosThemes.Accents;
        public IReadOnlyList<AxioosPreset> PresetOptions => AxioosThemes.Presets;
        public string AppearanceTheme { get => appearanceTheme; set => Set(ref appearanceTheme, AxioosThemes.FindTheme(value).Id); }
        public string AppearanceBackdrop { get => appearanceBackdrop; set => Set(ref appearanceBackdrop, AxioosThemes.NormalizeBackdrop(value)); }
        public string AppearanceAccent { get => appearanceAccent; set => Set(ref appearanceAccent, AxioosThemes.NormalizeAccent(value)); }

        public void ApplyPreset(string presetId)
        {
            var preset = AxioosThemes.Presets.FirstOrDefault(item => item.Id == presetId);
            if (preset == null) return;
            AppearanceTheme = preset.Theme;
            AppearanceBackdrop = preset.Backdrop;
            AppearanceAccent = preset.Accent;
            GlassmorphismLevel = preset.GlassLevel;
        }
        public string TempFolder { get => tempFolder; set => Set(ref tempFolder, value ?? string.Empty); }
        public int MaxParallelDownloads { get => maxParallelDownloads; set => Set(ref maxParallelDownloads, value); }
        public bool AutomaticCategorySelection { get => automaticCategorySelection; set => Set(ref automaticCategorySelection, value); }
        public string DefaultDownloadFolder { get => defaultDownloadFolder; set => Set(ref defaultDownloadFolder, value ?? string.Empty); }
        public int DoubleClickActionIndex { get => doubleClickActionIndex; set => Set(ref doubleClickActionIndex, value); }

        public void Reload()
        {
            var state = settingsService.Load();
            ShowProgressWindow = state.ShowProgressWindow;
            ShowDownloadCompleteWindow = state.ShowDownloadCompleteWindow;
            StartDownloadAutomatically = state.StartDownloadAutomatically;
            OverwriteExistingFiles = state.OverwriteExistingFiles;
            AllowSystemDarkTheme = state.AllowSystemDarkTheme;
            GlassmorphismLevel = state.GlassmorphismLevel;
            AppearanceTheme = state.AppearanceTheme;
            AppearanceBackdrop = state.AppearanceBackdrop;
            AppearanceAccent = state.AppearanceAccent;
            TempFolder = state.TempFolder;
            MaxParallelDownloads = state.MaxParallelDownloads;
            AutomaticCategorySelection = state.AutomaticCategorySelection;
            DefaultDownloadFolder = state.DefaultDownloadFolder;
            DoubleClickActionIndex = state.DoubleClickOpenFile ? 1 : 0;
            Categories.Clear();
            foreach (var category in state.Categories) Categories.Add(category);
        }

        public void Save()
        {
            settingsService.Save(new GeneralSettingsState
            {
                ShowProgressWindow = ShowProgressWindow,
                ShowDownloadCompleteWindow = ShowDownloadCompleteWindow,
                StartDownloadAutomatically = StartDownloadAutomatically,
                OverwriteExistingFiles = OverwriteExistingFiles,
                AllowSystemDarkTheme = AllowSystemDarkTheme,
                GlassmorphismLevel = GlassmorphismLevel,
                AppearanceTheme = AppearanceTheme,
                AppearanceBackdrop = AppearanceBackdrop,
                AppearanceAccent = AppearanceAccent,
                TempFolder = TempFolder,
                MaxParallelDownloads = MaxParallelDownloads,
                AutomaticCategorySelection = AutomaticCategorySelection,
                DefaultDownloadFolder = DefaultDownloadFolder,
                DoubleClickOpenFile = DoubleClickActionIndex == 1,
                Categories = Categories.ToArray()
            });
        }

        public Category GetCategoryAt(int index) => Categories[index];

        public void AddCategory(string displayName, string folder, string fileTypes)
        {
            Categories.Add(CreateCategory(Guid.NewGuid().ToString(), displayName, folder, fileTypes));
        }

        public void ReplaceCategory(int index, string displayName, string folder, string fileTypes)
        {
            var existing = Categories[index];
            Categories[index] = CreateCategory(existing.Name, displayName, folder, fileTypes);
        }

        public void RemoveCategoryAt(int index) => Categories.RemoveAt(index);

        public void ResetCategories()
        {
            Categories.Clear();
            foreach (var category in settingsService.GetDefaultCategories()) Categories.Add(category);
        }

        private static Category CreateCategory(string name, string displayName, string folder, string fileTypes)
        {
            return new Category
            {
                Name = name,
                DisplayName = displayName,
                DefaultFolder = folder,
                FileExtensions = new HashSet<string>(fileTypes.Replace("\r\n", string.Empty)
                    .Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            };
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
