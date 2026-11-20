# Independent public Test discovery and portable CLI qualification

Issue [#1945](https://github.com/NYGsatoshi/Coglatas/issues/1945) owns this
Security/Test candidate. It qualifies the public `ProjectTestPlan.Discover`
operation and actual portable `--discover` child processes on the immutable
Product from [#2084](https://github.com/NYGsatoshi/Coglatas/pull/2084):
`070ceb7f49e1e990a8395ea2e3a558a7185e1865`, tree
`c0f6cfbd7560cb2d4a3910aaafaffd4c04e36026`.

This adds 24 independently authored xUnit facts: fourteen Core facts and ten
CLI facts containing exactly 35 child-process invocations, including explicit
evaluator inspection. Those invocations are not another 35 xUnit facts. The
tests own their canonical Source, settings, identities, declarations and
expectations without importing Product test fixtures or invoking Product-authored
discovery tests. CLI tests launch `dotnet` with the built
`tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.dll`; they do not
substitute an in-process reflection call for the process boundary.

## Behavioral scope

The Core facts bind exact Input and Plan digests, Source changes, fixed
Tenant/Project/Branch/revision identity, evaluator identity, and typed candidate
locations including Proposal/base/captured target context. They check stable
TestId ordering, concurrent queries, caller-node and returned-list ownership,
detached-byte mutation, and cancellation followed by an unchanged positive.
Stale queries disclose no earlier roster or stamps. Test-count,
aggregate-assertion, declaration and binding-operation bounds refuse the whole
inventory. Empty suites, known empty assertion arrays, unsupported definitions
and invalid definitions retain distinct states; an unknown assertion count
never becomes a fabricated zero. Partial Source or unavailable foreign targets
cannot convert inventory into complete execution evidence. A known Boolean
counterexample remains discoverable and subsequently fails execution.

The real CLI facts independently check the binary-derived evaluator, exact
Source/envelope admission, Input/Plan digests and typed locations, and require
every discovery projection to retain `NotRun`, Missing evidence and zero
executed assertions. Discovery exit 0 means a ready inventory; the same
counterexample's separate run exits 1. A mixed valid/invalid inventory with an
evaluator mismatch must keep invalid exit 3; a valid evaluator mismatch stays
exit 2. Malformed UTF-8, oversized and future-format inputs, partial binding,
binding exhaustion, and drift refuse with their precise bounded contracts.
`CreateNew` preserves an existing output, Source and input envelope byte for
byte. A large admitted Source whose expanded discovery projection exceeds the
artifact byte bound must publish one small `ResourceExhausted` failure, with no
truncated prefix or earlier bound identifiers.

The CLI process ledger records the frozen case/invocation identity, arguments,
actual process ID, UTC interval, exit code, stdout/stderr, input lengths and
before/after hashes, and original output bytes with hashes. The runner checks
the exact 35-entry roster, unique identities, binary/evaluator bindings,
unchanged inputs and CreateNew ownership, including exact identity between an
input alias and its output receipt. If a completed child removes an input, its
record retains the PID, exit code and streams with an explicit null after-hash;
that failed preservation record cannot qualify a passing run. This ledger supplements the native
TRX; it does not authenticate a remote runtime or grant Artifact Trust.

## Composition and hosted admission

The runner composes exactly one committed Security C# blob into a fresh,
detached Product worktree. The immutable Product head/tree, staged source mode/blob/stage,
working bytes and complete tracked delta are checked before and after restore,
build, native test and compiled discovery. All untracked paths, including
ignored files, are examined with NUL separation. Only the frozen nine-project
reference graph's real root `bin`/`obj` directories may appear after restore;
an ignored `.cs` under another directory is still refused. Inherited `GIT_*`
overrides cannot redirect local source checks. Change-masking Git index flags are
refused before trusting a tracked diff. Every SDK invocation disables ancestor
`Directory.Build.props` and `Directory.Build.targets` imports, so external parent
files cannot add compiler inputs to this frozen composition.

Both hosted OS jobs use .NET SDK 10.0.403, Python 3.12 and immutable action
revisions with read-only contents and no persistent checkout credentials. An
explicit strict Release build precedes `dotnet test --no-build`. SHA-256 hashes
of the CLI, Domain, runtime configuration, dependency manifest and test assembly
must remain unchanged across the 35 processes. Each CLI evaluator digest must
match the domain-separated digest of the actual Domain binary.

The original TRX and compiled xUnit discovery must agree on all 24 frozen
identities, with unique bijective native test/execution records and no failed,
skipped or incomplete facts. Contradictory run-level errors and ErrorInfo
attached to passing results and nested `InnerResults` are refused. The 31 separate Python controls exercise native
admission, real disposable Git source guards and malformed synthetic process
ledgers. They are verifier controls and add no native or CLI execution credit.

The advisory depends on the shared
[`workflow-provenance` verifier](../workflow-provenance/README.md) introduced by
#2089. `github.workflow_sha` and `github.workflow_ref` are recorded separately
from the PR Test head and Product commit. Before native execution, its pinned
helper is compared with its committed and actual working-byte blob before it is
loaded. Python isolated mode prevents script-directory import shadowing. The
helper then verifies the exact immutable workflow commit, repository/ref/path,
regular Git mode and blob, equal workflow/Test tree and ordered Security-base/
Test-head merge parents. The shared refusal controls are retained separately.
Never retroactively restamp earlier discovery or Project Test receipts.

A successful archive contains twelve cohort evidence files, including the
final receipt, and four context files (`global.json`, `workflow-context.json`,
`admission-controls.log`, `provenance-controls.log`). The eleven non-receipt
evidence files have individual retained hashes. Fresh original GitHub metadata,
ZIP bytes, native discovery/TRX, binary/process receipts and actual workflow
objects still require independent audit before merge; configuration and local
Python controls alone do not establish hosted execution.

## Retained initial build failure

[PR #2094 run 44285545824](https://github.com/NYGsatoshi/Coglatas/actions/runs/44285545824)
stopped during the strict Release build on both hosts. The only build diagnostic
was xUnit2031 at the mixed invalid-definition/evaluator-mismatch assertion:
`Assert.Single` must receive its predicate directly instead of a preceding
`Where` call. Both original eleven-file archives remain failed evidence; no
native facts or CLI child processes executed. The equivalent predicate overload
preserves the exact-one-invalid-definition assertion. All 24 facts, 35 process
identities, Product pins, admission guards and `-warnaserror` remain in force.
The corrected source requires fresh originals from both hosted OS jobs.

## Review-driven admission hardening

The first successful [run 44287142941](https://github.com/NYGsatoshi/Coglatas/actions/runs/44287142941)
belongs to Test source `5afb02a0d6a89af02cd90bd5d5e0739181abe7df`. Its two
original archives retain their 24 native facts and 35 process records per host
under that source identity. A subsequent source review identified bounded gaps
in verifier refusal controls and individual assertions. Shared follow-up [#2095](https://github.com/NYGsatoshi/Coglatas/issues/2095) tracks the other affected cohorts. This revision adds
pre-execution provenance pinning and isolated loading, masked-index and nested-TRX
refusal, input-alias consistency, external build-import isolation, explicit
zero-test and candidate-roster assertions, and failed-child evidence retention.
All 24 fact identities, 35 successful process identities and the Product pin are
preserved. The revised source requires fresh hosted execution and independent
original audits; earlier successful receipts are not restamped.

## Remaining responsibilities

This is `SYNTHETIC_TEST_DISCOVERY_CORE_AND_CLI_ONLY`. Frozen local context is
not current persisted actor/session/Tenant/Project/Branch authority. This
cohort does not implement or qualify protected server read/export, revocation,
durable jobs, artifacts or storage, retention/deletion, policy-bound Merge,
formal Registry/Artifact adoption, the evolving paired-client work in #2090,
actual Native UI, production or release readiness. The earlier #1958/#2087
receipts remain valid only for their respective older Product/source scopes.

All receipt flags remain false: `requiredCheckQualified`,
`independentReviewComplete`, `nativeUiQualified`, `productionQualified`,
`trustQualified`, `releaseQualified`. Broader #1945 and parent #1031 remain
OPEN; #1144 remains BLOCKED; #1128 remains SUSPENDED / NOT_EVALUATED; Release
remains NO_GO. Source-review and hosted outcome records belong to this exact
candidate's PR and do not promote these qualifications.

## Local invocation

Run from the Security checkout. Choose a new absolute Product worktree and a new
absolute evidence directory outside that checkout for every invocation. The
example reserves a unique parent in the system temporary directory; its two
child paths do not exist before the runner creates them. Preserve earlier
evidence directories when running a later attempt.

```bash
qualification_root="$(mktemp -d /tmp/coglatas-test-discovery.XXXXXXXX)"
python -B tests/candidates/projectide-test-discovery/test_compose_and_verify.py -v
python -B tests/candidates/projectide-test-discovery/compose_and_verify.py --product-head 070ceb7f49e1e990a8395ea2e3a558a7185e1865 --worktree "$qualification_root/product" --evidence "$qualification_root/evidence"
```

The example assumes `/tmp` is outside the Security checkout. Otherwise choose
another external temporary parent. On Windows, choose equivalent fresh absolute
paths beneath an external temporary parent.

## Whole-document native TRX admission (#2095)

The independent reader rejects all result records outside the counted direct native test rows, including extension-namespace and aggregated records anywhere in the TRX. It refuses unbound error information and only admits informational RunInfo records directly under the native ResultSummary/RunInfos path. Counter totals alone are not evidence that every result in the document passed. The synthetic controls exercise these refusal cases; original C# identities and qualification flags remain unchanged.

## Exact isolated Python admission (#2095)

Both OS jobs preserve the original twenty-four compiled Core/CLI C# facts,
thirty-five genuine CLI invocations, immutable Product source, original raw
results and all six false qualification flags. The pre-execution provenance
verifier, provenance controls, candidate controls and native runner now bind
committed and unfiltered working Git bytes before isolated `python -I -B`
loading. The controls include actual uppercase Windows-cache refusals and a
hostile PYTHONPATH canary. Historical receipts remain source-specific; this
change cannot qualify user authority, Native UI, Trust, Production or Release.
