# Release verification

Release verification is artifact-specific. Compilation success alone is insufficient.

The verification record must identify:

- source commit and source tree;
- exact setup/executable SHA-256 values;
- Authenticode/signature verification result;
- SBOM/provenance artifact identities;
- external-binary versions and SHA-256 pins;
- acceptance evidence bundle identity;
- installer clean-install, upgrade, uninstall and side-by-side results;
- migration/recovery evidence required by the claimed compatibility table;
- known limitations that remain intentionally unclaimed.

Any mismatch between recorded and recomputed hashes is a release blocker. Any unresolved external dependency pin is a release blocker. Any failed required gate is a release blocker.

`tools/generate_sbom.py` is deliberately offline and deterministic. Its output inventories checked-in NuGet declarations, npm lock entries and `dependencies/external-binaries.json`; it does not turn unresolved pins, unverified licenses or an unlocked restore into release-ready evidence.

Published release hashes are generated and re-verified with `tools/release_checksums.py`; duplicate artifact basenames, symlinks, malformed manifest lines, missing files and hash mismatches fail closed.

## Offline provenance

`python tools/generate_provenance.py --output PROVENANCE.json <artifacts...>` records the exact clean Git commit/tree and SHA-256/size of named release artifacts without network access. The result is deterministic for the same clean source commit and artifact bytes. It **does not constitute a signed attestation** and must not be presented as code-signing, CI identity, or publisher proof. Those remain separate release gates.
