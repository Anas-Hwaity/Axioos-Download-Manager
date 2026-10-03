# Browser protocol v1

Browser protocol v1 is the local contract between the Chromium extension/native host path and the desktop application.

## Transport

The browser extension uses `runtime.connectNative()` to reach the registered native host. The native host bridges to the desktop over the product-scoped named pipe. The previous fixed localhost HTTP control plane is not the authoritative v1 path.

## Envelope

Messages use UTF-8 JSON and carry `protocolVersion`, `messageId`, `sessionId`, `type`, `sentAtUtc`, optional `replyTo`, and `payload`. The application-level maximum message size is **512 KiB in either direction**.

The first exchange is `Hello` / `HelloAck`. Unsupported protocol versions or product identities fail explicitly rather than falling through to best-effort legacy message shapes.

## Command results

Command results use stable statuses such as `accepted`, `rejected`, `duplicate`, `busy`, `unsupported`, and `error`, plus a stable machine-readable code and human-safe detail.

## Takeover safety

A browser download remains browser-owned until the desktop has durably accepted the takeover. The extension may pause the browser download while ownership is negotiated, but it resumes on transport loss/rejection/timeout. Browser cancellation occurs only after durable ACK. Duplicate requests are reconciled through durable idempotency/ownership state.

## Page sessions and media

Media observations are keyed by both tab and `pageSessionId`. Top-level navigation/reload/SPA history changes create a new session and stale observations are rejected. Native/browser media observations remain available independently from optional social analysis.

## Security

- unknown message types return unsupported;
- malformed/oversized messages fail closed;
- URLs, strings, arrays, and headers are bounded before expensive work;
- extension/product identity is validated;
- the desktop pipe is scoped to the current user and still performs protocol/product validation;
- secrets are not included in normal protocol logs.

The executable schema/DTO implementation remains authoritative for exact field names; this document describes the stable v1 behavior contract.
