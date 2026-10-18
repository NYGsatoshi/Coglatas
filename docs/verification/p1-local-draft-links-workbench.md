# P1 local Draft and Links workbench

Status: **IMPLEMENTED_AND_VERIFIED** for the local frontend slice only;
**REVIEW_READY** for independent review. Production Draft synchronization and
Links relationships remain **BLOCKED_BY_OWNER**. No specification approval or
server integration is implied.

## Source and implementation boundary

- Base: Product Main `9b44d631df88cdc2f15a45b6278b3e1b2552a8df`.
- Code-bearing commit: `cef3390a1f10c90ee75d94e09531c81f430a7da8`.
- Branch: `codex/webwave-draft-links-20261019`.
- Verification date: 2026-10-18 UTC / 2026-10-19 Asia/Tokyo.
- Runtime: Node 24; installed frontend manifest pins Angular 22.2.2,
  NgRx ComponentStore 22.0.1, RxJS 7.8.2 and Vitest 5.0.3.
- Final PR head and hosted CI runs must be recorded in the PR after the
  evidence-only commit. A configured check is not an executed check.

`/drafts` is reachable through the **Local drafts** navigation item. The root
`LocalDraftStore` uses the already-installed NgRx ComponentStore without
migrating existing signal facades. It retains one working draft across route
navigation within the same active scope. Text, title and opt-in synthetic
preview links stay in process memory only. Refreshing or closing the tab loses
them; no localStorage, sessionStorage or IndexedDB copy is written. A
before-unload handler warns while content exists, including after leaving the
Draft page, subject to the browser's normal dialog restrictions.

**Keep local checkpoint** creates an in-memory checkpoint, not a server save.
The editor discloses this distinction. New edits during a pending checkpoint
remain intact. A changed checkpoint offers explicit replacement retry or
restore; discard requires an inline confirmation. Adapter errors retain working
edits and show fixed text without raw exception details. Single-flight writes
consume exactly one result; empty responses recover to an error state.

`LOCAL_DRAFT_REPOSITORY` is a browser-only checkpoint seam, with an in-memory
default. `REMOTE_DRAFT_ADAPTER` defaults to **unavailable**, imports no
HttpClient, and cannot represent a successful remote save. Injectable failure,
conflict and denial outcomes support tests; they are not a DTO or endpoint
contract. No Draft or Links network request is made by this feature.

**Enable synthetic Links preview** explicitly opts into a fixed demo catalogue
of Project, Task and File labels. Search, selection, duplicate rejection and
removal affect the local draft only. Selection is disabled during a pending
save, and focus returns to the search control after add/remove. No real
resource lookup, resource navigation, production relationship persistence,
permission discovery or cycle validation is claimed. Those require an approved
contract and a separately reviewed adapter.

Tenant/user/Workspace changes and logout clear text, checkpoints, query and
preview links. The computed projection also redacts the old scope before
cleanup effects run. Public mutations and async results independently check the
current scope; repeated queued old input events and late responses cannot
restore the previous scope. The existing realtime protected-state clearer
immediately clears and cancels pending work on authorization invalidation.

## Executed checks

Commands ran from `frontend/` unless stated otherwise. `npm` was invoked with
its discovered npm 11.17.0 CLI path because it was absent from PATH. Dependencies
were reused read-only through a junction to the Coordinator's successful clean
install; no manifest or lockfile changed.

| Check | Result | Evidence |
| --- | --- | --- |
| `node ./node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/drafts/**/*.spec.ts' --include='src/app/layout/app-shell/*.spec.ts'` | **PASS**, 4 files / 42 tests, 0 failed, 0 skipped | Final local run: 2026-10-18 15:19 UTC; `agent-b-final-unit-2.log` |
| npm `run build` | **PASS** | Production build plus Syncfusion CSS sanitizer; `agent-b-build.log` |
| `node frontend/scripts/check-architecture.mjs` from repository root | **PASS** | Exit 0; canonical theme/vendor boundaries maintained |
| `git diff --cached --check` from repository root before code commit | **PASS** | Exit 0 |
| Built-browser Playwright, axe and Linux screenshot parity for this new page | **NOT_EXECUTED** | Coordinator quality workstream / hosted CI still required |
| Real-backend Draft/Links integration | **NOT_EXECUTED** | No approved production adapter or endpoint is introduced |
| Full frontend and backend suites on this branch | **NOT_EXECUTED** | Focused branch checks only; Coordinator and hosted CI own broader evidence |
| Low-spec hardware, memory and scroll benchmarks | **NOT_EXECUTED** | No numerical performance qualification claimed |

Local logs are retained under
`$env:TEMP/coglatas-webwave-20261019/`. Earlier runs passed 23, 32, 33 and 42
tests before final hardening; they are not added to the final 42-test total.
The final suite comprises 25 store tests, 6 Draft page tests, 2 shell facade
tests and 9 shell component tests. Test adapter scenarios are frontend behavior
evidence, not ASP.NET Core, PostgreSQL or production authorization evidence.

The build reports 929.33 kB initial raw output / 193.60 kB estimated transfer.
Its 500 kB warning and existing component-style warnings remain visible; there
is no new Draft stylesheet warning and no budget increase. These measurements
alone do not establish absence of a performance regression; the Coordinator's
same-environment Main build is the comparison source.

## Review repairs and remaining gates

Independent D browser review found a serious list-semantics violation in the
empty synthetic Links results: assigning `role="status"` to `li` replaced its
required list-item role. The repair keeps the `li` and puts a native `output`
inside it. The failing axe execution remains in `d-interim-browser.log`; the
unchanged integrated browser assertion must pass against the repaired source
before accessibility qualification. No rule or test was disabled.

Coordinator review identified synchronous checkpoint-read exceptions and link
edits changing the pending-operation state. Both were repaired and covered by
regressions. Additional same-tick actor/tenant/Workspace/logout tests verify
immediate redaction and rejection of queued input before effects flush. Empty
adapter output is also covered.

The following remain unavailable: server autosave, cross-device synchronization,
durable general Draft storage, real Links search/mutations, and approved
relationship constraints. Owner decisions for the Draft and Links contracts
remain separate. No private specification content, candidate digest, secret or
production data is included here. SEC-ARCH, RLS, role/GRANT/issuer, migrations,
key custody, disclosure and deployment HOLDs remain unchanged. This change
provides no Avalonia entry qualification.
