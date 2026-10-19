# Message confirmation and uncertain POST recovery

An own root `MessageCreated` event can arrive before or after an uncertain
Message POST transport failure. Previously, the timeline could show the
confirmed Message while the composer still reported failure and retained its
draft and retry identity.

The facade now reconciles the current send status only with a confirmed row
carrying the same client request identity and current author. A later event
must also match the current Tenant and Conversation. Confirmation clears only
the matching scoped draft; a different draft remains intact. An in-flight POST
stays tracked and continues to block another send until its response settles.
The server's existing idempotency and authorization contracts are unchanged.

Authorization, session, Tenant and Workspace boundaries still cancel requests
and clear their existing protected state immediately. Explicit POST access
denials still clear content even if an earlier event showed a confirmation.
Legacy POST 400 still requires the existing full HTTP revalidation. This change
does not preserve retry identity across authorization clearing, automatically
replay commands, or qualify immediate-create/send recovery in a real browser.

`message-send-recovery.spec.ts` exercises both event/error orderings for
status 0 and 503 with optimistic rendering enabled and disabled, unrelated
identity/scope controls, a newer command, failed-row retry with another draft,
explicit access denials, and authorization clearing followed by a fresh
authorized projection. These are Angular HTTP-testing and synthetic event
controls, not actual backend, PostgreSQL or WebSocket evidence.
