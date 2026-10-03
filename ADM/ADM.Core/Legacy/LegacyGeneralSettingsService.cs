using System;
using System.Collections.Generic;
using System.Linq;
using ADM.Core.Settings;

namespace ADM.Core.Legacy
{
    public sealed class LegacyGeneralSettingsService : IGeneralSettingsService
    {
        public GeneralSettingsState Load()
        {
            var config = Config.Instance;
            return new GeneralSettingsState
            {
                ShowProgressWindow = config.ShowProgressWindow,
                ShowDownloadCompleteWindow = config.ShowDownloadCompleteWindow,
                StartDownloadAutomatically = config.StartDownloadAutomatically,
                OverwriteExistingFiles = config.FileConflictResolution == FileConflictResolution.Overwrite,
                AllowSystemDarkTheme = config.AllowSystemDarkTheme,
                GlassmorphismLevel = Math.Max(0, Math.Min(100, config.GlassmorphismLevel)),
                AppearanceTheme = config.AppearanceTheme ?? "glacier",
                AppearanceBackdrop = config.AppearanceBackdrop ?? "aurora",
                AppearanceAccent = config.AppearanceAccent ?? "gradient",
                TempFolder = config.TempDir,
                MaxParallelDownloads = config.MaxParallelDownloads,
                AutomaticCategorySelection = config.FolderSelectionMode == FolderSelectionMode.Auto,
                DefaultDownloadFolder = config.DefaultDownloadFolder,
                DoubleClickOpenFile = config.DoubleClickOpenFile,
                Categories = config.Categories.ToArray()
            };
        }

        public void Save(GeneralSettingsState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var config = Config.Instance;
            config.ShowProgressWindow = state.ShowProgressWindow;
            config.ShowDownloadCompleteWindow = state.ShowDownloadCompleteWindow;
            config.StartDownloadAutomatically = state.StartDownloadAutomatically;
            config.FileConflictResolution = state.OverwriteExistingFiles ? FileConflictResolution.Overwrite : FileConflictResolution.AutoRename;
            config.TempDir = state.TempFolder;
            config.MaxParallelDownloads = state.MaxParallelDownloads;
            config.Categories = new List<Category>(state.Categories);
            config.FolderSelectionMode = state.AutomaticCategorySelection ? FolderSelectionMode.Auto : FolderSelectionMode.Manual;
            config.DefaultDownloadFolder = state.DefaultDownloadFolder;
            config.AllowSystemDarkTheme = state.AllowSystemDarkTheme;
            config.GlassmorphismLevel = Math.Max(0, Math.Min(100, state.GlassmorphismLevel));
            config.AppearanceTheme = state.AppearanceTheme ?? "glacier";
            config.AppearanceBackdrop = state.AppearanceBackdrop ?? "aurora";
            config.AppearanceAccent = state.AppearanceAccent ?? "gradient";
            config.DoubleClickOpenFile = state.DoubleClickOpenFile;
        }

        public IReadOnlyList<Category> GetDefaultCategories()
        {
            return Config.DefaultCategories.ToArray();
        }
    }
}
