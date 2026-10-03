# Dependency rules

- Domain/application behavior must not depend on WPF controls, browser APIs, SQLite implementation details, or yt-dlp process details when an abstraction exists.
- WPF depends on application/core interfaces and ViewModels; it must not query downloader internals directly for dashboard state.
- `SocialAnalysisService` depends on `IExternalMediaAnalyzer` and `IBrowserSessionProvider`; provider implementations cannot create downloads directly.
- Browser takeover ownership is persisted by the desktop before the extension is allowed to cancel the browser-owned transfer.
- Rule evaluation remains pure. Filesystem/database/downloader effects occur only after evaluation at the application boundary.
- New global mutable singleton/static state is forbidden without an ADR.
- Build/package scripts may not rewrite production C#/XAML/JS as a normal architecture mechanism.
- External executable updates are reviewed, pinned dependency changes; runtime self-update to `latest` is not part of the release path.
