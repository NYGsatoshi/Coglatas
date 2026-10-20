# First live Message in an empty conversation

An authorized conversation with no initial Messages has `status: empty`.
Previously, a current scoped `Messaging.MessageCreated.v1` event appended its
confirmed Message without changing that status. The timeline deliberately hides
rows while the status is empty, so the first incoming Message could remain
invisible even though its new-message banner appeared. A local send already
advanced an empty timeline to ready.

The receive path now makes the same empty-to-ready transition after its existing
Tenant, conversation, deletion and main-message checks. Other statuses remain
unchanged. No timeline visibility guard, subscription, authorization boundary,
HTTP fallback, polling interval or send/retry behavior changes.

The owning rendered regression starts with an actual empty authorized Group,
DirectMessage or WorkspaceChannel HTTP projection, emits the scoped event and
requires the confirmed row and disappearance of the empty state without an
additional HTTP request or local send. It also covers duplicate delivery,
wrong-Tenant/conversation events, a denied complete Message aggregate and late
events after session, Tenant, authorization and Workspace clear boundaries.
These component tests use HTTP and transport stubs; they do not establish real
backend or target acceptance.

The original licensed `f4e2500` run's Group UI assertion failed after its selected
public MessageCreated wire assertion passed. Its retained recipient trace shows
the new-message banner before the failed row assertion. Incremental snapshot
references were not reconstructed into a complete DOM. Preserve that original
failure; qualification of a successor still requires the unchanged real-backend
receive and subsequent unread/lifecycle journey, followed by independent review.
