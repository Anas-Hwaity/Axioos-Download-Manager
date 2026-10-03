using System;
using System.Collections.Generic;
using System.Text;

namespace ADM.Core
{
    public static class Links
    {
        public const string SupportUrl = ProductIdentity.DeveloperTelegramUrl;
        public const string IssueUrl = "https://github.com/" + ProductIdentity.ReleaseRepository + "/issues";
        public const string ChromeExtensionUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string FirefoxExtensionUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string OperaExtensionUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string EdgeExtensionUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string VideoDownloadTutorialUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string HomePageUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string YtDlpReleaseGH = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
        public const string DenoReleaseGH = "https://api.github.com/repos/denoland/deno/releases/latest";
        public const string AppLatestReleaseGH = "https://api.github.com/repos/" + ProductIdentity.ReleaseRepository + "/releases/latest";
        public const string AppUpdateCheckerUrl = "https://github.com/" + ProductIdentity.ReleaseRepository + "/releases/latest";
        public const string HelperToolsUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string MediaGrabberHowToUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
        public const string ManualExtensionInstallGuideUrl = "https://github.com/" + ProductIdentity.ReleaseRepository;
    }
}
