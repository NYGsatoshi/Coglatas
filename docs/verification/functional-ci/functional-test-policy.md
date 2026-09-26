# Functional CI test policy

Status: canonical Functional CI policy for Issue #577 / FCI-01.

This document defines what Coglatas counts as Functional CI coverage. The
journey inventory is maintained in
[`functional-journey-matrix.md`](./functional-journey-matrix.md), and execution
ownership is defined in
[`functional-gate-topology.md`](./functional-gate-topology.md).

## 1. Functional coverage boundary

A test receives **real functional** credit only when the user-visible behavior
under test crosses the production application boundary needed by that journey.
For the core Coglatas journeys this normally means production Angular,
ASP.NET Core, the real authentication/session/CSRF pipeline, EF Core, and a
migrated PostgreSQL database.

The following classes are distinct and must not be reported as equivalent:

| Class | Meaning | Functional journey credit |
| --- | --- | --- |
| `real-functional` | User-visible journey with core application APIs and required persistence/auth dependencies real | Yes |
| `focused-mock-regression` | Browser/component behavior with one or more core application routes intercepted or replaced | No real-functional credit; useful focused regression only |
| `visual-regression` | Screenshot/layout/theme comparison | No; complements functional coverage |
| `unit/integration` | In-process component/service/controller/domain tests | No journey credit; remains required at its own layer |
| `contract/static` | Build, schema, architecture, lint, static checks | No journey credit |

A test may still be `real-functional` when a genuinely external provider is
replaced at an explicitly approved adapter boundary, provided the application
path being claimed remains real and the journey does not claim the external
provider itself. Mocking or intercepting core auth, Workspace, Project, Task,
File, Message, Notification, Announcement, Audit, authorization, or persistence
APIs invalidates real-functional credit for the affected journey.

`tests/ui/angular-smoke.spec.ts` is therefore focused mock/visual regression,
not the owner of real functional coverage. It remains valuable for responsive,
accessibility, focus, shell, error-state, and screenshot regressions.

## 2. Gate classes

Every journey maps to one or more of these stable gate classes:

- `functional-fast` — required on a relevant pull request. It is a bounded,
  deterministic subset of P0 owner journeys and representative authorization,
  session, and CSRF denials.
- `functional-full` — runs on `main` and owns the complete P0 real-functional
  inventory.
- `functional-extended` — nightly or explicit dispatch. It owns longer P1,
  expanded negative matrices, retry/idempotency/reload/realtime cases, and
  quarantine rechecks.
- `functional-release` — exact-SHA release evidence. It consumes Functional CI
  results and adds deployment/integrated regression evidence such as #481 and
  #482; it does not automatically move every release-only test into PR CI.

Gate names are contracts. Renaming a gate requires an explicit migration of
required checks, final-gate verification, documentation, and any release
evidence consumer.

## 3. Domain classes

Every journey has one primary domain owner. Cross-domain journeys may list
secondary domains, but ownership is not duplicated.

Canonical domain classes are:

- `auth/session/onboarding`
- `workspace/membership`
- `project/task/execution`
- `files`
- `messaging`
- `notification/read-state`
- `announcement`
- `audit/navigation`
- `cross-scope authorization/privacy`

## 4. Priority classes

- **P0** — failure blocks the normal MVP-A product path, authorization/privacy
  boundary, or release confidence. All P0 journeys must have a
  `functional-full` owner; relevant representative subsets are candidates for
  `functional-fast`.
- **P1** — important functional behavior that is allowed to run outside the PR
  critical path. P1 normally belongs to `functional-extended`, and may also run
  in `functional-full` when runtime and determinism allow.

Priority is a product/risk decision, not a statement about current test
availability. Missing coverage remains visible as `BLOCKED`/`NOT_IMPLEMENTED`;
it is not downgraded to make a gate green.

## 5. Required journey metadata

Every row in the journey matrix must contain:

1. stable ID such as `FUNC-AUTH-001`;
2. user-visible goal;
3. deterministic preconditions/fixture;
4. positive path;
5. negative path;
6. primary domain owner;
7. mock policy;
8. required backend/storage dependencies;
9. expected gate(s);
10. P0/P1 classification;
11. existing test/script mapping;
12. timeout/runtime budget class; and
13. current implementation/status when ownership is not yet executable.

Stable IDs are never recycled. A retired journey remains documented as retired
or superseded and points to its replacement ID.

## 6. Runtime budget classes

Budgets are targets for one owner-journey/lane, excluding queued runner time.
They exist to prevent unbounded smoke suites from silently entering the PR
critical path.

| Budget | Target wall time | Intended use |
| --- | ---: | --- |
| `B1` | <= 2 min | PR-fast focused journey |
| `B2` | <= 8 min | normal Compose-backed owner journey |
| `B3` | <= 20 min | expanded negative/reload/realtime journey |
| `B4` | <= 60 min | release/deployment or exceptional explicit journey |

A journey that repeatedly exceeds its budget must be split, optimized, moved to
an appropriate later gate, or have its budget explicitly changed in the matrix.
Increasing a timeout alone is not a coverage fix.

## 7. Result-state policy

Functional status is one of:

