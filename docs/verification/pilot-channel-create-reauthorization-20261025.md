# Workspace channel create-dialog reauthorization — 2026-10-25

**Scope: isolated Angular regression only / release NO_GO / Owner HOLD BOTH.**

## Original source-bound first-attempt failure

Exact Main `b82a2aba6b287935592189e4202e4482487cc63b`,
[Main CI #40137647943](https://github.com/NYGsatoshi/Coglatas/actions/runs/40137647943),
attempt 1, [licensed real-backend P0 #121003583304](https://github.com/NYGsatoshi/Coglatas/actions/runs/40137647943/job/121003583304):
**1 of 8 required cases FAILED**. The failing synthetic Workspace-activation
case had already observed `general`, the correct Workspace, an enabled
channel-create action and completed click. The unmodified 15s
`channel-create-dialog` visibility assertion failed **before any channel POST**.

A bounded private examination of the original Playwright trace found one
additional current-Workspace channel-list GET shortly after that click.
Both nearby GETs returned HTTP 200 with `canCreate=true`, same one-row
synthetic payload. The failure-time snapshot remained in the Teams directory.
The screenshot taken later during test cleanup is not proof of a redirect
causing the original failure. Actual protected-state control-event
correlation is still **UNVERIFIED**. See [P0 #1391](https://github.com/NYGsatoshi/Coglatas/issues/1391).

## Test-only boundary control

The existing `WorkspaceChannelsFacade` registers a synchronous
`registerProtectedStateClearer` and a separate authorized catch-up callback.
The new scoped Angular test verifies this *possible* mechanism only:

1. Fresh granted directory allows the create dialog and a local synthetic name.
2. Protected-state invalidation immediately clears the dialog, name, pending
   action and directory capability without any server create request.
3. Attempting to reopen before reauthorization stays blocked.
4. A fresh 200 current-scope channel-directory result restores `canCreate`,
   **not** the old dialog/draft or an implicit POST. Only an explicit new
   user action reopens a blank dialog, with no automatic send.

These are unchanged existing fail-closed semantics; the test neither asserts
that realtime invalidation caused the original hosted failure nor requires a
protected dialog to remain open while authority is unknown. Subsequent
investigation must distinguish click handler nonexecution, realtime protected
clear, catch-up refresh, and actor/tenant/Workspace change with secret-free
chronology. A genuine authority revocation must keep its old-state purge.

## Non-authorizations and verification

- [ ] Exact-source Angular suite, required PR CI and independent source audit.
- [ ] Retain original Main E2E first-attempt failure, provider artifacts and
      original 15s assertion unchanged; no rerun can waive it.
- [ ] Obtain causal first-attempt real-stack evidence and, only if justified,
      propose a separate reviewed product fix and resulting Main qualification.

No product Angular code, backend API, authentication, SignalR, DB, RLS,
worker, school data, CI required check, timeout, retry, scan budget or
deployment changed. Held [Composer #1361](https://github.com/NYGsatoshi/Coglatas/pull/1361),
full SEC-16/17, exact staging approval, SEC-ARCH/RLS activation,
restore/rollback, physical IME, privacy permission and actual external
17-flow pilot remain **HOLD / RELEASE NO_GO**.
