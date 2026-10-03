# Chromium extension store-package boundary

`python tools/package_extension.py --output ADM-extension.zip` creates the deterministic runtime-only Chromium package. It excludes repository tests and embeds `ADM-EXTENSION-PACKAGE-MANIFEST.json` containing each packaged file's byte count and SHA-256.

This package is **not** store acceptance evidence. Public submission still requires owner-approved product identity/artwork, final permission/privacy disclosure review, store signing/publication, and browser-version acceptance. The package tool does not broaden permissions or change `manifest.json`.

## Firefox legacy package boundary

The same deterministic packager can package the tracked Firefox runtime with `--root ADM/firefox-amo`. Firefox remains a legacy MV2/local-control compatibility lane in this source boundary; packaging it is **not** AMO acceptance evidence. `dependencies/firefox-extension-permissions-policy.json` freezes the currently declared permissions and Gecko extension identity so accidental permission or identity expansion fails the offline release audit. Permission reduction is not guessed offline where AMO compatibility requirements are authoritative.
