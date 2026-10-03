using System.Collections.Generic;

namespace ADM.Core
{
    public sealed class ReleaseNoteSection
    {
        public ReleaseNoteSection(string title, IReadOnlyList<string> items)
        {
            Title = title;
            Items = items;
        }

        public string Title { get; }
        public IReadOnlyList<string> Items { get; }
    }

    public static class ReleaseNotes
    {
        public const string Summary =
            "Axioos Download Manager is a new product built on the Xtreme Download Manager 8.0.29 engine. " +
            "The download core was kept and hardened, and almost everything around it was rebuilt: the interface, the browser integration, " +
            "video detection, recovery after crashes, security, privacy and the test system that guards all of it.";

        public static IReadOnlyList<ReleaseNoteSection> Sections { get; } = new[]
        {
            new ReleaseNoteSection("New in 1.0.1", new[]
            {
                "A browser download now stays paused in the browser until you confirm it in Axioos. If you close the Axioos dialog, the browser simply continues the download, so nothing is lost.",
                "Downloads handed over from the browser carry the sign in cookies of that download, so files behind a login no longer fail in Axioos.",
                "Importing a download list is all or nothing: it is checked first, existing downloads are left alone, and Axioos tells you honestly whether it worked.",
                "Scheduled queues start and stop even when the computer was asleep or Axioos was opened late, including schedules that run past midnight.",
                "Moving downloads up or down in a queue is saved, and finished downloads leave their queue for good.",
                "The download dashboard can always be opened again after a failed attempt.",
                "Opening a finished download no longer freezes Axioos while Windows checks the file.",
                "History filters are labelled, the website filter accepts a pasted address and includes subdomains, and tags are picked from a list.",
                "The JavaScript helper for yt-dlp reports a failed install instead of silently missing.",
                "The browser extension wakes up far less often when Axioos is not running.",
                "The setup program is much smaller."
            }),
            new ReleaseNoteSection("New in Axioos", new[]
            {
                "A brand new main window with a three-pane workstation layout, a live inspector and a web based interface that stays smooth with long download lists.",
                "Twelve colour themes, six backgrounds, gradient or solid accents and four presets, applied live to the main window, every dialog and the browser extension.",
                "Adjustable glass effect with an animated light field, rounded corners and a consistent visual style across all windows.",
                "A live download dashboard with speed graph, per range telemetry, error history, retry and cancel.",
                "A searchable download history that stays fast with very large lists.",
                "A rules engine: match downloads by site, type, size or name and set the folder, category, speed limit or queue automatically.",
                "yt-dlp analysis for any site, not only YouTube: pick the exact quality and format, with live progress, cancel and a browser session retry for pages that need you to be signed in.",
                "Separate lists for media found by the browser and media found by yt-dlp, with readable names instead of stream file names.",
                "A single movable download button on the video you are watching, with hide options per site, for a set time or permanently.",
                "A new Axioos Integration Module browser extension (Manifest V3) that talks to the app over a secure native channel and can start the app when needed.",
                "Safe hand over of browser downloads: the browser download is only cancelled after Axioos confirms it took the job.",
                "Local diagnostics export with all private data removed, for easy problem reports.",
                "Update checks against the Axioos releases on GitHub."
            }),
            new ReleaseNoteSection("Fixed and hardened from XDM", new[]
            {
                "Resume is exact: a download only continues when the server proves it is the same file, otherwise it restarts cleanly instead of producing a corrupt file.",
                "Crash safe finishing: interrupted downloads are found and recovered on the next start, with verified backups of the download database.",
                "Temporary server errors (408, 429, 500, 502, 503, 504) are retried instead of failing the download, and 307 and 308 redirects are followed.",
                "Link refresh can no longer be triggered twice for the same download, and stopping a download waits for all workers to finish.",
                "yt-dlp analysis no longer hangs on pages that change their address while you browse, such as X and YouTube.",
                "Detected media is tied to the page you are on, so videos from other tabs or previous pages no longer leak into the list.",
                "TLS certificate checks and strong encryption are enforced for every connection.",
                "The local browser control channel rejects requests from web sites, limits request size and concurrency, and blocks header injection.",
                "Passwords, cookies and private links are never written to logs or diagnostics.",
                "File names suggested by web sites are cleaned so they cannot escape the download folder.",
                "Many dialogs were rebuilt so they no longer freeze, lose keyboard focus or show unreadable colours.",
                "Every error in the interface is now logged instead of silently ignored.",
                "Installer, browser registration and upgrades were repaired and are tested on real Windows installs."
            }),
            new ReleaseNoteSection("Quality", new[]
            {
                "Automated scenario coverage exercises the real download engine across a broad range of download and recovery conditions.",
                "Windows acceptance coverage exercises the installer, browser integration, crash recovery and the user interface on release candidates."
            })
        };
    }
}
