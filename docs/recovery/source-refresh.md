# Recovery source refresh

A refreshed URL may be used for an interrupted download only when it represents the same remote object strongly enough for the existing partial bytes to remain valid.

The representation-identity policy prefers strong validators and range-consistent metadata. Weak ETags are not strong `If-Range` validators. Same Content-Length, filename, or URL shape alone does not prove identity. If identity cannot be established, the application must not append new bytes to an old partial as though equivalence were proven.

Refresh is an application command/state transition; UI code does not directly mutate downloader internals. A rejected refresh leaves the existing recovery evidence intact and surfaces a diagnosed state rather than silently restarting into the same partial.
