# TASK-V1-PR07-A persistence, preferences, and dedupe foundation verification

## Status

PR07-A was merged and accepted as PR #274 at
`c5627eb09ecf19d66146eacdbc3e938c0a1c8563`. Same-sha post-merge `main`
workflow runs passed: CI `30724803612`, Code Quality `30724803621`,
Documentation CI `30724803620`, and npm Security Audit `30724803615`.

## Authority and identity

| Field | Value |
| --- | --- |
| Implementation repository | `NYGsatoshi/Coglatas` |
| Pull request | [#274](https://github.com/NYGsatoshi/Coglatas/pull/274), merged |
| Implementation base | `ca0f3fec26a78d4199fa834ce82509a6dfeda812` (`origin/main`) |
| Audit-start HEAD | `adff1e1e072acb4e4e3a47db0263b9dca8cbfbf3` |
| Historical code-bearing candidate | `b1f80fb212c820e22613d3c3ae637eaa6e77147e` |
| Accepted merge commit | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` |
| Branch | `task/v1-pr07-a-notification-foundation` |
| Canonical specification PR | `NYGsatoshi/Coglatas-Spec#62` (merged) |
| Canonical specification commit | `8b90c8897367606473515d17d3696e458b2ee7b5` |
| Implementation owner-decision record | `docs/decisions/task-v1-pr07-owner-decisions.md` (Resolved) |
| Migration | `20260801171714_AddTaskNotificationPreferenceFoundation` |

The historical NO-GO baseline remains in
`docs/verification/task-v1-pr07-gap-audit.md`. This ledger records only the
post-resolution PR07-A implementation and does not retroactively relabel that
audit as implementation evidence.

## Delivered contract

- `Notification.LogicalKey` is nullable and bounded to 512 characters.
  `CreateOrGetByLogicalKeyAsync` has an explicit logical identity and relies on
  PostgreSQL's filtered unique index as the authority. A duplicate unique
  violation is re-read safely; unknown database failures are rethrown.
- The identity is `(TenantId, UserId, LogicalKey)` for non-null keys. Tenant,
  recipient, and caller-composed event/version/category keys remain distinct.
  Soft-deleted rows retain their key and are returned rather than resurrected;
  legacy null keys may coexist.
- Workspace members own a nullable local digest time and an independent private
  preference version. Workspaces own an `08:00` local-time default and an
  independent settings version. These `TimeOnly` values intentionally contain
  no timezone. The preference version is not an EF entity-wide concurrency
  token; a tenant/member/version conditional update is its sole conflict
  authority, so unrelated Role/Status saves do not conflict or overwrite it.
- `GET`/`PATCH /api/me/workspaces/{workspaceId}/task-notification-preferences`
  enforce current active Tenant/Workspace membership, private ownership,
  quarter-hour validation, null inheritance, and version/ETag retry metadata.
  Omitted, zero, negative, or stale numeric `expectedVersion` values that bind
  to the DTO return typed 409; malformed JSON or incompatible JSON types (such
  as `"expectedVersion":"abc"`) follow the shared safe HTTP 400
  model-validation contract. Neither class mutates state, and general
  Workspace/member DTOs omit the private fields.
- `tasks.notificationsV1` is centrally registered and default disabled. It is
  not used to disable authorization, privacy, or dedupe.

## Migration and rollback safety

The migration adds only `notifications.LogicalKey`, its filtered unique index,
the two Workspace fields, and the two WorkspaceMember fields. A non-null
Workspace default backfills existing rows to `08:00`; existing member
preferences are null and their initial version is 1. Existing Notification
rows retain null keys.

The Down path drops only the added index/columns and preserves pre-existing
rows. Values stored only in the added columns are naturally lost if the Down
migration is applied; deployment rollback therefore requires accepting that
new preference/logical-key state is removed. It makes no table recreation,
rename, or unrelated cleanup.

## Explicitly excluded

- Task notification producers and `DeadlineAt` mutation/classification;
- digest ledger, worker, scheduling, and DST processing;
- a new semantic Outbox event family (the existing Notification service
  behavior remains available only when its new primitive is invoked);
- SignalR route or dispatch-authorization changes, notification-open endpoint,
  and Angular preference/notification behavior;
- PR06B and PR08 work.

## Focused evidence

| Check | Result in this worktree | Notes |
| --- | --- | --- |
| `dotnet restore Coglatas.slnx` | Passed | All projects already restored. |
| `dotnet build Coglatas.slnx --no-restore --configuration Release` | Passed, 0 warnings / 0 errors | Compiles Domain, Application, Infrastructure, Web, and tests. |
| `dotnet test ... --filter "FullyQualifiedName~HttpTenantIsolationTests"` | Passed: 31; failed/skipped: 0/0 | Covers active GET/PATCH, null inheritance, exact times, invalid times, 409 numeric version classes, safe 400 model binding, no mutation, isolation, revoked membership, and DTO privacy. |
| `dotnet ef migrations has-pending-model-changes ... --no-build` | Passed | PostgreSQL 18 temporary container; no model changes since the focused migration. |
| `dotnet ef migrations script 20260730120626_AddCanonicalGanttVersions 20260801171714_AddTaskNotificationPreferenceFoundation ... --no-build` | Passed | Script contains only the additive columns and the filtered logical-key unique index before the migration-history insert. |
| `dotnet test ... --filter "Scope=TaskV1PR07A"` | Passed: 11; failed/skipped: 0/0 | PostgreSQL 18 temporary container. Runs fresh/upgrade/Down, filtered index, concurrent logical-key writers, scope separation, soft-delete retry, preference winner/loser/retry, and Role/Status non-conflict coverage. |
| `dotnet test Coglatas.slnx --no-restore --configuration Release -m:1` | Passed: 507; failed/skipped: 0/0 | PostgreSQL 18 temporary container after applying all migrations to the shared CI-shaped database. |
| Exclusion-path and whitespace check | Passed | `git diff --check` passed; no frontend, hosted artifact, new producer/digest-worker, or realtime-route path changed. |
| Active-document local Markdown links | Passed | All local Markdown links in the changed active documentation resolve; the repository has no dedicated documentation-lint command. |
| Post-merge `main` CI / Code Quality / Documentation CI / npm audit | Passed at accepted merge commit | Same-SHA successful runs: CI `30724803612`, Code Quality `30724803621`, Documentation CI `30724803620`, and npm Security Audit `30724803615`. |

No local result that reports PostgreSQL tests as passed/skipped without
`POSTGRES_TEST_CONNECTION_STRING` is treated as PostgreSQL provider evidence.

## PR07-B entry gate

Gate passed on 2026-08-02. PR07-B began from exact accepted `origin/main`
commit `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` only after the four post-merge
workflow runs above completed successfully. The PR07-A migration, PostgreSQL
uniqueness/concurrency evidence, private preference contract, and default-off
feature key therefore form the accepted foundation for the sequential PR07-B
branch.
