using System;

namespace ADM.Core.Util
{
    public static class AcceptanceTestEnvironment
    {
        public const string EnabledVariable = "ADM_ACCEPTANCE_DIAGNOSTICS";
        public const string ProfileDirectoryVariable = "ADM_ACCEPTANCE_PROFILE_DIR";
        public const string DiagnosticsPathVariable = "ADM_ACCEPTANCE_DIAGNOSTICS_PATH";
        public const string SkipFirstRunVariable = "ADM_ACCEPTANCE_SKIP_FIRST_RUN";
        public const string CorrelationIdVariable = "ADM_ACCEPTANCE_CORRELATION_ID";
        public const string SettingsConstructionVariable = "ADM_ACCEPTANCE_SETTINGS_CONSTRUCTION";
        public const string SettingsConstructionResultVariable = "ADM_ACCEPTANCE_SETTINGS_CONSTRUCTION_RESULT";
        public const string StartDownloadAutomaticallyVariable = "ADM_ACCEPTANCE_START_DOWNLOAD_AUTOMATICALLY";

        public static bool IsEnabled => IsOne(Environment.GetEnvironmentVariable(EnabledVariable));
        public static bool SkipFirstRun => IsEnabled && IsOne(Environment.GetEnvironmentVariable(SkipFirstRunVariable));
        public static string? ProfileDirectory => IsEnabled ? Environment.GetEnvironmentVariable(ProfileDirectoryVariable) : null;
        public static string? DiagnosticsPath => IsEnabled ? Environment.GetEnvironmentVariable(DiagnosticsPathVariable) : null;
        public static string? CorrelationId => IsEnabled ? Environment.GetEnvironmentVariable(CorrelationIdVariable) : null;
        public static bool SettingsConstructionEnabled => IsEnabled && IsOne(Environment.GetEnvironmentVariable(SettingsConstructionVariable));
        public static string? SettingsConstructionResultPath => SettingsConstructionEnabled ? Environment.GetEnvironmentVariable(SettingsConstructionResultVariable) : null;
        public static bool ForceStartDownloadAutomatically => IsEnabled && IsOne(Environment.GetEnvironmentVariable(StartDownloadAutomaticallyVariable));

        private static bool IsOne(string? value)
        {
            return string.Equals(value, "1", StringComparison.Ordinal);
        }
    }
}
