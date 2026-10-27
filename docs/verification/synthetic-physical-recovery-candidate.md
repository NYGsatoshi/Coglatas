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
Unix-socket database. This is not the HTTPS pilot gate. The test-only application's HTTP
client rejects 301/302/303/307/308 redirects before following them, including
same-origin redirects, so requests carrying synthetic session/CSRF material
cannot silently move to another endpoint. The secret-free loopback GET/POST
positive, denial and redirect controls run via
`python3 -m unittest discover -s tests/ci -p 'test_synthetic_physical_recovery_http_guard.py' -v`.
They test transport handling, not restored-product authorization.

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
Candidate PASS also requires a nonempty, unique admitted immutable-ID set and
successful verified stops for every admitted container; a stop failure or empty
admission downgrades the result to FAIL with `CONTAINER_CLEANUP_NOT_VERIFIED`.
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
the physical copy. No earlier result qualifies the three-volume amendment.

## Reviewed three-volume execution

Exact test-bearing source `3b186c7dcbd916df80816448be6bd96f682c846a`, harness
SHA-256 `cb6c4355a9b0004ba05293852f9933d16dee430d7ed1a66b4fb30cc59b4b6c85`,
executed once from `2026-10-24T05:54:41Z` to `2026-10-24T05:56:29Z`.
Independent static review of the ownership/build bridge and separate preserved
backup amendment completed before execution. Protected GitHub review, hosted
exact-head checks and Owner approval remain separate.

Result: **SYNTHETIC_DATABASE_CANDIDATE_PASS**, sanitized private receipt SHA-256
`c3f3d0bdfdca34b14867800e1396ceeb0c3df8adc80ea99adc9afe05a41966e7`.

| Control | Actual result |
| --- | --- |
| Clean product build / same-build original DDL | PASS; pinned SDK 10.0.402, zero warnings/errors, DDL `f22155c2379b651322d6425a358a61e73b62858937f60f8b181727fceca31e1f` |
| Application source/build bridge | Source `3b186c7dcbd916df80816448be6bd96f682c846a`, source tree `7199c022a84f4c40c191f90feea60e3c09635859`; all four expected assemblies mapped in source/restored processes and matched their fresh build hashes |
| Real PostgreSQL | 18.6, exact local image recorded above |
| All seven strict source/restored comparisons | PASS; 115 tables / 215 rows include one synthetic control table/row; 74 migration-history rows |
| Deliberately changed synthetic schema/raw catalog, row, sequence | All detected; exact original state restored after controls |
| Exact-source revision-head and File-version guard controls | Positive controls passed; nonzero insert `23514` and duplicate archive-shaped COPY `23505` denied in both source/restored DBs |
| Physical backup manifest | `pg_verifybackup` PASS before and after restored-app controls; manifest SHA-256 unchanged: `35fdf649d1e11885e42e65bce7459c86ca5cd90eb7e768d9822d0a6703deba64` |
| Bounded restored app | Retained-key session PASS, missing-key session 401, missing-CSRF 403, restored-key restart/session PASS, fresh login PASS |
| Actual File bytes | Source/restored/restarted each 36 bytes, SHA-256 `12493505ebebd6efba113788e87cdbee6ffb2644009b6388064b95a18af18f51` |
| Resource receipt | Three fresh validated volumes; two verified immutable-ID containers stopped; preserved backup and stopped fixture resources retained privately |
| Full application recovery / target HTTPS / rollback | NOT_EXECUTED; no formal recovery or release approval |

The final exact-source third-position volume-name collision control also denied
admission before mutation. Earlier nine read-only ownership controls ran on their
own `858f10ec` source and remain historical. There are no acceptance-operation
retries or skips in this local candidate execution. Process-readiness polling is
distinct from acceptance operations. Later documentation-only commits do not
inherit runtime execution credit; this receipt binds the exact source above.

CodeRabbit's subsequent independent exact-HEAD review identified that the earlier
cleanup path could keep PASS after a stop failure. The cleanup amendment fails
closed and was exercised through the actual finalization block with verified
owned stopped resources: the positive control retained PASS, forced stop failure
returned FAIL, and empty admission returned FAIL. These focused amendment controls
do not transfer the earlier full physical execution to a later source.

The cleanup-corrected test-bearing source
`9f2caf320aaa96b83bf90c564ed577e5845b9cc7`, harness SHA-256
`aecb31f7b7a3f0779957135e7affc4cc8dcf0f9ef70da481ecf4229ee538e3c3`,
completed one fresh execution from `2026-10-24T06:13:30Z` to
`2026-10-24T06:15:04Z`: **SYNTHETIC_DATABASE_CANDIDATE_PASS**.
Receipt SHA-256:
`7baa7c72df0d352cdac7457953a01a26ead1999aef2fbc6336859e5d936e02bf`.
All seven strict comparisons, source/build/loaded-assembly bindings, guard/oracle
controls and bounded app/File/key/CSRF/restart/login controls passed on this exact
source. The fresh fixture had 115 tables / 215 rows / 74 migration rows. Both
`pg_verifybackup` checks passed with equal manifest SHA-256
`157d4df4eb0d794d6e92136eef0cb34e96338dd4914dd1319ed04ddbc754feff`.
The final receipt records two unique admitted IDs, two verified stops and
`cleanupState=VERIFIED`; three isolated volumes remain privately retained.
No acceptance operation retried or skipped. This is the corrected source's own
evidence, not inherited execution credit. Full recovery/target/rollback and
formal strategy approval remain incomplete.

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

