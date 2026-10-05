# TASK-V1-PR07 owner decisions

Status: Resolved

Audit baseline: `491d17db3701b7fb26010db8c0590eac7d24bd78`

Original specification baseline audited: `6e8e5c3651adeedc7a2709124e9af0fd927d35b5`

Canonical resolution:

- Coglatas-Spec PR: `NYGsatoshi/Coglatas-Spec#62`
- Specification merge commit: `8b90c8897367606473515d17d3696e458b2ee7b5`
- Resolution date: `2026-08-02`
- Canonical authority: `docs/specs/coglatas-core-v4/01-core/15-workspace-task-messaging-owner-decision-resolution.md` in Coglatas-Spec

This file records the implementation-repository synchronization of `PR07-OWNER-001` through `PR07-OWNER-003`. The questions below are closed and MUST NOT be reopened during PR07 implementation unless a later canonical specification change explicitly supersedes them.

## PR07-OWNER-001 — Exact mandatory recipients

Status: Resolved

### Approved mandatory recipient matrix

| Event category | Mandatory recipient source | Mandatory recipients |
| --- | --- | --- |
| Primary Assignee assigned | post-mutation relationship | new Primary Assignee |
| Primary Assignee removed | pre-mutation relationship captured inside the business transaction | previous Primary Assignee |
| Reviewer assigned | post-mutation relationship | new Reviewer |
| Valid direct mention | server-validated mention targets | each valid directly mentioned user |
| Submitted for Review | current Reviewer | current Reviewer |
| Returned or rejected from Review | current Primary Assignee | current Primary Assignee |
| Task becomes Blocked | current Primary Assignee and Reviewer | current Primary Assignee and current Reviewer |
| Major hard-deadline change | current Primary Assignee and Reviewer | current Primary Assignee and current Reviewer |
| Important `TaskComment` | current Primary Assignee, Reviewer, and Collaborators | current Primary Assignee, current Reviewer, and all current Collaborators |

### Required rules

1. Mandatory recipients and Watch-derived recipients are evaluated as separate sets.
2. Explicit unwatch suppresses only Watch-derived activity; it does not suppress a mandatory recipient.
3. The authenticated user actor is removed from the combined recipient set, including self-assignment and self-mention, unless a future canonical contract creates an explicit exception.
4. Relationship removal or replacement captures the previous recipient before mutation in the same business transaction. A rolled-back transaction creates no notification intent.
5. Overlapping relationships are deduplicated to one visible Notification per recipient-specific logical event.
6. A direct mention and Important marker on the same TaskComment share the `TaskCommentSignificant` source-event group and produce one recipient union.
7. Current authorization is checked at notification-intent creation, immediately before delayed/replayed dispatch, and when the notification target is opened.
8. Ordinary TaskComment activity without a valid direct mention and without the Important marker MUST NOT notify all Task participants.
9. Broad Task projection invalidation routes are not mandatory visible-notification recipients.

### Implementation consequence

PR07-B MUST implement the matrix centrally and MUST NOT infer additional mandatory recipients from creator, watcher, broad Project membership, realtime route membership, or UI visibility.

## PR07-OWNER-002 — Digest local-time granularity

Status: Resolved

### Approved contract

- `deadlineDigestLocalTime` is a local-time-without-timezone value.
- Allowed values are `00:00` through `23:45`, inclusive, at exactly 15-minute granularity.
- The Workspace default is `08:00` local time.
- A null per-user/per-Workspace value inherits the Workspace default.
- `effectiveDeadlineDigestLocalTime` is always non-null.
- `workspaceTimeZoneId` is the authoritative timezone identity used to derive the due instant.
- Browser timezone and Project settings are not authoritative.
- Project-specific digest time does not exist.
- Invalid format, minute, or range returns typed HTTP 400 `TASK_NOTIFICATION_PREFERENCE_INVALID_LOCAL_TIME`.
- The server MUST NOT silently round, coerce, substitute, or fall back from an invalid supplied value.

### Preference API contract

```text
GET   /api/me/workspaces/{workspaceId}/task-notification-preferences
PATCH /api/me/workspaces/{workspaceId}/task-notification-preferences
```

GET and successful PATCH return:

