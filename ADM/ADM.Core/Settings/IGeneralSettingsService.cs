using System.Collections.Generic;

namespace ADM.Core.Settings
{
    public sealed class GeneralSettingsState
    {
        public bool ShowProgressWindow { get; set; }
        public bool ShowDownloadCompleteWindow { get; set; }
        public bool StartDownloadAutomatically { get; set; }
        public bool OverwriteExistingFiles { get; set; }
        public bool AllowSystemDarkTheme { get; set; }
        public int GlassmorphismLevel { get; set; } = 100;
        public string AppearanceTheme { get; set; } = "glacier";
        public string AppearanceBackdrop { get; set; } = "aurora";
        public string AppearanceAccent { get; set; } = "gradient";
        public string TempFolder { get; set; } = string.Empty;
        public int MaxParallelDownloads { get; set; }
        public bool AutomaticCategorySelection { get; set; }
        public string DefaultDownloadFolder { get; set; } = string.Empty;
        public bool DoubleClickOpenFile { get; set; }
        public IReadOnlyList<Category> Categories { get; set; } = new Category[0];
    }

    public interface IGeneralSettingsService
    {
        GeneralSettingsState Load();
        void Save(GeneralSettingsState state);
        IReadOnlyList<Category> GetDefaultCategories();
    }
}
