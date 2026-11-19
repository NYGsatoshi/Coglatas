# Independent Draft Rename boundary candidate

Issue [#1930](https://github.com/NYGsatoshi/Coglatas/issues/1930) owns independent
Draft Rename security qualification. This candidate adds 24 independently
authored facts over the public Core and pure synthetic ViewModel APIs. It
contains no imported Product test fixture or authored Compiler result.

## Immutable inputs and scope

- Product commit: `ea339ed0bc094630654262ebb28568a95ba4198f`.
- Product tree: `3170396d28e390759acc454194eba411afef2749`.
- Product SDK: `10.0.403`, from that commit's `global.json`.
- Initial Security base: `f0e188d090f7542b4c824b5a4ed7aaa2c64fe6d9`.
- Independent class: `Coglatas.Tests.ProjectIde.SecurityDraftRenameCandidateTests`.
- Independent C# blob: `2518da1e6358a5fb3512cf49f0ceb469530cb4b7`.
- Independent runner blob: `58a2241bb7d8a0f7eea74b28dff42ca6614ed8cc`.
- Execution scope: `SYNTHETIC_DRAFT_RENAME_CORE_AND_VIEWMODEL_ONLY`.

The initial four-file candidate patch consisted of the test source, composition runner, scoped
advisory workflow and this README. The runner inserts only the committed test
source into an otherwise unchanged detached Product worktree. No Product
implementation, original test, shared CI owner or required-check policy changes.

## Independently asserted behavior

| Boundary | Assertions |
|---|---|
| Exact local transition | Before/after input and Source digests, immutable candidate identity, stable Proposal/base/captured target, typed IDs, settings and unrelated declarations |
| Document and reference integrity | Document-root and manifest digest recomputation; structural reference contexts advance; typed target contexts/manifests and opaque Source fields remain fixed |
| Refusal and resource limits | No-change, stale input, foreign location, Proposal/base/target/same-candidate drift, committed/Scenario misuse, reserved contexts, missing/unsupported/port targets, Unicode and exact UTF-8 byte limit |
| Fork and application | Exact committed base, captured target, retained inputs, copied equal canonical input, changed Source/settings/candidate refusal and cancellation |
| Retained reads | Plan and legacy reference rosters reject generic writes and expose no non-generic `ICollection`/`SyncRoot` storage; detached copies cannot alter retained or fresh reads |
| Pure ViewModel ownership | Exact plan object, current selection and newer generation; a successful local apply clears old projections and needs a fresh actual host response |
| Cancellation and reentry | Deterministic notifications cancel an owned Preview, preserve a newer selected Preview, stop before Source publication, preserve a synchronously superseding context, and retain the already published local context after cancellation |
| Synthetic materializations | Exact input/pin binding, owned immutable read rosters and no denied Source body in the exported unavailable entry |

The cancellation cases independently exercise the public behavior described by
[#2050's handoff](https://github.com/NYGsatoshi/Coglatas/issues/1930#issuecomment-6939509022).
The retained-read controls cover the distinct
[#2051 handoff](https://github.com/NYGsatoshi/Coglatas/issues/1930#issuecomment-6940087907).
They preserve the original inputs and any newer owned local operation. They
make no assertion about database commits or current server authorization.

Fixtures use their own IDs, names, Source objects and settings. Opaque extension,
package and reference fixtures retain the codec's mandatory `namespace`,
`artifactId`, `documentId` and `kind` headers. Each fixture round-trips its actual
Source through the pinned codec before binding it.

## Composition and evidence

From a clean committed Security checkout with the fixed Product object present:

```bash
python3 -B tests/candidates/projectide-draft-rename/compose_and_verify.py \
  --product-head ea339ed0bc094630654262ebb28568a95ba4198f \
  --worktree /absolute/fresh/rename-product \
  --evidence /absolute/fresh/rename-evidence
```

The worktree and evidence directories must be fresh and disjoint. The read-only
Ubuntu and Windows advisory pins the new C# and runner Git blobs. Its immutable
test head also binds the workflow; the runner records that workflow blob.
The workflow selects the Product SDK and runs the original .NET restore,
strict Release xUnit command and compiled discovery on each OS separately.

The runner retains the raw inserted C# bytes, Product/Test identities, source,
runner and workflow blobs, composed tree, actual SDK output, restore/test logs,
original TRX, compiled discovery and hashes of retained files. At initial
composition and after restore, test and discovery it checks regular source
bytes, the staged source blob, Product HEAD and the complete tracked delta.

Every local Git subprocess, including the raw source read, excludes inherited
`GIT_*` environment variables. Repository, worktree, index, object-store and
configuration overrides therefore cannot redirect these checks to another
checkout. Ordinary host and SDK variables remain inherited, and non-Git
commands retain their original environment.

Native acceptance requires exactly the frozen 24 fully qualified identities
under the exact class and TRX namespace. Definitions must use the exact class
and bare method names; results, definitions and entries must have a complete
one-to-one test/execution-ID binding. Failed, skipped, missing, duplicate,
substituted and foreign cases are refused. Compiled discovery must match that
same roster without shortening raw names.

Local Python/parser/composition checks are only verifier controls. This
checkout does not supply a local .NET execution result. Native execution and
current-head required CI remain separate gates, and a successful configuration
or synthetic TRX is never counted as a native test pass.

Before publication, local controls accepted the complete synthetic TRX and
discovery roster and refused 32 TRX mutations, seven discovery mutations and
two wrong Product pins. Actual detached composition against the fixed Product
accepted the original bytes before and after seven refusals: changed working
bytes, a source symlink, a source directory, a changed index blob, tracked
Product drift, changed Product HEAD and a missing staged source. The resulting
composed tree was `99826bc963678a787060e9bea94ac15d4b784175`. Python syntax,
both workflow jobs, source/runner pins and the 24-source-method roster were
also checked. These counts describe local verifier controls only.

The Git-environment follow-up used actual Git with fresh local checkouts of the
fixed Product. Three formerly admitted drift cases (repository/worktree,
worktree-only and alternate index redirection) were refused by the updated
runner. Two `core.worktree` configuration-override cases stayed refused on this
host. Clean composition and exact raw-source reads remained valid with hostile
common-directory, object-directory and alternate-object variables; host/SDK and
non-Git command environments stayed intact. The C# blob and fact roster did not
change. These are verifier controls, with no .NET execution result.

The original Ubuntu advisory run `44118464426` failed compilation with CS1503:
the declaration helper passed a Compiler input manifest to the public
`PlanDeclarationReader.Read(ProjectSource)` overload. The helper now passes
that input's exact `Source`; all 24 facts and assertions remain unchanged.
The original failed evidence is retained. Fresh hosted verification of the
corrected C# blob remains pending; no local .NET result is claimed.

Run `44119507599` then reported 23 passing facts and one failed opaque-object
fact, plus analyzer error xUnit2031. The public Source factory stores supplied
opaque metadata inside `extensions`; the fact now asserts that nested fixture
value and preserves the entire before/after `extensions` object. The declaration
helper uses the predicate overload of `Assert.Single`, retaining its exact
single-match requirement. The 24-fact roster and Product/runner pins remain
fixed. Both failed runs remain historical evidence; fresh hosted verification
of this correction is pending.

## Qualification limits

Every receipt retains these six independent fields as `false`, including after
all selected native tests pass:

- `requiredCheckQualified`
- `independentReviewComplete`
- `nativeUiQualified`
- `productionQualified`
- `trustQualified`
- `releaseQualified`

The actual `LocalNetCompilerHost` supplies local compilation responses for the
ViewModel controls. Those responses establish no remote caller identity,
persisted session or permission. Synthetic materialization admission represents
caller-supplied local claims; an immutable roster or matching digest grants no
authority.

Issue #1930 remains open for current actor/session/Tenant/Project/Branch/Draft
authorization and revocation, server recomputation of an admitted modifying
plan, durable atomic persistence, concurrent drift, payload-bound idempotency,
audit/Outbox and failure/cancellation at the actual write boundary. Broader
semantic/runtime coverage and actual Avalonia/Native UI remain unqualified.
This candidate does not close #1030 or #906. Main, Registry, RLS, Trust,
Eligibility, Native, Production and Release holds remain unchanged;
#1144 remains BLOCKED and #1128 remains SUSPENDED/NOT_EVALUATED.

Rollback is a reviewed revert of the applicable candidate changes on
`SecurityTestImplimation`, retaining original failed and incomplete evidence.

## Bounded #2095 admission follow-up

The current runner preserves the original 24 C# facts, their source blob and
immutable Product. It refuses nested TRX results even when the outer records
remain complete and Passed, plus contradictory `ErrorInfo` or non-informational
run-level `RunInfo` outcomes. Existing exact identities, counters, definition
bindings and compiled discovery remain required.

Source checks now inspect raw NUL-delimited Git records, reject
`assume-unchanged` and `skip-worktree`, and compare staged and unstaged deltas
separately. They enumerate ignored as well as ordinary untracked files.
Before restore no extra input is admitted; afterward only `bin/` and `obj/`
roots of projects committed in the immutable Product are allowed. Arbitrary
nested directories with those names and ignored `data/` source files fail.
Every restore, test and compiled-discovery invocation disables ancestor
`Directory.Build.props` and `Directory.Build.targets`. The frozen Product
contains neither file, so no committed Product import is suppressed.

`test_compose_and_verify.py` supplies 27 portable regression methods. They
verify the original C# blob/roster, mutate synthetic TRX and discovery, exercise
real disposable Git index masks and index-only drift, and test ignored inputs
and allowed output roots. A real SDK probe shows both poisoned parent imports
failing unprotected restore/test/discovery and being excluded by the guarded
commands on a separate package-free project. It does not compile or execute
any Product candidate. Both advisory jobs pin and execute these controls
separately from the 24 actual hosted C# cases; synthetic and copied reports
receive zero native execution credit. Python runs with isolated imports.

This workflow still records its reviewed Test workflow blob, rather than
claiming complete executing-workflow provenance. Fresh final-head Ubuntu and
Windows native outputs, original ZIP/run/job metadata, independent review and
ordinary CI remain required. These checks do not establish a hermetic build,
runtime authority, Native UI or all of #2095. #1930 and all six false
qualification fields, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Release
NO_GO remain unchanged.

Direct runner and control execution also checks each script's committed blob
and actual unfiltered bytes immediately before isolated Python starts. The
control invocation additionally binds the runner it imports. Three actual
PowerShell regression methods exercise all four host/entrypoint blocks with
working changes, changed commits, Git-normalized CRLF bytes, and local or
PYTHONPATH import shadows. Original pinned canaries must run only after the
specific refused input has been restored; these are bootstrap controls, not
Product execution.
