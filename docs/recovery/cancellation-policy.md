# Cancellation policy

Dashboard Cancel uses `DownloadCancellationPolicy.RetainPartial` by default. This preserves recoverable bytes and request metadata while making the lifecycle terminal with respect to Resume. Retry is the explicit way to create a new transfer attempt.

`DiscardPartial` is opt-in. Ordering is: persist `Cancelling`; signal/quiesce worker; detach worker handles; persist `Cancelled`; remove partial state/temp data. Request info is retained so Retry remains possible.

Pause is not Cancel: Pause retains partial data, records `Paused` telemetry, persists the legacy `Stopped` status and remains resumable. Delete is not Cancel: destructive entry/history removal is a separate user action.
