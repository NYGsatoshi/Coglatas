# Core Slice 4: immutable Compiler envelopes

Implements the pure manifest/result/diagnostic contract scope of #905. Depends on
the supported Slice 3 contracts in #1611; independent tests continue from #1612.
This is not a Compiler pipeline, execution engine, merge policy or UI acceptance.

`CompilerInputManifest` freezes Source context/digest/documents, package lock,
schema/compiler/semantic/IR/evaluator/Ruleset artifact versions and digests,
individual rules/configuration, descriptor locks, authorized snapshot references,
Resource/allocation/observation/evidence coverage, calendars/timezone locks,
capabilities/targets/scope, clock and logical/numeric profile. Missing applicable
fields are invalid. Unordered sets normalize by stable identities; duplicates
are rejected. Source/Scenario bindings and Tenant scope must agree. Engine,
stochastic and importer extensions currently support explicit None only; unknown
fields or unsupported contexts cannot become executable defaults. Pinned reference
integrity does not authenticate the host or authorize snapshot access.

Input envelopes verify supplied IDs/digests against the complete canonical
manifest. Content-derived UUID v8 identities retain full derivation digests.
The derivation tuple is `["coglatas.id/<TypeToken>/v1",1,components]`, encoded
with canonical encoding v1 and SHA-256; UUID bits use network order with version
8 and RFC variant. Type tokens are CompilerInputId, CompilerResultId and
DiagnosticId. Input components are the full InputDigest; result components are
InputId and ResultDigest. Independent Python golden vectors verify the mapping.

Result contracts separate execution, coverage, verdict and evidence completeness.
Every requested required evaluation and declared stage must be covered before
Complete is valid. Zero/missing evaluations, cancellation, timeout, exhaustion,
unsupported/missing basis and heuristic/approval evidence cannot produce technical
Pass. Partial required failure remains Fail. Evidence completeness is derived from
the actual recorded basis rather than an unchecked boolean. Artifact/plan fields
explicitly remain not-produced/not-evaluated at this envelope-only stage.

Diagnostics bind exact Source locations and locked rule versions. IDs use input,
rule/version, semantic location, finding code and discriminator. FindingKey omits
input/revision identity for explicit cross-revision tracking. Severity and message
presentation do not order diagnostics; canonical ordering uses stage/rule/version/
location/code/discriminator/ID. Arguments are bounded typed values. Suppression,
related-location and Quick Fix execution remain explicit empty/None contracts.
No diagnostic grants disclosure authority; host evidence policy is still required.

Freshness is a separate immutable assessment against an explicitly supplied desired
manifest. Complete/Pass/Stale and Partial/Fail/Current are supported combinations.
No clock, current head, network, random identity or operational state is read.
Invocation telemetry and current DecisionContext remain outside semantic digests.

Provisional processing caps are structural safety bounds, not performance/device
guarantees. Measured assurance remains #1128 SUSPENDED / NOT_EVALUATED.
Source parsing/binding, IR production and actual rule evaluation remain Slices 5-7;
host adapters/parity remain Slice 8. Incremental compilation, advanced descriptors,
raw invalid-Draft manifests and non-None importer/solver profiles are not claimed.

Verification uses the independent Test lineage and fresh fixed-candidate worktrees.
Exact current-head receipts and hosted evidence are recorded on the paired Draft
PRs. Source review, CODEOWNER approval and integration acceptance remain separate.
No Main/integration/Test-base merge, package, Required Check, RLS/SEC-ARCH,
Shadow/Enforce, production or Student Council decision changes. Angular is retained;
Web UI NOT_STARTED, Windows/Avalonia BLOCKED by #1144.
