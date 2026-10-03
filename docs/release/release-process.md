# Release process

A public release is produced only from a clean, identified source commit after the release preflight reports no blocking findings.

Required sequence:

1. Freeze the candidate source commit and record its tree identity.
2. Run the complete private audit and Windows acceptance matrices against this exact commit.
3. Resolve every distributed external binary to an owner-approved exact version, HTTPS provenance URL and SHA-256 in `dependencies/external-binaries.json`.
4. Restore and build from the committed dependency versions; the lock files record the reference resolution, and the hosted build restores without locked mode because the runner leaves out the implicit reference-assembly package. Do not download `latest` artifacts during the release build.
5. Build the exact installer artifact from the verified source/build outputs.
6. Generate the deterministic SPDX source-declaration SBOM with `python tools/generate_sbom.py --output SBOM.spdx.json`, then validate it against the committed lock files and completed license audit; generate provenance records and a deterministic `SHA256SUMS` with `tools/release_checksums.py`.
7. Sign the public executable/installer and verify the signature after signing.
8. Run installer install/upgrade/uninstall and side-by-side identity acceptance on the exact signed artifact.
9. Publish only when `python tools/release_preflight.py --public` succeeds for the clean candidate tree.

A ZIP, ad-hoc PowerShell bundle, unsigned setup artifact or source tree with unresolved dependency pins is not a public release.
