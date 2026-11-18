# P0 #1938: independent private lookup lifetime candidate

This Security-owned cohort follows the
[#2079 retained-storage handoff](https://github.com/NYGsatoshi/Coglatas/issues/1938#issuecomment-6967795977).
It qualifies optional private integer lookup tables and their owner/epoch lifetime.
The separate public reference cache (#2083) and symbol-session continuation
(Product #2082) retain their own qualification boundaries.

## Immutable compositions

| Role | Exact Product commit | Exact tree |
| --- | --- | --- |
| Corrected, merged #2079 | `1948e1834f79dfcf10bad5f0308546b2bcd939ae` | `5f8e0ce0b56ee5812277f5582de41ef9ee85fedd` |
| Original vulnerable #2079 source | `1696603417073e21db216244aabe72427fd61ced` | `77611a2465d30637abac525a9ee43d4009578254` |

The corrected merge has the reviewed source tree from `ee6ab354...`. Both
compositions compile the same complete independently authored C# file under the
Product's strict SDK 10.0.403 policy. Only the frozen native selection differs.
No Product source, test fixture helper, result or lockfile is repaired or imported
as independent evidence.

## Observations and limits

The 24 facts construct their own canonical Sources, fixed identities, declarations,
typed ports, requirement references and compiler manifests. They execute actual
`PureCoreCompiler.Compile`, `CompilerSemanticIndex.Build`, public index queries and
`SyntheticSemanticIndexProjection.Export`. The observations cover:

- Cold, warm and uncached canonical bytes, logical charges and current row identities.
- Changed display names, port types, document names and reference targets under
  reused positional tables; missing targets retain unresolved current outcomes.
- Reference and completion cursors remain owned by the original index instance.
  Stale/foreign queries return no borrowed rows or input stamp.
- Public collection and detached-byte mutation cannot poison subsequent indexes.
- Tenant, Project and Branch owner switches, pre-cancellation, late derived-work
  refusal, entry/key-byte bounds and explicit Clear preserve their exact boundaries.
- Six deterministic internal staging controls exercise Clear/owner invalidation
  before preparation, Clear/owner invalidation after preparation, cancellation at
  commit and a positive fresh lifetime. A narrow reflection adapter invokes real
  internal methods with tables constructed from an actual independently compiled
  index. This is internal staging evidence, not proof that a public concurrent
  caller was paused at that exact interval. It changes no private fields or Product
  code and supports both historical and corrected signatures.
- Sixteen bounded task-scheduled public build attempts across four owners combine
  concurrent scheduling and Clear while retaining each input's original results. This is a
  finite correctness check, not stress or performance assurance.
- Separate full compilations retain distinct original reference objects while
  the offline exporter produces equal bytes and its original byte limit still
  refuses oversized output. Query reference rows must belong to the current index.

## Frozen historical failures

Historical mode selects precisely these two negative facts:

- `ClearBeforePrivatePreparationCannotRepopulateAnInvalidatedLifetime`
- `NewOwnerBeforePrivatePreparationCannotReplaceItsRetainedTable`

The distinct positive is
`FreshPrivateLifetimeStillPublishesAndReusesItsActualTables`.

Historical qualification requires the original native test process exit code 1,
exactly three executed TRX cases, precisely those two Failed identities and the
one Passed identity, no skip/error/abort/incomplete counter, the exact intended
retention assertion message and the failing method's exact native stack frame. The two xUnit
Error RunInfo announcements must bijectively identify the verified failures.
An unavailable dependency, build error, unrelated assertion, missing positive,
extra case or unexpectedly passing negative is a refusal. Corrected mode requires
all 24 cases Passed and exit code 0. Exact compiled discovery must match the raw
TRX result/definition/entry bijections in either mode.

Authored assertions and synthetic verifier fixtures provide no native execution
credit. Fresh original Ubuntu and Windows results must be audited before claiming
the intended historical failure or corrected pass.

## Source and executing workflow admission

The runner composes only
`tests/Coglatas.Tests/ProjectIde/SecurityPrivateLookupCandidateTests.cs` into a
fresh detached exact-Product worktree. It verifies working bytes, 100644 Git mode,
stage zero, source blob, sole staged path and Product commit before and after
restore, execution and discovery. All untracked files, including ignored compiler
inputs, are rejected initially. After restore begins, only root `bin/` and `obj/`
under the fixed nine-project graph are admitted. Inherited `GIT_*` overrides are
removed from local Git commands; no mutable branch is a Product substitute.

The 29 portable Python methods cover exact native inventories, historical failure
admission, hostile Git environment overrides and disposable source/index/mode/HEAD
drift, including ignored C# and nested output lookalikes. They are verifier controls
and add no native C# or UI execution credit.

The shared provenance verifier and its 25 separate disposable-Git controls are
inherited from the #2089 base. Those controls provide no native execution credit.
Both static OS jobs run corrected and historical modes with read-only contents,
pinned Action commits, an exact Test checkout and no persisted credentials.
Each job captures actual `github.workflow_sha` and `github.workflow_ref` separately
from Test and Product identities. It retrieves the immutable executing commit and
requires the expected repository/path/ref, exact merge parents `[base, Test]`, a
Test descendant of that base, a regular 100644 workflow blob matching Test, and
equal executing/Test trees. An unavailable or divergent identity fails closed.
Each archive retains actual workflow context, Product SDK policy, Python version,
an explicit clean source-status file, separate provenance/admission logs and original source/native
result/discovery/hash receipts. No earlier receipt is restamped.

```text
python -B -m unittest discover -s tests/candidates/projectide-private-lookup -p test_compose_and_verify.py -v
python -B tests/candidates/projectide-private-lookup/compose_and_verify.py --product-head 1948e1834f79dfcf10bad5f0308546b2bcd939ae --worktree <fresh-current-product> --evidence <fresh-current-evidence>
python -B tests/candidates/projectide-private-lookup/compose_and_verify.py --historical --product-head 1696603417073e21db216244aabe72427fd61ced --worktree <fresh-historical-product> --evidence <fresh-historical-evidence>
```

## Remaining boundaries

The private cache retains positional construction, not runtime permission. These
synthetic fixed inputs do not qualify persisted actor/session authority, revocation,
confidentiality, open/apply/export policy, durable storage, retention/deletion,
actual Native/Avalonia or deployed integration. Byte counts concern framed roster
keys rather than managed heap usage; Clear does not erase caller-held old objects.

Keep #1938 and related #1930 OPEN. Every receipt keeps `requiredCheckQualified`,
`independentReviewComplete`, `nativeUiQualified`, `productionQualified`,
`trustQualified` and `releaseQualified` false. Ordinary CI and independent source
review remain separate; no formal human APPROVED, Artifact Trust, Eligibility,
RLS/Registry activation, Main adoption, Production or Release approval follows.
#1144 remains BLOCKED; #1128 remains SUSPENDED / NOT_EVALUATED; Release is NO_GO.
