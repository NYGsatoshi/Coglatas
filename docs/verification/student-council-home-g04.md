# Student Council pilot Home

## Source and scope

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
ground the adaptation; current Approved Dark tokens supply color/shell styling.
Unsupported sample Task/review/draft metrics, Project/IDE actions, recent/pinned
tabs and work/activity rows are omitted. Current revision-441 G04 shape/style/
export recovery failed; **current G04 visual parity is UNVERIFIED**. See
[traceability](student-council-penpot-traceability.md) for identities and hashes.
No Penpot design was edited, and no new dependency or runtime migration is added.

## Verification

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
