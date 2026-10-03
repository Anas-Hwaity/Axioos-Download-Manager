using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ADM.Wpf.UI.Diagnostics;

namespace ADM.Wpf.UI
{
    public sealed class GlassPanelField : Grid
    {
        private readonly Grid glowLayer = new Grid();
        private readonly Rectangle panelVeil = new Rectangle();
        private readonly Rectangle depth = new Rectangle();
        private readonly List<GlowInstance> glows = new List<GlowInstance>();
        private readonly TranslateTransform pointerParallax = new TranslateTransform();
        private bool motionRunning;
        private bool renderingSubscribed;
        private double targetX;
        private double targetY;
        private GlassAppearanceService? subscribedService;
        private Window? ownerWindow;

        public GlassPanelField()
        {
            IsHitTestVisible = false;
            Focusable = false;
            ClipToBounds = true;

            glowLayer.RenderTransform = pointerParallax;
            glowLayer.CacheMode = new BitmapCache();
            panelVeil.CacheMode = new BitmapCache();
            BuildPattern();

            Children.Add(glowLayer);
            Children.Add(panelVeil);
            Children.Add(depth);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void BuildPattern()
        {
            var pattern = GlassAppearanceService.Current?.Pattern ?? GlassPanelPattern.Create();
            glows.Clear();
            glowLayer.Children.Clear();
            foreach (var spec in pattern.Glows)
            {
                var glow = new GlowInstance(spec);
                glows.Add(glow);
                glowLayer.Children.Add(new Rectangle { Fill = glow.Light });
            }
            panelVeil.Fill = pattern.PanelBrush;
            depth.Fill = pattern.DepthBrush;
        }

        private void OnPatternChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnPatternChanged(sender, e)));
                return;
            }
            StopMotion();
            BuildPattern();
            ApplyLevel(GlassThemeManager.CurrentLevel);
        }

        public static double FieldOpacity(int level)
        {
            var t = GlassThemeManager.Clamp(level) / 100.0;
            return t <= 0.0 ? 0.0 : 0.28 + (0.42 * t);
        }

        public static double HighlightOpacity(int level)
        {
            var t = GlassThemeManager.Clamp(level) / 100.0;
            return t <= 0.0 ? 0.0 : 0.12 + (0.18 * t);
        }

        public static double PanelOpacity(int level) => HighlightOpacity(level);

        private static bool MotionAllowed =>
            (RenderCapability.Tier >> 16) >= 2 && SystemParameters.ClientAreaAnimation;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var service = GlassAppearanceService.Current;
            if (subscribedService == null && service != null)
            {
                service.LevelChanged += OnLevelChanged;
                service.PatternChanged += OnPatternChanged;
                subscribedService = service;
            }
            ownerWindow = Window.GetWindow(this);
            if (ownerWindow != null)
            {
                ownerWindow.PreviewMouseMove += OnPointerMove;
            }
            ApplyLevel(GlassThemeManager.CurrentLevel);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (subscribedService != null)
            {
                subscribedService.LevelChanged -= OnLevelChanged;
                subscribedService.PatternChanged -= OnPatternChanged;
                subscribedService = null;
            }
            if (ownerWindow != null)
            {
                ownerWindow.PreviewMouseMove -= OnPointerMove;
                ownerWindow = null;
            }
            StopMotion();
        }

        private void OnLevelChanged(object? sender, EventArgs e)
        {
            ApplyLevel(GlassThemeManager.CurrentLevel);
        }

        private void OnPointerMove(object sender, MouseEventArgs e)
        {
            if (ownerWindow == null || ownerWindow.ActualWidth <= 0 || ownerWindow.ActualHeight <= 0)
            {
                return;
            }
            var point = e.GetPosition(ownerWindow);
            targetX = ((point.X / ownerWindow.ActualWidth) - 0.5) * 10.0;
            targetY = ((point.Y / ownerWindow.ActualHeight) - 0.5) * 6.0;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            pointerParallax.X += (targetX - pointerParallax.X) * 0.14;
            pointerParallax.Y += (targetY - pointerParallax.Y) * 0.14;
        }

        private void ApplyLevel(int level)
        {
            var opacity = FieldOpacity(level);
            var motion = false;
            if (opacity <= 0.0)
            {
                Visibility = Visibility.Collapsed;
                StopMotion();
            }
            else
            {
                Visibility = Visibility.Visible;
                Opacity = opacity;
                panelVeil.Opacity = PanelOpacity(level);
                if (MotionAllowed)
                {
                    StartMotion();
                    motion = true;
                }
                else
                {
                    StopMotion();
                }
            }

            var owner = Window.GetWindow(this);
            AcceptanceDiagnostics.RecordStage(
                "glass.field",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "window={0};level={1};visible={2};opacity={3:0.000};panels={4:0.000};motion={5};pointer={6};tier={7}",
                    owner == null ? "none" : owner.GetType().Name,
                    level,
                    Visibility == Visibility.Visible,
                    opacity,
                    panelVeil.Opacity,
                    motion,
                    renderingSubscribed,
                    RenderCapability.Tier >> 16));
        }

        private void StartMotion()
        {
            if (!motionRunning)
            {
                foreach (var glow in glows)
                {
                    glow.Begin();
                }
                motionRunning = true;
            }
            if (!renderingSubscribed)
            {
                CompositionTarget.Rendering += OnRendering;
                renderingSubscribed = true;
            }
        }

        private void StopMotion()
        {
            if (motionRunning)
            {
                foreach (var glow in glows)
                {
                    glow.Stop();
                }
                motionRunning = false;
            }
            if (renderingSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                renderingSubscribed = false;
            }
            targetX = 0;
            targetY = 0;
            pointerParallax.X = 0;
            pointerParallax.Y = 0;
        }

        private sealed class GlowInstance
        {
            private readonly GlassGlowSpec spec;

            public GlowInstance(GlassGlowSpec spec)
            {
                this.spec = spec;
                Light = spec.CreateBrush();
            }

            public RadialGradientBrush Light { get; }

            public void Begin()
            {
                var start = new Point(spec.CenterX - spec.Sway, spec.CenterY);
                var end = new Point(spec.CenterX + spec.Sway, spec.CenterY);
                var sway = new PointAnimation(start, end, new Duration(TimeSpan.FromSeconds(spec.SwaySeconds)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    BeginTime = TimeSpan.FromSeconds(-spec.PhaseSeconds)
                };
                Light.BeginAnimation(RadialGradientBrush.CenterProperty, sway);

                var breathe = new DoubleAnimation(spec.BreatheLow, 1.0, new Duration(TimeSpan.FromSeconds(spec.SwaySeconds * 0.8)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    BeginTime = TimeSpan.FromSeconds(-spec.PhaseSeconds)
                };
                Light.BeginAnimation(Brush.OpacityProperty, breathe);
            }

            public void Stop()
            {
                Light.BeginAnimation(RadialGradientBrush.CenterProperty, null);
                Light.BeginAnimation(Brush.OpacityProperty, null);
            }
        }
    }

    internal sealed class GlassGlowSpec
    {
        public GlassGlowSpec(double centerX, double centerY, double radiusX, double radiusY, Color core, Color mid,
            double coreAlpha, double midAlpha, double sway, double swaySeconds, double phaseSeconds, double breatheLow)
        {
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
            Core = core;
            Mid = mid;
            CoreAlpha = coreAlpha;
            MidAlpha = midAlpha;
            Sway = sway;
            SwaySeconds = swaySeconds;
            PhaseSeconds = phaseSeconds;
            BreatheLow = breatheLow;
        }

        public double CenterX { get; }
        public double CenterY { get; }
        public double RadiusX { get; }
        public double RadiusY { get; }
        public Color Core { get; }
        public Color Mid { get; }
        public double CoreAlpha { get; }
        public double MidAlpha { get; }
        public double Sway { get; }
        public double SwaySeconds { get; }
        public double PhaseSeconds { get; }
        public double BreatheLow { get; }

        public RadialGradientBrush CreateBrush()
        {
            var center = new Point(CenterX, CenterY);
            var brush = new RadialGradientBrush
            {
                Center = center,
                GradientOrigin = center,
                RadiusX = RadiusX,
                RadiusY = RadiusY,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop(GlassPanelPattern.WithAlpha(Core, CoreAlpha), 0.00));
            brush.GradientStops.Add(new GradientStop(GlassPanelPattern.WithAlpha(Mid, MidAlpha), 0.42));
            brush.GradientStops.Add(new GradientStop(GlassPanelPattern.WithAlpha(Mid, 0.0), 1.00));
            return brush;
        }
    }

    internal sealed class GlassPanelPattern
    {
        public const double PanelWidth = 56.0;

        private GlassPanelPattern(IReadOnlyList<GlassGlowSpec> glows, Brush panelBrush, Brush depthBrush)
        {
            Glows = glows;
            PanelBrush = panelBrush;
            DepthBrush = depthBrush;
        }

        public IReadOnlyList<GlassGlowSpec> Glows { get; }
        public Brush PanelBrush { get; }
        public Brush DepthBrush { get; }

        public static GlassPanelPattern Create()
        {
            var blue = ResolveColor("GlassFieldBlueColor", Color.FromRgb(0x20, 0x46, 0xA8));
            var cyan = ResolveColor("GlassFieldCyanColor", Color.FromRgb(0x3B, 0x82, 0xC4));
            var violet = ResolveColor("GlassFieldVioletColor", Color.FromRgb(0x70, 0x46, 0xB5));
            var seam = ResolveColor("GlassFieldSeamColor", Color.FromRgb(0x05, 0x0A, 0x14));

            var glows = new List<GlassGlowSpec>
            {
                new GlassGlowSpec(0.18, 0.18, 0.72, 0.72, cyan, blue, 0.34, 0.16, 0.025, 28, 2, 0.88),
                new GlassGlowSpec(0.80, 0.12, 0.68, 0.70, violet, blue, 0.28, 0.14, 0.030, 34, 9, 0.88),
                new GlassGlowSpec(0.55, 0.82, 0.88, 0.62, blue, cyan, 0.24, 0.12, 0.020, 39, 15, 0.90),
                new GlassGlowSpec(0.96, 0.72, 0.48, 0.58, violet, blue, 0.18, 0.09, 0.018, 44, 21, 0.90)
            };

            var profile = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            profile.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, 0.00), 0.00));
            profile.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, 0.045), 0.48));
            profile.GradientStops.Add(new GradientStop(WithAlpha(blue, 0.025), 0.92));
            profile.GradientStops.Add(new GradientStop(WithAlpha(seam, 0.05), 0.98));
            profile.GradientStops.Add(new GradientStop(WithAlpha(seam, 0.00), 1.00));
            profile.Freeze();
            var drawing = new GeometryDrawing(profile, null, new RectangleGeometry(new Rect(0, 0, PanelWidth, PanelWidth)));
            drawing.Freeze();
            var panelBrush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, PanelWidth, PanelWidth),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, PanelWidth, PanelWidth),
                ViewportUnits = BrushMappingMode.Absolute
            };
            RenderOptions.SetCachingHint(panelBrush, CachingHint.Cache);
            panelBrush.Freeze();

            var depthBrush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            depthBrush.GradientStops.Add(new GradientStop(WithAlpha(seam, 0.00), 0.00));
            depthBrush.GradientStops.Add(new GradientStop(WithAlpha(seam, 0.04), 0.62));
            depthBrush.GradientStops.Add(new GradientStop(WithAlpha(seam, 0.22), 1.00));
            depthBrush.Freeze();

            return new GlassPanelPattern(glows, panelBrush, depthBrush);
        }

        public static Color ResolveColor(string key, Color fallback)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.TryFindResource(key) is Color color)
            {
                return color;
            }
            return fallback;
        }

        public static Color WithAlpha(Color color, double alpha)
        {
            return Color.FromArgb(ToByte(alpha * 255.0), color.R, color.G, color.B);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Max(0.0, Math.Min(255.0, Math.Round(value)));
        }
    }
}
