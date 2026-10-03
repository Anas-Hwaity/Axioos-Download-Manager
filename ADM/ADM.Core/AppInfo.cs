using System;
using System.Collections.Generic;
using System.Text;

namespace ADM.Core
{
    public static class AppInfo
    {
        public static string APP_VERSION = "1.0.2";
        public const string BASE_VERSION = "8.0.29";
        public const string BASE_TEXT = "Built on " + ProductIdentity.LegacyDisplayName + " " + BASE_VERSION;
        public static string APP_VERSION_TEXT = $"{ProductIdentity.DisplayName} {APP_VERSION}";
        public static string APP_DEVELOPER_TEXT = $"Developed by {ProductIdentity.DeveloperName}";
        public static string APP_COPYRIGHT_TEXT = "Based on Xtreme Download Manager \u00a9 2013 - 2023 Subhra Das Gupta, GPL-2.0";
        public static string APP_HOMEPAGE_TEXT = "github.com/" + ProductIdentity.ReleaseRepository;
    }
}
