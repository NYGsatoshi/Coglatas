# A-08 Communication Boundary Baseline

Issue: A-08 - [MVP-A][P0][CommunicationBoundary] Verify conversation, DM, thread, participant, and message access boundary baseline

Date: 2026-06-29

Branch: `main`

Commit: `98731d440ad73d67133f3ca33c80f342ef7cbe61`

Result: Needs verification

This communication boundary baseline does not imply production approval, MVP-A Go, production readiness, or acceptance of unrelated MVP-A blockers.

## A-08 Definition

No repo-owned A-08 definition was found before this evidence file was added. The working definition came from the attached issue text supplied for this task: verify the conversation, DM, thread, message, participant, read-state, notification, realtime or polling, and audit/log access boundary baseline; record evidence; keep unverified areas marked Needs verification or Blocked; and avoid copying real users, real messages, personal data, secrets, tokens, cookies, or session identifiers into evidence.

## Environment

| Item | Observed value |
| --- | --- |
| OS | Windows 10.0.26200, win-x64 |
| .NET SDK | 10.0.301 |
| .NET host/runtime | 10.0.9 |
| global.json | Present, pins SDK 10.0.301 |
| Docker client | 29.5.3 |
| Docker Compose | v5.1.4 |
| Docker runtime | Not verified in this pass; Docker emitted a local config access warning |
| Test data | Synthetic in-memory tenants, users, conversations, messages, notifications, and read state only |

## Implementation Summary

Conversation and DM behavior is implemented by the shared `Conversation` model with `ConversationType.Direct` or `ConversationType.Group`. The API surface is `ConversationsController`:

- `GET /api/conversations`
- `POST /api/conversations`
- `GET /api/conversations/{id}`
- `PATCH /api/conversations/{id}`
- `POST /api/conversations/{id}/leave`
- `GET /api/conversations/{id}/members`
- `POST /api/conversations/{id}/members`
- `DELETE /api/conversations/{id}/members/{userId}`
- `GET /api/conversations/{id}/messages`
- `POST /api/conversations/{id}/messages`
- `PATCH /api/messages/{id}`
- `DELETE /api/messages/{id}`
- `POST /api/conversations/{id}/read`

Notification behavior is implemented by `NotificationApplicationService`, `DbNotificationService`, and `NotificationsController`.

No separate direct-message controller, post-thread-to-conversation endpoint, SignalR hub, WebSocket hub, or dedicated realtime subscription surface was identified in this pass. Channel post threads exist separately as organization/channel content and were not accepted as A-08 conversation-thread coverage.

## Resource Classification

| Resource | Classification | Evidence |
| --- | --- | --- |
| Conversation metadata | active conversation participant scoped | `ConversationAuthorizationService.CanViewConversation`, HTTP tenant tests |
| Conversation body/title | active conversation participant scoped | `GetAsync`, `ToDetailAsync`, HTTP tenant tests |
| DM body | active DM participants only through `ConversationType.Direct` | `CanViewConversation`, new notification-body minimization |
| Message body | active conversation participant scoped | `ListMessagesAsync`, denied-response regression |
| Attachment reference on message | active conversation participant scoped for message listing; file-body coverage remains in A-07 | source inspection |
| Participant list | active conversation participant scoped | `ListMembersAsync`, denied-response regression |
| Participant role/state | active conversation participant scoped; removed participants denied by `LeftAt` guard | source inspection and regression |
| Read receipt / unread cursor | self-only active conversation participant scoped | `MarkReadAsync`, read-cursor regression |
| Notification body | recipient self-only; message notifications now generic | notification tests and new regression |
| Notification metadata | recipient self-only | `DbNotificationService.ListAsync`, mark-read/delete filters |
| Audit/security event metadata | tenant/admin scoped; message body metadata keys redacted | `DbAuditLogger` source inspection |
| Realtime / polling event body | Missing / Needs verification | no SignalR/WebSocket implementation identified |

Unknown resources are not treated as public.

## Actor Matrix

