# Architecture overview

## Product boundary

Axioos Download Manager is a Windows download manager derived from Xtreme Download Manager 8.0.29. The architecture is being modernized while preserving the proven download engine and compatibility boundaries that still require legacy external identifiers.

## Main layers

- **ADM.Core** owns application and domain behavior: download creation, queues, rules, history queries, recovery coordination, browser-session handling, social analysis contracts, and telemetry contracts.
- **Downloader implementations** own HTTP, HLS, and DASH transfer mechanics behind `IBaseDownloader` and explicit transfer-policy seams.
- **ADM.Wpf.UI** is the Windows presentation adapter. The main workspace uses the WebView2-hosted Axioos shell while WPF remains responsible for native dialogs and platform integration.
- **Chromium extension** owns browser download interception, page-session state, native media observation, user-facing overlay and popup state, and the browser half of the takeover transaction.
- **NativeMessagingHost / ADM.App.Host** bridge the Chromium native-messaging session to the long-running desktop named-pipe protocol.
- **Firefox sources** remain a legacy compatibility lane and are not the primary 1.0 browser integration path.
- **SQLite data access** owns durable download, history, and recovery records and indexed tag/history queries.

## Important invariants

1. Browser takeover cancels the browser copy only after durable desktop ownership is acknowledged.
2. Native media detection is primary; yt-dlp is a separate user-visible analysis path rather than the only media source.
3. Rule evaluation is deterministic and side-effect-free; side effects are applied once at the download-creation boundary.
4. Recovery and migration fail closed when durable state cannot be trusted.
5. UI state is a projection of authoritative application and download state, not a second source of truth.
6. Release validation covers the exact Windows artifact, browser integration, recovery behavior, and installer path that are publicly claimed.

See the protocol, recovery, privacy, security, and release documentation for the detailed contracts.
