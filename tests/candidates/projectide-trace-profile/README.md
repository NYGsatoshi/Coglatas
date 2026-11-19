# Independent fixed Model trace profile qualification — P0 #1962

This Security-owned candidate independently verifies the **local** fixed
simulation recording, trace profiler Core, current owned-summary navigation
and pure ViewModel lifecycle. Its fixtures produce actual synthetic recordings
and then exercise reads of those recordings. It observes no live operations,
assesses no people or Resources, grants no access and certifies no
Native/Avalonia UI.

## Immutable source and test boundaries

- Actual immutable Product HEAD:
  `ea339ed0bc094630654262ebb28568a95ba4198f`, tree
  `3170396d28e390759acc454194eba411afef2749`,
  containing `ProjectSimulationTraceProfile.cs` Git blob
  `754a509692cbdd4e3024233b82f4baf156797cff`
  and the already merged profiler implementation. The owned-profile wrapper
  is blob `0fc57fc62a3dfa4f97d5d60ff89d8a2cf6c5414f`; its simulation lifecycle
  is blob `a7c76c365caa4fb627a344f8eae81b9b9084a5d4`.
- Exactly **32** C# xUnit `[Fact]` cases from
  `SecuritySimulationTraceProfileCandidateTests.cs` Git blob
  `aa5420d0237cbba892983ad627997e1676c45286`:
  **12 original facts plus 20 new navigation/lifecycle facts**. Original
  method names, bodies and helpers remain byte-identical to source blob
  `680b7da997b4d9abee16536b7915df749d293972`.
- Composition script `compose_and_verify.py` Git blob
  `b61940ede0a3649da82f0af46d394f17efa7d418`.
  It checks the exact original Test Git blob/index, regular nonsymlink candidate
  bytes and unchanged immutable Product HEAD before composition execution, after
  restore, immediately after native tests and after compiled discovery. It rejects
  unrelated tracked Product changes, uses a detached Product worktree and retains the source,
  Product/composed tree, SDK and actual compiled TRX/discovery evidence.
- Product `global.json` pins SDK `10.0.403` without roll-forward.
  The read-only nonrequired advisory uses pinned GitHub Actions, the literal
  `ubuntu-24.04` and `windows-latest` runners, and no persisted checkout
  credentials. Both hosts must prove exactly 32 unique native compiled
  passing TRX results, 0 fail/skip/not-executed, and exact discovery parity
  before a PASS may be claimed.

The runner freezes every full native test identity. It rejects missing,
duplicate, substituted, foreign-namespace or non-Passed cases; TRX results,
definitions and entries must form one bijection, and each definition's
`TestMethod` must contain the exact class and bare method separately. Moving
part of the class into a dotted method name cannot substitute for that binding.
The result summary/counters and Results/TestDefinitions/TestEntries containers
must each occur exactly once in the native TRX namespace; inventory containers
may contain only their supported native record types. Exact compiled discovery
equality is required.
Discovery removes only its four-space CLI presentation prefix and preserves
the remaining raw identity, including trailing whitespace.

## Twelve preserved independent controls

1. Ordinary fixed simulated recording separates union busy ticks from
   per-Task integrals and unattributed idle time.
2. Explicit overlapping Task intervals count once for busy union and once
   per Task for the independent integral.
3. Changing a virtual start offset does not create extra utilization or
   misleading idle attribution.
4. Zero-duration start/end transitions maintain the engine's event ordering
   and zero duration without losing peak concurrency.
5. Exact `BigInteger` Task integral exceeds Int64 without rounding, while
   the union clock retains Int64 virtual tick bounds.
6. A bounded prefix keeps open Tasks open, retains its last committed clock,
   and cannot masquerade as a completed run.
7. A smaller query budget refuses the whole metric summary instead of
   truncating an existing complete recording.
8. Wrong recording digest or unsupported engine refuses every basis and
   metric field, not merely the final UI verdict.
9. Stale input and cancelled queries do not disclose retained metrics or
   rewrite the fixed recording's canonical input bytes.
10. The profile keeps `SimulatedModel`, `NotRun`, `NotEvaluated`,
    `Unattributed` and `FixedRecording` metadata, never operational or
    permission-bearing semantics.
11. Returned canonical bytes are independent and culture-invariant.
12. Windows pure ViewModel withdrawal/re-admission cannot reuse a previous
    successful profile when its expected recording is wrong or the run is no
    longer currently owned.

## Twenty new navigation and lifecycle controls

The new cohort independently constructs a committed one-task Source and its
matching public compiler profile. Every fixture requires a genuine completed
`LocalNetCompilerHost` response, exact accepted Compiler Result, declared
publication, actual execution plan and completed two-event synthetic run.
It uses no Product test setup helper or authored recording/profile object.

| Control group | Facts | Required observation |
| --- | ---: | --- |
| Current, detached, superseded and refused summary navigation | 4 | Only the exact current summary opens its already owned run; equal canonical content or digest cannot substitute for instance ownership. Refusal exposes no run or metrics. A read preserves Compiler/plan/run references and emits no change notification. |
| Reset, disposal, changed input, scoped readmission, new run and debugger replacement | 6 | Each boundary withdraws the old summary/navigation. Fresh genuine admission and explicit profiling are required where applicable; a paused debugger has no terminal run to profile. |
| Newer admitted/refused query at both notifications, with and without cancellation | 8 | Same-context nested queries preserve their exact newer summary object. An interrupted outer call returns false or throws cancellation without overwriting or clearing that newer object. |
| Pre-cancellation and cancellation without replacement | 2 | The previous or interrupted visible summary is withdrawn. The no-replacement case independently exercises both initial and published notifications. |

