# Conversation load denial normalization

Date: 2026-10-19. Base Main:
`fda001ffba8d5dfa120324c8bbdee17746eb2d81`.

## Failure and bounded correction

The production authentication interceptor returns `FrontendApiError.httpStatus`.
Two Conversation detail/message GET callbacks still forwarded `error.status`.
During rejected-send revalidation, normalized 400/403/404 responses therefore
entered the transient-error branch: protected rendered content was hidden, but
the same user's stored draft and retry identity could survive and return after
a later successful reload. A normalized ordinary detail 403 also rendered the
empty state instead of the existing permission-denied state.

Both callbacks now use the existing `httpStatus` helper, preserving support for
raw Angular and normalized production errors. Existing generation cancellation,
actor/scope boundaries and denial cleanup remain the owners of clearing. A
successful authorized revalidation after POST 400 still retains a validation
draft and stable retry identity; transient failures retain stored recovery state
while keeping protected visible content empty.

This is an ordinary error-classification/state-cleanup correction. No
unauthorized body disclosure or capability-gain tuple was established. It adds
no API, permission, role, database, dependency or new authorization contract.
The separate Direct-read P0, Group and Workspace Owner decisions remain held.

## Executed regression evidence

The owning tests use the actual `authSessionInterceptor`, actual error adapter
and CSRF service with Angular's HTTP testing backend. A passive observer asserts
that the received production errors carry the rejected `httpStatus` and no
`status` property. This is frontend behavior evidence, not real-backend or
PostgreSQL acceptance.

- Final tests against unchanged Main callbacks: **7 failed / 18 passed**, exit1.
  Six cases cover detail/message revalidation at normalized 400/403/404 and
  assert persisted draft cleanup, empty protected rendering, no later draft
  restoration and a fresh retry identity. The seventh covers ordinary detail403
  classification.
- Fixed owning suite: **25/25 passed**, exit0.
- Seven related suites: **197/197 passed**, no failures/skips. These include
  Messaging UI/generation boundaries, threads, composer/IME, draft storage,
  interceptor and error-adapter behavior as well as recovery.
- Frontend architecture check: **passed**.
- Production build: **passed**, initial930.13kB, estimated transfer193.49kB.
  Existing size/tooling and CDK test-focus warnings remain; no budget, timeout,
  test assertion or quality policy was relaxed.

Runner: `mcr.microsoft.com/playwright:v1.64.0-noble`, `CI=true`, one Vitest worker,
existing dependency volumes and no installs.

## Source/runtime binding qualification

Initial shared-cache runs were inconsistent: post-fix runs repeated the old
failure, and a later unchanged-callback run passed despite a dumped Angular
bundle still containing the old callbacks. Those runs remain retained and are
not accepted as final positive evidence. Disposable mounts for
`/work/frontend/node_modules/.vitest-cache`, `/work/node_modules/.vitest-cache`
and the corresponding `.vite` directories restored reproducible red/green
results without deleting shared state or changing repository test policy.

The accepted red and green runs use exactly the same final regression source.
Their source receipts record the respective callback file SHA256 and helper
presence; the old callback file has no diff from the exact base Main.

Local ignored evidence under `artifacts/messaging-normalized-denials/`:

| Receipt | SHA256 |
| --- | --- |
| `red-confirmed.log` | `cf530bb1b06c19ed8337d6c3dd73e2b05c898b9074b0c1687fd4911567ef35dd` |
| `green-confirmed.log` | `f5a46704f94bc20ccdc41958565bb442b0596a005c4a488dca472d30d8382264` |
| `related.log` | `aadde16f7d7a472e322eb2e91ef9cf584dba04ae5b11509729ad3b8704dd768d` |

`red-confirmed-source.json`, `green-confirmed-source.json`, `build.log` and
`architecture.log` accompany those receipts. Independent review and hosted
exact-head/Main gates remain the coordinator's next qualification steps.
