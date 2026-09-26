# Security Rules

This document defines security and authorization rules for Coglatas Portal. It is based on the current modular monolith structure in `src/Coglatas.Web`, `src/Coglatas.Application`, `src/Coglatas.Domain`, and `src/Coglatas.Infrastructure`.

Use this document with `docs/SECURITY.md`, `docs/API_CONTRACTS.md`, and `docs/AUTHORIZATION_MATRIX.md`.

## Role Names

Canonical documentation role names map to the current implementation as follows:

| Documentation role | Current implementation |
| --- | --- |
| `PlatformAdmin` | `SystemRole.PlatformAdmin`; `SystemRole.SystemAdmin` is an alias used by older code paths and controller role attributes. |
| `TenantAdmin` | Active `TenantUserRole.Owner` or `TenantUserRole.Admin` in the current tenant. |
| `WorkspaceAdmin` | Active `WorkspaceRole.Owner` or `WorkspaceRole.Admin` for the workspace. |
| `GroupAdmin` | `GroupRole.Owner` or `GroupRole.Admin` for the group. |
| `ProjectManager` | `ProjectRole.Owner` or `ProjectRole.Manager` for the project. |
| `Member` | Active resource member. The concrete enum depends on the scope, such as `TenantUserRole.Member`, `WorkspaceRole.Member`, `GroupRole.Member`, `ChannelRole.Member`, `ConversationMemberRole.Member`, or `ProjectRole.Contributor`. |
| `Guest` | `TenantUserRole.Guest`. A tenant guest has no elevated permissions unless also granted explicit workspace, group, channel, conversation, or project membership. |

Existing system roles `Teacher` and `Admin` are elevated staff roles for some event and announcement workflows. They are not tenant-wide administrators unless they also have `TenantAdmin` membership.

## 1. Authentication Rules

- Cookie authentication is the current browser UI authentication mechanism.
- Protected endpoints must require an authenticated user before tenant or resource data is returned.
- Login, invite registration, file upload, search, and API-token creation must stay rate limited when rate limiting is enabled.
- Passwords must be hashed with the configured password hasher. Raw passwords must never be stored, returned, or logged.
- Session keys, invite tokens, API tokens, webhook secrets, and similar credentials must be stored only as hashes.
- Suspended, archived, deleted, locked-out, expired-session, or revoked-session users must not be treated as authenticated active users.
- Password change, logout, role changes, tenant membership changes, and user suspension/archive operations must revoke affected sessions where implemented.
- Cookie settings for production must require HTTPS, secure cookies, HttpOnly cookies, same-site protection, HSTS, and persisted Data Protection keys.
- Unsafe cookie-authenticated browser requests must use CSRF validation when `Security:EnableCsrfProtection` is enabled.
- API token request authentication middleware is not currently complete. Until it exists, API-token validation must not be assumed to protect normal endpoints.

## 2. Authorization Rules

- Authorization must be enforced in Application services, not only with controller attributes.
- Controller `[Authorize]` and role attributes are coarse gates only. They do not replace tenant, workspace, group, channel, conversation, project, file, event, or admin checks.
- Tenant access must be checked before resource access.
- Every ID-based read, update, delete, download, archive, restore, membership, or status operation must verify that the authenticated actor can access that exact resource ID.
- Normal tenant endpoints must not allow `PlatformAdmin` to become an accidental cross-tenant bypass. Platform-wide actions belong under explicit platform/admin routes and must use explicit tenant predicates.
- Resource managers can act only inside the scope they manage. For example, a `ProjectManager` can manage project members and project work items only for that project.
- Author-owned permissions, such as editing a message or comment, must still verify that the actor can access the parent scope.
- Permission failures should return safe errors. Prefer not to reveal that a resource exists across tenant or resource boundaries.

## 3. Tenant Isolation Rules

- Tenant is the highest-level isolation boundary.
- Never trust client-provided `TenantId` on normal tenant endpoints.
- Resolve tenant from authenticated/server-side context whenever possible: host, subdomain, configured default, session cookie, or development-only tenant header when explicitly enabled.
- The development tenant header must remain disabled outside controlled development/setup flows.
- Tenant-owned entities must implement `ITenantEntity` and include `TenantId`.
- `AppDbContext` global tenant query filters are part of the isolation model and must not be bypassed in normal Application services.
- Normal tenant writes must use the current tenant context. New tenant-owned records with empty `TenantId` are stamped with the current tenant.
- Normal tenant writes with a different `TenantId` must be rejected.
- Platform-scope writes must set `TenantId` explicitly and must be limited to explicit platform or tenant infrastructure paths.
- `IgnoreQueryFilters` is allowed only for explicit platform/tenant infrastructure operations with a tenant predicate and a comment explaining the bypass.
- List endpoints must be scoped server-side by tenant and, when applicable, by workspace, group, conversation, project, owner, or recipient.

