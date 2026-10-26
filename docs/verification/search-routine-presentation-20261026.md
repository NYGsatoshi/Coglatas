# Inbox search presentation after routine catch-up

## Scope and authority

[Issue #1413](https://github.com/NYGsatoshi/Coglatas/issues/1413) remains open;
the historical hosted initiating cause remains UNKNOWN. The already merged
#1427 opener-render readiness correction is independent from this product
lifetime defect. Its owner assertions, deadlines and retry policy are unchanged.
No matching open remote PR or branch was found for the previously mentioned
local-only `Search52e7` candidate, and that candidate was unavailable in this
workspace. This correction was implemented independently from exact Main
`42ba000d9d4fc682165cb65de0a1bb61ad407481`; no held code was integrated.

## Immutable baseline and correction

Test-only comparator `9f9c13da301c15685ea372ad57abfe4d16b5d3e2` changes only
the new 46-line restoration test. Its first execution failed as desired: the
registered list catch-up destroyed the original search component; a successful
fresh list response created a different component with its explicitly opened
drawer closed. Product source was unchanged Main. The comparator is NEVER_MERGE.

Executable correction `4b2e06c46e15777d688173bbef4e39a447cabea0` retains only
two parent-owned local presentation booleans: mobile controls open and advanced
drawer open. Actor, Tenant, Workspace and a presentation boundary bind those
booleans. A separate revision advances on every routine list catch-up and keys
the child instance, including when loading and ready coalesce before Angular
renders. Query, results, applied/draft filters, author labels, requests and route
hydration stay in the destroyed child; none is copied into the parent.

Current-scope ordinary inbox-view selection and successful Later refresh keep
their original mounted-child/non-loading behavior. Their presentation reason
avoids a new teardown introduced by this correction; existing protected-request
cancellation, request generations and Thread boundary advancement are unchanged.
Initial/manual loads, rejected Later state, hard scope/session/authorization
boundaries and recovery from denied/error state invalidate old presentation.
Overlapping routine loads preserve only the two booleans, while each gets its
own fresh child identity. Old-child responses cannot populate the fresh search
child.

Output and queued focus restoration check both captured presentation identity
and child revision. Focus returns only to current local chrome when no newer
focus choice exists. Old widgets and delayed focus tasks cannot retarget a newer
actor, Workspace or refresh. URL filters still resolve again through the
existing fresh-child server requests.

## Validation and retained failures

The baseline and final first regression body are identical (SHA-256
`841113c7e0827964021c29e5556a14c9f81346b3ae7b8b9346747dc5a7b4dc73`).
The unchanged browser owner has SHA-256
`bb4dcf813b2a9f9462ebc0748bd39860a3073497d72868726f63d9a536af5a21`.

Twenty new controls cover restoration, discarded/cancelled search and author
work, coalesced loading, overlaps, old widget output, close intent, HTTP 403/500,
all four protected-state boundary reasons, actual actor/Tenant/Workspace
replacement, URL-author revalidation and newer/obsolete focus choices.
Focused execution passed 173/173: those 20, the unchanged 15 search tests and
138 unchanged Thread tests. An independent reviewer separately passed all 173,
built production Angular and executed the unchanged desktop/mobile search owner:
8 PASS, 4 existing profile exclusions, zero failures/retries. Exclusions are
not PASS. The author's final source-frozen run separately passed all 155 Angular
files / 1,868 tests, a fresh production build and the unchanged native search
owner with 8 PASS / 4 existing exclusions / zero failures or retries. Existing
Vite, focusability and style-budget warnings remain visible in the original logs.

All preparation failures remain separate evidence:

- An initial exploratory test required mounted DOM retention. Review selected
  child destruction plus presentation restoration instead; the permanent
  comparator above freezes that smaller desired contract.
- Three early candidate controls failed when hard scope changes coalesced before
  rendering. Explicit identity-keyed destruction repaired those failures.
- The first candidate native mobile ordinary-search owner timed out filling its
  hidden input after Unread selection. This was a candidate regression caused
  by unnecessary child teardown, repaired by preserving that existing mounted
  lifecycle. The original owner was not modified. This intermediate source was
  an uncommitted working tree based on Main above; no immutable commit identity
  is claimed for that execution.
- A following owner run encountered `EEXIST` because the existing observation
  producer intentionally refuses to overwrite its fixed first-attempt receipt.
  Original artifacts were retained in a separate local directory before using
  fresh outputs. No evidence overwrite, timeout increase or test retry was used.

Dependencies were installed cleanly with pinned npm 11.17.0; local script
invocations used npm 11.9.0 and Node 24.19.0. Tests use Vitest 5.0.5 and
Playwright 1.64.1/Chromium 156.0.8078.4. Mounted
Angular tests and native mocked-HTTP owners do not establish real PostgreSQL,
SignalR, physical-device, Japanese IME or full release acceptance. Current-head
PR CI and resulting-Main qualification remain separate. All Owner HOLD,
security/RLS activation, deployment and school-user boundaries remain in force;
the release disposition is still RELEASE_NO_GO.

## Main synchronization

Normal merge `e443400086ee3a19bb339198f6461026d1657799` incorporates refreshed
Main `ddeacc00bb1879dd72b289f4240093db6f0e3c38` after #1442. The incoming
four-file npm audit checker/workflow/documentation correction does not overlap
this feature. Both the frozen executable and synchronized candidate have the
identical complete frontend Git tree
`9346e8707f6b0f56b21a2b562fc753c6570b95f5`. The previous execution results keep
their original source attribution; current-head hosted checks remain required.
