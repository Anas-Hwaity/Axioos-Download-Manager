# Contributing

Changes should preserve the documented architecture, compatibility contracts, and release guarantees.

- Fix failure classes, not isolated symptoms.
- Add a failing-first regression for behavioral defects where practical.
- Keep production changes narrow; avoid generated line-ending churn and unrelated refactors.
- Do not add new global mutable state without an approved architecture decision.
- Browser takeover, recovery, migration and release paths fail closed when durability or identity cannot be proven.
- Never commit credentials, cookies, authorization headers, private tokens or full signed URLs.
- External binary updates are reviewed dependency updates; no runtime `latest` self-update is allowed in the public release path.
