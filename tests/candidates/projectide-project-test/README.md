# P0 #1945 — independent typed Project Test Core qualification

This is a SecurityTestImplimation-owned **candidate**, not an activated
runtime-admission component. The Product code is the local typed Project Test
Core and CLI, already integrated through ProjectIDE Product PR #1946.

## Immutable source boundaries

- Exact, independent Product integration commit:
  `3613c648f118cafcb1936fdf5a081aa251c6b4bf`; this is not automatically
  updated when ProjectIDE advances.
- Twelve independently authored xUnit facts in
  `SecurityProjectTestCandidateTests.cs`, source Git blob
  `50e2718d6d88d1020a21640b1d2a46ac4275d07c`.
- Runner `compose_and_verify.py`, Git blob
  `03d0060f02afd03c821cda9df2a812ad94bb479a`.
- The runner requires a clean committed Security Test checkout and distinct,
  fresh worktree/evidence paths. It checks regular, nonsymlink candidate bytes,
  the staged Git blob, exact Product HEAD and complete tracked Product delta
  after composition, restore, native test and compiled discovery. It composes
  exactly one immutable Test blob with a detached immutable Product commit.
- GitHub-hosted Ubuntu 24.04 and Windows runners use immutable
  `actions/checkout`, `setup-dotnet`, `setup-python`, `upload-artifact`
  actions, no persistent checkout credentials and read-only contents access.
- Product `global.json` pins .NET SDK 10.0.403 without roll-forward.
- Native actual compiled xUnit discovery and the complete TRX must match
  the **frozen original twelve facts**. Require the TeamTest/2010 namespace,
  exactly one supported summary/counter/inventory container, independently exact
  class and bare-method names, unique test/execution IDs and matching result,
  definition and entry bijections. Every non-PASS counter must be zero.
  Original logs, TRX and source receipts retain per-file SHA-256 and
  Product/composed Git trees for independent audit.

## Scope of the twelve tests

1. A local known Boolean counterexample is distinguishable from the ordinary
   Compiler PASS; a Test verdict cannot silently qualify a different producer.
2. Rational/decimal comparison uses one exact Source/input binding, a
   fractional 1/10 versus 0.1 equality, and an integer precision-boundary
   mismatch at 9007199254740993 versus 9007199254740992; all remain in one fact.
3. Collection count applies to actual decoded list members rather than
   filling unknown data with a zero default.
4. Unknown and null values remain Inconclusive with explicit reasons, never
   known PASS or trusted model evidence.
5. Zero discovery and an empty assertion roster cannot become a successful
   test-suite receipt.
6. Stale input digests and a foreign evaluator identity must be refused before
   execution, even when the request has a familiar Source.
7. Forged Project identity in the target declaration cannot expose a
   cross-project assertion execution.
8. Missing Task defaults and unsupported assertion kinds cannot execute as
   valid local tests.
9. Cancelled operations and explicit local assertion limits do not fabricate
   executed facts.
10. A previously known FAIL is not erased by an independent unsupported Test.
11. Partial Source scope cannot promote an individual successful assertion
    into complete-suite ModelProof.
12. Caller-owned values, public result arrays and read-only interfaces cannot
    alter retained result digest, canonical bytes or statement identities.

The tests independently construct fixtures using documented canonical Source
building blocks already included in the immutable Product tree. They are not
duplicates of Product-authored `ProjectTestRunnerTests` source or copies of
those tests into the Security branch.

## Native admission repair

The existing reader admitted same-count reports with substituted class names,
duplicate native IDs, missing definitions/entries and nonzero incomplete
counters. Its discovery check accepted longer method aliases because it searched
for a bare-method substring. This follow-up preserves the original twelve
C# facts and immutable Product pin while requiring complete native identity
binding and exact compiled discovery.

`test_compose_and_verify.py` exercises 31 admission controls using original
native TRX/discovery inputs, including the authentic positive, malformed
copies, unsupported or duplicate containers, class/method and native identity
refusals, counters, discovery aliases and significant whitespace. Real temporary
Git worktrees exercise actual candidate-byte, tracked Product, index-only and
same-tree Product HEAD changes after restore, test and discovery. Only the
dotnet boundary is controlled in those main-path regressions; they do not count
as native execution. Every failure remains `FAILED_OR_INCOMPLETE`, receives no
`nativeXunit` credit and preserves all six false flags.