Every reentry callback must fire and gates itself before its nested action.
Refused replacements remain actual non-null `Refused` profiles with absent
run/basis/period/metrics, and their navigation returns null. Caller-held
profile, recording, manifest, plan, input, Source and Compiler canonical bytes
remain unchanged throughout. Their retention is not a claim of physical
erasure after the ViewModel withdraws its own reference.

This cohort does not qualify Scenario or run-comparison semantics, regional
host behavior or other lifecycle combinations beyond these named controls.

## Actual authority and trust boundaries

This is only **synthetic local Core and Windows pure ViewModel** qualification.
The underlying model trace is not measured elapsed wall time, operational
observations, causal waiting attribution, Resource demand, staff performance,
predictive accuracy, a trusted evidence producer, an executed requirement
suite, an access grant, or eligibility for Release.

A real server-side profile query would require *current* actor/session,
Tenant/Project/Branch scope, capability checks, revocation, private or small
group contribution suppression, retention/redaction, bounded export and
audit. None are provided or approved by these tests. No new persistency,
actual Angular/Avalonia UI, remote trace reader, observed-data adapter,
Trust/Eligibility, Native/Production/Release activation or policy changes
are requested. The underlying issue #1962 remains OPEN for those separate
qualifications. Parent #1033 remains OPEN; #1144 BLOCKED, #1128
SUSPENDED/NOT_EVALUATED and Release NO_GO.

Do not credit this expanded candidate before both native hosts execute its
exact source. Verify every original artifact/file hash and compare raw native
test identities, source blobs and composed tree across hosts. Per-host ZIP/TRX
hashes and execution IDs may differ. Local source/parser checks do not establish
a C# build or native execution; running pure .NET on Windows does not establish
actual Windows/Avalonia UI acceptance.

All original receipts explicitly have
`requiredCheckQualified=false`, `independentReviewComplete=false`,
`nativeUiQualified=false`, `productionQualified=false`,
`trustQualified=false`, `releaseQualified=false`, even after a local
candidate PASS. Supplemental advisory PASS cannot replace the six original
app15368 Required Check contexts, independent formal source review, or
Security Owner governance. Historical Product-authored tests are not
independent Security approval.

### Local reproduction from an immutable Security Test HEAD

```text
python -B tests/candidates/projectide-trace-profile/compose_and_verify.py --product-head ea339ed0bc094630654262ebb28568a95ba4198f --worktree <fresh-product-worktree> --evidence <fresh-evidence-directory>
```

## Git environment isolation regressions

Every Git subprocess, including the raw committed-source read and detached
worktree creation, removes inherited `GIT_*` variables case-insensitively.
It preserves non-Git settings and never edits the caller's process environment.
This closes a reproduced source-binding gap: with `GIT_DIR` and `GIT_WORK_TREE`
pointing at a different disposable repository, the old runner resolved that
repository's HEAD despite receiving the intended checkout as its working
directory. No Product API or existing C# test source changes in this repair.

`test_compose_and_verify.py` contains **eight** regression methods. It admits
original retained native TRX/discovery before constructing fixtures, freezes
the unchanged C# blob and method roster, reproduces the old redirect with a
read-only unprotected Git call, and executes the actual runner's `main()`
against real disposable Git repositories. Separate controls cover a clean
environment, foreign repository/worktree, foreign index, foreign object/common
directories, counted Git configuration and parameter-based Git configuration.
Each controlled composition verifies the intended Test/Product/blob/tree and
all six false qualification flags, and compares every file in the foreign
repository before and after, including its index and object database.

Only the controls' `dotnet` command outputs are supplied from the retained
native inputs. These controls do **not** execute C#, add native passing cases,
or turn retained results into fresh execution evidence. The advisory first
runs the actual unchanged native candidate on each OS, then runs the eight
controls with that same host's original TRX and compiled discovery. It pins
the controls blob and retains their log separately in the workflow context.

```text
python -B tests/candidates/projectide-trace-profile/test_compose_and_verify.py --native-trx <original-candidate.trx> --compiled-discovery <original-compiled-discovery.log> -v
```

## Shared evidence admission follow-up (#2095)

The unchanged native candidate and immutable Product pin retain their original
execution scope. Native admission now rejects every nested result record,
including Passed or NotExecuted copies under otherwise complete passing outer
metadata, and refuses ErrorInfo or non-informational RunInfo records. Copied
report controls preserve the authentic outer names, counters and identities.

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

The committed suite contains 18 unittest methods, including the original
controls. Source/input, copied-report, SDK and bootstrap checks remain separate
from the unchanged native suite and fresh hosted Windows/Ubuntu execution.
All six broader qualification flags remain false. Complete shared #2095
acceptance, current authority, Native UI, production and Release qualification
remain open; #1144 stays BLOCKED, #1128 SUSPENDED / NOT_EVALUATED and Release NO_GO.
