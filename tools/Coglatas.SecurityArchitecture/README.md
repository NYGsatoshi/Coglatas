# SEC-ARCH typed contract tooling

Tracks #1146 and the owner-approved execution/governance scope in #1144–#1152. This BCL-only .NET 10 tool validates a closed version-1 typed document, reports conservative inventory changes and checks evidence metadata against independently supplied candidate/environment/time inputs.

```text
dotnet run --project tools/Coglatas.SecurityArchitecture -- validate <contracts.json> <as-of-UTC>
dotnet run --project tools/Coglatas.SecurityArchitecture -- inventory-diff <baseline.json> <candidate.json> <as-of-UTC>
dotnet run --project tools/Coglatas.SecurityArchitecture -- inventory-diff <baseline.json> <candidate.json> <as-of-UTC> <baseline-registry.json> <candidate-registry.json> <spec-root> <independent-source-SHA>
dotnet run --project tools/Coglatas.SecurityArchitecture -- evidence-check <contracts.json> <evidence.json> <40-character-SHA> <64-character-environment-digest> <as-of-UTC>
```

Commands return deterministic JSON diagnostics, exit 0 for valid metadata and exit 1 for rejected input. Supply an explicit ISO UTC timestamp ending in `Z` or `+00:00`, such as `2026-10-09T10:00:00Z`, to reproduce expiry/freshness decisions. Date-only values, local times and nonzero offsets are rejected. Trusted CI must supply its actual execution time and derive candidate/environment inputs independently of the evidence file.

Ordinary validation results retain `valid` and `diagnostics` and add `status=STRUCTURALLY_VALID|INVALID`, `approvalStatus=UNVERIFIED`, `executionAttestationStatus=UNVERIFIED` and `runtimeSecurityStatus=UNVERIFIED`. Exit 0 means structural metadata validation succeeded; it cannot grant owner approval or runtime acceptance. Registry and traceability commands retain their existing separate assurance fields; `registry-review-check` binds only its exact scoped live review reference.

## Contract boundary

Contracts.cs is the typed schema. API, RLS, SignalR, Kafka and service contracts have exactly one matching policy and explicit flow, identity, operation, scope, expected policy, activation, threat and evidence references. Unknown fields/types/enums, duplicate JSON fields/IDs, missing/null required values, unrestricted wildcards, invalid RLS/Kafka/TLS policy shapes, unapproved conditional metadata and invalid exceptions are rejected. Temporary exceptions require exact contract/resource scope, structural approval metadata and a current lifetime of at most 30 days.

Optional `mitigationReferences` distinguishes mitigation identities from threat references and expected policy. When present it must be nonempty, unique, bounded and free of wildcards or surrounding whitespace. Omitted legacy references remain absent from canonical serialization. Values are relationship claims, not formal approval. Canonical SPEC applicability is a separate private projection dependency; contract activation/activation approval cannot substitute for it or grant an exemption.

An inventory addition, removal or any contract change returns a review-visible rejection. Existing `POLICY_CHANGED`, `SCOPE_REMOVED` and `UNCLASSIFIED_ADDITION` diagnostics remain compatible. Additional diagnostics distinguish contract, principal, requirement-reference, evidence-requirement, activation, approval and exception changes, plus scope expansion and narrowing. Scope identities are opaque: replacing one records both the removed and added identity without inferring semantic containment. This intentionally conservative comparison also flags ordering changes in arrays. The tool does not write a new baseline or interpret a changed policy as approved. The optional history-aware form reuses the separate registry and its existing full pinned-source/retained-baseline validator. `HISTORY_CHANGED` identifies changed requirement allocation/history, and `REGISTRY_CHANGED` identifies changed registry bytes; both require review, including a structurally valid append. The original form compares contracts only. `registry-validate` and `traceability-transition-check` retain their existing transition-validation behavior.

Contract definitions have no verifier result field. Evidence has separate candidate SHA, environment digest, verifier/version, execution reference/digest, normalized expected/observed policy, identity category, UTC timestamp, outcome, controls and exception identity. Optional `contractDigest` binds the exact canonical contract serialization, including principal and scope; when supplied, stale or malformed bindings fail. Legacy records remain readable without this field and do not establish scope binding. Missing, stale, different-SHA, disabled, failed, erroneous or unverified evidence cannot satisfy the metadata check. Manual review cannot become PASS. Conditional product applicability cannot exempt isolated Runtime evidence; runtime records require both positive and rejection controls.

## Assurance limits and remaining work

This tool checks structure and consistency. It does not authenticate an approval reference/digest, attest a claimed execution, fetch a trusted report, verify the report bytes against ExecutionDigest, or prove claimed control counts. A forged internally consistent evidence file can pass structural checks, while its runtime security and execution attestation statuses remain explicitly `UNVERIFIED`. Standalone `evidence-check` has no independent verifier catalogue: nonempty verifier names are claims. Use the existing traceability commands to bind the exact registered contract digest and pinned verifier identity/version/source; even those bindings cannot authenticate execution. Runtime adapters and trusted CI report/receipt reconciliation under #1152/#842/#614 are required before these checks can establish security acceptance or be promoted as assurance. A valid result here is not PRE-AVALONIA PASS.

The versioned SPEC registry and traceability commands implement deterministic identity history, immutable source/anchor, contract/verifier and execution-link validation under #835/#836. See [registry governance and commands](../../docs/verification/specification-contract-registry.md). Canonical allocation and owner-approved interpretation/mapping remain pending; syntax, structural validity and self-declared execution links do not establish normative authority. The initial formal mapping remains private and Draft under SA-02/SA-03. Tests use clearly synthetic IDs and policies, without publishing private specification content.

Schema version 1 and all seven original command forms are retained; history-aware inventory comparison adds an optional form. Enum names preserve existing case-insensitive spelling, while composite enum strings and numeric values are rejected. Stable identities, paths and digests must consume the complete input. The bounded SPEC families include `MSG` and `SEARCH` alongside the existing families; accepting a family does not allocate or approve an identity. Existing contract/registry canonical serialization is unchanged. The optional evidence digest is omitted when absent. Any future breaking schema change requires a new supported version and a separately reviewed migration; validation never migrates files automatically.

This change does not enable product RLS, Kafka, services, Enforce evaluation, new Required Check names or ruleset changes. Product activation and concrete RLS migration/privilege changes retain their separate owner gates.

## Verification

See the [Wave 1 audit and mutation coverage](../../docs/verification/security-architecture-wave1.md) for the requirement classifications, 28 requested negative cases and remaining approval/runtime boundaries.

```text
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter FullyQualifiedName~SecurityArchitectureCliTests
bash scripts/ci/test-route-main-ci-changes.sh
```

The tests invoke all three real CLI entry points with normal and deliberately invalid synthetic inputs, including wrong/stale SHA, disabled verifier, false NOT_APPLICABLE, removed scope, expired exception and malformed/manual-result fields. Existing backend CI discovers these tests; tool-only changes route the SecurityArchitecture test scope. No new Required Check is introduced.
