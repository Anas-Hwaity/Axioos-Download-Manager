using System.Collections.Generic;
using ADM.Core;

namespace ADM.Wpf.UI.Dialogs.Settings.Services
{
    internal sealed class WebShellSettingsService
    {
        public string AppDirectory => Config.AppDir;

        public string ShellAppearance => Config.Instance.AppearanceShell ?? string.Empty;

        public string Theme => Config.Instance.AppearanceTheme ?? AxioosThemes.DefaultTheme;

        public string Backdrop => Config.Instance.AppearanceBackdrop ?? AxioosThemes.DefaultBackdrop;

        public string Accent => Config.Instance.AppearanceAccent ?? AxioosThemes.DefaultAccent;

        public int GlassLevel => Config.Instance.GlassmorphismLevel;

        public bool BrowserMonitoringEnabled => Config.Instance.IsBrowserMonitoringEnabled;

        public bool SpeedLimitEnabled => Config.Instance.EnableSpeedLimit && Config.Instance.DefaltDownloadSpeed > 0;

        public int SpeedLimitKiB => Config.Instance.DefaltDownloadSpeed;

        public IEnumerable<Category> Categories => Config.Instance.Categories ?? Config.DefaultCategories;

        public void Save(string shellAppearance, string? theme, string? backdrop, string? accent, int? glassLevel)
        {
            var config = Config.Instance;
            config.AppearanceShell = shellAppearance;
            config.AppearanceTheme = AxioosThemes.FindTheme(theme ?? config.AppearanceTheme).Id;
            config.AppearanceBackdrop = AxioosThemes.NormalizeBackdrop(backdrop ?? config.AppearanceBackdrop);
            config.AppearanceAccent = AxioosThemes.NormalizeAccent(accent ?? config.AppearanceAccent);
            if (glassLevel.HasValue)
            {
                config.GlassmorphismLevel = GlassThemeManager.Clamp(glassLevel.Value);
            }
            Config.SaveConfig();
        }
    }
}
