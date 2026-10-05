# A-09 Audit Security Baseline

Issue: A-09 - [MVP-A][P0][AuditSecurity] Verify audit log, security event, denial tracking, and metadata-only logging baseline

Date: 2026-06-29

Branch: `main`

Commit: `9a317c7449453d79fb9d41e4f81e70ba0aae2d87`

Result: Needs verification

This audit/security baseline does not imply production approval, MVP-A Go, production readiness, or acceptance of unrelated MVP-A blockers.

## A-09 Definition

No repo-owned A-09 evidence file was found before this file was added. The working definition came from the attached issue text supplied for this task: verify that authentication, authorization denial, private data access denial, file denial, conversation/DM denial, admin operation, and destructive-operation risk are recorded as audit or security events where implemented; verify audit/security records are metadata-only; record evidence without copying raw logs, real messages, real files, secrets, tokens, cookies, passwords, connection strings, or personal/private bodies.

## Environment

| Item | Observed value |
| --- | --- |
| OS | Windows 10.0.26200, win-x64 |
| .NET SDK | 10.0.301 |
| .NET host/runtime | 10.0.9 |
| global.json | Present, pins SDK 10.0.301 |
| Docker client | 29.5.3; config access warning observed |
| Docker Compose | v5.1.4 |
| Docker runtime | Not verified in this pass |
| Test data | Synthetic in-memory tenants, users, conversations, files, audit logs, and security events only |

## Implementation Summary

Audit storage exists through `AuditLog` and `SecurityEvent` entities. Audit writes are centralized in `DbAuditLogger`; audit/security reads are served by `DbAuditQueryService` and `AuditController`.

Access controls confirmed in source and tests:

- `AuditController` is `[Authorize]`, with additional platform/system role requirements on platform routes.
- `DbAuditQueryService` requires authentication plus tenant owner/admin or platform/system admin.
- A-09 tightened tenant-admin audit and security-event reads with explicit `TenantId` predicates in addition to the EF tenant query filter.
- Tenant-admin audit/security read tests cover tenant scoping, non-admin denial, and platform-admin global reads on synthetic data.

Metadata minimization confirmed or improved in this pass:

- `DbAuditLogger` removes metadata keys named `password`, `token`, `secret`, `rawFilePath`, `filePath`, `messageBody`, `body`, `cookie`, `connectionString`, and `environmentVariable`.
- A-09 removed raw submitted email values from `AuthService` login security metadata; login security events now use `emailProvided` and `userId` when known.
- A-09 added metadata-only conversation/message denial audit entries with generic summaries and no request/message body metadata.
- Existing file denial audit entries are generic and metadata-only.

## Event Category Matrix

| Event category | Should audit | Implemented status | Severity | Required fields observed | Forbidden-field check | MVP-A status |
| --- | --- | --- | --- | --- | --- | --- |
| authentication success | yes | implemented | info | action, user id, timestamp, tenant via current context when available | raw submitted email removed from metadata in A-09 | Needs verification |
| authentication failure | yes | implemented | warning | action, emailProvided, user id when known, timestamp | raw submitted email removed from metadata in A-09 | Needs verification |
| authorization denied | yes | partial | warning/security varies by caller | action, actor id, target type/id where logged | no raw body observed in tested paths | Needs verification |
| tenant boundary denied | yes | partial | warning/security | tenant/query role checks and scoped results | cross-tenant audit/security reads tested with synthetic data | Needs verification |
| project boundary denied | yes | partial | unknown | authorization tests exist; broad denial logging matrix not complete | no private body copied into evidence | Needs verification |
| conversation access denied | yes | implemented for service denial paths changed in A-09 | warning by audit action | actor id, action, target conversation/message id, timestamp | HTTP test asserts no synthetic message body/email/storage key in denial audit metadata | Needs verification |
| DM access denied | yes | partial | warning by audit action | shared conversation model path | same-tenant non-DM pair and admin/non-participant policy still incomplete | Needs verification |
| message/thread access denied | yes | partial | warning by audit action | message/conversation id on covered message denial paths | message body not logged in covered denial entries | Needs verification |
| file metadata denied | yes | implemented for covered file service paths | warning by audit action | actor id, action, file object id, timestamp | generic summary; no filename/body/storage key metadata | Needs verification |
| file download denied | yes | implemented for covered file service paths | warning by audit action | actor id, action, file object id, timestamp | generic summary; no file body/storage path/signed URL metadata | Needs verification |
| upload denied | yes | partial | unknown | quota-denial audit exists; full upload failure matrix incomplete | no real file data copied into evidence | Needs verification |
| admin operation | yes | implemented for many service actions | info | actor id, action, target type/id, timestamp, selected role/status metadata | metadata call sites mostly ids/status; full admin matrix incomplete | Needs verification |
| role/permission change | yes | implemented for system role and membership paths | info | old/new role or target user id | no password/token/body observed in inspected metadata | Needs verification |
| participant add/remove | yes | implemented | info | actor id, action, conversation id | no message body metadata | Needs verification |
| grant create/revoke | yes | not fully inventoried | unknown | not verified | not verified | Needs verification |
| notification access denied | yes | partial | unknown | notification self-scope tests exist; denial audit not fully verified | notification body exposure addressed in A-08 | Needs verification |
| destructive DB operation attempt | yes | not implemented as a dedicated runtime event | unknown | not found | not verified | Needs verification |
| seed/migration failure | yes if applicable | not verified | unknown | not verified | not verified | Needs verification |
| suspicious invalid id access | yes | partial | warning by audit action where covered | submitted target id on covered conversation/file paths | resource body not logged in covered tests | Needs verification |
| repeated denial/rate-limit candidate | yes if implemented | not implemented or not verified | unknown | not verified | not verified | Needs verification |
| error/exception event | yes | partial | error/security unknown | generic exception response path fixed in A-05 | raw stack traces not returned by global handler; live logs not captured | Needs verification |

