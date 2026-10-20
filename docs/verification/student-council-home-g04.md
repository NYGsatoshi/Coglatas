# Student Council pilot Home

## Live revision-442 follow-up

Read-only recovery on 2026-10-20 within 14:52:36–15:09:07 UTC returned the
current G04 structure, generated styles and actual 1536×1024 PNG from CoglatasUI
revision 442. The source identities and hashes are recorded in
[Penpot traceability](student-council-penpot-traceability.md). The historical
revision-339 fallback and unavailable revision-441 probes below remain preserved;
revision 442 is now the active design reference.

The follow-up removes the duplicate eyebrow, uses the localized Home heading,
and adapts the existing four authorized destination links to compact horizontal
cards with 16px gaps and 6px corners. Existing semantic theme tokens are reused.
Workspace data, current selection/authentication guards, capabilities and
Messaging/session Thread state are unchanged. No sample Project/Task metric,
activity, API or permission is introduced.

The follow-up normally composes Main
`f9294cf5142d43b6fd93df5406d69f5d63c32255`. Its first owning unit attempt
passed nine tests and failed one old Japanese heading expectation; after the
expected Home label was corrected, all ten passed. The first revision-442
build passed but reported Home CSS 303 bytes above the existing 4 kB warning
budget. Its fourteen Home browser cases and two unchanged golden cases passed
with zero skips/retries. These outputs are preserved. A selector/shorthand
reduction retains the intended layout without changing the budget or controls.
On `c5e85b68`, all ten owning units passed, the production build passed with
no Home style-budget warning, and fourteen Home browser cases plus the two
unchanged shared-shell golden cases passed with zero skips/retries. All
eighteen retained render originals are byte-identical to the first revision-442
candidate and were visually inspected. Existing unrelated build warnings remain.

The subsequent normal merge of Main
`7c1301bbdeb80a44d1d586a0642b4ec80fa65cb1` adds only the previously accepted
Files provider-test class. All 719 frontend/browser build inputs and 85 built
distribution files remain identical; this source bridge is not a new runtime
test of that Main. Source acquisition and local render/accessibility checks are
complete. Independent follow-up review, final-head CI and real-backend Home
acceptance remain pending.
Full G04 content/font/pixel parity and target-host release are not claimed.

## Historical initial implementation: source and scope

Base Main: `df9e06fe485c73f007e42a58d2c60cb8c6c0a85f`.
Branch: `codex/pilot-home-g04-20261020`.

`/home` is an authenticated pilot entry with four separate destination cards,
an authorized Workspace list/selection, and selected Workspace context. It uses
the existing `WorkspacesFacade`, `WorkspaceSelectionFacade` and `AppShellFacade`;
there is no additional global data owner, HTTP contract or persistence. It does
not inject/load/reset Messaging, read message bodies, mark conversations read,
change draft/retry state or discard session Thread tabs. Existing root/login
Workspace selection is retained; Home is available through navigation and brand.

Role and unread availability come from the mapped server projection. Authoritative
zero remains zero; unavailable is explicit. Members navigation requires existing
`openMembers`; selection delegates to the established boundary and requires
existing `openWorkspace`. Presentation is not authorization. Failed transitions
remain visible, simultaneous selection is blocked, refresh is disabled while
loading, and protected context disappears when the shared state revalidates or
denies access. Feature entry cards follow the existing filtered navigation.

The actual original revision-339 G04 PNG and native tree were inspected before
implementation. Its heading, four cards, continuation panel and context panel
ground the adaptation; the approved Dark tokens then available supplied color/shell styling.
Unsupported sample Task/review/draft metrics, Project/IDE actions, recent/pinned
tabs and work/activity rows are omitted. Then-current revision-441 G04 shape/style/
export recovery failed; **G04 visual parity at that checkpoint was UNVERIFIED**. See
[traceability](student-council-penpot-traceability.md) for identities and hashes.
No Penpot design was edited, and no new dependency or runtime migration is added.

## Historical initial verification

Pinned image: `mcr.microsoft.com/playwright:v1.64.0-noble`; existing dependency
volumes, `CI=true` and `VITEST_MAX_WORKERS=1`. Unit calls used disposable tmpfs
mounts for both root/frontend `.vitest-cache` and `.vite` paths, preventing
cross-revision transforms from shared dependency volumes.

| Check | Executed result |
| --- | --- |
| Home, AppShell projection, Feature menu and Workspace selection units | 50 passed, zero failures/skips; includes pending/failed selection, authoritative zero/unavailable, denied/loading clear, active-pointer mismatch and immediate logout clear. |
| Relevant inventory controls | 49 passed, zero failures/skips, including two new Home component/owner mutation controls. A subsequent final-source CLI recheck verified exactly 42 routes. |
| Source architecture check | Passed. |
| Production build | Passed, initial bundle 941.71 kB / estimated transfer 196.63 kB. Existing initial/style budget warnings remain; the new Home stylesheet did not exceed its budget. |
| Changed browser-owner TypeScript | Strict `--moduleResolution Bundler --module ESNext` check passed for the shell and real Workspace helper files. |
| Pinned-Linux Home browser owner | Final v3: 14 passed in 46.2 seconds, zero skips/retries; six widths in desktop/mobile Chromium profiles plus denied/error/empty recovery. |
| Visual inspection | All 18 v3 originals opened: twelve ready frames at 1536/1366/960/768/390/320 CSS pixels, two denied frames and four narrow scrolled-context frames. The historical original PNG/tree were also inspected. |

The browser owner checks independent destination links, visible focus and 44px
targets, document overflow, unchanged absence of Conversation requests, complete
axe results, trusted Tab access from Open channels to View members, and viewport/
hit-testing of the focused Members control. At 320/390px the lower context needs
vertical scrolling; the additional scrolled originals show its controls intact.
It does not claim every control is above the fold.

