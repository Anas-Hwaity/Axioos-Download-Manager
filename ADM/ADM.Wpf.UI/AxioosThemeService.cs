using System;
using System.Windows;
using System.Windows.Media;
using TraceLog;
using ADM.Wpf.UI.Diagnostics;

namespace ADM.Wpf.UI
{
    public sealed class AxioosThemeService
    {
        private static Color White => Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static Color Black => Color.FromRgb(0x00, 0x00, 0x00);
        private ResourceDictionary? themeDictionary;

        public static AxioosThemeService? Current => (System.Windows.Application.Current as App)?.ThemeService;

        public string ThemeId { get; private set; } = AxioosThemes.DefaultTheme;

        public static bool CurrentIsDark => AxioosThemes.FindTheme(Current?.ThemeId).IsDark;
        public string BackdropId { get; private set; } = AxioosThemes.DefaultBackdrop;
        public string AccentId { get; private set; } = AxioosThemes.DefaultAccent;

        public static void Apply(string? theme, string? backdrop, string? accent)
        {
            var application = System.Windows.Application.Current;
            var service = Current;
            if (application == null || service == null) return;
            service.Apply(application, theme, backdrop, accent);
        }

        public void Apply(System.Windows.Application application, string? theme, string? backdrop, string? accent)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            var chosen = AxioosThemes.FindTheme(theme);
            var palette = chosen;
            var backdropId = AxioosThemes.NormalizeBackdrop(backdrop);
            var accentId = AxioosThemes.NormalizeAccent(accent);
            var next = Build(palette, backdropId, accentId);

            var merged = application.Resources.MergedDictionaries;
            var index = themeDictionary == null ? -1 : merged.IndexOf(themeDictionary);
            if (index >= 0)
            {
                merged[index] = next;
            }
            else
            {
                var glassLevel = GlassAppearanceService.Current?.LevelDictionary;
                var levelIndex = glassLevel == null ? -1 : merged.IndexOf(glassLevel);
                if (levelIndex >= 0) merged.Insert(levelIndex, next);
                else merged.Add(next);
            }
            themeDictionary = next;
            ThemeId = chosen.Id;
            BackdropId = backdropId;
            AccentId = accentId;

            try
            {
                GlassAppearanceService.Current?.Rebase(application, next, palette.IsDark);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Glass level rebase after theme change failed");
            }
            AcceptanceDiagnostics.RecordStage("axioos.theme.applied", palette.Id + ";" + backdropId + ";" + accentId);
        }

