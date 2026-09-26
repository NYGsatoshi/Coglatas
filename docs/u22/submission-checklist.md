# U-22 2026 submission checklist

Status: final evidence complete. The final immutable submission identity is
recorded by the annotated `u22-2026-submission` tag and matching GitHub Release
created after this documentation-only finalization commit is merged.

## Submission record

| Field | Final value |
| --- | --- |
| Submission SHA | Raw target SHA recorded in the annotated tag and matching GitHub Release at freeze. |
| Release identifier | `u22-2026-submission` annotated tag and GitHub Release. |
| Freeze date and time (JST) | Recorded in the tag annotation and GitHub Release at creation. |
| Evidence owner | Repository owner / U-22 release operator. |
| Demo environment | Test-only loopback Compose (`coglatas-u22-demo`), ASP.NET Core, PostgreSQL, and an approved licensed frontend build; teardown verified. |
| Last verification date and time (JST) | 2026-08-26 21:56 JST. |

The raw SHA and release identifier must name the same source tree. A tag ref is
not a substitute for recording its raw target SHA. Evidence from an earlier
commit, a mutable branch tip, mocked responses, or a different deployment is
not final-baseline evidence.

## Final record protocol

1. Merge this evidence-only finalization commit.
2. Create the annotated `u22-2026-submission` tag at its exact current commit.
3. Put the raw 40-character target SHA, JST freeze time, and final-gate links
   in both the tag annotation and matching GitHub Release metadata.
4. Use that raw SHA, rather than only the mutable tag ref, in contest records.

## Product vertical slice

- [x] Login is usable with the selected test or review account.
- [x] Workspace selection is understandable; authorized Workspace creation is
  demonstrated if the selected journey creates one.
- [x] Project creation uses the server-projected options, creates a Draft, and
  explicit activation opens the operational Project context.
- [x] New Task clearly names its current Project and does not ask for raw
  internal identifiers.
- [x] Task metadata, named Milestone and eligible Assignee choices where
  authorized, and validation/retry behavior use the canonical create flow.
- [x] Goal, Deliverable, and Constraints are optional structured Brief fields
  whose entered values persist with the Task.
- [x] Source scope shows the effective server-authorized Project policy or a
  permitted complete Task override. The UI does not imply source consumption.
- [x] The advisory quality checklist evaluates Goal, Deliverable, Constraints,
  and effective source policy; it can focus missing Brief fields and never
  blocks creation solely because optional information is missing.
- [x] Create is idempotent, duplicate-submit resistant, and followed by an
  authoritative Task detail view or safe recovery state.
- [x] Task detail makes the current Task state, configured current phase, and
  available Activity understandable without inventing a percentage or history.
- [x] Keyboard operation and the 320-pixel presentation are checked for the
  journey screens.

## Truthfulness boundary

- [x] Demo narration and screenshots describe source scope as policy only.
- [x] No material claims outbound Web retrieval, provider selection, file
  materialization, raw-source persistence, runtime output, or execution unless
  a separately approved implementation and evidence are present in the exact
  baseline.
- [x] Current phase is described as the configured current Workflow Stage.
- [x] Activity is described only as authorized records that already exist. It
  is not presented as durable phase-transition history.
- [x] Any test fixture Activity is labelled synthetic presentation data and is
  not described as a user action, an execution result, or a transition record.
- [x] Test-only seed data, credentials, ports, and Compose overlays are never
  represented as a production deployment.

## Final verification evidence

Record the exact command, environment, final SHA, result, and CI run or local
artifact link for every applicable gate. A skipped provider test, a license
preflight-only branch, or a mocked browser test cannot substitute for its
corresponding required real execution.

