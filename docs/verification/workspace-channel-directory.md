# Workspace channel directory and authoring candidate

Status: implementation and regression-test candidate in draft PR #1303. Not a
school-user release decision. Existing pilot NO_GO and held workstreams persist.

## Connected implementation

The authenticated `/teams` route now loads the selected Workspace's bounded
channel directory. The previous joined-conversation and Project-channel activity
view remains at `/teams/activity`; DM remains separate. Directory rows open the
existing Workspace-scoped Conversation/Message and canonical Message Thread UI.
No legacy Channel/Post storage is connected or duplicated.

The new `api/workspaces/{workspaceId}/chat-channels` boundary supplies paginated
metadata discovery, idempotent named-channel creation, public self-join and
manager-driven addition of an existing Workspace member. Public discovery does
not grant Message history access. Private channels are returned only for current
readable participants. Counts and search use the same visibility relation.

Creation uses current persisted Workspace Owner/Admin authority, not global role
claims. The canonical `general` identity remains owned by Workspace provisioning.
The existing idempotency coordinator commits the claim, Conversation, creator
participation, audit and authorization Outbox together. An unchanged uncertain
create retry retains its scoped request identity in memory. A confirmed create
whose follow-up list fails exposes a GET/navigation recovery, not another POST.

Admission rechecks current account, Tenant, Workspace and membership state.
Public self-join cannot restore a removed or explicitly read-denied participant.
Manager addition requires the current channel manager and an admitted recipient;
ReadOnly Workspace participation does not become posting or management authority.
The existing generic Conversation membership, rename, moderation and
thread-creation operations also recheck Workspace contribution for Workspace
channels, and generic channel member admission maps a ReadOnly Workspace
recipient to ReadOnly participation.
These paths serialize their own admission mutations, but do not establish a
globally atomic revocation contract with every existing mutation route.

The UI cancels old requests and clears directory data, names, drafts and pending
state on actor, Tenant, Workspace and explicit authorization boundaries. It does
not infer an authorization grant from an icon, cached row or successful lookup.
Create/join responses must match the expected scope and operation before success
is displayed. Nonparticipant private rows are rejected rather than rendered.

## Design source and adaptation

The read-only CoglatasUI Approved Dark Team Channel board was inspected through
its structure and original PNG: file `52a5041f-08a1-80f1-8008-c5a09f8b5f61`, board
`9b18ec83-d336-8057-8008-c5a1fe4aaa3d`. The candidate uses the existing semantic
colors, a 266px directory column, compact headings, standard Lucide icons and
responsive stacked layouts. Creation and membership dialogs retain keyboard
focus containment, Japanese composition protection where text submits, and
scrollable content with reachable actions.

This is a Workspace-directory adaptation, not complete board parity. Multiple
team/group hierarchy, channel rename/archive/removal UI, private/public conversion,
new reactions, calls, Wiki, Task tabs and arbitrary attachment intake are not
implemented by this slice. No fake users, messages, unread counts or presence are
introduced to reproduce design samples. Rendered six-width comparison, physical
IME and complete accessibility acceptance remain outstanding.

## Verification record

Source `c3abcb27b93e7ca7744859947c88ff7679a2e2f9`, PR CI `39551718858`, completed
both native producer jobs successfully: Angular production build `119062355171`
and .NET graph build `119062355182`. The latter also generated the security
OpenAPI. Other runtime, test and quality lanes were still pending at that read;
these producer outcomes do not establish a full PR pass or qualify successors.

The resumed work adds eleven PostgreSQL facts for persisted creation/replay,
invalid/generic create rejection, public discovery versus history, private member
admission, ReadOnly downgrade of stale channel management, read-denied
self-join rejection and voluntary rejoin, generic Conversation API
management-bypass prevention and ReadOnly recipient admission, current Workspace
authority, scoped paging, literal case-insensitive search with wildcard escaping,
and revoked-target/default-channel boundaries. They use the existing
migrated temporary-database fixture and `PostgreSqlFact`, not an early-return test.

Ten facade regressions cover server-owned capability, same-key retry, 409 replay identity, committed
create recovery, synchronous authorization clearing, Workspace and actor changes,
explicit denial, non-optimistic join and logout. Existing nine pure wire/catalog
cases and additional HTTP command integrity cases are separate mocked evidence.
On the exact prior HEAD `6e627343331d388ad398eeb30a0581d756f4dd43`,
PR CI `39627913231` completed successfully, including PostgreSQL, frontend
and six-width static browser coverage. These successes do not qualify later
review fixes. The new tests cover a further literal-search fact, denied
self-join, idempotency conflict and HTTP 409 mapping; their execution is pending.
Concurrent admission/revocation, real-backend browser runs and independent
release approval remain required.

The editing container has Node/TypeScript but no .NET SDK or Docker, and local Git
checkout failed because GitHub DNS resolution was unavailable. Connected GitHub
CI provides the prior-HEAD test evidence above; no local application build,
browser rendering or PostgreSQL execution is claimed.
No schema, migration, RLS, grant, dependency, lockfile, required-check, quality
threshold, production fixture or deployment change is included.