| Actor | Result |
| --- | --- |
| Anonymous user | Protected conversation and notification controllers require authentication; anonymous protected API behavior is already covered in A-03/A-04 evidence. |
| Authenticated normal user | Can use conversation APIs only where active participant checks pass. |
| Tenant member | Tenant membership alone does not grant conversation body access in the tested synthetic path. |
| Non-tenant member / outsider | Cannot read seeded conversation or message bodies in HTTP tests. |
| Conversation participant | Can read seeded conversation messages in HTTP tests. |
| Non-participant | Cannot read seeded conversation messages; denied response does not include synthetic message body or participant email. |
| Removed participant | Cannot read, edit, or delete an existing seeded message after leaving in HTTP tests. |
| DM participant | Covered by the direct conversation model for the tested participant-scoped paths. |
| Non-DM participant | Cross-tenant and non-participant denial tested; explicit same-tenant non-DM pair matrix remains Needs verification. |
| Thread participant / non-thread participant | Needs verification; no dedicated conversation-thread endpoint identified. |
| Message author | Can edit/delete only while still an active participant after this pass. |
| Non-author | Cannot edit by author check; broader moderator/admin policy remains scoped to group conversation admins. |
| Admin / teacher / school admin | No separate message-body admin override accepted in this pass. Admin non-participant DM/body access remains Needs verification by product policy, not Pass. |

## Endpoint Matrix

