# Messaging create-dialog scope-bound asynchronous callbacks

Status: **IMPLEMENTED IN REVIEW BRANCH / EXACT-HEAD CI PENDING / RELEASE NO_GO**.

## Current source boundary

This independent Web pilot change concerns the existing new Direct Message and
Group DM creation dialog in the Angular `MessagesPageComponent`. It does not
implement the held #1361 composer draft preservation approach, change
the Thread owner, or modify any authentication service or server API.

Before this change, closing the create dialog already unsubscribed
the active recipient lookup, and an Angular effect reacted to Workspace
changes. However, a response callback can execute **before a scheduled
change-detection effect**, after the authenticated actor/tenant/Workspace
has already changed. The prior recipient callback wrote its received
names and the prior create callback could route a successful old-Workspace
conversation to `/dm/...` without synchronously checking current scope.

The repair captures a local, opaque dialog identity at open:
`[current Tenant ID, current actor ID, current Workspace ID]` plus a local
generation. It is **not** a credential and never substitutes for backend
authorization. Search results/errors, recipient selection and Group picker
updates require the same active identity/generation. A successful or failed
create response from an old scope is discarded instead of rendering names,
committing current UI selection or navigating. The existing Angular
cleanup effect still closes/reset the dialog for genuine actor/Tenant/
Workspace changes. Closing/reopening the same scope replaces the
generation, so late callbacks cannot attach to a newly opened dialog.

Server request bodies, retry/idempotency, authorized current-scope
conversation success, existing Group-vs-Direct DTO verification and server
authorization remain unchanged. A command committed before a scope change
is **not rolled back** by browser UI; this guard prevents UI attribution
to a different current identity, not server-side cross-scope authorization.

## Focused regression expectations

- [ ] An in-flight synthetic recipient lookup from Workspace A cannot render
      a Workspace A display name after switching to Workspace B, even when
      its response completes before the scheduled Angular cleanup effect.
- [ ] A pending Direct creation response from Workspace A cannot navigate
      into that old-scope conversation after Workspace B becomes active.
- [ ] A pending Group creation success from Workspace A cannot navigate
      under a now-different Workspace, despite containing a formally
      valid old-Workspace DTO; this is **in addition** to existing
      wrong-workspace-response denials.
- [ ] Current-scope Direct and Group successes remain usable and the
      existing recipient selection, IME, dialog focus restoration,
      permissions, server API and full tests remain unchanged.

## Qualification and release

Run the exact-head Angular unit tests including `messaging-ui.spec.ts`
and `group-dm.spec.ts`, independent source review, required PR CI
and actual first-attempt Functional and Security checks. Verify that
a canceled HTTP operation does not show a user-facing success or leak
a stale server response. Do not infer a security bypass or a resolved
real-browser compositor/Thread defect from this local boundary correction.

The existing unresolved first-attempt Composer #1360 / Thread #1365
failures, SEC-16/17 full matrix, strict RLS/SEC-ARCH mapping approvals,
explicit approved staging SSH/HTTPS environment, backup/rollback,
school privacy/operator approval and actual 17-flow release acceptance
remain independent. **Both Owner HOLDs / public release NO_GO.**
