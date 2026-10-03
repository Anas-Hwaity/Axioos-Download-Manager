# Browser protocol v1 contract

This directory is the canonical wire-contract fixture set for Phase 4. It mirrors §§38–40 of the canonical engineering plan.

- UTF-8 JSON application messages are capped at **512 KiB in either direction**.
- `protocolVersion` is `1`.
- `messageId` identifies a command; `replyTo` correlates responses.
- `sessionId` is `null` only for the initial `Hello` fixture before `HelloAck` assigns a session.
- Unknown envelope fields remain forward-compatible.
- Unsupported message types must return `UnsupportedMessageType`; they are not success.
- Malformed/oversized native frames terminate the affected native session after redacted logging.
- `sentAtUtc` is diagnostic only.

The existing localhost transport remains a parity bridge until the native-messaging path reaches the Phase-4 exit gate. These files do not declare port 8597 to be the protocol-v1 production transport.
