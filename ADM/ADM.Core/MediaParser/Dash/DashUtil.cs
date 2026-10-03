using System;
using System.Text.RegularExpressions;

namespace ADM.Core.MediaParser.Dash
{
    public static class DashUtil
    {
        private static Regex XS_DURATION_PATTERN = new Regex(
            "^(-)?P(([0-9]*)Y)?(([0-9]*)M)?(([0-9]*)D)?(T(([0-9]*)H)?(([0-9]*)M)?(([0-9.]*)S)?)?$" +
            "(T(([0-9]*)H)?(([0-9]*)M)?(([0-9.]*)S)?)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static long ParseXsDuration(string value)
        {
            var match = XS_DURATION_PATTERN.Match(value);
            if (match.Success)
            {
                var negated = !string.IsNullOrEmpty(match.Groups[1].ToString());
                var years = match.Groups[3].Value;
                var durationSeconds = (!string.IsNullOrEmpty(years)) ? Double.Parse(years) * 31556908 : 0;
                var months = match.Groups[5].ToString();
                durationSeconds += (!string.IsNullOrEmpty(months)) ? Double.Parse(months) * 2629739 : 0;
                var days = match.Groups[7].ToString();
                durationSeconds += (!string.IsNullOrEmpty(days)) ? Double.Parse(days) * 86400 : 0;
                var hours = match.Groups[10].ToString();
                durationSeconds += (!string.IsNullOrEmpty(hours)) ? Double.Parse(hours) * 3600 : 0;
                var minutes = match.Groups[12].ToString();
                durationSeconds += (!string.IsNullOrEmpty(minutes)) ? Double.Parse(minutes) * 60 : 0;
                var seconds = match.Groups[14].ToString();
                durationSeconds += (!string.IsNullOrEmpty(seconds)) ? Double.Parse(seconds) : 0;
                var durationMillis = (long)(durationSeconds * 1000);
                return negated ? -durationMillis : durationMillis;
            }
            else
            {
                return (long)(Double.Parse(value) * 3600 * 1000);
            }
        }
    }
}
