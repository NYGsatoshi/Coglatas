# P0 #1966 — independent Compiler output snapshot lifecycle candidate

This Security-owned candidate independently tests the **already integrated**
local `ProjectCompilerOutputSnapshot` and pure `SyntheticWorkbenchViewModel`
against an immutable ProjectIDE Product. The new lifecycle fixtures compile
their own synthetic input, accept the genuine host response and exercise
snapshot queries on that accepted compilation through public APIs.

## Exact Product/Test composition

- Pinned Product commit `d570a6727b5e5af5c8902c1bf2d0575c66c29143`,
  tree `c00aac4581a7aecfe1e13b5fba89275ff6373938`, from ProjectIDE
  integration. Qualification applies to this exact source, without adopting
  later regional host changes.
- Product output wrapper blob `af304c2dec8694bd68a388ded3381c740516acec`
  and ViewModel blob `6ab45725d5694aa9f36849f7e87a0e57e069534f`.
- Independent Test C# blob `b13bc41f398ca0877d1d9505a2899fd7d835b657`,
  **35 native xUnit facts: 11 original Core facts plus 24 new lifecycle
  facts**. The original eleven method identities, bodies and helpers remain
  byte-for-byte intact from Test blob
  `a7832c98fcded823532b974eee11b74168ddfbf2`.
- Independent composition runner `ff7702d6f9103810d1bd26cf736d819c44229ba1`
  (current Security Test-owner runner model). The runner stages **only** the
  original exact Test blob into a detached, clean Product worktree, verifies
  the actual source bytes, Git index identity, Product HEAD and unrelated
  tracked Product files after composition, restore, test and discovery.
  Product and Test source remain separate.
- Product `global.json` pins .NET 10.0.403 and roll-forward disabled;
  the nonrequired Windows-latest and Ubuntu-24.04 advisory checks SDK/source
  pinning and requires **35/35 actual compiled xUnit PASS**, 0 failed,
  skipped or not executed on each host.
- The runner requires the native TeamTest/2010 `TestRun` namespace, one direct
  result summary and counter set, and one direct supported result, definition
  and entry inventory each. Extra containers, foreign namespaces and
  unsupported inventory records cannot be ignored. Each definition binds
  the exact class and bare method separately. Result names must equal the
  frozen roster; result, definition and entry IDs form a unique bijection
  with matching raw names. Compiled discovery contains the same 35 distinct
  identities. Count-only, duplicated, substituted, missing or non-Passed
  cases cannot qualify.
- Original TRX, compiled discovery, source blobs, Product and composed tree,
  logs and SHA256 receipts are retained as separate hosted artifacts.

## Eleven real Core controls

1. Default Manifest produces **no original body** while retaining the exact
   original input/Source/context, result/execution/verdict axes, output
   references and canonical digest.
2. AvailableOutputs captures only the three actually published semantic model,
   IR and SourceMap objects with same result/source binding and exact byte sum.
3. A mismatched expected Result digest refuses the request without exposing
   Source identity, input/context, output count or result summary.
4. A body budget equal to the real UTF-8 byte count admits the three bodies;
   one byte less discards **all** captured bodies atomically, preserving
   original Result metadata and NotCaptured flags.
5. Public output dictionary, Keys, Values, generic/legacy interfaces, copied
   entries, and nonpersistent mutations must not expose owned mutable state.
6. Nested SourceJson reparse and copied canonical byte edits cannot change
   retained originals, data digest or body identity.
7. Current vs stale desired-input freshness must be separately reported, with
   the actual source/result axes and compiler result unchanged.
8. Legacy artifacts without a retained producer must not fabricate bodies.
9. Cancelled read queries must not publish or mutate the original compilation.
10. Cancelled compiler execution must not fabricate unrecorded intermediate
    states, complete verdicts or output bodies.
11. Unsupported mode/out-of-bound budgets and absent run identity or
    unadopted Trust/Eligibility/retention/authority flags never qualify.

## Twenty-four real ViewModel lifecycle controls

The new fixtures independently construct a committed one-task Source and
matching compiler profile. Each requires a real completed
`LocalNetCompilerHost` response, the accepted original Result, its declared
publication and actual execution plan. Positive body queries require the
`CapturedAvailableOutputs` state and all three real output roles.

