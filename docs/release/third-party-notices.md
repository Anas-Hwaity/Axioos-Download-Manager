# Third-party notices and release audit

This file is a release-audit checklist, not a substitute for the license texts shipped with a release.

## Upstream XDM

The project is derived from Xtreme Download Manager source distributed under GPL terms. Preserve the repository `LICENSE`, source availability obligations, upstream attribution and modification notices in public distributions.

## yt-dlp

The social analyzer is an external dependency. A public package may include/use an owner-approved yt-dlp artifact only after its exact version, provenance URL, SHA-256 and license record are resolved in `dependencies/external-binaries.json`. The current offline manifest intentionally does not claim that release pin yet.

## ffmpeg and other external binaries

If a public package distributes ffmpeg or another external executable, add the exact distributed artifact to the checked-in external-binary manifest and complete its license/provenance audit before release. Do not infer redistribution rights merely because development machines contain a binary.
