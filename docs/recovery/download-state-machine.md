# Download lifecycle state machine

The canonical lifecycle distinguishes pause, cancel and delete. They are not aliases.

## Persisted states

Legacy numeric values remain stable: `Downloading=0`, `Stopped=1`, `Finished=2`, `Waiting=3`. New values are appended: `Cancelling=4`, `Cancelled=5`.

## Pause

`Downloading -> Stopped` in the current legacy persistence adapter. The coordinator records a Pause stop intent before signaling the worker. The worker quiesces and keeps partial state. Telemetry reports `Paused`. Resume is allowed.

## Cancel

`Downloading|Stopped|Waiting -> Cancelling -> Cancelled`. `Cancelling` is persisted before any worker is signaled. The default dashboard policy is `RetainPartial`; the entry remains retryable but cannot be resumed as if it were merely paused. `DiscardPartial` is an explicit policy that deletes partial state/temp data only after worker handles have quiesced while retaining request metadata required for Retry/Download again.

## Delete

Delete/history removal remains a separate operation and is never implied by Cancel.

## Recovery rule

A startup observation of `Cancelling` is not success. Recovery must reconcile the worker/state files and complete cancellation or surface NeedsAttention; it must not silently reinterpret the record as Paused.