| Control group | Facts | Required observation |
| --- | ---: | --- |
| Explicit admission and rejected/invalid queries | 2 | Admission is required; a new query withdraws the previous view while retaining the real compiler Result and plan. |
| Reset, disposal, newer request and scoped readmission | 4 | Reset requires a newer generation and readmission; disposal rejects later queries; two genuine same-content responses retain distinct generation ownership; scoped readmission clears the current view and requires another query. |
| Lifecycle reentry at both notifications | 8 | Reset, disposal, genuinely changed input and scoped readmission at the initial and published notifications prevent the interrupted query from republishing old bodies. |
| Cancellation with a newer admitted or refused query | 4 | Cancellation at each notification throws while leaving the exact newer captured or `RefusedWrongResult` snapshot visible. |
| Cancellation without a replacement | 1 | Both notification boundaries withdraw the interrupted snapshot without erasing the caller-held prior bytes or changing the original Result and plan. |
| Newer query ownership without cancellation | 4 | A same-context admitted or refused query at each notification keeps its exact object; the interrupted outer query returns false. |
| Pre-cancelled query | 1 | An already-cancelled ViewModel query withdraws the prior visible snapshot before throwing, while returned bytes, Result and plan remain intact. |

Every reentry hook must fire and is armed once before its nested action.
Refusal controls require the actual non-null refused snapshot with no bodies,
input/result/context binding or serialized source/result details. Retained
caller-held snapshots are compared by their original digest and exact
canonical data/body bytes across lifecycle changes.

This is a **bounded local Core and pure ViewModel** qualification, not an admission test of
actual Tenant/actor/session permissions, persistent body storage, cross-project
snapshots, private SourceMap redaction, current Query/Open/export authority,
durable TTL/Pin, Native/Avalonia UI, or Release. A caller-held SourceJson
object may remain valid after a ViewModel clears its own retained reference;
view withdrawal is not physical erasure. Running these pure .NET controls on
Windows does not qualify an actual Windows/Avalonia UI or native runtime.
Regional collector/host coverage remains outside this lifecycle slice.

## Native evidence admission regressions

