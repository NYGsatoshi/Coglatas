# Isolated PostgreSQL preparation and recovery controls

Tracks the migration/data-preservation slice of #1148/#1156 through #1158.
`SecurityArchitectureRlsRecoveryTests` uses current migrated synthetic rows and
the existing model/SQL row fixtures in a disposable database. Four provider
facts exercise transactional preparation failure, successful preparation,
failed recovery rollback, exact canonical recovery, live security mutations
and committed incomplete recovery. No product migration, role or activation
changes. **PRE-AVALONIA SEC-ARCH: BLOCKED.**

All 114 current migrated tables receive row-count/multiset SHA-256 and native
schema/security catalogue identities. Every table in the existing proposed
105-table scope must have seeded rows. Unseeded tables remain explicitly empty;
their fingerprints are preservation identities, not row-operation evidence.
The deny-all policy and NOLOGIN fixture role only exercise DDL/recovery
mechanics. Actual application/worker policy semantics and identity/root
classification are separate pending decisions.

The snapshot retains native constraints, indexes, internal/non-internal
triggers and function-body hashes, with separately captured policy/ACL/default
privilege/role identities. Row contents and function bodies are hashed inside
PostgreSQL and never exported. Effective ACL normalization compares canonical
authority, including equivalent implicit/explicit owner grants; physical
catalogue bytes and OIDs are outside that claim. Reads use UTC, a repeatable-read
transaction and ordered table locks in the isolated database.

Missing policy, TRUNCATE grant, BYPASSRLS, permissive policy and disabled-RLS
mutations must invalidate the prepared gate. Exposure controls use actual
seeded rows; rollback restores the retained prepared identity. Partial reverse
DDL and changed row contents cannot pass by retaining table counts alone.
Detailed table identities and stage checkpoints use the existing private
inventory output boundary and exclusive receipt creation. Keep them private.

Run with a disposable PostgreSQL 18.6 instance and the established
`POSTGRES_TEST_CONNECTION_STRING` provider setting:

```text
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --filter FullyQualifiedName~SecurityArchitectureRlsRecoveryTests --logger trx
```

A missing provider causes discovery to skip locally and fail in CI; it is not
execution evidence. Exact candidate/assembly/run reconciliation remains
separate. Detailed activation prerequisites and compatibility/recovery review
are retained privately. The isolated reverse probe returns to today's inactive
RLS baseline; it does not authorize operational RLS disablement. These tests
establish no application/worker equivalence, realistic-volume/legacy migration,
backup/PITR restore, personal approval or #842/#614 qualification.
