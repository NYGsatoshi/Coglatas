# Local typed Project Tests and pinned-input CLI

This is the first Core/CLI implementation of #1031 under #888 IDE-START-01,
PTEST-01/03/06/07/08/15/19/21/30 and CI-PROJ-01/02/07. It does not complete
#1031 or the paired client acceptance contract.

## Source and supported meaning

A coglatas.project-test declaration, schema version 1, is a first-class
Project Source entity with the existing EntityId, DocumentId, display name and
revision context. Its payload contains an ordered assertions array. Each
assertion has a unique namespaced key, a typed pinned target entity
reference and portId. Assertion locations use
/payload/assertions/{escapedKey}, using RFC 6901 escaping for each complete key
(including slash, tilde and Unicode data), following the existing identity-keyed port
location convention. The canonical Project Source is the only writable AST;
repository files are explicit exports/import inputs, never a second writable
copy. Unknown members/operators/versions remain preserved and unexecutable.

The initial fixture is the admitted immutable Source itself. Assertions read
local Task input-port defaults, without evaluating connected outputs, running
Tasks, loading new dependencies or changing model/operational state:

- coglatas.assert-boolean compares a known Boolean with its typed known
  expected value.
- coglatas.assert-numeric-comparison uses the shared exact numeric Core
  for decimal/rational defaults and expectations. Comparison is
  coglatas.equal, coglatas.less or coglatas.greater.
  There is no floating-point coercion, tolerance, implicit unit conversion or
  new numerical assurance claim.
- coglatas.assert-collection-count compares a known decoded list,
  sequence, set or map's structural count with nonnegative expectedCount.
  It makes no claim about unknown element values or Resource demand.
- coglatas.assert-reference-resolved consumes the existing Compiler
  binding for a reference-valued default. Only resolved is Pass; a known
  missing target in complete scope is Fail. Other resolution states remain
  Inconclusive. Existing local value-reference limitations remain unsupported;
  no alternate resolver or implicit target authorization is introduced.

Compiler binding records each assertion's target port reference using the
existing scope rules and charges its materialized-snapshot lookup work against
the existing logical budget. Ordinary compilation checks Test shape and
references but never executes assertions or imports Test PASS as Compiler PASS.
Draft fork/Rename rebases local Test fixture targets, preserves IDs and pinned
foreign contexts, and reserves unresolved future contexts. Semantic Diff
normalizes local target context without hiding real Test changes.

## Plans, results and bounds

ProjectTestPlan.Compile accepts the immutable binding model and an
explicit host-fixed evaluator artifact identity. A request's evaluator pin is
compared with this binding at execution; it is never accepted as its own host
identity. Plans bind the input, original declaration digests, actual reference
resolution basis, evaluator and limits. Results bind input/Source/plan digests,
assertion locations and separate execution, coverage, verdict, input freshness
and evidence kind. ModelProof here describes a supported local assertion or
counterexample; it grants no Requirement evidence eligibility or Merge authority.

Expected and actual failure values are referenced by canonical digests and exact
Source locations; the admitted immutable Source retains the original values.
The result does not duplicate every potentially large/private value. Persisted
retrieval, confidential export/redaction and current display authority remain
unimplemented runtime responsibilities.

Zero discovery/execution, empty tests, unsupported/invalid Test AST, missing
fixture defaults, unavailable targets, unknown/null actual values, wrong actual
types, stale requested input, evaluator mismatch, cancellation and logical or
artifact bounds cannot become suite PASS. Invalid AST/type mismatches are Error;
unsupported tests and missing fixtures are NotRun. A known failure remains Fail
when other work is incomplete. A successful assertion in partial Source does
not become a complete suite PASS. No wall-clock timeout, skip policy, required
suite selection or durable cancellation behavior is claimed.

A declaration permits at most 64 assertions. A local plan permits at most 256
Tests/1024 assertions; callers may lower these provisional limits. The existing
canonical byte, type/value, exact-number and Compiler logical bounds remain in
force. All assertion attempts share the run-wide operation budget remaining
after Compiler binding; each consumes one operation, including exact numeric
comparison. Exhaustion preserves prior known failures and never yields PASS. Concurrent executions retain no mutable test context or ambient clock,
PRNG, network or file access in Domain.

## Actual local CLI

tools/ProjectIde.Tests is a portable framework-dependent .NET CLI:

    dotnet run --project tools/ProjectIde.Tests -- --evaluator

This prints the exact host evaluator identity, including the Domain assembly's
content digest and assembly version. Export a fixed input with that evaluator
pin using the existing CompilerInputManifest.ToEnvelope() API. Then run:

    dotnet run --project tools/ProjectIde.Tests -- SOURCE_JSON INPUT_ENVELOPE_JSON NEW_RESULT_JSON

The CLI reconstructs CompilerInputManifest.ReadEnvelope; Source, settings,
package lock, pinned dependencies and identity must match. It does not restore
packages, rewrite a lock or treat a cache/artifact digest as permission. Source
and envelope reads are each bounded by the existing 4 MiB default. Output uses
CreateNew: no existing file, Source or manifest is overwritten. Exit codes are
0 Pass, 1 Fail, 2 Inconclusive/NotRun, 3 Error or input/output failure. Early
failures have a distinct CLI-failure envelope and cannot impersonate a bound
Test run. Cancellation is explicit. The CLI provides no remote snapshot loader;
references requiring unsupplied snapshots remain incomplete.

The assembly binding identifies the actual binary, not a trusted attestation or
formal adoption. Different host builds need their own pinned envelope and
qualification; matching test logic alone is not a cross-binary identity claim.

## Verification and remaining work

Developer regressions cover four assertion families, fixed manifests,
round-trip/Compiler separation, Draft/Rename/Diff, partial and unsupported
inputs, failures, empty discovery, missing fixtures, invalid AST/types,
cancellation, budgets, evaluator mismatch and deterministic concurrent reuse.
Actual local CLI checks cover success, failure, Unknown, zero discovery,
unsupported Test AST, mismatched envelopes and existing-file preservation.
Exact-head hosted/native evidence and CodeRabbit review are recorded on the
associated PR; local execution does not substitute for them.

Paired Angular/Windows Test panels, Test editing/import mapping, richer fixture
and Scenario selection, shared expression evaluation beyond defaults, units,
Requirement/Resource/schedule/graph/Simulation assertions, durable jobs, remote
adapters, current runtime authority and policy-bound server Merge consumption
remain open. Independent Security qualification is tracked in P0 #1945 without
modifying SecurityTestImplimation. Actual Avalonia remains blocked under #1144;
#1128 remains SUSPENDED/NOT_EVALUATED. No Native, production or release activation
is granted.