The bounded follow-up to
[#1966 comment 6946003242](https://github.com/NYGsatoshi/Coglatas/issues/1966#issuecomment-6946003242)
repairs two malformed-input admissions in the original #2055 runner:

- Moving the class suffix into a dotted method name no longer preserves
  admission merely because concatenating the two strings produces a known FQN.
- A second `Results` container with a failed `GenericTestResult` is rejected
  even when the first container retains all 35 valid passing facts.

`test_compose_and_verify.py` requires retained native TRX and compiled discovery
inputs. Its current 36 controls include the unchanged authentic positive and both
counterexamples, namespace and complete-inventory refusals, native identity
bijection, counter and discovery failures, and real temporary Git/worktree
composition boundaries. Only the `dotnet` outputs in the temporary main-path
controls are controlled; actual source bytes, index and Product HEAD changes
are performed and must be rejected after restore, test and discovery. Every
receipt retains all six false qualification flags and failure never receives
`nativeXunit` credit. The command wrapper also preserves nonzero process exits.

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

Both advisory hosts run these controls against their newly produced original
TRX/discovery and retain a separate `admission-controls.log`. The workflow pins
the controls blob independently. These are parser and composition regressions,
not additional native xUnit executions. Local replay uses the original #2055
Ubuntu TRX SHA256
`c7737d8da98d5f4f93aec468f88f340dfdbb6e121fbdb7a3a0b4a3878b9feb00`
from run `44054347293`, attempt 1, artifact `14315037803`.

The original #2055 native 35/35 results, source/tree binding and cross-host
artifact audit remain valid. The malformed copies demonstrated future evidence
admission defects; they did not demonstrate a hosted execution bypass. The
native command's exit status remains independently required. Fresh exact-source
hosted qualification is still required for this repaired runner.

```text
python -B tests/candidates/projectide-compiler-output/test_compose_and_verify.py --native-trx <retained-candidate.trx> --compiled-discovery <retained-compiled-discovery.log> -v
```

## Admission and open work

Every receipt must set `requiredCheckQualified`,
`independentReviewComplete`, `nativeUiQualified`,
`productionQualified`, `trustQualified` and `releaseQualified`
to `false` regardless of actual xUnit success.

Do **not** mark this candidate qualified before native GitHub CI succeeds on
its exact HEAD, each original artifact/file hash is verified and the selected
raw native test identities agree across hosts. The source blobs and composed
tree must match across hosts; per-host ZIP/TRX hashes and native execution IDs
may differ. Local source and parser checks do not establish a C# build
or native execution. A nonrequired source advisory cannot replace the six
original required app15368 checks, formal independent current-head review or
Security Owner adoption. The remaining #1966 authorization, redaction,
persistence, hold and runtime-specific acceptance checks remain OPEN.
#1144 is BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, Release NO_GO.

Repeat on a fresh checkout with the immutable Product commit available:

```text
python -B tests/candidates/projectide-compiler-output/compose_and_verify.py --product-head d570a6727b5e5af5c8902c1bf2d0575c66c29143 --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```


## Shared admission boundary follow-up (#2095)

The unchanged 35 C# facts now run behind complete nested-result admission checks,
including records hidden outside the direct Results container. Passing counters
cannot conceal run-level errors. Binary NUL-delimited Git checks reject both
`assume-unchanged` and `skip-worktree`, independently compare staged and unstaged
Product changes, and reject ignored as well as ordinary untracked inputs.
After restore, only `bin/` and `obj/` roots beside committed Product projects
are admitted as generated output; arbitrary nested or unregistered output names
remain rejected. The fixed Product tree contains no tracked Directory.Build
props/targets. Every restore, test and discovery call suppresses ancestor imports.

Both workflow launch blocks independently check committed and actual Python
bytes with `git hash-object --no-filters` immediately before isolated Python
execution. Controls exercise the real PowerShell launch blocks with changed
working, normalized CRLF and committed bytes and a neighboring import shadow.
A disposable real SDK project demonstrates poisoned ancestor imports failing
without the switches and succeeding with them. This SDK control executes no
Product xUnit. Native candidate totals remain exactly 35 on each hosted OS.
The newline-filename Git control is explicitly skipped on Windows, whose
filesystem forbids that fixture; all other Python controls remain required.

This cohort has no shared provenance helper. The advisory still does not retain
an executing-workflow provenance receipt; independent original API/job/archive
and source review remains necessary. Generated output admission is not continuous
build attestation, package provenance, Product authorization, or permission to
close the broader #2095 inventory. All six qualification fields remain false.

## Review correction controls

The global native record check also refuses `TestResultAggregation`, including
empty records and records with ordinary child output. Regression mutations cover
Failed, Passed and NotExecuted outcomes in native and foreign namespaces across
the root, result, output, definition and summary. The authentic positive remains
admitted; mutations receive no native execution credit.

Both staged and unstaged path inventories explicitly disable rename pairing. A
real Git control demonstrates that rename-aware output can hide a staged Product
deletion behind the candidate destination, then verifies separate paths are
refused and restoring the deleted Product input restores admission. Ignored-input
controls first assert a positive `git check-ignore` match. Fixture setup force
stages its known files so an ambient ignore cannot remove a committed project.
Ignored inputs outside the registered output roots are refused; files inside
allowed roots after restore are admitted by path and phase, without a claim about
how their content was produced.

The disposable SDK probe writes the exact 10.0.403 policy with roll-forward
disabled and validates the selected version before either ancestor import is
created, overriding a conflicting parent SDK policy. Bootstrap fixtures now use
distinct script bytes and pins, require every runner/control role binding in each
original host launch block, and assert which isolated canary actually executed.
Restored original bytes must pass after every rejected mutation. These remain
verifier controls; immutable Product and C# inputs, historical outcomes, native
case counts and all six qualification holds are unchanged.

## Whole-document native TRX run-information admission (#2095)

The corrected reader accepts only native informational RunInfo directly under ResultSummary/RunInfos. It rejects warnings and Passed/Completed run-message outcomes, misplaced or foreign RunInfo containers and any ErrorInfo, even if the original thirty-five native C# results have fully passing counters and identities. No candidate C#, Product source or qualification flags are changed by this synthetic evidence hardening.