| Gate | Required result | Final evidence |
| --- | --- | --- |
| Backend Release build | Pass | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687) on `00e9a74`. |
| Full backend tests | Pass with all skips explained | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687): full backend suite and result verifier passed. |
| PostgreSQL tests and migration application | Pass against disposable PostgreSQL | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687): PostgreSQL migrations and EF model check passed; WPC Final02 and WPC-02B/C/D passed on the product-equivalent tree. |
| EF pending-model check | No pending model changes | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687). |
| Angular unit tests | Pass | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687). |
| Angular production build | Pass | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687). |
| Frontend architecture and Syncfusion license guard | Pass | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687): architecture and licensed-build safeguards passed. |
| Storybook build, where required by current CI | Pass or documented non-gate exception | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687). |
| Pinned Linux Playwright | Pass | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687): Linux Docker Playwright smoke passed. |
| Required real-backend P0 acceptance | Pass against ASP.NET Core and PostgreSQL | [P0 32879036799](https://github.com/NYGsatoshi/Coglatas/actions/runs/32879036799): 7 required tests and 7 JUnit cases passed. |
| Real Backend My Tasks acceptance | Pass | [My Tasks 32879036893](https://github.com/NYGsatoshi/Coglatas/actions/runs/32879036893) passed on the U-22 product-equivalent tree. |
| WPC security acceptance / applicable authorization checks | Pass | [WPC-Final01 32907406707](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907406707), [WPC-Final02 32907409334](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907409334), [WPC-Final03 32907411779](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907411779), [WPC-02B](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907414431), [WPC-02C](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907417712), and [WPC-02D](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907419970) passed on the product-equivalent tree. |
| U-22 same-lineage journey | Pass against the real backend | [P0 32879036799](https://github.com/NYGsatoshi/Coglatas/actions/runs/32879036799): same-lineage Workspace, Project, and Task test passed. |
| Security/dependency checks | Pass or approved, documented exception | [Main CI 32910712687](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910712687), [Documentation CI 32910722755](https://github.com/NYGsatoshi/Coglatas/actions/runs/32910722755), [npm Security Audit 32907404098](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907404098), and [WPC-Final03 32907411779](https://github.com/NYGsatoshi/Coglatas/actions/runs/32907411779) passed. |
| Compose configuration and chosen demo rehearsal | Pass | 2026-08-26 JST: approved-license Test-only loopback rehearsal passed on `00e9a74`; app health, focused same-lineage browser journey, seeded 320px UI, direct-route reload, and scoped teardown passed. |

Product-tree note: [P0 32879036799](https://github.com/NYGsatoshi/Coglatas/actions/runs/32879036799) ran on the same product tree as `baf2540`; `00e9a74` differs only in the Documentation CI workflow. This finalization patch changes only U-22 Markdown evidence, so it does not alter the verified product behavior.

## Security and boundary checks

- [x] An unauthorized Tenant cannot create, read, or infer the selected
  Workspace, Project, or Task.
- [x] An unauthorized Workspace or Project cannot be used through direct
  routes, stale browser state, or crafted create requests.
- [x] Task Brief, source-policy reads and writes, Task detail, Activity, and
  task-related Files retain their existing server-side resource boundaries.
- [x] Lost create permission, stale capability, malformed API success, invalid
  server response, and duplicate create have representative regression
  coverage.
- [x] Cookie-authenticated unsafe requests retain CSRF protection and the
  idempotency boundary remains server-authoritative.
- [x] No credential, Syncfusion license value, test secret, raw source content,
  or protected resource name leaks into logs, artifacts, or documentation.

## Demo and documentation readiness

- [x] Deterministic test-only demo data is reproducible, if used.
- [x] The demo can be repeated from a clean, isolated test stack.
- [x] The scripted product path was rehearsed on the release-candidate source; the tag/release record will name the frozen SHA.
- [x] The demo shows a created Task truthfully and labels any synthetic
  Activity.
- [x] Architecture, implementation, setup, inventory, limitations, and this
  checklist have been reviewed against the exact final source.
- [x] The implementation summary truthfully discloses AI/Codex-assisted
  development and retains repository-owner responsibility for approval and
  submission claims.
- [x] Third-party and repository-license review status is recorded in the
  inventory; commercial components have an approved entitlement path.

## Freeze decision

Do not write `U22_RELEASE_READY` until the annotated tag and GitHub Release
record the exact final SHA, every applicable box above is checked, and the
known limitations remain accurate. After freeze, permit only blocker fixes on
the submission baseline; record each such change by creating a new immutable
baseline and repeating the affected gates.
