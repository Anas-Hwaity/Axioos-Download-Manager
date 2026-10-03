# Security policy

This repository treats browser-to-desktop messages, remote HTTP metadata, filenames, external analyzers, imported state and update artifacts as untrusted inputs.

## Release security invariants

- Browser takeover is fail-safe: the browser copy is cancelled only after durable desktop ownership is acknowledged.
- Fixed localhost HTTP is not an acceptable final browser control plane.
- Cookies, authorization values, passwords, bearer tokens and full signed URLs must not appear in normal logs or release diagnostics.
- External executables distributed with a release require an approved version, HTTPS provenance URL and exact SHA-256 pin.
- A corrupt recovery database is preserved as evidence; the application must not silently replace it with an empty database.
- Release binaries/installers require published checksums, an SBOM/provenance record and signature verification before public promotion.

## Vulnerability handling

Do not include credentials, cookies, signed URLs or private user data in a public issue. Use a private maintainer/security channel configured for the repository when reporting a vulnerability. If no private channel is configured, do not publish exploit details or secrets; contact the repository owner privately first.
