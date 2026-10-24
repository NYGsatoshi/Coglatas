# Isolated physical recovery candidate — issue #1355

Status: **REVIEW ONLY / formal strategy unapproved / release NO_GO**.
The canonical logical backup/restore runbook remains unchanged. Neither Owner
HOLD is released. This candidate needs independent review, exact-source execution,
Owner strategy approval and separate authorized-target qualification.

## Problem and bounded candidate

The strict logical-restore failures retained in #1355 remain failures. A foreign-key
topological order alone cannot qualify original-DDL-first replay. Current-source
trigger controls demonstrate two additional obstacles in disposable schemas:

- The canonical UI revision-head insert guard accepts revision zero and rejects
  nonzero inserts with SQLSTATE `23514`; copying a progressed head is not ordinary
  initial creation.
- The exact File-version migration creates an initial version on FileObject
  insertion. The control proves that creation, then an actual archive-shaped
  `COPY file_versions` fails with duplicate-key SQLSTATE `23505`.

The controls use trigger bodies from the current public migration source and
minimal synthetic parent tables. They do not reproduce every application restore
dependency. They run with enabled guards and rollback their entire transactions.

The new probe examines **physical `pg_basebackup`**, which preserves PostgreSQL's
catalog representation instead of reparsing a logical dump. It is a proposed
alternative backup strategy, not an approved repair to the existing procedure.
It creates only fresh local resources, uses an immutable existing image, disables
container networking and publishes no database port. Optional application controls
use the existing Test-only fixture and a loopback HTTP process over an isolated
Unix-socket database. This is not the HTTPS pilot gate.

## Reproduction and evidence

Prepare the pinned SDK, locked dependencies and local tools through their normal
installation process. Generate the original EF DDL from the exact reviewed source:

```bash
dotnet build src/Coglatas.Web/Coglatas.Web.csproj --configuration Release --no-restore --disable-build-servers -m:1
ConnectionStrings__DefaultConnection='Host=unused;Database=unused;Username=unused' \
  dotnet ef migrations script --project src/Coglatas.Infrastructure \
  --startup-project src/Coglatas.Web --configuration Release --no-build \
  --output work/original-ef-ddl.sql
python scripts/ci/synthetic-physical-recovery-probe.py \
  --execute-isolated-synthetic --postgres-image-id '<existing local sha256:image-id>' \
  --original-ef-ddl work/original-ef-ddl.sql --evidence-dir work/fresh-recovery-evidence \
  --with-application-controls
```

Use a short, fresh private evidence path. Existing evidence is rejected. Files,
keys, cookies, rows, raw schema/catalog and application logs remain private; do
not attach that directory to public CI or a PR. Only sanitized outcome/digest
metadata may be published. Cookies and passwords remain in process memory.

The probe checks collisions for every generated container/volume name, validates
resource labels, immutable image, network mode and exact mounts before admitting
DDL or a restored server, and stops only verified owned immutable container IDs.
It leaves stopped resources and failure receipts for inspection; it never prunes
volumes, removes backups or touches an existing deployment.

For optional application controls it rebuilds clean current product source,
regenerates the EF script and requires byte equality with the supplied script.
The receipt separates harness commit/digest, application build source/tree, SDK,
build log, generated DDL and assembly digests. Expected assemblies must appear in
the actual process memory mappings and retain the build digests. Current-source
build verification is distinct from independently attested CI provenance.

The source application stops before the consistent DB/files/keys checkpoint.
`pg_verifybackup` validates the physical archive before target startup. The
archive is copied into a third fresh, ownership-verified target volume. Only
that copy may be mounted by the restored server. The preserved archive is
verified again after application controls, with equal before/after manifest
digests required. Strict
comparisons cover schema-dump bytes, raw constraint/index expression trees,
validated/deferrable constraints, trigger state, table RLS flags and ACLs, all
table row-count/SHA-256 manifests, migration history, sequence state, role flags,
memberships and database ACL/configuration fields. A synthetic non-default
sequence gives a positive preservation control. Deliberate changes to synthetic
schema/catalog, row and sequence controls must be detected, and their reset must
restore exact equality. No product policy, guard or oracle is weakened.

Only after those comparisons does optional application verification exercise the
retained session, real authorized File bytes, missing-CSRF denial, missing-key
session denial, restored-key restart/session/File continuity and fresh login.
These operations may append normal session/Audit records after the comparison
checkpoint. Full recovery remains `NOT_EXECUTED` because the complete matrix,
target HTTPS and target rollback are independent requirements.

