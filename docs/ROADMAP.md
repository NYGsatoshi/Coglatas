# Roadmap

This is the active source for scope, deferred work, readiness, and current technical debt. Historical plans and status snapshots are in `docs/archive/`.

Implementation status is maintained in `docs/AI_CONTEXT.md` and `docs/KNOWN_ISSUES.md`. Items listed as MVP scope here may be backend-only or partially implemented; this file expresses intended direction, not proof of completion.

## Current Target

Usable operation by mid-July 2026 for an initial school activity deployment of about 100 users.

## MVP Scope

Included:

- Auth, sessions, logout, password change, invite registration, user suspension.
- Tenant model, membership, switching, PlatformAdmin APIs, TenantAdmin APIs.
- Workspaces, groups, channels, posts, threads.
- Direct conversations, messages, unread/read state.
- Announcements with read confirmation.
- Database-backed notifications.
- File metadata, upload validation, local storage abstraction, authorized download.
- Projects, members, milestones, tasks, dependencies, assignments, comments, dashboards, Gantt data.
- Artifacts and artifact versions.
- Forms/events foundations.
- Tenant-scoped search.
- Audit logs and security events.
- Tenant metadata export.
- UI shell, tenant switcher, Platform Admin area, Tenant Admin area.
- Radial menu and docking foundations as data/preset support only.

MVP rules:

- REST APIs remain the source of truth.
- SignalR is deferred until REST workflows are stable.
- Authorization is enforced in Application services.
- Broad lists are paginated or bounded.
- File uploads validate size, extension, MIME type, quota, feature flag, and authorization.
- UI exposes empty, error, and permission states without fake backend behavior.

## Deferred Features

- Production object-storage adapter.
- Full tenant restore and file-body tenant export.
- Password reset flow.
- API token authentication middleware.
- API request metering middleware.
- Full-text search engine.
- OAuth provider flows and outbound webhook delivery.
- Task automation, outbound Web retrieval, source materialization, and a
  provider/egress contract. Issue #357's Project/Task policy and immutable
  next-run snapshot foundation deliberately does not implement these; canonical
  specification promotion and an explicit security-reviewed provider contract
  are required before execution work starts.
- Advanced Gantt drag editing and advanced resource planning.
- Live streaming, voice/video calls, and end-to-end encrypted messaging.
- Full billing/payment integration.
- Advanced SSO.
- Full plugin marketplace and full free-form docking.

## Current Readiness

Development and controlled technical-evaluation build, not a turnkey pilot or broad SaaS deployment.

Known blockers:

- First-user/PlatformAdmin bootstrap depends on the explicit `COGLATAS_SEED_ADMIN_*` startup seed and operator control.
- Invite registration membership atomicity has an Issue #527 implementation candidate with PostgreSQL-backed transaction, replay, rollback, and cross-scope coverage; hosted CI remains the merge gate.
- Production object storage is not implemented.
- PostgreSQL-backed search isolation tests are enforced in CI through `POSTGRES_TEST_CONNECTION_STRING`.
- Backup/restore drill has not been recorded for each target environment.
- Full tenant restore is not implemented.
- On-prem Compose now stages migrations before the app, but fresh-stack runtime evidence and the intended reverse-proxy topology are still required.

OnPremSingleTenant is the safest controlled pilot mode after manual smoke and restore rehearsal.

## Active Technical Debt

- Implement production object storage adapter.
- Complete API token authentication middleware.
- Rehearse and record backup/restore before pilot.
- Add direct service tests for MIME type upload rejection.
- Run and record final manual acceptance pass.
- Finish broader service-level feature gates where currently advisory.

Recently resolved items:

- Authenticated HTTP tenant isolation harness exists.
- Suspended tenant write guard exists at tenant-owned save boundaries.
- CSRF token issuance and unsafe-method validation exist for cookie-auth browser clients.

## Acceptance Checklist

Local demo:

- Build passes.
- Tests pass.
- App starts.
- `/health/live` and `/health/ready` pass.
- Basic smoke workflow passes.

Internal/on-prem pilot:

- Local demo checklist passes.
- Target deployment mode is configured.
- Tenant isolation is manually verified.
- File upload/download authorization is verified.
- Backup and restore drill is recorded.
- Known limitations are accepted by the project owner.

SaaS pilot:

- Internal/on-prem checklist passes.
- Object storage adapter is implemented and health-checked.
- PostgreSQL-backed search isolation tests pass.
- Tenant resolution is verified for every configured host/subdomain.
- Restore evidence exists.