```text
deadlineDigestLocalTime nullable
effectiveDeadlineDigestLocalTime non-null
workspaceTimeZoneId
version
optional matching ETag
```

PATCH requires `expectedVersion`.

- stale or omitted `expectedVersion` returns typed HTTP 409 `TASK_NOTIFICATION_PREFERENCE_VERSION_CONFLICT`;
- the losing or malformed request does not mutate the stored preference;
- only safe current-version/ETag retry metadata may be returned;
- another user's preference is never exposed through these current-user routes.

### Implementation consequence

PR07-A is unblocked and owns persistence, DTOs, validation, inheritance, active-membership authorization, optimistic concurrency, and focused HTTP/PostgreSQL evidence for this contract.

## PR07-OWNER-003 — Background-job and Outbox delivery profile

Status: Resolved

### Approved state-machine boundary

| Responsibility | Owner | Automatic attempts and terminal state | Operator behavior |
| --- | --- | --- | --- |
| Task deadline-digest generation | separate `NotificationDispatchJob` digest idempotency/claim ledger | at most 3 attempts, then terminal `Failed` | audited restart creates a new generation attempt; no separate digest dead-letter table |
| User-visible Notification | Notification persistence | recipient-facing read/open lifecycle | normal recipient-owned operations; no scheduler semantics |
| Transactional Outbox delivery | dedicated Outbox contract | at most 10 attempts, then terminal `DeadLetter` | bounded, capability-gated, audited replay with current authorization rechecked |

### Required rules

1. The digest ledger owns Workspace-local scheduling, candidate identity, idempotency, claim, generation attempts, and terminal generation state.
2. The digest ledger MUST NOT be used as an Outbox scheduler.
3. The Transactional Outbox MUST NOT be used as digest candidate or idempotency state.
4. A short database claim timeout is allowed only for digest concurrency safety and does not introduce the general Full-scope `JobLease` feature.
5. Digest `Failed` is not Outbox `DeadLetter`.
6. Successful digest generation creates the authorized visible Notification and its existing `Notifications.NotificationCreated.v1` Outbox signal.
7. Outbox claim, retry, dead-letter, replay, retention, and stale-lock recovery remain governed by the dedicated Outbox contract.
8. Current authorization is rechecked at Notification creation, Outbox dispatch/replay, and Notification open.

### Canonical realtime event boundary

PR07 uses the existing approved event families:

```text
Projects.TaskChanged.v1
Projects.TaskAssignmentChanged.v1
Projects.TaskWorkflowChanged.v1
Projects.TaskCommentChanged.v1
Projects.ProjectChanged.v1
Notifications.NotificationCreated.v1
Notifications.NotificationReadStateChanged.v1
Security.AuthorizationStateChanged.v1
```

PR07 MUST NOT create an equivalent `TaskDeadlineDigestReady` event or invent unapproved per-category Task event families. Any new event family requires a separate canonical catalog amendment with exact schema, routing, authorization, and acceptance.

## Privacy and payload boundary

Broad Task events, Outbox payloads where not recipient-specific, ordinary logs, and ordinary audit metadata MUST NOT contain:

- TaskComment or Task description body;
- review reason;
- Watch source or opt-out state;
- digest preference value;
- complete digest Task list;
- recipient relationship set;
- restricted titles or display fields;
- attachment content, storage path, grant, or token;
- credentials or secrets;
- stack traces, SQL, raw errors, or authorization internals.

Recipient-specific presentation remains subject to current authorization and must fall back to a safe unavailable state.

## Implementation-start decision

The canonical owner-decision gate is complete.

```text
TASK-V1-PR07 overall implementation: GO for the sequential PR07 lane
PR07-A: GO
PR07-B: blocked only by PR07-A completion
PR07-C: blocked only by PR07-A and PR07-B completion
PR07-D: blocked only by PR07-A through PR07-C completion
PR07-E: blocked only by PR07-A through PR07-D completion
```

One implementation lane remains mandatory:

```text
PR07-A -> PR07-B -> PR07-C -> PR07-D -> PR07-E
```

This synchronization changes documentation status only. It does not implement production Notification, digest, Outbox, SignalR, API, database, or Angular behavior.