# HTTP resume decision table

| Response to resume/range request | Required behavior |
|---|---|
| `206` with expected start offset and compatible representation | Continue from the verified offset. |
| `206` with conflicting start/range | Reject the response; do not write it into the existing partial. |
| `200` when a Range resume was requested | Never append the full body to the partial. Re-probe/restart according to recovery policy. |
| `416` | Reconcile remote length/local state; do not assume completion without verification. |
| Strong validator changed | Treat representation as changed; do not reuse incompatible partial bytes. |
| Weak ETag only | Do not use as a strong `If-Range` validator. |
| Content encoding/representation changed | Reject unsafe append/resume and re-evaluate source identity. |
| No validators | Use the weaker documented identity/restart policy; never infer equivalence from length alone. |

Validate these semantics with deterministic protocol fixtures and native crash/restart scenarios before release.