Before any Main fixture initializes Git, both retained inputs must pass the
frozen native/discovery admission checks; empty, malformed and foreign fixtures
stop the controls instead of supplying negative-test credit. Each Main refusal
checks its exact error, receipt error and complete reached-stage sequence.
Local Git children, including the raw source read and temporary fixture setup,
remove inherited `GIT_*` process settings while preserving every non-Git
environment variable. These operations perform no remote Git access. A real
disposable two-repository control demonstrates the old environment redirect,
runs the repaired Main path under the same pollution, and compares every file
in the disposable caller repository to prove it was unchanged. The original
caller repository is never used as the pollution target.

Both advisory hosts run the pinned controls against their own newly produced
original native outputs and retain `admission-controls.log` separately.
Local replay uses original run `43619001017`, attempt 1, Ubuntu artifact
`14123266465`, whose ZIP SHA256 is
`ff361b43a21acd722c8589ed5c37990413c4067b4bc817f33d8fb05a6a601889`.
Original #1958 native evidence remains historical and valid for its source.
Malformed copies demonstrate reader defects, not a hosted execution bypass.
Fresh native twelve-case execution and exact-head CI are required for this repair.

```text
python -B tests/candidates/projectide-project-test/test_compose_and_verify.py --native-trx <retained-candidate.trx> --compiled-discovery <retained-compiled-discovery.log> -v
```

## Original and remaining acceptance boundaries

The tests are a **local synthetic Core-only** slice. They do not exercise
Test-definition editing and lifecycle, browser original-byte admission,
current persisted actor/session/Tenant/Project/Branch capability admission,
Query/Open/Preview/Run/Cancel/Export authority, server-side redaction and
revocation, durable jobs, artifacts/caches, an actual cross-host attested CLI
binary, evaluation policy under shared Merge, Native/Avalonia UI, or production
readiness. A local ModelProof verdict is not Requirement evidence eligibility,
a formal Artifact trust marker, a server-run token or a Merge authorization.

The current first candidate has no real hosted execution claim until its
**exact HEAD** two-host advisory completes, original TRX and ZIP artifacts are
downloaded and verified, and original six app15368 Required Check contexts
succeed. A passing supplemental advisory never replaces required checks,
independent current-source review or applicable Security Owner governance.
No formal GitHub `APPROVED` status is inferred from parser or native results.

All recorded receipt flags are explicitly false:
`requiredCheckQualified`, `independentReviewComplete`,
`nativeUiQualified`, `productionQualified`, `trustQualified` and
`releaseQualified`.

Issue #1945 must remain open for residual independent Security boundaries.
Parent #1031 stays open; #1144 remains BLOCKED, #1128 is
SUSPENDED/NOT_EVALUATED and Release remains NO_GO.

### Repeat locally on the immutable Product

```text
python -B tests/candidates/projectide-project-test/compose_and_verify.py --product-head 3613c648f118cafcb1936fdf5a081aa251c6b4bf --worktree <new-product-worktree> --evidence <new-evidence-directory>
```

## Shared evidence admission follow-up (#2095)

The unchanged native candidate and immutable Product pin retain their original
execution scope. Native admission rejects every InnerResults container and
every XML record whose local name ends in TestResult outside the exact direct
inventory. This includes Passed or NotExecuted GenericTestResult copies beneath
cases, summaries or definitions with otherwise complete passing outer metadata.
It also refuses ErrorInfo or non-informational RunInfo records. Copied-report
controls preserve the authentic outer names, counters and identities.

Before relying on a Product diff, every composition check reads NUL-delimited
Git path/index records as raw bytes and rejects assume-unchanged, skip-worktree
and other nonordinary flags. Staged and unstaged changes remain separate;
untracked and ignored source is refused. Only bin/obj directories beside
committed Product csproj files may contain generated output after restore.
Nested or unrelated output-looking names cannot hide new compiler input.

Every restore, native test and compiled-discovery command disables ancestor
Directory.Build.props and Directory.Build.targets imports. A disposable real
SDK probe first demonstrates each poisoned parent import failing, then checks
that the isolated command succeeds. Its custom VSTest target exercises import
behavior only; it executes no Product C# fact and adds no native case credit.

Both workflows compare committed and actual runner/control bytes immediately
before each Python invocation, use git hash-object --no-filters, and invoke
Python with -I -B. Actual PowerShell bootstrap controls reject a changed script
before its canary runs and verify isolation against local/PYTHONPATH import
shadows. These cohorts have no shared provenance helper; this does not create
an executing-workflow provenance receipt that was absent previously.

The committed suite contains 31 unittest methods, including the original
controls. Source/input, copied-report, SDK and bootstrap checks remain separate
from the unchanged native suite and fresh hosted Windows/Ubuntu execution.
All six broader qualification flags remain false. Complete shared #2095
acceptance, current authority, Native UI, production and Release qualification
remain open; #1144 stays BLOCKED, #1128 SUSPENDED / NOT_EVALUATED and Release NO_GO.