## Ordinary hosted CI qualification boundary

The advisory `Synthetic Physical Recovery Candidate` pull-request workflow invokes the existing probe once with application controls on a fresh GitHub-hosted runner. It uses a read-only repository token, no protected environment or secrets, the source-selected SDK/tools and normal public NuGet restoration, and an official PostgreSQL 18.6 image resolved to its observed immutable local identity and repository digest. The repository has no NuGet lockfiles; this is not a locked-dependency reproducibility claim. No privileged branch namespace, target endpoint, existing volume, formal strategy, Oracle, guard, timeout or required check is changed.

Only a closed validated outcome is emitted to native logs and the explicitly named public JSON artifact. PASS requires successful probe exit and every existing strict comparison, negative/guard, source/build/assembly, backup, bounded application/File and owned-container-stop condition. Missing, malformed, mismatched or incomplete evidence fails closed. Actual source/run/attempt identities and failure outcomes remain separate; artifact publication cannot repair failed execution. Raw SQL, receipts, catalogs, rows, keys, cookies, child output and application logs are never uploaded.

The probe leaves verified stopped resources and private files intact during the job. Ephemeral runner destruction then discards them: **raw private evidence and volumes are NOT_RETAINED across jobs or after teardown**. The sanitized outcome and digest record is retained; no private/off-host retention or later byte reinspection is established without a separately approved store. Cancellation, timeout or missing receipts remain failed/incomplete. This bounded candidate cannot approve the physical strategy, close #1355, qualify full G09, target HTTPS/rollback, school use or either Owner HOLD.


## Separate hosted execution of the documented logical procedure

The ordinary pull-request logical qualification job executes the existing runbook's custom-format `pg_dump` and `pg_restore --exit-on-error` once against two newly owned synthetic PostgreSQL containers. It reuses the unchanged probe's ownership checks, snapshots, negative controls and all seven exact byte comparisons. A nonzero command exit, missing snapshot or strict byte mismatch remains an actual failed job; there is no acceptance retry, expected-failure success, normalization, second restore or constraint bypass. The physical candidate and logical procedure are separate jobs and evidence records.

Database authority is limited to the synthetic PostgreSQL bootstrap role. Source application, assembly, File and guard controls support capture integrity; full restored-application authorization/Audit acceptance, target HTTPS, rollback and formal recovery acceptance remain `NOT_EXECUTED` or not granted. Only closed metadata, synthetic resource identifiers and digests are public. Private dumps, raw snapshots, credentials, keys and child logs are discarded with the ephemeral hosted runner and are not retained off-host. This execution does not adopt the physical strategy, change the canonical logical runbook, release either Owner HOLD or authorize Main integration or target operations.


## Additive synthetic restored-runtime continuation controls

A separate ordinary, read-only pull-request job composes the unchanged physical candidate's build, owned-resource, snapshot, trigger and oracle primitives. A small test-only .NET helper calls the existing public synthetic seed methods before backup, then exercises actual product ASP.NET Core HTTP, cookie/CSRF/Session tenant resolution and SignalR WebSocket services against the source and restored databases. No HTTP seed endpoint or development-header authorization is invented. Every healthy restore must pass all seven exact byte comparisons before new runtime actions or fault injections.

The bounded controls cover positive Alpha/Beta access, existing cross-tenant denial contracts, visible ReadOnly write denial, Files reads, a supplied canonical pending Outbox envelope, actual HTTP Message/Audit/Outbox production and existing `ClientRequestId` business idempotence. Positive WebSocket observations compare durable event type/schema, tenant, aggregate, actor and payload identity; passive exclusions require live receivers and separate new positive events. Restart exclusions are observations of 15 seconds per check, not a global exactly-once WebSocket guarantee.

A newly added, validated constraint on the owned restored synthetic database deliberately rejects the first `communication.message_posted` audit. Its actual HTTP failure must preserve complete Message, conversation-member, Outbox and Audit row counts and scalar digests. Only that temporary constraint is removed, followed by a required successful real producer control. Original constraints, migrations and strict restore oracles are unchanged. A distinct fresh backup clone must verify successfully before a deliberate `PG_VERSION` byte corruption is rejected; the original backup and its manifest must remain unchanged. This is a corruption negative, not a retry of a failed healthy restore.

The local crash/fallback control kills only the newly owned loopback application and verifies the unchanged owned source checkpoint before starting it again. It does not qualify production cutover, zero data loss, target rollback, HTTPS, off-host custody or an adopted recovery strategy. Database authority remains the synthetic PostgreSQL bootstrap role; formal RLS/least-privilege activation and G09 approval are not granted. Only closed control metadata and digests are public; private rows, catalogs, payloads, credentials, keys, dumps and application logs are discarded at ephemeral hosted-runner teardown. Both Owner HOLDs and the prohibition on Main integration remain.