## 4. Workspace / Group / Project Scope Rules

- Workspace membership is required to view workspace-scoped data unless a specific Application service currently grants a platform/admin override.
- `WorkspaceAdmin` can manage workspace details and workspace members.
- `GroupAdmin` can manage group details and group members. `WorkspaceAdmin` can manage groups in that workspace.
- Group membership does not replace workspace membership. Users added to a group must belong to the workspace first.
- `ProjectManager` can manage project details, members, milestones, tasks, assignments, dependencies, comments by others, and project-owned files.
- Project membership does not replace parent access. Users added to a project must belong to the workspace and, for group-scoped projects, the group.
- Project read access currently includes explicit project members and users who can view the parent workspace or group.
- Event and announcement scope must be exactly one of the supported scopes when required by the use case. Events currently require exactly one of workspace, group, or project.
- Cross-scope moves must authorize both the existing resource and the destination scope.

## 5. IDOR Prevention Rules

- Treat every route ID, query ID, owner ID, target ID, file ID, message ID, and membership ID as untrusted.
- Load the target resource and authorize against its actual stored parent IDs. Do not trust parent IDs supplied beside a child ID.
- For child resources, verify the parent relationship from storage. Examples:
  - Task, milestone, assignment, dependency, comment, artifact, and artifact version access must resolve to the owning project.
  - Group access must resolve to the owning workspace.
  - Channel access must resolve to the owning group and workspace.
  - Message access must resolve to the owning conversation.
  - File access must resolve through `Attachment.OwnerType` and `Attachment.OwnerId` to the owning project, conversation, or channel.
- List endpoints must use server-side scoping. Client filters can narrow results but must never be the security boundary.
- When an actor lacks access to a cross-tenant or cross-scope ID, prefer a not-found style response unless the API contract intentionally distinguishes forbidden access.

## 6. Admin API Rules

- Platform APIs live under `/api/platform/*` and require explicit `PlatformAdmin` authorization.
- System administration APIs under `/api/admin/*` require explicit platform/system admin authorization and must also check admin permission in the Application service.
- Tenant administration APIs under `/api/tenant/*` apply only to the current tenant and require `TenantAdmin` unless the endpoint is intentionally read-only for authenticated tenant users.
- `PlatformAdmin` may manage tenants, plans, subscriptions, platform usage, and system lifecycle operations only through explicit platform/admin code paths.
- `TenantAdmin` may manage current-tenant settings, usage views, users, integrations, webhooks, and API tokens only in the current tenant and only when required feature flags are enabled.
- Admin operations that change user status, system roles, tenant status, tenant membership, settings, plans, subscriptions, integrations, webhooks, API tokens, or lifecycle state must write audit logs.
- The last active platform/system admin must not be demoted or archived.
- Sensitive system settings must be masked in responses.

## 7. File Access Rules

- File bodies must be accessed only through `IFileStorageService`.
- `FileObject` is canonical metadata for new uploads. `Attachment` links a file to an owning resource.
- Upload must authorize the target owner before storage write.
- Upload must check the file-sharing feature flag, tenant quota, configured max size, extension, and MIME type.
- Storage keys must be generated server-side and tenant-namespaced, for example `tenants/{tenantId}/files/{fileId}` or `tenants/{tenantId}/projects/{projectId}/files/{fileId}`.
- User-provided filenames are metadata only. They must be sanitized and must not influence storage paths.
- Download and metadata reads must authorize against the owning scope and require the current tenant to match the file tenant.
- Deleted, archived, quarantined, infected, or non-active file records must not be downloaded by default.
- Permanent raw object-storage URLs, local filesystem paths, signed URLs, and storage credentials must not be exposed in normal API responses.
- Local filesystem storage must reject path traversal and stay under the configured root.

## 8. DTO Response Rules

