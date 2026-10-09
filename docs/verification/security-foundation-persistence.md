# Security Evaluation persistence verification

Status: #1120 implementation candidate; not normally merged or exact-Main qualified.
Base: `35e541436222654b5ee345082ec82487b6d323b5`, qualified Main run
[37858309449](https://github.com/NYGsatoshi/Coglatas/actions/runs/37858309449), attempt 1.
#1117-#1119 are closed with implementation/merge/acceptance evidence.
Before publication, the branch incorporated current Main
`336cb97fad18820eed27cbfa8acb92c1e4b36099` (PR #1136's legacy dependency
update). Its Main qualification was still running at that checkpoint; no
dependency-update completion claim is inferred from the merge alone.

## Durable model and privacy

`security_evaluation_runs` and `security_evaluation_rule_results` are narrow
Security records. They do not duplicate #1034's shared evidence graph. Source
identities reuse #905's immutable typed context; digests reuse `ContentDigest`
and the existing canonical JSON implementation. The input/binding digest and
schema version remain durable and immutable. Digest equality grants no access.

The fixed identity snapshot stores original actor/operation, resource and
optional claimed/host Source identities, claimed/host policy snapshots and
claimed/host compiler provenance. Source snapshots contain known context IDs
and input/context digests. The context digest covers the complete canonical
context, including unknown extensions; extensions are not retained as text.
The compiler stays absent when unknown, with no fabricated production identity.

Policy content, full Project Source, arbitrary basis/evidence JSON, credentials,
tokens, private keys, decrypted data and exception text are excluded. JSONB is
only the closed typed identity snapshot, with nested allowlists enforced by a
database insertion guard. Rule rows contain typed safe metadata only. This does
not authenticate the stored policy/host evidence or create a new authority.

## Lifecycle and authorization

A run starts Pending with no decision/terminal timestamp. One transaction locks
that scoped parent, inserts all results in ordinal RuleId order, then writes the
terminal execution state. Only Completed has an outcome; failed/cancelled/timed
out/not-executed remain distinct. A completed aggregate must match complete
nonempty rule coverage and `Quarantine > Deny > Unknown > Allow`.

Binding metadata, mode, creation time and digests cannot change during the sole
terminal transition. Triggers reject terminal rewrites, rule updates/deletion
and late insertion. Concurrent callers serialize on the parent; one commits,
the other receives AlreadyTerminal. Re-evaluation appends a fresh EvaluationId.
The store refuses unrelated pending changes or an existing transaction and
detaches only its own tracked rows. A failed commit/transaction never returns
durable success; exceptions contain no stored exception evidence.

Every operation requires an authenticated current actor and available non-platform
Tenant scope. Creation/terminalization require existing Project contribution
authority, including its existing active/activated Project guard. Reads require
current Project read authority. Queries also predicate Tenant/Project explicitly;
known EvaluationId or ContentDigest never grants access. Subject identity must
match the current creating/terminalizing actor; a currently authorized historical
reader can differ from the original actor.

Permission is checked again before write commit and read return. Existing
repositories can return tracked authorization models, so the store compares
relevant cached facts with current PostgreSQL rows without refreshing, detaching
or mutating caller entities. Changed/deleted facts cause unavailable/null;
a fresh context reauthorizes a reader whose remaining permission is valid.
Existing global authorization services and ProjectIDE controls are unchanged.
This is request-time revalidation, not a new platform authorization locking protocol.

## Migration evidence

Migration: `20261009000906_AddSecurityEvaluationRecords`. It adds only two Security
tables, their indexes/constraints/guards and Project `(Id, TenantId)` uniqueness
for the composite scope FK. The model snapshot is scoped to Security additions.
The generated target designer reflects existing excluded Artifact/Audit Finding
metadata; no corresponding tables/columns are created by this migration.

Disposable container: `coglatas-sec-fnd-postgres-20261008`, `postgres:18-alpine`,
actual PostgreSQL **18.6**, loopback port 51989. The owned baseline database
`security_foundation_baseline_migrated` was explicitly upgraded from **73 to 74**
applied migrations; latest history entry is the Security migration above.
Connection credentials are synthetic local test values and are not recorded here.
No production database migration was applied.

Provider tests use isolated disposable databases. The migration test applies the
previous schema, retains three seeded Projects through upgrade and downgrade,
stores/terminalizes a run, rolls back to the previous migration, confirms the
Security table is absent, and upgrades again with no pending migrations and
empty Security history. Down removes Security rows/tables/guards and the added
Project key; it is destructive to Security history and not a retention API.

Append-oriented does not promise perpetual retention. The store has no history
update/delete method, TTL, Pin quota or new deletion capability. Database parent
deletion/cascade remains available to a future separately authorized #1034
retention/redaction adapter, verified on disposable data only.

## Executed checks

- Initial test fixture used non-activated Planning Projects, and the existing
  contribution guard correctly refused writes. The fixture was corrected to
  explicitly active/activated Projects; no authorization guard was weakened.
- Two meaningful authorization regressions then failed: stale Workspace
  membership still allowed read; stale Project Owner role still terminalized
  after revocation to Viewer. After current-row comparison, both pass; fresh
  Viewer scope still reads the unchanged Pending record.
- 14 initial provider cases passed after repair; expanded verification passes
  **222 focused ProjectIDE/provider tests**, zero failures/skips, on PostgreSQL 18.6.
  This includes 17 Security provider cases and nine new Domain lifecycle cases.
- Covered: safe identity round-trip; Candidate/Scenario and unknown extension
  digest; ordered rules; reevaluation; terminal-once contention; known-ID Tenant/
  Project/unauthorized denial; same-scope revocation; SQL immutability/late insertion;
  failed Pending insert and terminal transaction rollback; caller unit-of-work
  preservation; privacy canaries/closed metadata; composite scope FK; future
  retention cascade; mismatched binding and Enforce rejection.

- The full backend suite passed **1,986 tests**, zero failures/skips, with the
  real PostgreSQL 18.6 fixture. After strengthening the privacy fixture to use a
  real throwing evaluator, all 222 focused tests passed again.
- Architecture passed **11 tests**, zero failures/skips. EF's
  `has-pending-model-changes` reports no changes since the migration, including
  the scoped model snapshot.

PR checks, reviews, normal merge and exact-Main qualification are still pending.
Optional local InspectCode previously
rejected its invocation before analysis and is not claimed as a pass. GitHub
required ReSharper/full Qodana lanes remain authoritative quality gates.

## Boundaries and remaining work

This implements persistence only. #1121's awaited Shadow seam and #1122's complete
freshness-aware authorized read model/observability/exit gate are not implemented
by a table or this narrow repository read. No public Revision API, full compiler,
Merge engine, Avalonia, Semantic Firewall, new dependency or enforcement activation
is added. Disabled remains the default; explicit Shadow is non-authoritative.
Persistence failure's nonblocking ProjectIDE integration is verified in #1121,
not inferred from this adapter throwing an exception.

The private specification proposal remains Draft and unratified. Fixed S01-S14
selections are preserved without inferring unavailable original option wording;
public API, retention and future enforcement HOLDs are unchanged. Numerical
performance remains SUSPENDED / NOT_EVALUATED; #1128 and #1046 are separate.
This candidate is not a whole-product production-readiness or intrusion-prevention
guarantee.
