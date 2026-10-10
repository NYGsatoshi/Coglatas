# Task denial and Project refresh regression

The Task detail facade now preserves an authoritative safe denial while the
Task view remains mounted. A denied Task read cancels older Project collection
requests and queued overview refreshes. Subsequent overview events cannot
replace that denial with a compact cached Task row or an indefinite loading
state. A fresh authorized Task aggregate can restore the detail, and actual
Task view destruction permits authorized Project overview loading.

## Preserved hosted failure

PR #1188 run `38003771870`, attempt `1`, job `114070470605`, tested merge
`4fafaefb52c5b6d101e5e8a8dcda309ae2408f80` failed the existing `AUTHZ-002`
browser assertion at
`tests/functional/security-negative/cross-scope-negative-matrix.spec.ts:371`.
After Workspace membership revocation and `page.goBack()`, the Task GET
returned safe `404`, but `permission-denied-state` was absent.

The original allowlisted diagnostic receipt has SHA-256
`081984e240f77dd1843cb00e7a035b68353b0837d8fb198f936be715ba378847`.
It does not contain a protected Task DOM projection or screenshot. The exact
hosted callback ordering therefore remains inferred. The Task facade source
in that tested merge and the local repair base
`f05195a333ebb60c5001ed93cadf9e0013ec9a00` has identical Git blob
`6e4c56b12d117689e41ec79961578f579d831211`.

## Deterministic regression controls

The unchanged production source failed three focused controls: a late Project
Task collection replaced denial with `ready`, and queued Project refreshes
before or after the denied aggregate replaced denial with `loading`.
The isolated baseline reported **56 passed, 3 failed**. An initial baseline
also retained cleanup cascades; those failures have not been discarded.

The first repair passed those controls, but one additional ordering exposed
the same loading regression after Workspace invalidation released the Task
resource IDs. A subsequent guard based on `Router.url` passed that ordering,
but a positive navigation control demonstrated that the outgoing URL could
block a legitimate Project retry after detail teardown. These intermediate
failed controls remain preserved in the local development receipts.

The final guard tracks the mounted detail view separately from its released
resource IDs. Workspace invalidation preserves its denial until a fresh
authorized aggregate or actual view teardown; session and Tenant boundaries
retain their existing clearing behavior. Existing real-time catch-up paths
still perform fresh authorization before restoring protected projections.

Five new controls cover:

- Late Project collection completion after a safe Task denial, followed by a
  positive fresh authorized aggregate and parent Project read.
- Project refresh queued before the denied aggregate.
- Project refresh queued after the denied aggregate.
- Project refresh queued after Workspace invalidation releases resource IDs.
- Authorized Project loading after actual denied Task view teardown, while
  the outgoing Router URL still names the Task.

## Local verification and limits

The final local development patch passed **71 tests**: all **61** Task facade
controls and all **10** Task detail component controls, with no failed or
skipped cases. `npm run build` completed using the existing production
configuration. Existing bundle and component budget warnings remain visible;
no threshold or baseline was changed. A comment-only clarification was added
after these checks.

The HTTP testing backend provides deterministic frontend regression evidence.
It does not establish PostgreSQL behavior, real backend compatibility, or a
fresh hosted `AUTHZ-002` pass. The browser matrix, its safe-denial assertions,
and its 15-second expectation timeout are unchanged. Exact-head hosted browser
qualification remains required, with the original failed run preserved.

This repair grants no SEC-ARCH normative approval, product activation, or
assurance promotion. The broader pre-Avalonia qualification remains blocked
by its separately recorded engineering gaps and owner decisions.