## Retained development executions

All observations below use Product baseline
`0c5f9baace186940f948758b1731fd3ec891c2f3`, PostgreSQL 18.6 local image
`sha256:c293117fcecda7344b5480222e813b9f673d7abd69b1dd95eff239b768b04f59`,
SDK 10.0.402 and EF tool 10.0.13. They are historical development controls, not
execution credit for a later amended harness or integrated Main.

| Execution | Result | Scope and limitation |
| --- | --- | --- |
| Initial database probe | FAIL | All five then-selected strict comparisons matched; post-restore control collided with its own synthetic schema. Receipt `d23f6a58f2f519ab59e04d09141e3f9806a7af9f39a29b32aa1d8eefd174abc2` |
| First optional application probe | FAIL | Initial new-harness 20-second startup window elapsed. Separate retained diagnostic subsequently observed health/live, health/ready and unauthenticated status HTTP 200 on that source; no product failure is established. Receipt `3f0cb54e7f0feb7881232e83b31801b914514be94972a609381b16146a7ea85d` |
| Cookie-copy development probe | FAIL | Seven strict comparisons matched and source login/File control passed; Python CookieJar deepcopy failed on an RLock. No restored-app pass is inferred |
| Test-bearing `e1fa74bdb0b9dd7ea81d30f2877f5d2d6f77e99a` | SYNTHETIC_DATABASE_CANDIDATE_PASS | `pg_verifybackup`, seven strict comparisons, both trigger controls, restored/restarted 36-byte File SHA-256 `12493505ebebd6efba113788e87cdbee6ffb2644009b6388064b95a18af18f51`, retained/missing keys, CSRF 403, restart session and fresh login. Receipt `9a5ca9f31440dbaca8253d5eb1edc55b83e85db21417bac2d1a88e09b8fc9bef`. Predates independently requested resource-ownership/build-bridge/oracle-control amendments |
| Test-bearing `858f10ec9360a4187cc23fd590e2e25c88aefd5b` | SYNTHETIC_DATABASE_CANDIDATE_PASS | Clean source/build/DDL/loaded-four-assembly bridge, PostgreSQL 18.6, all seven strict comparisons, all synthetic oracle negatives and bounded app controls passed; 115 tables/215 rows include one synthetic control table/row and 74 migration rows. Two verified containers stopped. Receipt `853a6f46f5698da5d277327580a963d2c67bca5de35bfd883a6005eb9948373a`. This earlier two-volume implementation started the target on the basebackup volume; preserved immutable-archive qualification was absent and no formal recovery pass follows |

The new harness uses a bounded 90-second startup window, stricter than the existing
security-runtime setup's 90 attempts at two seconds and Functional setup's
240 seconds. This corrects premature new-harness startup termination; it changes
no existing acceptance deadline, retry, assertion, check or formal gate. Each
login/denial/File acceptance operation executes once. Failed receipts are retained.

The previous two-volume executions are historical only: target startup mutates
the physical copy. Current three-volume amended-source execution and independent
review: **PENDING**. No earlier result qualifies this amendment.

## Owner decision and remaining release work

| ID | Decision | Recommendation | Effect if held |
| --- | --- | --- | --- |
| REC-1355-STRATEGY | Review physical cluster backup plus consistent uploads/keys/config capture versus a separately proven logical repair | Review this bounded candidate; do not adopt from synthetic success alone | Existing strict restore gate remains blocked |
| HOLD-A | Authorized target, HTTPS origin, identities and recovery/rollback window | Retain HOLD until an exact environment authorization record exists | Target execution remains NOT_EXECUTED |
| HOLD-B | Detailed SEC-ARCH mapping/policy/role/grant/activation approval | Retain HOLD; this probe grants none | RLS formal approval/activation remain blocked |

Physical recovery needs supported PostgreSQL-major/platform/storage compatibility,
reviewed recovery roles and operator privileges, encrypted off-host retention,
actual recovery-point/timing evidence, full Audit/authorization controls and
independent review. The small synthetic fixture does not populate every table or
exercise every trigger, role, File route or tenant boundary. Image/source
provenance, complete restored application qualification, target acceptance,
rollback, school/privacy approval and final Owner GO remain incomplete.
This physical method backs up a whole cluster; it is not tenant-level restore.
