# P0 #1930 — independent semantic-index ViewModel ownership

This Security-owned cohort adds 24 independently authored xUnit facts over the
actual `LocalNetCompilerHost`, original scoped Source admission and the pure
`SyntheticWorkbenchViewModel` semantic-index/reference-Graph APIs. It constructs
its own Sources, declarations, typed ports, relation endpoints, fixed snapshots,
compiler settings and matching host profile through public APIs. No Product-owned
test helper, authored successful compiler result or internal index/Graph
constructor is used.

Existing [#1930 full-index handoff](https://github.com/NYGsatoshi/Coglatas/issues/1930#issuecomment-6961518117)
and [final-source handoff](https://github.com/NYGsatoshi/Coglatas/issues/1930#issuecomment-6963705134)
retain independent consumer qualification after the separately merged 22-fact
Core cohort. This addition includes the integrated
[#2070 reference neighborhood](https://github.com/NYGsatoshi/Coglatas/pull/2070).
The earlier Core, Draft Rename and other candidate sources and pins remain
unchanged. This bounded local cohort does not close #1930.

## Frozen inputs

- Product commit: `09347c1113f5f9c007bbbc5052278d5b5151feb5`.
- Product tree: `5abb6b4f4389f200a289a84b99a0dd13e60072cc`.
- Original SDK: .NET `10.0.403`, with Product roll-forward policy unchanged.
- Execution scope: `SYNTHETIC_SEMANTIC_INDEX_VIEWMODEL_ONLY`.
- Independent class:
  `Coglatas.Tests.ProjectIde.SecuritySemanticIndexViewModelCandidateTests`.

The workflow pins the C# source, composition runner and control blobs. A fresh
read-only composition adds only the original C# file to
`tests/Coglatas.Tests/ProjectIde/SecuritySemanticIndexViewModelCandidateTests.cs`
in an otherwise unchanged detached Product worktree. Both literal Ubuntu 24.04
and Windows jobs use the same immutable Product and separate original evidence.

## Independently exercised boundaries

| Group | Facts | Required behavior |
| --- | ---: | --- |
| Admission, original regions and exact ownership | 7 | Genuine host response and explicit scoped Source read precede index publication. Actual root/dependency relation origins and definitions navigate their original Source. Equal symbols, references, earlier Graphs and copied edges cannot substitute for current ownership or overwrite a newer Source target. Graph edge storage retains immutable original membership. |
| Completion and reference pages | 3 | Completion continuation belongs to its current origin/index. Actual 70-reference membership appears once in 32/32/6 pages. An independent 260-reference compile retains an empty 256-scan first page and the late original edge in its final four scanned rows. Old Graphs cannot borrow a newer page. |
| Source and host lifecycle | 4 | Scoped Source readmission, a genuine same-input response at a newer generation, reset and disposal withdraw the previous index/query/Graph. Reset requires a strictly newer host generation. Disposal rejects later semantic operations while caller-held immutable objects remain unchanged. |
| Cancellation and synchronous reentry | 8 | Pre-cancelled index/query reads withdraw interrupted views. At both initial and published notifications, older operations cannot overwrite or erase newer admitted/refused index/query owners. Reset, disposal and readmission prevent stale publication. A reentrant navigation preserves the newer selected Graph target. |
| Refused materializations and remaining bounds | 2 | Missing, denied and wrong-version snapshots cannot publish a private index; a separate restored supply succeeds. Current document navigation retains original regional identity and rejected page sizes 0/33 leave original index and Compiler bytes intact. |

Every notification hook is armed once, marks itself fired before nested work,
and must be observed by the test. Newer refused pages remain actual non-null
`InvalidCursor` results with no target, input digest, rows, scan count or cursor;
they are distinguished from a cleared view. Larger page fixtures use an explicit
three-million-operation compiler profile already permitted by Product. They do
not change any Product/default budget, the 32-result/256-scan limits, or client
transport admission.

## Evidence and source admission

The dedicated nonrequired advisory runs strict Release xUnit with
`-warnaserror`, requiring exactly the frozen 24 fully qualified fact names,
24 PASS, zero failure/skip and identical compiled discovery. Native TRX results,
definitions, class/bare-method metadata and entries must bind bijectively.
Duplicate, foreign, missing or substituted cases cannot qualify.

Original C# bytes, Product/Test/composed identities, TRX, compiled discovery,
command logs and SHA256 receipts are retained. The runner rechecks the raw
regular source file, staged blob, Product HEAD, exact single-path cached delta,
empty unstaged delta and absence of unexpected untracked inputs after composition,
restore, execution and discovery. NUL-separated untracked inventory includes
Git-ignored inputs such as `data/*.cs`. The initial composition permits no
untracked files. After restore, only `bin/` and `obj/` directly beneath project
directories containing an immutable Product `.csproj` are admitted; arbitrary
nested or unregistered output-directory names cannot hide compiler inputs.
All Git subprocesses remove inherited `GIT_*`
overrides while preserving ordinary host/SDK settings.

Nineteen portable verifier controls exercise synthetic report substitution,
counter/namespace/method/discovery mismatches and actual disposable Git source,
index, HEAD, staged-only Product, ignored and ordinary untracked-input,
generated-output admission and environment-redirection failures. Their passing
result is verifier coverage only. This local environment
has no .NET SDK; actual C# execution must come from the fresh hosted jobs. An
initial local control adaptation still used the earlier 22-case synthetic
counter fixture; it was corrected to the new frozen 24-fact roster without
changing the native suite or admission rules.

## Explicit limits

These are headless Core/host/pure ViewModel tests, including on Windows. They do
not execute actual Avalonia/Native controls, qualify rendered browser behavior,
resolve current actor/Tenant authorization, or grant remote query/open/apply,
retention, Trust, Eligibility, production or release authority. All six receipt
qualification flags stay false. Exact-current Security CI and independent
source/original-artifact review remain separate gates. #1144 stays BLOCKED,
#1128 SUSPENDED/NOT_EVALUATED and Release NO_GO.