- `PASS` — the owner journey executed all required assertions on the candidate
  SHA.
- `FAIL` — the journey executed and a required assertion or infrastructure
  invariant failed.
- `SKIPPED` — the test was discovered but intentionally skipped at runtime.
- `BLOCKED` — the journey could not execute because a required dependency,
  fixture, environment, migration, browser, or service was unavailable.
- `QUARANTINED` — a known unstable test was removed from blocking ownership
  under the quarantine rules below.
- `NOT_RUN` — the journey was not selected by a legitimate routing decision for
  that event.
- `NOT_IMPLEMENTED` — the matrix requires the journey but no owner test exists
  yet.

Only `PASS` satisfies a required journey. `SKIPPED`, `BLOCKED`, `QUARANTINED`,
and `NOT_IMPLEMENTED` must never be translated to PASS. `NOT_RUN` is valid only
when the gate topology says that journey is not required for the event.

Required-test manifests such as
`scripts/ci/real-backend-pr-p0-required-tests.txt` and
`scripts/ci/real-backend-my-tasks-required-tests.txt` are the preferred pattern:
removal, rename, or skip of a required Playwright case must be observable rather
than silently producing a green job.

## 8. Quarantine policy

Quarantine is temporary exception handling, not a second definition of green.
A quarantined journey/test must have:

- a linked tracking Issue;
- the owning stable journey ID;
- the reason and first observed failure;
- a named owner or owning domain;
- an expiry/review date or removal condition; and
- a separate recheck in `functional-extended`.

A quarantined test cannot satisfy the P0 owner slot for `functional-fast`,
`functional-full`, or `functional-release`. If the only owner is quarantined,
the journey status is `QUARANTINED` and the corresponding required gate is not
considered complete.

Retries may diagnose flakiness, but a pass-after-retry must preserve the first
failure in machine-readable evidence. Repeated retries must not erase the
signal.

## 9. Duplicate journey ownership

When multiple tests exercise the same user goal, exactly one is the **owner
test** for Functional CI. Other tests become focused regression, negative
matrix, visual coverage, or release projection.

Selection order:

1. prefer the test that exercises the complete real production boundary;
2. prefer deterministic isolated fixtures over shared mutable data;
3. prefer the smallest test that still proves the journey contract;
4. keep broader cross-screen or public-deployment tests as downstream evidence
   instead of duplicating every feature-owner assertion.

This is why #481 is a deployment-path projection of core real-functional
journeys and #482 is an integrated cross-screen regression gate; neither should
be copied wholesale into every pull request.

## 10. Authorization, privacy, and evidence hygiene

Authorization/privacy negative paths are functional requirements, not optional
security-only checks. Representative 401/403/404, CSRF, revocation, stale
session, and cross-scope denial behavior belongs in the journey matrix.

Functional runners and artifacts must not persist or print:

- passwords or invite tokens;
- auth cookies or CSRF token values;
- protected response bodies that are not needed for the assertion;
- inaccessible Tenant/Workspace/Project/Task/File/Message identifiers or names;
- production-user data; or
- external provider secrets.

Synthetic fixture identities use non-production data (for example
`@example.test`) and deterministic/resettable state where practical.

## 11. Matrix change rule

Any pull request that adds, removes, renames, skips, changes the mock boundary of,
or changes gate ownership for a Functional test **must update**
`functional-journey-matrix.md` in the same pull request when the change affects a
listed journey.

A new Functional test is incomplete until its matrix row identifies:

- its stable journey ID (new or existing);
- owner vs focused-regression role;
- P0/P1 class;
- expected gate(s);
- mock policy and real dependencies; and
- runtime budget.

If no existing journey ID fits, create a new stable ID rather than overloading a
semantically different row. FCI-03/FCI-08 may later automate enforcement, but
this documentation rule is effective immediately.

## 12. Existing suites: canonical interpretation

- `tests/ui/angular-smoke.spec.ts`: focused mock/visual/accessibility regression;
  not real-functional ownership when core APIs are intercepted.
- `tests/ui/run-real-backend-p0.mjs`: current Compose-backed P0 owner slice and a
  seed for `functional-fast`/`functional-full` ownership.
- `tests/ui/run-real-backend-my-tasks.mjs`: real-backend My Tasks owner.
- `scripts/ci/run-mbj01-bootstrap-acceptance.sh`: real bootstrap persistence and
  initial Workspace authorization.
- `scripts/ci/run-mbj02-invite-acceptance.sh`: real invite onboarding and
  negative invite boundaries.
- `scripts/ci/run-mbj03-session-acceptance.sh`: real session/password lifecycle,
  CSRF, restart persistence, suspension/reactivation, and expiry.
- `scripts/ci/run-mvp-a-authz-boundary-acceptance.sh`: representative anonymous,
  admin/member, CSRF, and logout authorization boundary.
- #464 / the Compose-backed Task execution Golden Path: owner evidence for real
  Task execution and durable result behavior.
- #481: protected public-HTTPS deployment projection; release-only unless a
  future decision explicitly changes that contract.
- #482: integrated cross-screen terminal regression; downstream release
  evidence, not a substitute for domain owner tests.
