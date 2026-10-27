# Workspace member detail during routine catch-up

Status: **IMPLEMENTED_NOT_VERIFIED** for hosted qualification. Student Council
remains **RELEASE_NO_GO** and every existing Owner hold remains in force.
Tracking: [P0 #1411](https://github.com/NYGsatoshi/Coglatas/issues/1411).

## Retained first-attempt failure

Exact Main `d78f3722f04c3573c3f0890f9ca950d24090144e`,
[run 40219519075](https://github.com/NYGsatoshi/Coglatas/actions/runs/40219519075),
attempt 1, [licensed job 121248997329](https://github.com/NYGsatoshi/Coglatas/actions/runs/40219519075/job/121248997329):
seven of eight required P0 browser cases passed and the Workspace case failed,
with no browser retry. The original 15-second `workspace-member-detail-name`
assertion failed in `workspace-member-management-real.ts:163`. The call site
is the **second** role transition at line 77, restoring ReadOnly to Admin;
the second PATCH had not been attempted. This is separate from the earlier
Channel-dialog failure and the Thread failures on Main f31/0149.

Protected artifact `12608468939` was independently acquired. Its actual ZIP
SHA-256 matches the upload receipt:
`4e38c88c3d5ebd5815fe50d434a666166ca2f5d67442881422409fd0afdb167c`.
Only bounded timing and structural conclusions are retained publicly:

1. The first selected detail HTTP read and UI assertion succeeded. The explicit
   ReadOnly PATCH then returned 200, followed by reload and the existing
   persisted-role/recipient-denial assertions.
2. The second native detail click ran at 65,632.819–65,658.666 ms in the trace's
   monotonic timeline. Its distinct detail GET started afterward, at
   65,675.471 ms, and completed with 200 at 65,687.129 ms. Earlier matching
   detail work had already finished. The helper's identity/body checks passed.
3. A same-route member-list GET started at 65,687.935 ms and completed with 200
   at 65,698.582 ms; a management-options GET overlapped it.
4. The unchanged detail-name assertion ran at 65,729.641–80,743.780 ms and
   found no element. A discrete recorded frame in that interval shows the
   Members directory and ReadOnly target row with no detail panel. Cleanup
   navigation began later, at 80,748.969 ms. The uploaded failure screenshots
   show that later cleanup route and do not prove navigation caused the failure.

A successful HTTP response does not prove Angular applied its result. The
trace does not identify which callback initiated the adjacent list refresh;
ordinary catch-up versus a protected invalidation must not be inferred from
HTTP ordering alone. Raw traces, cookies, identities, URLs and screenshots remain
outside repository/public artifacts. No generic migration log or unrelated
successful owner is used to waive the browser failure. Extended owners were
blocked by the failed licensed dependency and supplied no execution credit.

## Independently demonstrated product mechanism

The registered ordinary `WorkspaceMembersFacade` catch-up previously called
the same list reload used by an explicit manual recheck. That reload always
closed and cancelled the selected detail without restoring navigation after
fresh reads. Deterministic controls reproduce the loss both while the selected
detail request is pending and after its authorized body has been applied.
Overlapping ordinary catch-ups also lose the selected identity.

Test-only RED `64333827d5cf2023b0924e2405c2631f8c462511` leaves the product
unchanged on Main d78: **50 PASS / 3 FAIL**, with no teardown cascade. This
establishes the local mechanism; it does not retrospectively prove the original
hosted callback or a causal relation to the preceding Thread-only correction.

## Bounded correction

Only a registered routine catch-up may retain the selected Workspace/member
navigation identity in a loading state. It immediately discards the displayed
name and role and cancels the superseded detail request. A fresh authorized list
must still include the target, followed by a distinct ordinary detail GET that
validates its returned identity. List-only names or roles never supply detail
authority. Overlapping list/detail refreshes retain only that scoped identity.

The existing manual reload still clears detail without automatic restoration.
User close/new selection, target disappearance, errors/denials and hard session,
Tenant, Workspace or authorization boundaries veto old navigation. Detail/list
callbacks and the public detail projection also check current AuthSession actor
and Tenant identity; a response from a replaced identity cannot repopulate the
detail while the central boundary observer is pending.

The management facade still closes its editor and confirmation on catch-up.
Restoring read-only navigation never restores a mutation dialog, selected role,
confirmation, command authority or automatic write. Server membership checks,
role contracts, API, RLS, global Realtime, Messaging, IME and deployment remain
unchanged. No deadline, retry or canonical real-backend assertion is changed.

## Local validation and remaining qualification

The final relevant selection passes **314/314 tests across 17 files**, including
80 owning facade controls and two real Angular page/facade controls with mocked
HTTP and a stubbed grid. They cover pending/applied detail, overlapping list and
detail reads, close/new selection, missing targets, list/detail denials, actual
AuthSession user/Tenant refresh, route replacement, all four protected clears,
manual recheck and independent management-editor/confirmation cleanup.
No skipped tests or retries. Clean lock-matching dependencies report Vitest
5.0.5. Existing Vite and jsdom focusability warnings remain visible.

```sh
npm --prefix frontend test -- --include='src/app/features/workspaces/**/*.spec.ts' --include=src/app/core/auth/auth-session.facade.spec.ts --include=src/app/core/workspace/workspace-selection.facade.spec.ts --include=src/app/core/realtime/realtime.facade.spec.ts
npm --prefix frontend run build
npm --prefix frontend run check:architecture
git diff --check
```

The production build, architecture and diff checks pass; existing bundle/style
warnings and budgets are unchanged. These Angular controls are not actual
ASP.NET Core/PostgreSQL or physical-device acceptance. Required exact-head CI,
independent review, the unchanged real eight-case owner and independent
resulting-Main qualification remain required. Historical first-attempt failures
and P0 investigations stay open until their source-bound exit criteria are met.
