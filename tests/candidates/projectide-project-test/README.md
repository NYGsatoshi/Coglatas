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
  `5241b5929e0a014d0ab657844855c6dd91bee66b`.
- The runner requires a clean committed Security Test checkout and distinct,
  fresh worktree/evidence paths. It checks the copied candidate's actual
  working-tree bytes both before compilation and after native discovery,
  independently of the staged Git blob; it rejects any other tracked Product
  changes instead of certifying the index alone. It composes exactly one immutable Test blob
  with a detached immutable Product commit without rewriting Product source.
- GitHub-hosted Ubuntu 24.04 and Windows runners use immutable
  `actions/checkout`, `setup-dotnet`, `setup-python`, `upload-artifact`
  actions, no persistent checkout credentials and read-only contents access.
- Product `global.json` pins .NET SDK 10.0.403 without roll-forward.
- Native actual compiled xUnit discovery must match **all twelve distinct**
  passing TRX results, with zero failed/skipped/not-executed and a successful
  run-level summary. The raw original logs, TRX and source receipts are retained
  with per-file SHA-256 and Product/composed Git trees for independent audit.

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

## Original and remaining acceptance boundaries

The tests are a **local synthetic Core-only** slice. They do not exercise
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
a human current-HEAD `APPROVED`, or Security Owner governance.

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
