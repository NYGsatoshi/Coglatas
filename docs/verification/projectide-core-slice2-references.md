# ProjectIDE Core Slice 2c: reference and value shapes

Status: IMPLEMENTED / ACCEPTANCE_PENDING for the bounded contracts below;
Slice 2 remains partial. Parent #905; authority #888/#798, approved Spec #300
(merged as `16066f182beef0cd7ce57aa23849922f634d82c7`). Product stacks on #1605.
Tests branch from #1604's SecurityTestImplimation lineage; no product files are
copied into that branch. Exact final candidate and combined verification receipts
are recorded in PRs after execution, separately from hosted CI.

## Supported contract

Reference type descriptors declare entity/document/resource/external-snapshot
reference kind and expected target kind. Values carry typed UUID, Tenant/Project,
exact Branch/committed/candidate/Scenario context, source ProjectLocation and
pinned manifest version/digest. Parsing checks structural consistency; explicit
target-context and manifest checks distinguish scope, Branch and revision drift.
None claims that a manifest exists, is authorized, or resolves the target.
Cross-Project references remain explicit pinned inputs; cross-Tenant origin is
invalid. Existing Requirement PlanEntityReference wire contracts remain compatible.

List/sequence preserve ordered typed values. Set/map require the explicit
`coglatas.canonical-value` identity rule: exact canonical tagged Source value
identity, including type and Known/Null/Unknown basis. This is representation
identity, never semantic equality of unknown values or reference resolution.
Set/map wire entries must already be in Unicode scalar order of that identity;
duplicates and unordered representations are invalid and retained. No silent
sorting changes Source. Map keys support known exact scalars only; unknown/null
keys and other identity policies remain Unsupported. Nested value types must
match exactly; no numeric/unit/null coercion is added.

Optional has explicit present/absent payloads; absence differs from Null, Unknown
and an empty list. Nullable uses an explicit nullable descriptor and retains its
element type. Structures declare unique named fields, requiredness and explicit
field types (including nullable/Unknown flags). Optional field omission stays
absent. Undeclared fields, future nested descriptors and extensions remain opaque
with no typed result. Inline structures are finite JSON trees; named structural
references and recursive descriptor binding remain Unsupported until the pinned
Slice 5 registry/cycle contract is available. No arbitrary CLR/script execution.

Provisional bounds are 16 type/value levels, 1,024 elements/fields per container
and 8,192 visited type/value nodes per decode. Bounds apply recursively across the
whole operation. Exceeding them yields Invalid with complete Source data retained;
it cannot become an executable success or a product capacity guarantee.

## Verification and remaining boundaries

Domain Release build passed with zero warnings/errors during implementation.
Independent tests cover reference contexts/manifests/IDs, ordered/nested values,
duplicate identities, type mismatch, nullable/Unknown/optional states, structures,
ports, unsupported preservation, culture and digest stability. Final results are
bound to exact commits in the PR receipt.

CodeRabbit CLI installation on native Windows fails with
`Unsupported operating system: mingw64_nt-10.0-26200`; WSL also lacks its configured
VHD. No CLI review ran. Repository PR review remains separate and may be limited.
No automated review success is inferred.

Full reference resolution, symbol tables, descriptor binding, relations and
Compiler input/result/IR belong to later slices. Collection defaults retain
existing scalar input connection rules; multi-input merge semantics are still
Unsupported. Artifact/approval execution semantics and richer typed expressions
remain unimplemented. No Core or client DONE claim is made.

No API, DB, RLS, runtime Shadow/Enforce, UI, packages, CI/protection, Student
Council release judgment or operational changes. Web UI acceptance NotRun;
Windows/Avalonia remains BLOCKED under #1144. #1128 stays SUSPENDED/NOT_EVALUATED.
No Main/integration/SecurityTestImplimation push or unaccepted PR merge.
