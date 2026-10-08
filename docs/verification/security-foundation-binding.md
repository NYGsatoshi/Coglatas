# Security Foundation deterministic binding — SEC-FND-02

## Scope and authority

This is the completed #1118 binding slice, originally based on qualified Main
`de577d5e3534f510a61dfc746c0162c604eb1e8f`. #1117 is closed with
[normal-merge and exact-Main evidence](https://github.com/NYGsatoshi/Coglatas/issues/1117#issuecomment-6065340779).
This slice owns integrity bindings only. Rule execution, persistence, ProjectIDE
integration and authorized reads remain #1119-#1122.

#905 owns Source/Revision/Compiler authority, #906 owns current authorization,
Review and Merge, #1034 owns shared evidence/retention, #834 owns traceability,
and #804 owns dependencies. No package, canonicalizer, cryptography framework,
HTTP route, migration, compiler or enforcement is introduced. The fixed owner
choices remain preserved; their unavailable original option text is not inferred.
The private specification proposal remains Draft and unratified.

## Canonical representation

`SecurityBinding.Create(request, evidence)` freezes one representation using
existing `SourceJson.FromObject`, `CanonicalJson`, `SourceRevisionContext.Data`,
typed Project identities and `ContentDigest.Compute`.

| Field | Bound meaning |
| --- | --- |
| `schemaId`, `schemaVersion` | `coglatas.security-binding`, version 1 |
| `domainTag`, `canonicalEncoding` | `coglatas.security-binding/1`, UTF-8 |
| `subject` | Declared Tenant and user identity |
| `operationId` | Declared operation identity |
| `claimed.resource` | Canonical revision context and declared input digest |
| `claimed.source` | Supplied Source's actual context and canonical input digest, or null |
| `claimed.policy` | Declared policy set, version, schema and content digest, or null |
| `claimed.compiler` | Declared version, build identity and optional exact commit, or null |
| `expected.source` | Independently supplied host Source context and actual digest, or null |
| `expected.policy` | Snapshot derived from independently supplied canonical policy content, or null |
| `expected.compiler` | Independently supplied known compiler identity, or null |

Claims and host evidence remain separate. Contradictory inputs cannot alias a
qualified binding by silently replacing a claimed value with its expected value.
Canonical contexts include their preserved unknown fields. Source digests retain
all canonical Source bytes, including preserved unsupported/unknown data.
Digest projections use the existing explicit SHA-256 algorithm and lowercase
hexadecimal value; arbitrary digest-object extensions are not new identity fields.

Evaluation ID, database row ID, execution timestamps and enforcement mode do not
enter this representation. Actual content inside the canonical Source remains
input; this slice does not strip unknown Source fields by guessing their meaning.
Property/dictionary iteration and equivalent numeric spelling do not change it.
The existing codec's processing bounds apply; these are not performance guarantees.

## Policy, provenance and trust

`SecurityPolicyEvidence` retains transient canonical content and derives its
snapshot using the existing digest primitive with `coglatas.security-policy/1`.
Policy set, version, positive schema version and content digest are independently
bound. Different content under the same version therefore changes the binding.
This is opaque content binding, not a Policy DSL or policy execution engine.

`SecurityCompilerProvenance` projects supplied version/build metadata and may
include an exact lowercase 40-character Git commit identity when actually known.
Missing identity stays null. #905 provides no production compiler or canonical
engine digest, so this slice invents neither an engine identity nor a fallback.
`test-host/1` and `fixture-build-1` are explicit fixture identities only.

Constructing evidence or matching a digest cannot authenticate policy ownership,
establish host trust, grant resource access or satisfy current authorization.
The applicable host must load evidence independently of request claims. #1119
will evaluate missing evidence and mismatches; this slice returns no decision.

Full Source/policy content and canonical binding/context JSON are transient.
They are not logging DTOs or approved durable payloads. The later persistence
adapter must project only whitelisted safe metadata and digests.

## Verification

The independent golden vector uses the existing `source-v1` fixture, canonical
ASCII policy content and separately computed domain-separated SHA-256 bytes.
The fixture pins both canonical output and digest:

- Policy content: `a79895fb82a26a9789f5211177d387c63ed1c06e186147ca8202072734b8bee3`.
- Binding: `1ee42152300d116adb7f8b30ab08f31d108a890d8db1cef3421882f8e873cbf8`.

Coverage includes ordering/numeric equivalence; Tenant/Project/Branch/Revision;
proposal/candidate/base/captured-head identities; independent claimed and host
values; policy set/version/schema/content; compiler version/build/commit;
unknown Source/context preservation; missing evidence; execution-metadata
independence; and domain/schema separation.

The authorization negative test uses the existing Project authorization service
and use case, including a positive authorized control. Possession of the exact
binding does not permit an actor without current Workspace membership to view
the Project; permission revocation is rechecked independently of the digest.
These are service tests, not PostgreSQL integration evidence.

Initial fixture failures correctly rejected Sources without their mandatory
Project root document. The fixtures were corrected without changing Source
validation. A second fixture assumed a Workspace member lacked legacy read
authority; the test now uses an actually unauthorized actor and preserves that
existing visibility policy. The initial golden test already matched.

Focused Source/binding/configuration/authorization coverage: 150 passed, zero
failed/skipped, including current-permission revocation with an unchanged digest.
Architecture: 10 passed, zero failed/skipped, including all new immutable types
in the existing pure-core dependency guard.
Final full solution after inspection cleanup: 1,874 backend and 10 architecture
tests passed, zero failed/skipped. `POSTGRES_TEST_CONNECTION_STRING` and
`COGLATAS_TEST_USE_MIGRATED_TEMPLATE=true` were set for the disposable local
PostgreSQL 18.6 database with 73 existing migrations. No migration is introduced
or applied by this binding slice; no production database is used.

The first local whole-solution inventory exposed an unused public policy-domain
constant and two style suggestions. The constant is now private, and the new
evidence carrier uses a primary constructor with get-only properties. No rule,
budget, baseline or suppression changes. Normal-merge and exact-Main
qualification evidence is recorded below.
The final pinned InspectCode 2026.2.2 whole-solution inventory and unchanged
checker pass: 2,396 findings, zero rule regressions/critical findings/unresolved
symbols/model failures, with both unresolved thresholds explicitly zero.
Existing debt remains; this is a Windows diagnostic, not a substitute for
applicable Linux PR/Main qualification.
No completed foundation, production compiler, release-readiness or
production-wide access-prevention claim follows from these primitives.
Numerical performance remains `SUSPENDED / NOT_EVALUATED`; #1128 is separate.

## Normal merge and exact-Main qualification

PR #1134 merged normally as `035a54f404878223fe4796aa403553d044b201fa`.
Its exact-Main licensed P0 failure exposed an asynchronous fixture authorization
handoff race and remains recorded. Independent repair PR #1135 merged normally
as `a271599a9cca2f2d3c4ad1f99c4648f17a3b060f`. That exact Main qualified in
[run 37838083775](https://github.com/NYGsatoshi/Coglatas/actions/runs/37838083775),
attempt 1: all six required checks, 1,891 backend/10 architecture tests with
PostgreSQL 18.6, 1,154 frontend unit/143 UI tests, licensed P0, fast/full/extended
Functional, CodeQL, both full Qodana lanes and five-role deep Security passed.
All three authenticated ZAP roles had zero high/medium/low findings; Gitleaks
passed. #1118 is closed with
[acceptance, failure-history and environment evidence](https://github.com/NYGsatoshi/Coglatas/issues/1118#issuecomment-6068755642).

The independent Dependabot updater failed and remains #804 work. Its result is
not presented as a pass. No specification HOLD or future enforcement boundary
was approved by this implementation qualification.
