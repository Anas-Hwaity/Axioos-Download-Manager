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
            new ReleaseNoteSection("New in 1.0.4", new[]
            {
                "The download dashboard opens again, and it shows sizes, speeds and time left in a readable form.",
                "File names in Arabic, Hebrew, Persian, Urdu, Chinese, Japanese, Korean, Thai, Bengali, Hindi, Russian, Greek and every other script are read correctly from the server, so they look right in the list and on disk.",
                "Right-to-left file names keep their extension at the end, and invisible characters that can disguise a file type are removed from names.",
                "A file name supplied by the browser, by a batch, by the command line or by Download again is kept, whether or not Axioos asks before it starts.",
                "Analysing a page with yt-dlp works after Axioos has started with Windows.",
                "Replacing an existing file is safe: the old file stays until the new one is complete, so pausing or cancelling no longer damages it.",
                "Several downloads with the same name that finish together each get their own name instead of failing.",
                "Deleting a download while it is being put together also removes its temporary data.",
                "Resuming checks that the file on the server is still the same file. If it changed, Axioos says so instead of saving a mixed file.",
                "Servers that hand out a file in limited slices are followed to the end, so one slice is no longer saved as the whole file.",
                "After Axioos takes over a browser download, the browser copy is cancelled even when the first attempt fails.",
                "Streaming downloads no longer accept short or misplaced parts, and playlists that use byte ranges are read correctly.",
                "Live streams are recorded until they end. Pause stops the recording and saves what was captured.",
                "Resuming a paused streaming download (HLS or MPEG-DASH) no longer closes Axioos.",
                "A resumed download can no longer stay stuck just before the end.",
                "Setup removes the previous version before it installs the new one. It saves a copy of your download list first and keeps your downloads, settings and logs.",
                "Axioos keeps a log file in its data folder, limited in size, so a problem can be reported with its details.",
                "The copy of the browser extension in the data folder is refreshed after every update."
            }),
            new ReleaseNoteSection("New in 1.0.3", new[]
            {
                "The speed limit now really slows a download down to the value you set. Before, a limited download could run at full speed.",
                "The speed limit link in a download's progress window now limits only that download. The limit for all downloads lives in Settings and in the main window.",
                "The speed limit chosen in the Advanced options of a new download is applied, and it is kept after pause, resume and restart of Axioos.",
                "A proxy chosen for one download is used for that download instead of the general proxy setting.",
                "Looking at a scheduled queue no longer changes its days or turns its schedule off.",
                "Pressing Delete on a running download and answering No leaves it running.",
                "Check for updates now really checks, and says so when the update service cannot be reached.",
                "Streaming downloads (MPEG-DASH) can be restarted and downloaded again, and a restart that cannot work no longer removes the download from the list.",
                "Restart and Download again keep the file name you chose and keep Save as MP3.",
                "A download taken over from the browser keeps the file name the browser showed.",
                "Previewing download rules no longer saves them, so Cancel discards them as expected.",
                "The Default button for the user agent in Advanced settings works.",
                "Copying the same link again after copying something else opens the download window again.",
                "Limits for connections set by a rule are applied again when a download is resumed."
            }),
            new ReleaseNoteSection("New in 1.0.2", new[]
            {
                "Axioos no longer freezes while it joins the video and audio of a large download, and pausing during that step works.",
                "Answering No to Clear finished downloads now keeps the list. Before, the list came back empty after a restart.",
                "Deleting a download that has tags no longer makes the whole list disappear on the next start.",
                "Delete file from disk now removes finished downloads from disk, and leftover data files are cleaned up.",
                "Shut down when all downloads finish only acts when the last download really finished, not after a pause or a failed download.",
                "Queue and scheduler, settings and other windows open correctly when Axioos was started in the tray.",
                "Streaming downloads no longer hang after a dropped connection, and stopping one while it is being assembled no longer leaves a cut off file marked as finished.",
                "A download that needs a user name and password keeps them when it is resumed.",
                "The setup program has its own identity, so it no longer replaces or collides with Xtreme Download Manager. It still upgrades Axioos 1.0.0 and 1.0.1.",
                "From this version on, Axioos remembers when you turn off Start with Windows and keeps it off after later updates. The settings window no longer opens twice after setup.",
                "The browser monitoring switch in the extension is remembered, and the choice to stop showing the download complete window is saved.",
                "The download button that Axioos adds to web pages only reacts to your own clicks.",
                "yt-dlp and its JavaScript helper are checked against the published file before they are installed. They are still downloaded only once.",
                "Cookies are no longer passed on when a download is redirected to a different web site.",
                "Axioos no longer crashes when a second Windows account starts it while another account has it open."
            }),
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
