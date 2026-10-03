using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TraceLog;
using ADM.Wpf.UI.Diagnostics;

namespace ADM.Wpf.UI
{
    public sealed class GlassAppearanceService
    {
        private readonly Dictionary<string, Freezable> baselines = new Dictionary<string, Freezable>(StringComparer.Ordinal);
        private ResourceDictionary? levelDictionary;
        private GlassPanelPattern? pattern;
        private int generation;
        private bool lightSurfaces;

        public static GlassAppearanceService? Current => (System.Windows.Application.Current as App)?.GlassAppearance;

        public int CurrentLevel { get; private set; } = GlassThemeManager.DefaultLevel;

        public event EventHandler? LevelChanged;

        public event EventHandler? PatternChanged;

        internal GlassPanelPattern Pattern => pattern ??= GlassPanelPattern.Create();

        internal ResourceDictionary? LevelDictionary => levelDictionary;

        public void Rebase(System.Windows.Application application)
        {
            Rebase(application, null, !lightSurfaces);
        }

        public void Rebase(System.Windows.Application application, ResourceDictionary? source, bool darkTheme)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            lightSurfaces = !darkTheme;
            baselines.Clear();
            if (source != null)
            {
                foreach (var (key, _) in GlassThemeManager.LevelDrivenResources)
                {
                    if (source.Contains(key) && source[key] is Freezable fresh) baselines[key] = fresh.CloneCurrentValue();
                }
            }
            pattern = null;
            PatternChanged?.Invoke(this, EventArgs.Empty);
            Apply(application, CurrentLevel);
        }

        public void Apply(System.Windows.Application application, int level)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            level = GlassThemeManager.Clamp(level);

            CaptureBaselines(application);

            var next = new ResourceDictionary();
            foreach (var (key, kind) in GlassThemeManager.LevelDrivenResources)
            {
                if (!baselines.TryGetValue(key, out var baseline)) continue;
                var value = baseline.CloneCurrentValue();
                var opacity = GlassThemeManager.ChannelOpacity(kind, level);
                if (lightSurfaces && (kind == GlassThemeManager.Channel.Surface || kind == GlassThemeManager.Channel.Control || kind == GlassThemeManager.Channel.State)) opacity = 1.0;
                if (lightSurfaces && (kind == GlassThemeManager.Channel.CardShadow || kind == GlassThemeManager.Channel.MenuShadow)) opacity *= 0.45;
                if (value is Brush brush)
                {
                    brush.Opacity = opacity;
                }
                else if (value is DropShadowEffect effect)
                {
                    effect.Opacity = opacity;
                }
                if (value.CanFreeze)
                {
                    value.Freeze();
                }
                next[key] = value;
            }

            var merged = application.Resources.MergedDictionaries;
            var index = levelDictionary == null ? -1 : merged.IndexOf(levelDictionary);
            if (index >= 0)
            {
                merged[index] = next;
            }
            else
            {
                merged.Add(next);
            }
            levelDictionary = next;
            generation++;
            CurrentLevel = level;

            AcceptanceDiagnostics.RecordStage("glass.applied", Describe(level, next.Count));
            LevelChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CaptureBaselines(System.Windows.Application application)
        {
            foreach (var (key, _) in GlassThemeManager.LevelDrivenResources)
            {
                if (baselines.ContainsKey(key)) continue;
                var authored = FindAuthored(application.Resources, key);
                if (authored is Freezable freezable)
                {
                    baselines[key] = freezable.CloneCurrentValue();
                }
                else
                {
                    Log.Debug("Glass level resource is missing or not freezable: " + key);
                }
            }
        }

        private object? FindAuthored(ResourceDictionary root, string key)
        {
            if (root.Contains(key)) return root[key];
            object? found = null;
            foreach (var merged in root.MergedDictionaries)
            {
                if (ReferenceEquals(merged, levelDictionary)) continue;
                var candidate = FindAuthored(merged, key);
                if (candidate != null) found = candidate;
            }
            return found;
        }

        private string Describe(int level, int count)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "level={0};generation={1};resources={2};surface={3:0.000};control={4:0.000};border={5:0.000};borderStrong={6:0.000};aurora={7:0.000};field={8:0.000};highlight={9:0.000}",
                level,
                generation,
                count,
                GlassThemeManager.ChannelOpacity(GlassThemeManager.Channel.Surface, level),
                GlassThemeManager.ChannelOpacity(GlassThemeManager.Channel.Control, level),
                GlassThemeManager.ChannelOpacity(GlassThemeManager.Channel.Border, level),
                GlassThemeManager.ChannelOpacity(GlassThemeManager.Channel.BorderStrong, level),
                GlassThemeManager.ChannelOpacity(GlassThemeManager.Channel.Aurora, level),
                GlassPanelField.FieldOpacity(level),
                GlassPanelField.HighlightOpacity(level));
        }
    }
}
