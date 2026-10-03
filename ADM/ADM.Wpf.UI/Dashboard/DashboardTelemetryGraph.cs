using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ADM.Core.Telemetry;
using TraceLog;

namespace ADM.Wpf.UI.Dashboard
{
    public sealed class DashboardTelemetryGraph : FrameworkElement
    {
        public static readonly DependencyProperty SpeedSeriesProperty = DependencyProperty.Register(
            nameof(SpeedSeries), typeof(IReadOnlyList<DownloadSpeedSample>), typeof(DashboardTelemetryGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
            nameof(Ranges), typeof(IReadOnlyList<DownloadRangeTelemetry>), typeof(DashboardTelemetryGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<DownloadSpeedSample>? SpeedSeries
        {
            get => (IReadOnlyList<DownloadSpeedSample>?)GetValue(SpeedSeriesProperty);
            set => SetValue(SpeedSeriesProperty, value);
        }

        public IReadOnlyList<DownloadRangeTelemetry>? Ranges
        {
            get => (IReadOnlyList<DownloadRangeTelemetry>?)GetValue(RangesProperty);
            set => SetValue(RangesProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var width = Math.Max(0, ActualWidth);
            var height = Math.Max(0, ActualHeight);
            if (width < 24 || height < 24) return;

            var surface = TryFindResource("GlassPanelSurfaceStrongBrush") as Brush ?? Brushes.Transparent;
            var border = TryFindResource("GlassBorderBrush") as Brush ?? Brushes.DimGray;
            var accent = TryFindResource("GlassAccentBrush") as Brush ?? Brushes.DeepSkyBlue;
            var success = TryFindResource("GlassSuccessBrush") as Brush ?? Brushes.LightGreen;
            var muted = TryFindResource("GlassMutedTextBrush") as Brush ?? Brushes.Gray;

            try
            {
                dc.DrawRoundedRectangle(surface, new Pen(border, 1), new Rect(0.5, 0.5, width - 1, height - 1), 10, 10);
                DrawSpeed(dc, width, height * 0.68, accent, muted);
                DrawRanges(dc, width, height, accent, success, border);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Dashboard graph could not be drawn");
            }
        }

        private void DrawSpeed(DrawingContext dc, double width, double speedHeight, Brush accent, Brush muted)
        {
            var samples = SpeedSeries;
            var left = 10d;
            var right = Math.Max(left, width - 10d);
            var top = 8d;
            var bottom = Math.Max(top + 1, speedHeight - 5d);
            dc.DrawLine(new Pen(muted, 0.6), new Point(left, bottom), new Point(right, bottom));
            if (samples == null || samples.Count < 2) return;

            var max = samples.Max(x => Finite(x.BytesPerSecond));
            if (max <= 0) return;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (var i = 0; i < samples.Count; i++)
                {
                    var x = left + (right - left) * i / Math.Max(1, samples.Count - 1d);
                    var ratio = Math.Min(1d, Math.Max(0d, Finite(samples[i].BytesPerSecond) / max));
                    var y = bottom - (bottom - top) * ratio;
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(accent, 1.6), geometry);
        }

        private static double Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0d : value;

        private void DrawRanges(DrawingContext dc, double width, double height, Brush accent, Brush success, Brush border)
        {
            var ranges = Ranges;
            var area = new Rect(10, height - 22, Math.Max(1, width - 20), 10);
            dc.DrawRoundedRectangle(null, new Pen(border, 1), area, 4, 4);
            if (ranges == null || ranges.Count == 0) return;

            var maxEnd = ranges.Max(x => Math.Max(x.End, x.Start));
            var minStart = ranges.Min(x => Math.Min(x.Start, x.End));
            var span = Math.Max(1d, maxEnd - minStart + 1d);
            foreach (var range in ranges)
            {
                var start = Math.Max(0d, (range.Start - minStart) / span);
                var logicalLength = Math.Max(1d, range.End - range.Start + 1d);
                var length = Math.Min(1d - start, logicalLength / span);
                var segment = new Rect(area.X + area.Width * start, area.Y + 1, Math.Max(1, area.Width * length), area.Height - 2);
                dc.DrawRectangle(accent, null, segment);

                var completedRatio = Math.Min(1d, Math.Max(0d, range.Downloaded / logicalLength));
                if (completedRatio > 0)
                {
                    dc.DrawRectangle(success, null, new Rect(segment.X, segment.Y, segment.Width * completedRatio, segment.Height));
                }
            }
        }
    }
}
