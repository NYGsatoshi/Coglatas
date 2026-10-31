# Core Slice 5: canonical parsing and partial binding

Product-only continuation of #905 after #1614, with independent tests after #1615.
`CanonicalSourceParser` retains exact raw buffers and their distinct digest, rejects
context substitution, and delegates canonical JSON/schema/set normalization to the
existing codec. Invalid JSON and unsupported DSL formats return no substitute AST.
No arbitrary DSL/script translation, migration or heuristic repair is added.

`ProjectSemanticBinder` derives an immutable partial editor model from one locked
CompilerInput. It publishes typed entity/port symbols, exact locations, declaration
and relation views, reference states and deterministic typed findings. Supported
multi-document Project-root and Requirement references bind by scoped identity;
names and file paths never resolve targets. The baseline schema/declaration/relation
rules must be explicitly required. Descriptor interpretation requires its exact
version/digest lock. Unknown Source/declaration/descriptor meaning remains partial.

External symbols require an explicitly pinned snapshot ID/version/content/context
and host authorization. Materialized substitutions are invalid; absent, partially
covered, ambiguous, unsupported and out-of-scope targets remain distinct. Unauthorized
snapshot bodies are not interpreted or indexed. Core does not fetch or authorize
anything. Diagnostics disclose only the local origin and safe invariant codes.

Input and materialized Source traversal share a deterministic logical operation
budget and aggregate declaration bound. Numeric profiles are checked before typed
publication; budget exhaustion preserves Source and yields Unknown. Cancellation
is checked before work and between bounded binding stages. Identical immutable
inputs produce identical supported model findings; no incremental cache is claimed.

This is partial binding, not semantic validation or an execution plan. Relation-
target references, typed value-reference literals, external Source-reference wire extensions, named structural/
recursive descriptors, package/extension loading, DSL import and unsupported
calendar/evidence semantics remain explicit Unsupported. Slice 6 owns graph/semantic
validation and derived IR; Slice 7 owns actual supported rules. A complete binding
result alone is not a Compiler Pass or merge authority.

Current tests cover canonical invalid/unsupported buffer recovery, cross-document
symbols, complete/incomplete absence, exact external pins, authorization, ambiguity,
unknown declarations/schema, baseline omission, numeric/logical budgets, unmapped
origins and cancellation. Exact Product/Test/combined identities and CI receipts
are recorded on paired Draft PRs; standalone Test dependency failures are retained.

No protected branch push/merge, Required Check relaxation, dependency, production,
RLS/SEC-ARCH/Shadow/Enforce or Student Council decision change. Angular retained;
new Web UI NOT_STARTED and Windows/Avalonia BLOCKED by #1144. #1128 remains
SUSPENDED / NOT_EVALUATED. Source review/CODEOWNER/integration acceptance are pending.