| Endpoint or surface | Resource type | Required access | Actor tested | Expected result | Actual result | Result |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /api/conversations` | conversation metadata/list | authenticated active participant | synthetic participant | returns only active participant conversations in tenant scope | matched expectation | Pass |
| `GET /api/conversations/{id}` | conversation detail/participant list | active participant | participant / cross-tenant user / outsider | allowed for participant; denied for wrong tenant and outsider | matched expectation | Pass |
| `GET /api/conversations/{id}/messages` | message body | active participant | participant / outsider / wrong-tenant context | allowed participant body; denied body is generic | matched expectation | Pass |
| `POST /api/conversations/{id}/messages` | message create | active participant | synthetic participant | allowed, creates message and generic notification | matched expectation | Pass |
| `PATCH /api/messages/{id}` | message edit | author who remains active participant | removed author | denied after leave | matched expectation | Pass |
| `DELETE /api/messages/{id}` | message delete | author active participant or group admin | removed author | denied after leave | matched expectation | Pass |
| `GET /api/conversations/{id}/members` | participant list | active participant | source plus denial regression | non-participant denial should not expose participant details | matched expectation in denied message test | Pass for tested path |
| `POST /api/conversations/{id}/read` | read cursor | self-only active participant and same-conversation message ID | cross-conversation message ID | rejected without private body exposure | matched expectation | Pass |
| `GET /api/notifications` | notification body/metadata | recipient self-only | recipient A / recipient B | each user sees only own seeded notification | matched expectation | Pass |
| `PATCH /api/notifications/{id}/read` | notification state | recipient self-only | wrong recipient | denied | matched expectation | Pass |
| realtime / WebSocket / SignalR | event body | server-side membership required | not applicable | no unauthorized subscription/event delivery | no implementation found | Missing / Needs verification |
| channel post threads | thread body | channel/group membership | not tested in A-08 | should not be treated as conversation-thread coverage without explicit mapping | not accepted | Needs verification |

Existing safe denials often return `400 BadRequest` through current controller result mapping. This evidence treats those as safe not-found style denials only; it does not accept the HTTP status contract as final.

## Fixes Applied In This Pass

| Area | Change |
| --- | --- |
| Removed participant edit/delete | `CanEditMessage` and `CanDeleteMessage` now require the actor to remain able to view the conversation. |
| Deleted message delete path | `CanDeleteMessage` now rejects already-deleted messages. |
| Read cursor validation | `MarkReadAsync` now rejects a `LastReadMessageId` that is missing, deleted, or from another conversation. |
| Message notifications | New message notifications use a generic body instead of embedding message text. |
| Regression tests | Added HTTP tests for participant-scoped message body access, generic denied responses, removed-participant mutation denial, read-cursor scope validation, and generic message notification bodies. |

## Commands Executed

| Area | Command | Result |
| --- | --- | --- |
| A-08 search | `rg -n "A-08|MVP-A|communication|conversation|message|DM|direct message|thread|participant|read receipt|unread|notification|COMM-01|COMM-02|COMM-08|access control|evidence|blocker" .` | No pre-existing A-08 evidence file found; messaging and notification implementation found. |
| Environment | `dotnet --info` | Passed; SDK 10.0.301 and host/runtime 10.0.9 observed. |
| Docker version | `docker --version` | Passed with local Docker config access warning; client 29.5.3 observed. |
| Docker Compose version | `docker compose version` | Passed; v5.1.4 observed. |
| Build | `dotnet build Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m:1` | Passed; 0 warnings, 0 errors. |
| Initial focused test slice | `dotnet test tests\Coglatas.Tests\Coglatas.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~HttpTenantIsolationTests --logger "console;verbosity=normal"` | First run before rebuild passed 4/4 old tests; rerun after rebuild passed 11/11 including A-08 tests. |
| Full backend suite | `dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1` | Passed; 138/138, 0 warnings, 0 errors. |

## Test Result Summary

| Test surface | Total | Passed | Failed | Caveat |
| --- | ---: | ---: | ---: | --- |
| Focused HTTP tenant and communication boundary slice | 11 | 11 | 0 | Synthetic Kestrel/in-memory data; not live PostgreSQL. |
| Full backend suite | 138 | 138 | 0 | PostgreSQL-specific tests still require separate live connection-string evidence when a live DB assertion is needed. |

## Boundary Results

| Boundary | Result |
| --- | --- |
| Anonymous conversation/message access | Pass by `[Authorize]` route protection and existing A-03/A-04 evidence; conversation-specific anonymous curl on a live app remains Needs verification. |
| Non-participant conversation/message body | Pass for seeded synthetic HTTP test; denied response does not include message body or participant email. |
| Removed participant read/edit/delete | Pass for seeded synthetic HTTP test after fix. |
| Cross-tenant conversation/message ID probing | Pass for seeded synthetic HTTP tests; denied response does not include other tenant message body. |
| DM body | Partial / Needs verification; direct conversations use the same participant guard and message notification body is now generic, but a same-tenant non-DM-participant matrix was not fully built. |
| Thread body | Missing / Needs verification; no dedicated conversation-thread endpoint identified. |
| Read receipt / unread cursor | Pass for same-conversation message validation in synthetic test; broader read-receipt visibility matrix remains Needs verification. |
| Notification body | Pass for recipient scoping and generic message notification body in synthetic tests; broader source notification matrix remains Needs verification. |
| Realtime / polling | Missing / Needs verification; no SignalR/WebSocket hub or polling subscription implementation identified beyond normal list endpoints. |
| Audit/log leakage | Partial; audit metadata redaction includes `body`, `messageBody`, token, cookie, secret, and related keys, but live runtime log review was not run. |

## Limitations

- A-08 is not Accepted because fresh-runtime authenticated communication smoke still depends on the existing MVP-A baseline identity/bootstrap gap.
- Live PostgreSQL, Docker/container runtime, frontend UI, and live runtime log review were not completed in this pass.
- Same-tenant non-DM participant, admin non-participant DM access, teacher/school-admin behavior, explicit thread behavior, SignalR/WebSocket delivery, and broader polling behavior remain Needs verification or Missing depending on implementation.
- Current controller mappings still return `400 BadRequest` for many safe-denial cases; status-code contract quality remains a follow-up.
- No real messages, production tenants, personal data, secrets, token values, cookie values, session identifiers, or connection strings were copied into this evidence.

## Required Follow-Up

1. Resolve the MVP-A baseline identity/bootstrap blocker, then run live authenticated communication smoke with approved synthetic users.
2. Run the full backend suite after A-08 changes and record the result.
3. Add explicit same-tenant DM participant/non-participant tests, including admin non-participant policy.
4. Decide whether channel post threads count for A-08 thread coverage, or add a dedicated conversation-thread implementation and tests.
5. Verify live runtime audit/security logs for denied conversation/message/read/notification actions without copying private bodies.
6. Decide whether safe-denial responses should use 403/404 instead of the current generic 400 mapping.
