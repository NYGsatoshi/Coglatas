# Security

This is the active implementation security guide. Root `SECURITY.md` is only the vulnerability reporting policy.

Use `docs/SECURITY_MODEL.md` for the audited implemented/partial/planned status and `docs/KNOWN_ISSUES.md` for open mismatches.

## Baseline

- Passwords are hashed with a proven password hasher.
- Invite tokens, session keys, API tokens, and webhook secrets are stored as hashes, not raw values.
- Production secrets must come from environment variables, protected deployment config, or a secret manager.
- Do not log passwords, tokens, signed URLs, file contents, webhook secrets, storage credentials, or sensitive message bodies.
- Production must use HTTPS, secure cookies, HSTS, persisted Data Protection keys, and disabled setup/development tenant-header switches.

## Authentication

The bundled browser UI uses cookie authentication. Login lockout is persisted for existing accounts, and unknown-email login attempts use generic responses with audit/rate-limit controls.

Implemented auth capabilities include login, logout, password change, current-user lookup, DB-backed session validation, user suspension checks, CSRF token issuance, and CSRF validation for unsafe browser requests when enabled.

Invite registration is partial: it creates a user and session but does not create tenant or workspace membership. Initial administrator bootstrap is available only through explicit startup environment variables and must not be left enabled casually in production.

Deferred auth capabilities:

- Password reset flow.
- API token request authentication middleware.
- External SSO.

## CSRF

When `Security:EnableCsrfProtection` is enabled, browser clients get a token from `GET /api/security/csrf-token` and send it with unsafe methods using `X-CSRF-Token`. The bundled frontend `api()` helper handles this for `POST`, `PUT`, `PATCH`, and `DELETE`.

External API clients should use a future non-cookie API token authentication path instead of browser cookies.

## Authorization

Authorization must be enforced in Application services. Controller attributes are useful for coarse checks but are not enough for resource isolation.

Rules:

- Tenant access is checked before resource access.
- Tenant `Owner` and `Admin` roles apply only within their tenant.
- `PlatformAdmin` is a system-level role for explicit platform APIs under `/api/platform/*`.
- Normal tenant endpoints must not become platform-admin cross-tenant bypasses.
- Return `403` or `404` according to the policy without leaking cross-scope record existence.

Resource checks are required for workspaces, groups, channels, conversations, projects, tasks, files, artifacts, forms, events, notifications, audit logs, integrations, and tenant administration.

## Tenant Isolation

Tenant is the highest-level isolation boundary.

- Tenant-owned entities include `TenantId` and implement `ITenantEntity`.
- `AppDbContext` applies global tenant query filters and stamps new tenant-owned entities with the current tenant.
- Added or modified tenant entities whose `TenantId` differs from the current tenant are rejected unless the operation is explicit platform scope.
- `IgnoreQueryFilters` is allowed only in explicit platform/tenant infrastructure services with tenant predicates and comments explaining the bypass.
- Never accept `TenantId` from tenant endpoint request bodies.
- Platform-scope exports and admin operations must still predicate explicitly by target tenant.

Tenant isolation tests live in `tests/Coglatas.Tests/Tenancy`.

Run:

```powershell
dotnet test Coglatas.slnx --filter "FullyQualifiedName~Tenancy"
```

## Files

File bodies are stored through `IFileStorageService`; metadata is stored in PostgreSQL.

Upload/download rules:

- Authorize before upload and download.
- Validate tenant feature flags and quotas.
- Validate size, extension, and MIME type.
- Generate storage keys server-side.
- Use tenant-namespaced keys such as `tenants/{tenantId}/files/{fileId}`.
- Treat user filenames as metadata only.
- Do not expose raw permanent object-storage URLs.
- Local storage must reject path traversal and stay under the configured root.

Local filesystem storage is acceptable for development and small on-prem pilots only. Production SaaS needs a real object-storage adapter.

## Audit Logs

Important operations should create audit logs. Audit metadata must be useful for security review without containing secrets or raw content.

Audit logs should include:

- Actor user ID.
- Action.
- Target type and ID.
- Tenant/workspace/project scope when applicable.
- Timestamp.
- Trace/correlation ID when available.
- Redacted summary metadata.

Normal application code should treat audit logs as append-only.

## Integrations, Webhooks, And API Tokens

Integration accounts, webhook endpoints, and API tokens are tenant-owned.

- `IntegrationAccount.SettingsJson` must not contain raw secrets, tokens, passwords, API keys, private keys, or OAuth refresh tokens.
- `WebhookEndpoint.SecretHash` stores only a hash.
- `ApiToken.TokenHash` stores only a hash; raw token values are returned once at creation.
- Revoked or expired API tokens must fail validation.
- API token usage must never bypass tenant filtering or resource authorization.
- Webhook APIs require the `WebhookIntegration` feature flag.
- API token APIs require the `ApiAccess` feature flag.

Outbound webhook delivery and API token authentication middleware are deferred.

## Rate Limiting And Lockout

Named rate-limit policies exist for sensitive implemented routes such as login, invite registration, file upload, search, and API token creation. Password reset rate limiting is deferred with the password reset feature.

Login lockout is implemented for existing accounts. Generic responses must be preserved to avoid account enumeration.

## DTO And Over-Posting Protection

- Do not bind EF entities directly from request bodies.
- Use request DTOs and response DTOs.
- Do not accept server-managed fields such as `TenantId`, audit fields, owner IDs, hashes, or statuses from untrusted clients unless the use case explicitly allows them.
- Project query results to DTOs.
- Keep secrets and hashes out of responses.

## Current Security Limitations

- First-user/PlatformAdmin bootstrap is startup-seed based and must be explicitly enabled and operationally controlled.
- Invite registration does not create tenant/workspace membership.
- `Features:*` and most `Platform:*` appsettings switches are not runtime authorization gates.
- Forwarded-header handling for production reverse proxies is not configured.
- Production object storage adapter is not implemented.
- PostgreSQL-backed search isolation tests are enforced in CI through `POSTGRES_TEST_CONNECTION_STRING`.
- Full tenant restore is not implemented.
- Password reset is not implemented.
- API token authentication middleware is not implemented.
- Full external SSO is not implemented.
- Each pilot environment still needs a recorded backup/restore drill before real school data is relied on.
