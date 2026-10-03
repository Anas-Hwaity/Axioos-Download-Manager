using System;

namespace ADM.Wpf.UI
{
    public static class GlassThemeManager
    {
        public const int DefaultLevel = 100;

        internal enum Channel
        {
            Surface,
            Control,
            State,
            Border,
            BorderStrong,
            Accent,
            Specular,
            Aurora,
            CardShadow,
            MenuShadow
        }

        internal static readonly (string Key, Channel Kind)[] LevelDrivenResources =
        {
            ("GlassSurfaceBrush", Channel.Surface),
            ("GlassSurfaceStrongBrush", Channel.Surface),
            ("GlassPanelSurfaceBrush", Channel.Surface),
            ("GlassPanelSurfaceStrongBrush", Channel.Surface),
            ("GlassTableSurfaceBrush", Channel.Surface),
            ("ListViewBackcolor", Channel.Surface),
            ("CategoryListBackground", Channel.Surface),
            ("ControlBackcolor", Channel.Surface),
            ("StatusbarBackcolor", Channel.Surface),
            ("GlassInputBrush", Channel.Control),
            ("TextBackcolor", Channel.Control),
            ("SearchBackcolor", Channel.Control),
            ("NewDownloadTextBoxBackground", Channel.Control),
            ("ButtonBackcolor", Channel.Control),
            ("GlassScrollTrackBrush", Channel.Control),
            ("ListViewHeaderBackcolor", Channel.Control),
            ("GlassSurfaceHoverBrush", Channel.State),
            ("CategoryHighlight", Channel.State),
            ("CategoryMouseOverBackground", Channel.State),
            ("ButtonMouseOverBackcolor", Channel.State),
            ("ButtonMousePressedBackcolor", Channel.State),
            ("ToolButtonMouseOverBackcolor", Channel.State),
            ("ToolButtonMousePressedBackcolor", Channel.State),
            ("ListViewMouseOverBackcolor", Channel.State),
            ("ListViewSelectedBackcolor", Channel.State),
            ("ListViewHeaderHoverBackcolor", Channel.State),
            ("ProgressBarBackcolor", Channel.State),
            ("GlassBorderBrush", Channel.Border),
            ("GlassBorderStrongBrush", Channel.BorderStrong),
            ("GlassAccentSoftBrush", Channel.Accent),
            ("GlassSpecularBrush", Channel.Specular),
            ("GlassAuroraCyanBrush", Channel.Aurora),
            ("GlassAuroraPurpleBrush", Channel.Aurora),
            ("GlassAuroraBlueBrush", Channel.Aurora),
            ("GlassCardShadow", Channel.CardShadow),
            ("GlassMenuShadow", Channel.MenuShadow)
        };

        public static int CurrentLevel => GlassAppearanceService.Current?.CurrentLevel ?? DefaultLevel;

        public static int Clamp(int level) => Math.Max(0, Math.Min(100, level));

        public static void Apply(int level)
        {
            var application = System.Windows.Application.Current;
            var service = GlassAppearanceService.Current;
            if (application == null || service == null) return;
            service.Apply(application, level);
        }

        internal static double ChannelOpacity(Channel channel, int level)
        {
            var t = Clamp(level) / 100.0;
            double value;
            switch (channel)
            {
                case Channel.Surface: value = 1.00 - (0.30 * t); break;
                case Channel.Control: value = 1.00 - (0.22 * t); break;
                case Channel.State: value = 1.00 - (0.16 * t); break;
                case Channel.Border: value = 0.12 + (0.34 * t); break;
                case Channel.BorderStrong: value = 0.24 + (0.42 * t); break;
                case Channel.Accent: value = 0.24 + (0.68 * t); break;
                case Channel.Specular: value = 0.02 + (0.72 * t); break;
                case Channel.Aurora: value = 0.78 * t; break;
                case Channel.CardShadow: value = 0.10 + (0.58 * t); break;
                case Channel.MenuShadow: value = 0.12 + (0.64 * t); break;
                default: value = 1.0; break;
            }
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
