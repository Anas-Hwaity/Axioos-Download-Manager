# Download concurrency and state ownership

Authoritative lifecycle mutations belong to the application/download coordination boundary. Worker threads perform transfer work and report events; they do not independently invent authoritative lifecycle states.

## Invariants

- A requested pause is not `Paused` until active workers have actually stopped at the required durable boundary.
- UI commands address downloads by stable ID and route through application commands.
- Application locks are not held across arbitrary external callbacks or long-running waits.
- Browser takeover has a single durable ownership decision. Duplicate requests reconcile through idempotency state.
- Cancellation and retry are explicit transitions; transport loss is not silently converted into success.
- Presentation telemetry is read-only projection data and may be sampled/bounded without becoming recovery state.

Changes to the downloader event model require native soak and race validation before release.
