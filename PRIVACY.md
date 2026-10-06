# Privacy

Axioos Download Manager is designed as a local desktop download manager with browser integration. The application does not require a cloud account for its core download workflow.

## Browser integration

The current Chromium extension can observe browser download events and, when browser monitoring is enabled, media-related network requests so it can present downloadable media detected on the current page. Browser-to-desktop communication uses native messaging and the local desktop protocol; media URLs and metadata are not sent to an Axioos-operated remote service by this codebase.

The current development manifest still requests broad host/network permissions needed by the inherited monitoring model. Reducing these to optional permissions remains a release-hardening item; public store disclosures must match the exact permissions in the released extension.

## Social analysis and cookies

Native/browser media detection remains the primary path. `Analyze with yt-dlp` is a separate user-visible action for public sites. Analysis contacts the selected site through yt-dlp. On Windows, the app may download the Deno JavaScript runtime when it is needed for YouTube analysis.

First-pass social analysis is cookie-free. If the analyzer reports that authentication is required and browser-session assistance is enabled, target-origin cookie material may be passed locally from the extension to the desktop for that operation. The desktop session store is operation/origin-scoped, single-use, held in memory, bounded, and expires after a short lifetime. Cookie values must not be written to normal logs.

## Local data

Download history, recovery state, rules, settings, and diagnostics are stored locally. Diagnostic exports must sanitize secrets such as cookies, authorization values, passwords, bearer tokens, and sensitive signed-query values before release use.

Axioos keeps a local log file, `log.txt`, in its data folder (`.adm-app-data` in your user folder). It records what the app did and any errors, with cookies, passwords and signed link values removed. The log never leaves your computer unless you send it yourself. It is limited in size: when it reaches about 4 MB it is renamed to `log.1.txt` and a new one is started, and the three most recent older files are kept. Setup keeps this folder when you update or remove the app, and stores a copy of your download list in its `backups` folder before each update.

On NTFS, Axioos also writes a hidden `Axioos.Origin` alternate data stream beside a completed file. It records that the file was downloaded by Axioos and the completion date. The marker does not change the file's contents. It is skipped on file systems that do not support this stream.

## Telemetry

No telemetry upload is enabled by default by the architecture defined in this repository. Internal download telemetry used by the dashboard is an in-process/local projection of download state, not an analytics upload service.
