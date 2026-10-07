# Message catch-up settlement race

PR #1108 remains relevant on Main `746286446c978bdcbd220393d70aff6839dd1b3d`.
This defect is independently demonstrated and is separate from #1113's Task
execution-scope draft overwrite. No Licensed Project File timeout is attributed
to this Message path.

| Stage | Expected | Observed before repair | Evidence | Classification |
| --- | --- | --- | --- | --- |
| Routine Message catch-up waits for dispatched protected commands | Keep already dispatched command alive | Initial protected request snapshot settles | Existing facade and deterministic Message test | Product lifecycle |
| A Report enters while the empty-snapshot continuation is queued | Recheck outstanding requests before reload | One `if` check proceeds to reload and aborts the new Report | Focused regression expects `report.cancelled === false`, observes true with exact Main facade | Proven race |
| Same-generation settlement | Wait until all current protected requests settle | Original candidate loop rechecks after every await | Repaired focused suite 45/45; full suite 1149/1149, no skips | Causal repair |
| Session / Tenant / authorization / Workspace boundary | Cancel and clear immediately | Existing security boundary cancellation retained | Four added boundary variants and existing regressions | Preserved boundary |

The minimal fix replaces the one-time check with a loop bounded by the same
request generation and conversation identity. It changes neither HTTP request
semantics nor authorization, persistence, DTOs, fixtures, waits or timeouts.
The deterministic red proof substitutes the exact Main facade into the PR's
regression suite; the focused success-boundary test fails before repair and
passes after it. New commands are never replayed or automatically dispatched.

Fresh exact-head protected CI and Licensed acceptance, followed by exact merged
Main qualification, remain required before this PR receives merge credit.
