# P0 #1938: independent reference-cache retention and lifetime candidate

This additive Security-owned cohort qualifies the public reference-resolution
cache introduced in Product #2076 and the reference-lifetime repair in #2080.
It does not replace the earlier symbol-cache, semantic-index, immutable-envelope
or client lifecycle cohorts or change their Product pins. The owning handoffs are
[#1938 reference storage](https://github.com/NYGsatoshi/Coglatas/issues/1938#issuecomment-6966702799)
and [#1938 reference lifetime](https://github.com/NYGsatoshi/Coglatas/issues/1938#issuecomment-6968262608).

The 24 independently authored facts construct their own fixed Sources, typed
references, document references, compiler manifests and snapshot claims through
public constructors and canonical JSON. They invoke actual
`ProjectSemanticBinder.Bind`, `CompilerBindingClosure.Build`,
`PureCoreCompiler.Compile` and `CompilerSemanticIndex.Build`. No Product-authored
test, Product fixture helper, private cache accessor, reflection or fabricated
compiler result is imported as independent evidence.

## Immutable compositions

| Role | Exact commit | Exact tree |
| --- | --- | --- |
| Corrected Product | `71c261b1beabd8f93383ee059aab14d800b4339c` | `c3bcc23a211c3e32d073c2e38ba7ccd56bb5e843` |
| Historical vulnerable Product | `ee6ab354c1f9bdcc9d181e4b329954af7f50d852` | `5f8e0ce0b56ee5812277f5582de41ef9ee85fedd` |

The corrected pin is merged #2082, a stable descendant containing the separate
#2076 reference-cache addition, #2079 index-cache work and #2080 reference-lifetime
repair. This cohort is about the reference cache; it does not qualify #2082's
separate symbol-cache lifetime or #2079's private index lookup cache. The
historical pin is #2079's unchanged source before the #2080 reference repair.
Both compositions use SDK 10.0.403 with the Product's existing strict SDK policy.

The workflow pins the exact C# source, composition runner and verifier-control
Git blobs. Each receipt records original Security Test HEAD, Product HEAD/tree,
candidate blob/SHA-256, staged composed tree and raw native/discovery identities.
The same complete C# bytes are compiled for both Product versions. Only the
frozen executed selection differs; historical code is not rewritten to produce
an expected failure.

The workflow retrieves a missing Product object from the public origin using
only its full immutable SHA, then checks both the resolved commit and tree before
reading its SDK policy. A complete branch/tag checkout need not contain the old
unmerged source commit retained by a closed pull request.

## Required observations

| Area | Independent observations |
| --- | --- |
| Actual reuse and current model | Cold, warm and uncached public fields/charges agree; warm references reuse the actual immutable object while models and current input ownership remain separate. An unrelated declaration edit can reuse only unchanged lookup dependencies. |
| Input and object integrity | Generic/legacy collection mutation, detached reference records, caller byte arrays, returned context/location/Source bytes and snapshot-array copies cannot rewrite retained references or the old published model. |
| Fresh dependency admission | Missing, denied, conflicting, wrong-version and changed-byte snapshots cannot borrow a prior resolved external reference. Missing/added/removed local targets and unsupported target kinds retain distinct outcomes. Typed origin and manifest substitutions are refused. |
| Lifetime barriers | Actual snapshot enumeration, later dependency-region binding and full compilation after the root bind each run deterministic Clear and new-owner controls. Invalidated work must preserve exactly the current retained entry/byte counts; new-owner controls also preserve the same retained public reference instance. |
| Meaningful continuation | Corrected full-Compile controls require actual hook entry, Completed execution, a complete binding closure and three actual dependency regions. A deliberately different session must positively retain fresh dependency work from the earlier immutable root. |
| Bounds and cancellation | One-entry eviction and a one-byte key limit preserve uncached results; byte counters remain key-byte counters. A reference-free Tenant/Project/Branch switch flushes retention. Pre-cancellation preserves the existing owner; cancellation during real supply publishes no retained reference. |
| Concurrency and views | Four actual task-scheduled owners perform bounded binds and Clears without cross-owner public results. New full indexes preserve canonical data but reject earlier-instance cursors. A reused document reference republishes its exact target symbol after the earlier first origin is removed. |
| Full compilation | Cached and uncached whole result/dependency/model/IR bytes and logical charges agree. A separate local compilation establishes Completed/PASS/non-null IR; external version-one publication limitations are not relabelled global PASS. |

The three-context graph and every branch/tenant identity are synthetic constants.
The concurrency control is a finite correctness regression, not stress testing or
a timing/performance claim. All ordinary logical operation, transport and query
limits are preserved.

The full-index control uses each actual whole compilation's
`Dependencies.Binding`, whose declared-snapshot scope is asserted explicitly.
The separately rebuilt reachable-only public closure must still produce an
Incomplete index with incomplete coverage. Fresh complete indexes retain their
strict canonical-data and foreign-cursor refusal assertions.

## Original historical RED

Historical mode selects exactly these six negative public-API facts:

- `ClearingDuringActualSnapshotEnumerationPreventsLateRetention`
- `NewOwnerDuringActualSnapshotEnumerationKeepsItsExactRetainedObjects`
- `ClearingBeforeRegionalContinuationPreventsOldRootRepopulation`
- `NewOwnerBeforeRegionalContinuationCannotBeReplacedByAnOldRoot`
- `ClearingAfterRootBindingWithinFullCompilePreventsRegionalRetention`
- `NewOwnerAfterRootBindingWithinFullCompileSurvivesOlderContinuation`

The separate positive fact is
`ASeparateSessionMayRetainFreshRegionsFromTheEarlierImmutableRoot`.

A historical run qualifies only when the real test command exits exactly 1 and
the original native TRX contains seven executed cases, precisely the six named
failures and the one named positive, with no skip, error, abort or incomplete
counter. Each failed result must contain the intended retained-lifetime assertion
message and that exact failing method in its native stack. Compilation errors,
unexpected failures, missing results, a failed positive or an unexpectedly
passing negative cannot become expected RED. Compiled discovery, original
result/definition/entry identities and C# source bytes must all match.

Corrected mode requires all 24 cases Passed and a zero test-command exit code.
Source inspection and synthetic TRX controls do not prove that either native
selection executed. Fresh original hosted results must be inspected before
claiming RED/GREEN qualification.

## Source and native admission

The runner uses a fresh detached worktree at the exact Product commit and stages
only `tests/Coglatas.Tests/ProjectIde/SecurityReferenceCacheCandidateTests.cs`.
Before and after restore, native execution and discovery it requires the exact
committed working bytes, Product HEAD, candidate blob, regular-file mode 100644,
index stage zero, sole staged path, no unstaged tracked drift and no unexpected
untracked input, including Git-ignored files. Before restore there is no generated
output allowance. After restore/build begins, only root `bin/` and `obj/` under
the nine fixed projects in the actual test project-reference graph are allowed.
Nested lookalike directories, test data, artifacts and node_modules are not
blanket exemptions. Every local Git call removes inherited `GIT_*` overrides. No
Product file is repaired, overwritten or silently normalized by the candidate.

Strict TRX admission requires the original TeamTest namespace, exactly one direct
result/definition/entry inventory, the full frozen case roster, exact class and
bare-method metadata, unique native test/execution IDs and complete bijections.
Compiled discovery removes only its CLI presentation indent, not identity text.
Unexpected namespace, duplicate/aliased identity, changed outcome or malformed
container is a refusal.

The 28 portable Python methods retain real disposable-Git source/index/HEAD/drift
and hostile-environment controls plus positive/malformed native report controls.
The new mode-only index substitution uses `core.filemode=false` to demonstrate
why an unchanged source blob and empty unstaged diff are insufficient. Separate
ignored-source controls reproduce the difference between Git ignore rules and
compiler inputs, reject ignored C# and nested lookalike output directories, and
admit only the explicit SDK-output paths after the initial clean composition.
Separate
historical controls reject wrong counters/outcomes, unrelated assertion messages
or stacks, swapped positive/negative outcomes and mismatched Product modes.
Historical admission requires exactly six xUnit adapter Error RunInfo
announcements, each a single plain Text child matching the exact elapsed-time,
fully qualified failed method and `[FAIL]` format. These names must bijectively
match the six results already verified through their assertion messages, stacks
and native identities. Missing, duplicate, foreign, positive-case or malformed
announcements are rejected. Corrected mode permits no Error RunInfo; unrelated
Error, Failed, Aborted, Timeout or unsupported run information and run-level or
passing-case ErrorInfo cannot hide behind expected assertion RED.
These methods provide no native C# execution credit.

```text
python -B -m unittest discover -s tests/candidates/projectide-reference-cache -p test_compose_and_verify.py -v
python -B tests/candidates/projectide-reference-cache/compose_and_verify.py --product-head 71c261b1beabd8f93383ee059aab14d800b4339c --worktree <fresh-current-product> --evidence <fresh-current-evidence>
python -B tests/candidates/projectide-reference-cache/compose_and_verify.py --historical --product-head ee6ab354c1f9bdcc9d181e4b329954af7f50d852 --worktree <fresh-historical-product> --evidence <fresh-historical-evidence>
```

The nonrequired advisory has static Ubuntu 24.04 and Windows latest jobs, each
with corrected and historical modes. It uses pinned Action commits, exact PR
source checkout, no persisted credentials and read-only contents permission.
Every leg retains its original C# source, SDK/restore/test/discovery logs, TRX,
source/receipt hashes, workflow context and separate verifier-control log. A
failed attempt remains failed historical evidence; it is never relabelled after
a later source correction.

## Retained initial diagnostic attempt

[PR #2083 run 44240613367](https://github.com/NYGsatoshi/Coglatas/actions/runs/44240613367)
at Security head `e43cc8b6f35d12c8876abfe72d6a740a8e8c8748` is a failed attempt.
Both corrected-host jobs compiled and executed all 24 native facts: 23 passed and
`FullCompilationAndFreshIndexesKeepCanonicalBytesChargesAndCursorOwnership`
failed because the test supplied the public reachable-only closure while
expecting a complete declared-snapshot index. The corrected setup above consumes
the actual full-compilation dependency closure and retains the reachable-only
negative control. The original results remain 23 PASS / 1 FAIL per host.

Both historical jobs stopped before native execution because the historical
Product object was absent from the branch/tag checkout. Explicit immutable-SHA
retrieval addresses that setup failure. This attempt establishes neither
historical native RED nor corrected 24-case GREEN; fresh four-leg originals are
required after the source correction.

[The second run 44242753210](https://github.com/NYGsatoshi/Coglatas/actions/runs/44242753210)
at source `b9f6fe9f12c86933cfb45a84e3cc75cce6f72b95` also remains a failed workflow
attempt. Its corrected legs each executed 24 PASS / 0 FAIL. Both historical legs
executed the six intended invariant failures and one positive, but the previous
parser rejected xUnit's six normal Error RunInfo failure announcements before
compiled discovery. Their eleven-file archives establish that diagnostic
observation, not completed historical qualification. The narrow adapter-to-result
bijection above corrects that verifier error. Replaying retained TRX files is a
parser control and provides no fresh native execution credit; all four legs must
run again with the corrected runner and the same C# source.

## Remaining boundaries

The snapshot authorization booleans are supplied fixed claims. Fresh admission
of those claims is not persisted actor/session/Tenant authorization, revocation,
privacy, retention/deletion/export policy or durable storage. Clear removes cache
retention; it cannot revoke old objects already held by a caller or establish
heap erasure. The measured byte count concerns exact dependency-key text, not
total managed memory. Compiler/index data grants no query/open/apply authority.

Keep #1938 and related #1930 OPEN. All receipts preserve
`requiredCheckQualified`, `independentReviewComplete`, `nativeUiQualified`,
`productionQualified`, `trustQualified` and `releaseQualified` as false.
Ordinary required CI and independent source review remain separate. No formal
human APPROVED, actual Native/Avalonia, RLS/Trust/Eligibility, Main, Production or
Release adoption follows. #1144 remains BLOCKED and #1128 SUSPENDED /
NOT_EVALUATED; Release remains NO_GO.

## Executing workflow revision

Fresh runs use the shared [executing workflow provenance contract](../workflow-provenance/README.md)
from #2089. Actual workflow SHA/ref, exact merge parents, workflow mode/blob and
complete tree are checked independently from the Test checkout and Product pin.
The additional provenance controls grant no native execution or qualification
credit. Earlier receipts remain unchanged with the precise limitation documented
in that contract; they are never retroactively restamped.
