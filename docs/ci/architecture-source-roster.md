# Architecture source-case inventory — bounded source-only control

## Scope and authority

This Draft provides a small, read-only companion to Architecture Contract Advisory
[#1824](https://github.com/NYGsatoshi/Coglatas/pull/1824). It identifies the
original Architecture `[Fact]` declarations from the fixed project source files.
It is **not** an alternative to ArchUnitNET, the native `dotnet test` run, or the
existing exact TRX verification. No workflow, Required Check, test-selection,
ReSharper, product, scanner, or governance setting is changed.

The `CORE` profile requires the two existing source files and exactly 12 Facts.
The `WITH_NEGATIVE_ORACLE` profile additionally requires
`LayerDependencyNegativeControls.cs` from the unmerged #1824 and exactly 13
Facts. The independent `#1878` composed trial includes the latter file; the
current Security base does not. The two profiles are explicit, so a missing
negative-oracle file is never silently treated as a passing 13-case inventory.

## Source evidence contract

`python3 -B scripts/ci/architecture_source_roster.py --root . --profile CORE`

The bounded parser rejects unexpected `.cs` siblings, malformed or missing source
files, symlinked leaves, unsupported Fact/Theory syntax, wrong namespaces and
classes, missing methods, and duplicate case names. It accepts only the current
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
invalid encodings and symlinks. They qualify only the standalone source parser,
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
