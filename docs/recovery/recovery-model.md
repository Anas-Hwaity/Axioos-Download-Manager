# Recovery model

Recovery state is durable application state, not an inference from the visible download list. The canonical recovery schema is owned by `RecoverySchema`; startup classification is performed by `RecoveryStartupReconciler`, with integrity/safe-mode checks before mutating recovery state.

## Safety rules

- Never fabricate progress when a partial file is missing or inconsistent.
- Never silently replace a corrupt database with a new empty database.
- Preserve DB/WAL/SHM evidence before repair when integrity is suspect.
- Remote representation identity is validated before resuming partial bytes; equal filename/length alone is insufficient.
- Finalization is idempotent. If a final file exists after a crash, verify it before committing completion state.
- Destructive cleanup is delayed until the durable completion/finalization boundary is satisfied.

## Startup classifications

The recovery layer distinguishes recoverable, completed, missing, inconsistent, and needs-attention states from durable records and filesystem evidence. Native forced-kill/storage-failure proof remains a release-gate item; offline source verification does not promote it.
