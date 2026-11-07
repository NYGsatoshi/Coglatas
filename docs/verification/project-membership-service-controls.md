# Project membership service controls

The additive `ProjectServiceTests.MembershipLifecycle.cs` cases reuse the existing
recording Repository fixture and production Project/Workspace authorization
services. They exercise the membership commands independently of canonical
Project creation, which retains its separate idempotency and activation contract.

Owner and Manager controls add, list, change and remove a target membership while
checking projected identity, unchanged join time, save counts, audit actions,
Project invalidations and target-scoped authorization signals. An unrelated
Project membership for the same target must remain unchanged.

Denial cases cover missing/empty identity, unavailable parent Workspace or
membership, non-managing Project roles, missing/deleted Projects, missing target
users, unavailable target Workspace membership, missing target Group membership,
duplicate membership and missing update/removal targets. Every rejected command
independently checks unchanged membership rows and no audit, invalidation,
authorization-signal or save calls.

These are service-level controls with recording dependencies. They do not prove
PostgreSQL persistence, transaction rollback, current persisted account or Tenant
queries, HTTP status/privacy envelopes, Outbox serialization or actual Realtime
delivery/revocation. Those require their own provider and transport evidence.
No Project lifecycle, authorization rule, membership role, existing assertion,
coverage/import scope, quality budget or deployment behavior is changed.

Native execution, exact source identity, hosted checks, independent review and
the original PR Gate must be recorded separately. Main coverage changes require
measurement on the resulting Main; local coverage is not an additive credit.
