using System;
using System.Globalization;
using System.Windows.Data;
using TraceLog;
using Translations;

namespace ADM.Wpf.UI.Dashboard
{
    public sealed class DashboardTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = parameter as string ?? string.Empty;
            return DashboardText.Describe(key, value, TextResource.GetText(key));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public static class DashboardText
    {
        private const double Kibi = 1024d;
        private const string Unknown = "---";

        public static string Describe(string key, object? value, string? template)
        {
            var pattern = template == null || template.IndexOf("{0}", StringComparison.Ordinal) < 0 ? Fallback(key) : template;
            var text = ValueText(key, value);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, pattern, text);
            }
            catch (FormatException ex)
            {
                Log.Debug(ex, "Dashboard text could not be formatted");
                return text;
            }
        }

        public static string ValueText(string key, object? value)
        {
            switch (key)
            {
                case "DASHBOARD_DOWNLOADED":
                case "DASHBOARD_TOTAL":
                    return Size(Number(value));
                case "DASHBOARD_SPEED":
                case "DASHBOARD_AVERAGE":
                    return Speed(Number(value));
                case "DASHBOARD_ETA":
                    return Duration(Number(value));
                case "DASHBOARD_PERCENT":
                    return Percent(Number(value));
                default:
                    return value == null ? Unknown : System.Convert.ToString(value, CultureInfo.CurrentCulture) ?? Unknown;
            }
        }

        public static string Size(double? bytes)
        {
            if (bytes == null || bytes.Value < 0) return Unknown;
            var amount = bytes.Value;
            if (amount < Kibi) return string.Format(CultureInfo.CurrentCulture, "{0:F0} B", amount);
            if (amount < Kibi * Kibi) return string.Format(CultureInfo.CurrentCulture, "{0:F1} KB", amount / Kibi);
            if (amount < Kibi * Kibi * Kibi) return string.Format(CultureInfo.CurrentCulture, "{0:F1} MB", amount / (Kibi * Kibi));
            return string.Format(CultureInfo.CurrentCulture, "{0:F2} GB", amount / (Kibi * Kibi * Kibi));
        }

        public static string Speed(double? bytesPerSecond)
        {
            if (bytesPerSecond == null || bytesPerSecond.Value < 0) return Unknown;
            return Size(bytesPerSecond) + "/s";
        }

        public static string Duration(double? seconds)
        {
            if (seconds == null || seconds.Value < 0 || seconds.Value > 8640000d) return Unknown;
            var whole = (long)Math.Round(seconds.Value);
            var hours = whole / 3600;
            var minutes = whole % 3600 / 60;
            var rest = whole % 60;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, rest);
        }

        public static string Percent(double? percent)
        {
            if (percent == null) return Unknown;
            var bounded = Math.Max(0d, Math.Min(100d, percent.Value));
            return string.Format(CultureInfo.CurrentCulture, "{0:F1}%", bounded);
        }

        private static double? Number(object? value)
        {
            if (value == null) return null;
            double number;
            try
            {
                number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                Log.Debug(ex, "Dashboard value is not a number");
                return null;
            }
            if (double.IsNaN(number) || double.IsInfinity(number)) return null;
            return number;
        }

        private static string Fallback(string key)
        {
            switch (key)
            {
                case "DASHBOARD_HOST": return "Host: {0}";
                case "DASHBOARD_STATE": return "State: {0}";
                case "DASHBOARD_DOWNLOADED": return "Downloaded: {0}";
                case "DASHBOARD_TOTAL": return "Total: {0}";
                case "DASHBOARD_SPEED": return "Speed: {0}";
                case "DASHBOARD_AVERAGE": return "Average: {0}";
                case "DASHBOARD_ETA": return "Time left: {0}";
                case "DASHBOARD_RESUME_STATE": return "Resume: {0}";
                case "DASHBOARD_CONNECTIONS": return "Connections: {0}";
                case "DASHBOARD_ATTEMPT": return "Attempt: {0}";
                case "DASHBOARD_SOURCE_REFRESH": return "Source refresh: {0}";
                case "DASHBOARD_RETRY_STATE": return "Retry: {0}";
                case "DASHBOARD_RECOVERY": return "Recovery: {0}";
                case "DASHBOARD_LAST_ERROR": return "Last error: {0}";
                case "DASHBOARD_SEGMENTS": return "Segments: {0}";
                default: return "{0}";
            }
        }
    }
}
