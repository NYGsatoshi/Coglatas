# Architecture source-case inventory — bounded source-only control

## Scope and authority

This Draft provides a small, read-only companion to Architecture Contract Advisory
[#1824](https://github.com/NYGsatoshi/Coglatas/pull/1824). It identifies the
original Architecture `[Fact]` declarations from the fixed project source files.
It is **not** an alternative to ArchUnitNET, the native `dotnet test` run, or the
existing exact TRX verification. No workflow, Required Check, test-selection,
ReSharper, product, scanner, or governance setting is changed.

The `CORE` profile is valid **only when** the project has exactly the two
existing source files, the negative-oracle file is **absent**, and those files
declare exactly 12 Facts. The `WITH_NEGATIVE_ORACLE` profile requires those
two source files **plus** `LayerDependencyNegativeControls.cs` from the unmerged
#1824, declaring exactly 13 Facts. The independent #1878 composed trial
contains the latter file while the current Security baseline does not.
The exact-file-set check must reject `CORE` on a composed tree containing the
negative oracle and reject `WITH_NEGATIVE_ORACLE` when it is missing.

## Source evidence contract

`python3 -B scripts/ci/architecture_source_roster.py --root . --profile CORE`

Use that command **only** for the original two-file Security baseline.
For the three-file composed Architecture candidate, use:

`python3 -B scripts/ci/architecture_source_roster.py --root . --profile WITH_NEGATIVE_ORACLE`

The bounded parser rejects unexpected `.cs` siblings, malformed or missing source
files, symlinked leaves, unsupported Fact/Theory syntax, wrong namespaces and
classes, missing methods, and duplicate case names. A bounded source read uses
one opened descriptor (no-follow where available), comparing its identity
with the checked file and rechecking descriptor metadata after reading. It accepts only the current
simple top-level `public void Method()` syntax with a directly adjacent `[Fact]`.
Broader syntax requires a separately reviewed parser; this is **not a complete
C# or Roslyn parser**. The source files are not executed by this tool.

Output uses SHA-256, with a distinct domain for each fully-qualified case name,
and a canonical full-roster hash. It also contains a SHA-256 for each source
file. No raw case name or test title is emitted. These hashes are pseudonymous
identities, not digital signatures or authenticated Git producer receipts.

A source-only result has explicit independent false qualification states:

- `nativeCaseIdsVerified=false`
- `artifactProvenanceVerified=false`
- `requiredCheckQualified=false`

A future native admission may compare this roster against independently observed
compiled test-case identities and TRX results, but it must verify that the source
input comes from the same exact tested Git tree, and that producer and artifact
provenance satisfy D10. It must not infer that `caseCount=13` proves the cases
actually ran or that source text matches compiled C# semantics.

## Focused tests and rollback

`python3 -B scripts/ci/test_architecture_source_roster.py`

The isolated fixtures check both declared profiles and reject missing/additional
files, duplicate or added cases, changed namespaces, dynamic test attributes,
invalid encodings, source-file replacement between check and open, and symlinks.
A platform unable to create the symlink must **fail** the guard control rather
than register a positive result from a skipped test. They qualify only the standalone source parser,
not the native Architecture suite. No extra runner or artifact owner is activated.

The existing Governance Identity Drift, current-head CodeRabbit and cross-domain
release/PR-N assurance remain HOLD. Rollback reverts only this companion parser,
its offline controls and documentation from a Security-targeted candidate; it
must retain all earlier native successes, failures, retries and skip records.

```text
PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
NATIVE_EXECUTION_RECONCILED=false
ARTIFACT_PROVENANCE_VERIFIED=false
REQUIRED_GATE_QUALIFIED=false
```