## Denial Logging Result

A-09 added metadata-only audit logging for conversation/message denial paths in `ConversationService`:

- conversation get/list-members/list-messages/mark-read access denial;
- conversation update/add-member/remove-member manage denial;
- message send/edit/delete denial.

The focused HTTP test asserts denied conversation reads create `ConversationAccessDenied` audit entries and that those entries do not contain synthetic message bodies, participant email, or storage key metadata.

Remaining denial logging gaps: same-tenant DM non-participant/admin policy, thread/realtime surfaces, grant/revoked-grant cases, notification denial events, rate-limit/repeated denial candidates, and live runtime log review.

## Admin Operation Audit Result

Admin and tenant-admin operations have audit calls in services such as `AdminService`, `TenantService`, `TenantAdministrationService`, integrations, announcements, forms, projects, groups, events, files, and tenant exports.

This pass did not complete every admin operation in the requested matrix. Audit log read access itself is authorization-gated and tenant-scoped for tenant admins, with platform/system global reads reserved to platform/system roles.

## Communication Audit Result

Communication mutation events already existed for create, participant add/remove/leave, message sent/edited/deleted, and attachment added. A-09 added denial audit entries for covered denied conversation/message operations.

No confirmed message/DM body in audit metadata was observed in the tested synthetic paths. Same-tenant non-DM policy, admin non-participant DM/body policy, thread behavior, and realtime/polling audit coverage remain Needs verification.

## File Audit Result

File upload/download/delete and denied file access audit entries exist in `FileService`, with A-07 evidence covering generic denied file metadata/download audit entries and private cache headers.

Explicit grant/revoke, revoked-grant access attempts, object storage/signed URL behavior, and live file audit review remain Needs verification.

## Error/Exception Logging Result

A-05 changed `GlobalExceptionHandlingMiddleware` to return generic unhandled-exception responses in every environment. This A-09 pass did not capture live runtime logs and did not verify all internal logging sinks. Raw stack traces, connection strings, SQL values, and secrets were not copied into this evidence.

## Audit Log Access Boundary Result

A-09 tightened `DbAuditQueryService` so tenant-admin audit/security reads include explicit `TenantId` predicates in the query service, not only ambient EF filters. Existing and rerun tests cover:

- tenant admin sees only current-tenant audit logs;
- tenant owner without workspace filter remains tenant-scoped;
- tenant admin querying another tenant workspace gets no audit rows;
- tenant admin sees only current-tenant security events;
- platform admin can read global audit/security data;
- non-admin audit query is denied.

## Test Result Summary

| Command | Result | Sanitized summary |
| --- | --- | --- |
| `dotnet test tests\Coglatas.Tests\Coglatas.Tests.csproj --filter "FullyQualifiedName~TenantIsolationSecurityTests|FullyQualifiedName~HttpTenantIsolationTests"` | Blocked in sandbox | NuGet restore attempted `api.nuget.org:443` and was blocked by sandbox socket permissions. |
| same focused command with approved network access | Pass | 31/31 passed before the login metadata regression was added. |
| `dotnet test tests\Coglatas.Tests\Coglatas.Tests.csproj --no-restore --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~TenantIsolationSecurityTests|FullyQualifiedName~HttpTenantIsolationTests"` | Pass | 42/42 passed after final A-09 changes. |
| `dotnet test tests\Coglatas.Tests\Coglatas.Tests.csproj --no-restore` | Pass | 146/146 backend tests passed after final A-09 changes. |

## Result

Needs verification.

No confirmed audit/security log private body, token, cookie, password, connection-string, file body, or message body exposure was observed in the tested synthetic paths after the A-09 changes.

A-09 acceptance is not fully satisfied because live runtime logs were not captured, Docker/PostgreSQL runtime evidence was not refreshed, the complete event-category matrix was not executed end-to-end, and several denial/admin/file/communication categories remain partial or not implemented.

## Required Follow-up

- Run fresh-runtime authenticated audit/security smoke after the baseline identity/bootstrap blocker is resolved.
- Capture sanitized live audit/security summaries without copying raw logs or private data.
- Complete same-tenant DM, admin/non-participant DM, grant/revoked-grant, notification denial, thread, realtime/polling, destructive-operation, migration/seed failure, and repeated-denial/rate-limit audit matrices.
- Decide whether authentication security events may store `SecurityEvent.Email` for authenticated current-user contexts, or whether that column also needs pseudonymization/minimization by policy.
- Extend metadata sanitization beyond the current key denylist if future call sites may pass arbitrary metadata.

## Explicit Warning

A-09 completion does not mean MVP-A Go or production readiness.