- EF entities must never be returned directly from APIs.
- Request and response DTOs must be explicit and use-case specific.
- Request DTOs must not expose server-managed fields such as `TenantId`, ownership IDs, audit timestamps, hashes, storage keys, internal statuses, lockout fields, or deleted metadata unless the use case explicitly allows the field.
- Response DTOs must not include password hashes, token hashes, raw tokens except the one-time API-token creation response, webhook secrets, session keys, invite token hashes, storage credentials, raw file contents, local file paths, or sensitive setting values.
- Soft-deleted messages may be represented with safe tombstone DTO fields, but the original body must not be returned.
- DTO projections should be used for read APIs and should avoid loading full EF navigation graphs.

## 9. Soft Delete Rules

- Deleted resources must be excluded by default from read, list, search, notification, file, event, project, and admin dashboard workflows.
- Soft delete is represented by `DeletedAt` and, for some entities, a status such as `Archived`, `Deleted`, or `Quarantined`.
- EF global query filters currently enforce tenant isolation, not universal soft-delete filtering. Application services and repositories must explicitly filter deleted records.
- Delete operations for auditable user-facing content should soft-delete or archive unless a hard delete is explicitly required and documented.
- Restore operations must be explicit, authorized, audited, and scoped to the same tenant/resource boundary as deletion.
- File deletion must mark both `Attachment` and linked `FileObject` deleted when both exist.

## 10. Audit Logging Rules

- Audit logs are tenant-owned and append-only from normal Application code.
- Important operations must create audit logs, including authentication security events, tenant creation/status changes, tenant membership changes, admin user changes, system role changes, settings changes, plan/subscription changes, workspace/group/project lifecycle changes, event changes, announcement changes, file upload/download/delete, integration/webhook/API-token changes, and security access failures where practical.
- Audit entries should include actor user ID, action, target type, target ID, tenant, workspace/group/project scope when applicable, timestamp, safe summary, metadata, and correlation/trace ID when available.
- Audit metadata must not contain secrets, passwords, tokens, signed URLs, storage paths, file contents, raw message bodies, request bodies, connection strings, or other sensitive values.
- Security events are tenant-scoped and should be queryable only by `TenantAdmin` or platform/system admin according to current service behavior.
- Failed audit logging should not break normal business operations unless the event is security-critical.

## 11. Validation Rules

- Validate input before executing use cases.
- Enforce required fields, string length limits, enum validity, date/time ordering, paging bounds, unique scope constraints, feature flags, quotas, and parent-child relationships.
- Validate JSON settings fields as the expected JSON kind.
- Validate integration settings to prevent raw secrets in stored JSON.
- Webhook URLs must be absolute HTTPS URLs.
- API token expiry must be in the future and raw token values must be returned only once.
- File uploads must validate size, extension, MIME type, generated storage key, and owner authorization.
- Tenant, workspace, group, and project membership changes must verify that target users exist and belong to required parent scopes.

## 12. Error Response Rules

- Error messages must be safe for production.
- Production errors must not expose stack traces, SQL, connection strings, file paths, storage keys, raw request bodies, secrets, or internal exception details.
- Use the shared error response shape from `src/Coglatas.Web/Models/ErrorResponse.cs` for new generalized error handling. Existing controller-local `{ error = ... }` responses must still keep messages safe.
- Authentication failures should return unauthorized responses.
- Cross-tenant and cross-scope resource access should generally look like not found to avoid resource enumeration.
- Validation errors may explain the invalid field when that does not reveal protected state.
- Include trace or correlation IDs when available.

## 13. Test Requirements For Security-Sensitive Changes

- Security-sensitive fixes must include focused tests unless the change is documentation-only.
- Add Application service tests for authorization rules, role checks, resource-specific access, and validation that depends on database state.
- Add HTTP integration tests for authentication, CSRF, tenant resolution, controller role gates, and route behavior.
- Add tenant isolation tests for any new `ITenantEntity`, repository, query, list endpoint, search path, export path, notification path, audit path, integration path, or admin path.
- Add IDOR tests for every new ID-based read, update, delete, download, archive, restore, membership, or status operation.
- Add file tests for upload authorization, extension/MIME/size validation, quota enforcement, path traversal prevention, tenant-namespaced storage keys, and download authorization.
- Add soft-delete tests that prove deleted resources are hidden by default and restore/archive flows require authorization.
- Add audit tests for high-impact operations when the behavior is security relevant.
- Regression tests should cover the denied case, not only successful access.