        internal static ResourceDictionary Build(AxioosTheme t, string backdrop, string accent)
        {
            var d = new ResourceDictionary();
            var dark = t.IsDark;
            var solid = accent == "solid";
            var a1 = t.AccentA;
            var a2 = solid ? t.AccentA : t.AccentB;
            var mid = Mix(a1, a2, 0.5);
            var deep = t.Background;
            var tone = t.GlowA;
            var tone2 = t.GlowB;
            var panel = dark ? Mix(deep, tone, 0.35) : White;
            var panel2 = dark ? Mix(deep, tone2, 0.30) : Mix(White, deep, 0.45);
            var field = dark ? Mix(deep, tone, 0.22) : Mix(White, deep, 0.25);
            var ink = t.Text;
            var muted = t.Muted;
            var surfaceAlpha = dark ? 1.0 : 1.18;
            var auroraScale = backdrop == "flat" || backdrop == "grid" ? 0.0 : backdrop == "mesh" ? 1.25 : backdrop == "spotlight" ? 0.55 : 1.0;

            d["GlassBackdropBaseBrush"] = Solid(deep, 1);
            d["GlassBackdropBrush"] = Backdrop(t, backdrop, deep, tone, tone2, a1, a2);
            d["GlassFieldBlueColor"] = Mix(tone, tone2, 0.5);
            d["GlassFieldCyanColor"] = tone;
            d["GlassFieldVioletColor"] = tone2;
            d["GlassFieldGlintColor"] = Mix(tone, ink, 0.12);
            d["GlassFieldSeamColor"] = deep;

            d["GlassSurfaceBrush"] = Mutable(Linear(Stop(panel, 0.62 * surfaceAlpha, 0), Stop(Mix(panel, deep, 0.25), 0.56 * surfaceAlpha, 0.52), Stop(panel2, 0.52 * surfaceAlpha, 1)));
            d["GlassSurfaceStrongBrush"] = Mutable(Linear(Stop(panel, 0.76 * surfaceAlpha, 0), Stop(Mix(panel, deep, 0.15), 0.70 * surfaceAlpha, 1)));
            d["GlassSurfaceHoverBrush"] = Mutable(Linear(Stop(Mix(panel, a1, 0.14), 0.72 * surfaceAlpha, 0), Stop(Mix(panel2, a1, 0.08), 0.62 * surfaceAlpha, 1)));
            d["GlassInputBrush"] = Mutable(Linear(Stop(field, 0.72 * surfaceAlpha, 0), Stop(Mix(field, deep, 0.2), 0.66 * surfaceAlpha, 1)));
            d["GlassPanelSurfaceBrush"] = Mutable(Linear(Stop(panel, 0.66 * surfaceAlpha, 0), Stop(Mix(panel, deep, 0.3), 0.60 * surfaceAlpha, 0.45), Stop(panel2, 0.64 * surfaceAlpha, 1)));
            d["GlassPanelSurfaceStrongBrush"] = Mutable(Linear(Stop(panel, 0.78 * surfaceAlpha, 0), Stop(Mix(panel, deep, 0.2), 0.72 * surfaceAlpha, 1)));
            d["GlassTableSurfaceBrush"] = Mutable(Linear(Stop(field, 0.70 * surfaceAlpha, 0), Stop(Mix(field, deep, 0.15), 0.64 * surfaceAlpha, 1)));
            d["GlassPanelEdgeBrush"] = Linear(Stop(a1, 0.80, 0), Stop(mid, 0.50, 0.58), Stop(a2, 0.0, 1), horizontal: true);
            d["GlassAccentBrush"] = Linear(Stop(a1, 1, 0), Stop(mid, 1, 0.65), Stop(a2, 1, 1), horizontal: true);
            d["GlassAccentSoftBrush"] = Mutable(Linear(Stop(a1, 0.79, 0), Stop(mid, 0.63, 0.58), Stop(a2, 0.63, 1), horizontal: true));
            d["GlassDownloadsBrush"] = Linear(Stop(a1, 0.89, 0), Stop(mid, 0.89, 0.58), Stop(a2, 0.87, 1), horizontal: true);
            d["GlassAuroraCyanBrush"] = Mutable(Radial(0.38, 0.42, Stop(tone, 0.79 * auroraScale, 0), Stop(tone, 0.38 * auroraScale, 0.48), Stop(tone, 0, 1)));
            d["GlassAuroraPurpleBrush"] = Mutable(Radial(0.5, 0.5, Stop(tone2, 0.66 * auroraScale, 0), Stop(tone2, 0.29 * auroraScale, 0.52), Stop(tone2, 0, 1)));
            d["GlassAuroraBlueBrush"] = Mutable(Radial(0.5, 0.5, Stop(Mix(tone, tone2, 0.5), 0.61 * auroraScale, 0), Stop(Mix(tone, tone2, 0.5), 0.29 * auroraScale, 0.55), Stop(Mix(tone, tone2, 0.5), 0, 1)));

            d["GlassBorderBrush"] = Mutable(Solid(ink, dark ? 0.20 : 0.16));
            d["GlassBorderStrongBrush"] = Mutable(Solid(dark ? a1 : Mix(a1, Black, 0.1), 0.62));
            d["GlassTextBrush"] = Solid(ink, 1);
            d["GlassMutedTextBrush"] = Solid(muted, 1);
            d["GlassCardShadow"] = Mutable(Shadow(dark ? Black : Mix(deep, Black, 0.18), 46, 12, dark ? 0.48 : 0.30));
            d["GlassMenuShadow"] = Mutable(Shadow(dark ? Black : Mix(deep, Black, 0.18), 42, 12, dark ? 0.56 : 0.34));
            d["GlassDangerBrush"] = Solid(dark ? Color.FromRgb(0xFF, 0x8A, 0x9A) : Color.FromRgb(0xD3, 0x3C, 0x4A), 1);
            d["GlassSuccessBrush"] = Solid(dark ? Color.FromRgb(0x6E, 0xE7, 0xA8) : Color.FromRgb(0x1F, 0x9D, 0x62), 1);
            d["GlassDisabledInputBrush"] = Solid(deep, 0.88);
            d["GlassDisabledTextBrush"] = Solid(Mix(muted, deep, 0.2), 1);
            d["AboutWebsiteBrush"] = Solid(dark ? a1 : Mix(a1, Black, 0.15), 1);
            d["BrowserWikiLinkBrush"] = Solid(dark ? a1 : Mix(a1, Black, 0.15), 1);

            var selected = dark ? Mix(a1, deep, 0.45) : Mix(a1, White, 0.55);
            d["CategoryForegroundNormal"] = Solid(muted, 1);
            d["CategoryForegroundSelected"] = Solid(ink, 1);
            d["CategoryHighlight"] = Mutable(Solid(selected, 0.68));
            d["CategoryListBackground"] = Mutable(Solid(Mix(deep, tone, 0.2), 0.70));
            d["CategoryMouseOverBackground"] = Mutable(Solid(Mix(tone, a1, 0.12), 0.66));
            d["ButtonBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.5), 0.74));
            d["ButtonForecolor"] = Solid(ink, 1);
            d["ButtonMouseOverBackcolor"] = Mutable(Solid(Mix(tone, a1, 0.2), 0.86));
            d["ButtonMousePressedBackcolor"] = Mutable(Solid(Mix(tone, deep, 0.3), 0.94));
            d["ButtonDisabledBackcolor"] = Solid(Mix(deep, tone, 0.2), 0.48);
            d["ButtonBorder"] = Solid(ink, 0.19);
            d["ButtonFocusedBorder"] = Solid(a1, 1);
            d["ButtonOverBackcolor"] = Solid(Mix(tone, a1, 0.15), 0.82);
            d["ToolButtonForecolor"] = Solid(Mix(ink, muted, 0.2), 1);
            d["ToolButtonMouseOverBackcolor"] = Mutable(Solid(Mix(tone, a1, 0.25), 0.58));
            d["ToolButtonMousePressedBackcolor"] = Mutable(Solid(Mix(tone, deep, 0.4), 0.72));
            d["ToolButtonMouseDisabledForecolor"] = Solid(Mix(muted, deep, 0.35), 1);
            d["ToolbarBordercolor"] = Solid(ink, 0.14);
            d["SearchBorder"] = Solid(ink, 0.19);
            d["SearchBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.35), 0.68));
            d["ControlBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.3), 0.68));
            d["ControlBordercolor"] = Solid(ink, 0.15);
            d["ControlForecolor"] = Solid(Mix(ink, muted, 0.1), 1);
            d["TextBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.4), 0.72));
            d["TextForecolor"] = Solid(ink, 1);
            d["DisabledTextBackcolor"] = Solid(deep, 0.52);
            d["TextMouseOverBorder"] = Solid(a1, 0.75);
            d["TextBorder"] = Solid(ink, 0.18);
            d["TextFocusedBorder"] = Solid(a1, 1);
            d["ListViewBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.25), 0.61));
            d["ListViewForecolor"] = Solid(ink, 1);
            d["ListViewMouseOverBackcolor"] = Mutable(Solid(Mix(tone, a1, 0.2), 0.55));
            d["ListViewMouseOverForecolor"] = Solid(ink, 1);
            d["ListViewSelectedForecolor"] = Solid(ink, 1);
            d["ListViewSelectedBackcolor"] = Mutable(Solid(selected, 0.72));
            d["ListViewIconForecolor"] = Solid(muted, 1);
            d["ListViewHeaderForecolor"] = Solid(muted, 1);
            d["StatusbarBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.25), 0.68));
            d["StatusbarIconcolor"] = Solid(muted, 1);
            d["PlaceHolderForecolor"] = Solid(Mix(muted, deep, 0.2), 1);
            d["NewDownloadTextBoxBackground"] = Mutable(Solid(Mix(deep, tone, 0.4), 0.72));
            d["HyperlinkForecolor"] = Solid(dark ? a1 : Mix(a1, Black, 0.15), 1);
            d["ScrollBarThumbColorNormal"] = Solid(Mix(muted, deep, 0.4), 0.52);
            d["ScrollBarThumbColorHot"] = Solid(muted, 0.78);
            d["ControlDisabledBackcolor"] = Solid(deep, 0.50);
            d["TabSelectionColor"] = Solid(a1, 1);
            d["ProgressBarBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.5), 0.50));
            d["ProgressBarForecolor"] = Solid(a1, 1);
            d["ListViewHeaderBackcolor"] = Mutable(Solid(Mix(deep, tone, 0.45), 0.68));
            d["ListViewHeaderHoverBackcolor"] = Mutable(Solid(Mix(tone, a1, 0.2), 0.80));
            d["ListViewHeaderBordercolor"] = Solid(a1, 0.28);
            d["ListViewRowBordercolor"] = Solid(ink, 0.08);
            d["GlassScrollTrackBrush"] = Mutable(Solid(Mix(deep, tone, 0.2), 0.46));
            d["GlassScrollThumbBrush"] = Solid(Mix(muted, a1, 0.25), 0.62);
            d["GlassScrollThumbHotBrush"] = Solid(Mix(muted, a1, 0.55), 0.86);
            d["GlassScrollThumbPressedBrush"] = Solid(a1, 0.96);
            d["GlassScrollButtonHoverBrush"] = Solid(Mix(tone, a1, 0.2), 0.55);
            d["GlassScrollGlyphBrush"] = Solid(muted, 1);
            d["GlassScrollGlyphHotBrush"] = Solid(ink, 1);
            d[SystemColors.WindowBrushKey] = Solid(Mix(deep, tone, 0.2), 1);
            d[SystemColors.ControlBrushKey] = Solid(Mix(deep, tone, 0.25), 1);
            d[SystemColors.ControlTextBrushKey] = Solid(ink, 1);
            d[SystemColors.WindowTextBrushKey] = Solid(ink, 1);
            d[SystemColors.GrayTextBrushKey] = Solid(muted, 1);
            d[SystemColors.HighlightBrushKey] = Solid(selected, 0.72);
            d[SystemColors.HighlightTextBrushKey] = Solid(ink, 1);
            d[SystemColors.MenuBrushKey] = Solid(Mix(deep, tone, 0.3), 0.98);
            d[SystemColors.MenuBarBrushKey] = Solid(Mix(deep, tone, 0.3), 0.98);
            d[SystemColors.MenuHighlightBrushKey] = Solid(Mix(tone, a1, 0.25), 0.92);
            d[SystemColors.MenuTextBrushKey] = Solid(ink, 1);
            return d;
        }

        private static Brush Backdrop(AxioosTheme t, string backdrop, Color deep, Color tone, Color tone2, Color a1, Color a2)
        {
            switch (backdrop)
            {
                case "flat":
                    return Solid(deep, 1);
                case "spotlight":
                    return Layered(deep, Glow(0.5, -0.1, 0.7, 0.6, tone, 1, 0.7));
                case "grid":
                    return GridBrush(deep, t.Text);
                case "grain":
                    return Layered(deep, Glow(0.1, 0.0, 1.1, 0.9, tone, 1, 0.6), Glow(1.0, 1.0, 0.8, 0.8, tone2, 1, 0.6));
                case "mesh":
                    return Layered(deep, Glow(0.15, 0.2, 0.6, 0.6, tone, 1, 0.7), Glow(0.85, 0.15, 0.5, 0.6, a2, 0.22, 0.7),
                        Glow(0.7, 0.95, 0.6, 0.6, tone2, 1, 0.7), Glow(0.35, 0.8, 0.4, 0.4, a1, 0.12, 0.7));
                default:
                    return Layered(deep, Glow(0.0, 0.0, 1.2, 0.9, tone, 1, 0.55), Glow(1.0, 1.0, 0.9, 0.9, tone2, 1, 0.55));
            }
        }

        private static RadialGradientBrush Glow(double x, double y, double radiusX, double radiusY, Color color, double alpha, double fade)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(x, y),
                GradientOrigin = new Point(x, y),
                RadiusX = radiusX,
                RadiusY = radiusY
            };
            brush.GradientStops.Add(Stop(color, alpha, 0));
            brush.GradientStops.Add(Stop(color, 0, fade));
            brush.GradientStops.Add(Stop(color, 0, 1));
            brush.Freeze();
            return brush;
        }

        private static Brush Layered(Color background, params Brush[] layers)
        {
            var unit = new RectangleGeometry(new Rect(0, 0, 1, 1));
            unit.Freeze();
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Solid(background, 1), null, unit));
            foreach (var layer in layers) group.Children.Add(new GeometryDrawing(layer, null, unit));
            group.Freeze();
            var brush = new DrawingBrush(group)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 1, 1),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }

        private static Brush GridBrush(Color deep, Color line)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Solid(deep, 1), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(Solid(line, 0.06), 1);
            pen.Freeze();
            group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0.5), new Point(32, 0.5))));
            group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0.5, 0), new Point(0.5, 32))));
            group.Freeze();
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 32, 32),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 32, 32),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }


        private static GradientStop Stop(Color color, double alpha, double offset) =>
            new GradientStop(Color.FromArgb((byte)Math.Round(Math.Max(0, Math.Min(1, alpha)) * 255), color.R, color.G, color.B), offset);

        private static LinearGradientBrush Linear(GradientStop a, GradientStop b, GradientStop? c = null, GradientStop? e = null, bool horizontal = false)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = horizontal ? new Point(1, 0) : new Point(1, 1)
            };
            brush.GradientStops.Add(a);
            brush.GradientStops.Add(b);
            if (c != null) brush.GradientStops.Add(c);
            if (e != null) brush.GradientStops.Add(e);
            brush.Freeze();
            return brush;
        }

        private static RadialGradientBrush Radial(double x, double y, GradientStop a, GradientStop b, GradientStop c)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(x, y),
                GradientOrigin = new Point(x, y),
                RadiusX = 0.62,
                RadiusY = 0.62
            };
            brush.GradientStops.Add(a);
            brush.GradientStops.Add(b);
            brush.GradientStops.Add(c);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Solid(Color color, double opacity)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        private static Freezable Mutable(Freezable frozen) => frozen.CloneCurrentValue();

        private static System.Windows.Media.Effects.DropShadowEffect Shadow(Color color, double blur, double depth, double opacity)
        {
            var effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = color,
                BlurRadius = blur,
                ShadowDepth = depth,
                Opacity = opacity
            };
            effect.Freeze();
            return effect;
        }

        internal static Color Mix(Color a, Color b, double amount)
        {
            var t = Math.Max(0, Math.Min(1, amount));
            return Color.FromRgb(
                (byte)Math.Round(a.R + ((b.R - a.R) * t)),
                (byte)Math.Round(a.G + ((b.G - a.G) * t)),
                (byte)Math.Round(a.B + ((b.B - a.B) * t)));
        }
    }
}