Earlier failures remain retained: the first visual run had twelve accessible-name
failures and two passes because the selected button exposed `Select` rather than
its visible `Selected` state. The label and a focused regression were corrected;
axe was unchanged. One intermediate unit run had 48 passes/one failure because
the intentional Workspace icon change still had the old house oracle; the
decorative grid assertion now reflects the explicit Home/Workspace distinction.
A superseded intermediate run was stopped before completion. No failed run is
converted into acceptance evidence.

An additional broad `tsc -p tools/frontend-inspections/tsconfig.repo-scripts.json`
attempt failed on wider repository diagnostics and NodeNext extension rules.
That command did not execute the following chained build and is not counted as
passing. The focused browser-owner check and independent final build above
executed separately. Logs remain under the local ignored Home evidence directory.

## Backend acceptance still required

The existing real-backend Workspace-management owner now reads the actual
authorized Workspace list, opens brand → Home, compares the selected Workspace
and projected Members entry, then returns through the native Members link and
checks the existing Add member control. It adds no fixture, mutation, retry or
cleanup change, and records `homeContextChecked` in that journey's result.
This helper is type-checked but **NOT_RUNTIME_QUALIFIED** here. Its canonical
licensed real-backend P0 execution is still required on the reviewed final head.

The local browser owner uses synthetic static HTTP data and preserves existing
screenshot baselines and thresholds. Neither these images nor the source-bound
checks establish PostgreSQL/permission, public HTTPS, physical-device/IME,
exact-font, complete accessibility or current G04 parity assurance. Independent
review is required before PR publication; exact-head CI and backend qualification
remain required before merge/acceptance decisions.

## Historical required shell golden follow-up

Independent review of frozen `59ff1224910581c66a15368d43785cdb632010a3`
identified that the separate required shared-shell golden case had not run.
The prior candidate receipt and its files remain unchanged. The exact existing
`matches approved Angular P0 screenshot baselines` case was then executed in
the pinned Linux image, with one worker and zero retries:

| Existing case | Initial result | Final unchanged-assertion replay |
| --- | --- | --- |
| Desktop 1280×900, `desktop-shell-workspaces.png`, ratio 0.055 | Passed; baseline unchanged | Passed |
| Mobile drawer 390×844, `mobile-shell-workspaces-drawer.png`, ratio 0.002 | Failed, 4,645 differing pixels; failure/expected/actual/diff retained | Passed after the single approved-scope mobile baseline refresh |

The mobile expected/actual/diff and refreshed original were inspected. The
differences are the intended first Home row, separate Workspace grid icon and
resulting menu/pinned positions. Drawer/header/gutter geometry is unchanged.
Only the failing Linux mobile PNG was regenerated through that exact selected
mobile test; no other baseline, assertion, tolerance, workflow or timeout changed.
Its new SHA-256 is
`cb00d916ceefa170a09616241455119705cd9b0e2b4505b0b131cb573b767ced`,
identical to the retained initial actual. The desktop baseline retains SHA-256
`922374de24f4504bac47303cae96d06802a31f5bb78367e4668069c6f9b65a31`.
The final pair passed **2/2 in 5.4 seconds**, zero skips/retries, with an explicit
process exit 0. This is bounded shared-shell evidence, not a full-suite pass.

The strict focused browser-owner type command was repeated on frozen source
and explicitly persisted exit **0** separately from its empty diagnostic log.
The initial combined wrapper's exit 1 belonged to the mobile snapshot failure.
The configured image resolved during this execution to
`mcr.microsoft.com/playwright@sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f`.
Existing root/frontend dependency volumes and the previously built matching
frontend were reused. No unit runner or unit-transform cache was used in this
follow-up. Exact commands, exits, image identity and file hashes are retained in
the separate private follow-up receipt. Real-backend Home remains unqualified.

## Historical initial native CI and quality correction

Published head `3d52cff52198d5499be8c3f97c621149eabfd1d4` passed all six
required checks in [PR CI 39422098993](https://github.com/NYGsatoshi/Coglatas/actions/runs/39422098993),
attempt 1. Primary output records 1,686 units, 61 architecture controls and
397 static browser passes, with 13 unchanged legacy/project-specific browser
skips. All 14 Home cases and both required shell goldens passed. Skips and
unselected backend/security/performance work receive no execution credit.

SonarCloud's quality gate passed but reported four quality warnings: three
native status-element recommendations and one nested component-selection
ternary. The follow-up uses native `output` elements with valid phrasing content
and matching block styling, and explicit checker branches with the same
assertions. Existing state, authority and routing semantics are unchanged.

Final local qualification passed 10 Home units, the focused strict browser-owner
type check, production build and theme sanitization. The completed 49 inventory
controls and 42-route guard are reused: all indexed production TypeScript/data
inputs remain unchanged; the later unit-oracle refinement is an excluded spec.
The selected pinned-Linux owner passed **16/16 in 38.8 seconds**, zero skips or
retries: 14 Home cases plus the unchanged golden pair. All 18 capture originals
were opened and are byte-identical to the previously reviewed v3 originals.
No further snapshot, tolerance, dependency or workflow changed.

Earlier structural probes, the pre-collection `ENOMEM` failure and interrupted
build remain diagnostic evidence. The inventory stop-race observation was
corrected after final files showed all 49 controls and the guard completed with
exit 0. No failed or incomplete attempt is counted as a behavioral pass.
At that checkpoint, fresh hosted CI/Sonar for this correction and licensed
real-backend Home were pending. Then-current revision-441 G04 parity remained
**UNVERIFIED**.
