# Thread intent during routine catch-up

Related investigation: [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365),
[#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360) and
[#611](https://github.com/NYGsatoshi/Coglatas/issues/611).

On baseline Main `0c5f9baace186940f948758b1731fd3ec891c2f3`, a controlled
Angular/HttpTestingController interleaving reproduces a separate local race.
Routine catch-up captures an open root A and clears protected projections.
Fresh authorized conversation/messages finish while the parallel conversation
list remains pending. The user can now open authorized root B and type its draft.
When the list finally finishes, the continuation restores its older root-A
snapshot, replacing B with A in loading state and hiding B's draft.

The repair records local Thread navigation and per-root close intent. Captured
same-scope tab metadata and drafts merge with current entries, which win for
matching roots. A root closed after capture cannot return. A newer selection or
close prevents automatic selection of the old root while unrelated captured
drafts remain available for a subsequent fresh authorized read. Existing current
generation and fresh actor, Tenant, Workspace, Conversation, route and participant
checks remain mandatory. No old response body is cached or restored, and posting
authority is unchanged. Reopening retained A requires a new authorized Thread GET
before its body or draft renders; B retains its newer active projection and draft.

The focused controls cover a new selection, a close before list completion,
a closed captured root, late list 401/403 denial, and an initially empty captured
tab list. Reopening retained A must preserve its draft after a fresh HTTP read. They require
rendered matching root/draft or complete protected-state clearing as applicable.
No existing owner assertion, retry, timeout, threshold or required job changes.

This is a reproduced component mechanism, not the established initiating cause
of hosted #1365 or #1360. In particular, an empty captured tab list already skips
restoration on baseline Main; this repair does not explain the historical
post-reload missing-panel observation. Browser tests with mocked HTTP or direct
routine-entry invocation do not qualify real HTTP/SignalR/PostgreSQL acceptance.
Physical IME and exact resulting-Main Full acceptance remain separate.

No API, schema, migration, RLS, security policy, deployment or Owner HOLD changes.
The existing held PRs remain untouched. Release remains NO_GO.
