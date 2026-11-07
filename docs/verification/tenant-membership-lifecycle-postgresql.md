# Tenant membership persistence and invalidation controls

These five PostgreSQL Facts exercise41 internal scenarios through the real
Tenant service, repositories, session service, database audit logger, unit of
work, authorization publisher and transactional outbox. Each scenario creates
an isolated database from the existing migrated PostgreSQL18.6 template and
asserts the actual Npgsql provider. Connection absence cannot qualify the run.

The cohort covers new membership and reactivation from Invited, Suspended,
Archived and Left; role/status changes, reactivation and existing no-op behavior;
removal from Active, Suspended and Left; unavailable/anonymous/non-current
management authority; missing/deleted/already-active targets; and current-Tenant
listing. Eighteen management denial scenarios exercise all four operations.
They include missing membership, Invited/Suspended/Archived/Left managers,
suspended/archived/deleted accounts and Tenants, and foreign request scope.

Assertions read a separate database context. They verify membership identity,
role/status and join metadata; scoped audit actions and payload; the native
durable authorization envelope and user routing; pending outbox rows; session
revocation timestamps/counts and pre-existing revoked/actor sessions; and an
unchanged foreign-Tenant membership. Rejected operations create no audit,
outbox, membership or session changes. Session revocation remains the existing
user-wide operation; these tests do not invent Tenant-local session semantics.

Local native source `a39475dd155f2c3a3279d92b39f50288c25a9393` executed5/5 Facts,
zero skipped, all41 scenarios PASS against a dedicated loopback PostgreSQL18.6
container with the existing privacy settings. The preceding2-Fact/14-scenario
`e0f0cf20f2a40eef8ed76eb48be6915bdbea6d62` run retains separate original TRX,
report, log, source archive and seven original/executed DLL/PDB pairs. Its
receipt SHA256 is `f4c90ee3719058b84d276fa0fc7bc4b6fcbf7f26134b48c21f73263aed80afee`.

The initial two new Facts failed because their assertions read a nested durable
payload field from the envelope root; that authoring failure is retained. The
expanded authoring build initially used private-set deletion properties, then
used the existing `MarkDeleted` API; no product entity or contract changed.
An initial build-only invocation lacked the new output directory's restore
assets and did not execute tests. Current-head hosted checks, independent
review, original PR Gate and resulting Main measurements remain separate.
The first0de5 PR ReSharper run rejected one redundant nullable cast in the
session assertion. Its removal preserves the same revocation timestamp/null
expectations; the original failed run and annotation remain retained.

This is synthetic service/persistence evidence. It does not prove HTTP cookie
handling, realtime delivery, dispatcher execution, application-role/all-table
RLS, transaction failure atomicity or caller-owned rollback. In particular,
passing persistent audit/outbox assertions do not establish rollback across
the session service's intermediate saves. No production migration, real user
invitation, formal security disposition, Pilot acceptance or Release GO occurs.
