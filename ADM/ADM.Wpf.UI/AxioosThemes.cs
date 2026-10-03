using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace ADM.Wpf.UI
{
    public sealed class AxioosTheme
    {
        public AxioosTheme(string id, string displayName, bool isDark, string background, string glowA, string glowB,
            string text, string muted, string accentA, string accentB, string onAccent)
        {
            Id = id;
            DisplayName = displayName;
            IsDark = isDark;
            Background = Parse(background);
            GlowA = Parse(glowA);
            GlowB = Parse(glowB);
            Text = Parse(text);
            Muted = Parse(muted);
            AccentA = Parse(accentA);
            AccentB = Parse(accentB);
            OnAccent = Parse(onAccent);
        }

        internal AxioosTheme(string id, string displayName, bool isDark, Color background, Color glowA, Color glowB,
            Color text, Color muted, Color accentA, Color accentB, Color onAccent)
        {
            Id = id;
            DisplayName = displayName;
            IsDark = isDark;
            Background = background;
            GlowA = glowA;
            GlowB = glowB;
            Text = text;
            Muted = muted;
            AccentA = accentA;
            AccentB = accentB;
            OnAccent = onAccent;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public bool IsDark { get; }
        public Color Background { get; }
        public Color GlowA { get; }
        public Color GlowB { get; }
        public Color Text { get; }
        public Color Muted { get; }
        public Color AccentA { get; }
        public Color AccentB { get; }
        public Color OnAccent { get; }

        public SolidColorBrush SwatchBackground => Frozen(new SolidColorBrush(Background));

        public LinearGradientBrush SwatchAccent
        {
            get
            {
                var brush = new LinearGradientBrush(AccentA, AccentB, 45);
                brush.Freeze();
                return brush;
            }
        }

        private static SolidColorBrush Frozen(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static Color Parse(string hex)
        {
            var value = hex.TrimStart('#');
            if (value.Length != 6) throw new ArgumentException("Theme colours must be #RRGGBB.", nameof(hex));
            return Color.FromRgb(
                Convert.ToByte(value.Substring(0, 2), 16),
                Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16));
        }
    }

    public sealed class AxioosChoice
    {
        public AxioosChoice(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }

    public sealed class AxioosPreset
    {
        public AxioosPreset(string id, string displayName, string theme, string backdrop, string accent, int glassLevel)
        {
            Id = id;
            DisplayName = displayName;
            Theme = theme;
            Backdrop = backdrop;
            Accent = accent;
            GlassLevel = glassLevel;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Theme { get; }
        public string Backdrop { get; }
        public string Accent { get; }
        public int GlassLevel { get; }
    }

    public static class AxioosThemes
    {
        public const string DefaultTheme = "glacier";
        public const string DefaultBackdrop = "aurora";
        public const string DefaultAccent = "gradient";

        public static IReadOnlyList<AxioosTheme> Themes { get; } = new[]
        {
            new AxioosTheme("glacier", "Glacier", true, "#0A1120", "#1B3A5C", "#33205A", "#E8F1FF", "#8EA6C4", "#5EE7F7", "#7B8CFF", "#07101F"),
            new AxioosTheme("aurora", "Aurora", true, "#07131A", "#0F4D45", "#2A1A55", "#E6FFF8", "#8FBFB2", "#4EF0B5", "#A78BFA", "#04140F"),
            new AxioosTheme("ocean", "Ocean", true, "#050D1F", "#0B3A78", "#062A4A", "#E6F0FF", "#87A2C7", "#38BDF8", "#2563EB", "#03101F"),
            new AxioosTheme("orchid", "Orchid", true, "#130A1A", "#4A1850", "#1E2A66", "#FBEAFF", "#C3A3CF", "#FF7AD9", "#9F7AFF", "#1A0620"),
            new AxioosTheme("ember", "Ember", true, "#140C0A", "#5A2412", "#3D1030", "#FFF1EA", "#C7A89A", "#FFB347", "#FF5F6D", "#1A0B06"),
            new AxioosTheme("crimson", "Crimson", true, "#12070A", "#5C0F1E", "#2A0B2E", "#FFECEF", "#C79AA3", "#FF4D6D", "#FF8FA3", "#1F0509"),
            new AxioosTheme("gold", "Midnight Gold", true, "#0B0F1C", "#1C2B4F", "#3A2D10", "#F5F1E6", "#A9A38F", "#FFD166", "#F4A261", "#1A1405"),
            new AxioosTheme("lime", "Cyber Lime", true, "#070807", "#1D2A0A", "#0A1F1F", "#F0FFE8", "#97A88F", "#C6FF3D", "#3DFFC6", "#0A0F02"),
            new AxioosTheme("graphite", "Graphite", true, "#111214", "#2A2C30", "#1C1D20", "#F2F3F5", "#9A9EA6", "#FFFFFF", "#B8BCC4", "#111214"),
            new AxioosTheme("paper", "Paper", false, "#EEF2F8", "#CFE0FF", "#E9DCFF", "#172033", "#5D6A82", "#2F6BFF", "#7C4DFF", "#FFFFFF"),
            new AxioosTheme("solar", "Solar", false, "#F7F1E8", "#FFD9A8", "#FFE9D6", "#2B2118", "#7C6A58", "#F07B1D", "#E0452B", "#FFFFFF"),
            new AxioosTheme("sakura", "Sakura", false, "#FBF0F3", "#FFD1DC", "#E3E1FF", "#2D1A22", "#85616E", "#E8457A", "#9B5DE5", "#FFFFFF")
        };

        public static IReadOnlyList<AxioosChoice> Backdrops { get; } = new[]
        {
            new AxioosChoice("aurora", "Aurora"),
            new AxioosChoice("mesh", "Mesh"),
            new AxioosChoice("spotlight", "Spotlight"),
            new AxioosChoice("grid", "Grid"),
            new AxioosChoice("grain", "Grain"),
            new AxioosChoice("flat", "Flat")
        };

        public static IReadOnlyList<AxioosChoice> Accents { get; } = new[]
        {
            new AxioosChoice("gradient", "Gradient"),
            new AxioosChoice("solid", "Solid")
        };

        public static IReadOnlyList<AxioosPreset> Presets { get; } = new[]
        {
            new AxioosPreset("signature", "Signature", "glacier", "aurora", "gradient", 60),
            new AxioosPreset("night-shift", "Night shift", "graphite", "grain", "solid", 35),
            new AxioosPreset("neon", "Neon", "lime", "grid", "gradient", 45),
            new AxioosPreset("daylight", "Daylight", "paper", "spotlight", "gradient", 70)
        };

        public static AxioosTheme FindTheme(string? id) =>
            Themes.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

        public static string NormalizeBackdrop(string? id) =>
            Backdrops.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))?.Id ?? DefaultBackdrop;

        public static string NormalizeAccent(string? id) =>
            Accents.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))?.Id ?? DefaultAccent;
    }
}
