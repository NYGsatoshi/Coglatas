# Core value-reference binding: pinned snapshot slice

Parent #905; approved A-R2 MODEL-10, COMP-01/03/04/07, INDEX-07 and PEE-04.
The controlling parallel Web/Windows amendment is Coglatas-Spec #300, merged as
`16066f182beef0cd7ce57aa23849922f634d82c7`. These sources require typed scoped
references, fixed manifests and explicit partial/unsupported outcomes; they do
not supply an executable Resource/evidence adapter or a self-manifest convention.

## Contract inventory before implementation

| Existing kind or shape | Defined contract and bounded implementation |
| --- | --- |
| Entity reference | Typed EntityId, expected declaration kind, Tenant/Project, exact context, origin and manifest version/digest. Resolve only against an authorized, exact CompilerInput snapshot pin and materialized Source. |
| Document reference | Typed DocumentId and `coglatas.source-document` expected kind, same context/origin/manifest checks. Document symbols are published only for successfully resolved document values. |
| Resource reference | Typed Tenant-owned ResourceId and declared resource kind. Structural codec only; actual Resource snapshot resolution remains Unsupported. A Project entity/resource-binding is not a Resource. |
| External-snapshot reference | Typed snapshot ID and expected kind. Codec only; a versioned target-kind/receipt contract is unavailable, so binding remains Unsupported. |
| Nested values | Existing list/sequence/set/map values, inline structures, optional and nullable wrappers retain exact declared child types. Traverse decoded Known values; no coercion, recursive descriptors or reference-as-map-key equivalence. |
| Null / Unknown / absent | No target resolution or guessed dependency; Unknown basis is opaque provenance. Input bytes still consume raw processing budget. |
| Named structures, artifact, approval and expression dereference | No descriptor registry, invocation or evaluation semantics are invented. Existing Unsupported status is retained. |
| Local typed self-manifest | Existing local Source has an unversioned document manifest and a whole-Source digest. A reference embedded in Source cannot simply contain that whole-Source digest without a self-hash cycle. Retain `value-reference-unavailable`; owner/schema decision required before a new encoding. Existing Project roots/Requirement/Relation references keep their established wire behavior. |

## Implemented candidate

Task input-port defaults are the currently supported declaration positions that
can contain typed reference values. Visit their decoded immutable value trees,
using PortId, structure field tokens and canonical set/map identities; list and
sequence positions remain ordered. This traversal never interprets opaque
unsupported declaration data or Unknown basis. Parsing already enforces exact
port/default/nested TypeRef equality. Target binding additionally checks expected
entity/document kind. Supplied origin must match the actual declaration scope,
document, entity and any explicit property path; diagnostics use the actual origin.

Entity/document target binding uses the existing authorized external snapshot
manifest convention: pin version and **materialized Source digest**, not a new
manifest hash domain. Context includes exact Tenant/Project/Branch/revision or
candidate/Scenario stamps. Missing authorized complete scope, unknown coverage,
ambiguous duplicate materializations, unsupported target meaning and out-of-scope
remain separate states. Kind/manifest/origin mismatches are Invalid findings;
the existing reference enum uses Unsupported with no symbol for these failures.
No hidden target names or bodies enter diagnostics. Every typed visit shares the
logical operation allowance and cancellation checks.

Successful semantic reference resolution is not external dependency lowering.
The existing `external-closure-not-lowered` validation gate blocks IR, transitive
execution/dependency closure and Complete Pass. No value reference is followed
recursively, so a pointer cycle cannot start unbounded evaluation or silently
become an execution dependency. Existing kind-specific Relation cycle policy is
unchanged. Stable symbols retain scoped typed IDs and Source-backed origins;
no timestamp, generated random identity, network or live lookup is introduced.

## Requirement evidence / Resource materialization assessment

| Concern | Pure Core boundary / remaining runtime work |
| --- | --- |
| Requirement identity | Existing EntityId plus exact Source/input context; no new writable Requirement identity. |
| Target and evidence eligibility | Existing target binding and required rule family remain. Links, true conditions and synthetic evidence do not satisfy a Requirement. `requirement-evidence-unavailable` remains Unknown. |
| Provenance / receipts | Existing pinned input/evidence envelopes represent claims, not actual materialization. No manufactured receipt or observation is added. Authenticated producer, actual snapshot body, schema/version/digest, target/input, coverage/as-of and authorization are required before eligibility. |
| Resource scope / freshness | Resource bindings declare Tenant/ResourceId/version/digest. Actual authorized definitions/allocations, aggregate coverage and freshness are unavailable; `resource-snapshot-unavailable` remains Unknown. |
| Input/output manifest / determinism | Existing full input identity, deterministic result, source map and separate execution/coverage/verdict remain. Missing/Unsupported/exhausted/cancelled work cannot gain Complete Pass. |
| Next slice | Specify a non-self-referential local manifest binding and actual typed materialization receipt/eligibility model with negative tests; real acquisition and authority adapters require their own scope. |

## Consumers and decisions

The #1653 inspection contract already transports distinct reference states and
Source origins. It intentionally lists local symbols only; resolved external keys
must not be guessed into local navigation. Compatibility will be checked on an
isolated combined candidate against shared fixture reproduction, Angular tests and
actual browser journeys plus Windows Pure ViewModel tests. Passing those checks
is bounded synthetic parity, never native Windows or shared-feature completion.

Owner/schema decision required: choose a versioned document/dependency manifest
that excludes self-reference digest fields, or an explicitly pinned external
manifest reference with a dedicated immutable payload/receipt. Neither alternative
is silently promoted to formal v1 contract in this slice. External closure,
Resource/evidence receipts and richer expressions remain separate slices.

Before-fail, exact final Product/Test/combined identities, fresh Windows/Ubuntu
results, inventory and artifact SHA-256 are appended to Draft PR receipts.
Ordinary CI, supplemental success, full review and owner acceptance remain separate.
#1128 remains SUSPENDED / NOT_EVALUATED; #1144 native/activation HOLD is unchanged.
