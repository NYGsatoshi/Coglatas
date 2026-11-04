# Requirement Evidence and Resource Pure Core foundation

Scope: #905/#1034/#1029; Product based on #1657 `48ff196985ddbf90db82386c5413c8f659ea64a7`;
independent Test based on #1658 `8f701e8db3097c74c9a63c30669fbfe36be7bb48`.
Uses A-R2 MODEL-03/10, COMP-01/02/11, ANALYSIS-04/07, SEM-01,
PTEST-09/11 and Q09-B. No private specification text is reproduced here.

## Reused contracts and implemented checks

| Concern | Existing contract / bounded assessment |
| --- | --- |
| Requirement identity | EntityId + DocumentId + exact SourceRevisionContext. Request identifies the declaration itself, not a text span/property/relation or current-name lookup. |
| Target | Existing PlanEntityReference; compare full immutable target data, including Tenant/Project/Branch/revision/candidate/Scenario. |
| Evidence input and identity | Existing CompilerResultId/full digest and CompilerInputManifest; exact complete input comparison rejects scope, revision, compiler/profile/manifest substitution. |
| Evidence kind | CompilerEvidenceKind and declared Requirement evidence kind. Missing/unsupported, wrong kind, human approval and heuristic stay explicit. |
| Provenance | Preserve the original immutable CompilerResult, its rule basis/premise digests and original claim location/target. No receipt/producer authenticity is inferred. |
| Coverage/completeness | Check result and Requirement-rule coverage, execution, verdict and evidence completeness independently. Missing/partial results remain incomplete. |
| Freshness | Compare captured result input to the explicitly supplied desired input. Current means matching input only; it grants no authority or eligibility. |
| Duplicates/ambiguity | Repeated ResultId is invalid; competing distinct result identities are ambiguous. Do not choose the first successful claim. |
| Budgets/cancellation | Use the input logical operation/declaration limits, a provisional 1024-claim cap and cancellation during enumeration/assessment. Null entries consume budget too. |
| Diagnostics | Existing locked required rule/version and CompilerDiagnostic; deterministic codes/order, local desired-input locations, no foreign Project names/content in diagnostics. |

`RequirementEvidenceClaim` is an in-memory association of existing location,
target and result contracts. It defines no serialized format or new authority.
`RequirementEvidenceValidator.Assess` is a caller-invoked consistency helper;
the compiler does not consume these claims as satisfaction. Its `Outcome` always
has Partial coverage, Inconclusive verdict and Missing eligibility basis, even for
a structurally consistent synthetic Complete Pass result. Invalid evidence is not
proof that the Requirement itself is false. Known compiler condition failures
retain their existing semantics independently.

Claims are bounded before deterministic assessment ordering. Interrupted work is
labelled Cancelled/ResourceExhausted, never complete. Original claims are retained
for the authorized caller; they are not a safe public export. A host must apply
view/export protection to those original objects. Findings use only the desired
authorized Source location and stable codes. No IO, wall clock, network or ambient
authority lookup is added.

## Resource boundary

`ResourceReferenceValidator.Assess` compares a typed Tenant Resource reference to
an actual ResourceBindingDeclaration present in the supplied Source. It checks
Tenant/ResourceId, expected ResourceKind, binding identity/data, exact version and
digest, Source origin, context and input coverage, with budget/cancellation.
An EntityId of a Project's binding is not a Tenant ResourceId. Reusable
Human/Equipment/Facility and consumable Material keep their existing distinction.

Every assessment retains Unsupported resolution and Undetermined freshness, plus
`resource-snapshot-unavailable`. A matching reference/binding pin is not a Resource
definition, allocation contribution, materialization receipt or reservation.
Cross-Project context remains explicitly unavailable to this helper; Q09-B is not
replaced with a same-Project-only Resource policy. Existing generic snapshot pins
do not specify the ResourceId-to-body/definition/allocation mapping and cannot be
treated as Resource materializations. No acquisition, authorization or revocation
cache, stock arithmetic, aggregate coverage claim or hard reservation is added.

## Manifest, closure and consumer boundaries

The task selects Option B (separately pinned immutable dependency Manifest and
actual Materialization Receipt). The separate proposal is
`docs/verification/projectide-manifest-option-b.md` on the Manifest decision PR.
Its concrete schemas/domains/reference mapping/producer trust remain unapproved.
Existing external entity/document reference digests still identify materialized
Source; they are not reinterpreted as dependency-manifest digests.

`requirement-evidence-unavailable`, `resource-snapshot-unavailable`, local
`value-reference-unavailable` and `external-closure-not-lowered` remain intact.
Transitive IR lowering requires each dependency's exact approved input/manifest,
authorized body and supported semantic closure. Current reference resolution
cannot prove those premises. No incomplete IR is executable.

No consumer wire/result shape changes. Exact combined verification with Inspector
Product #1653 `d77b9814f96ba593b98b8842cf1d1355cf310885` and Test #1654
`930bb8866cf2b8c3c08a414ffc2aa782072ba443` must reproduce the existing fixture,
Core/Pure ViewModel inventory, Angular build/TypeScript/Vitest/Node and actual
desktop/mobile browser cases. This is synthetic compatibility only; actual
Avalonia Views/native UI/IME/accessibility/activation remain HOLD under #1144.

Verification receipts retain exact Product/Test/base/parent and combined commit/
tree, clean checkout/ownership/equality, complete test identities/outcomes and
artifact hashes. Ordinary PR CI and supplemental CI are independent. Initial
baseline compile failures for absent new APIs are build-time before evidence,
not executed behavioral failures. Latest results are recorded in the Draft PRs
and the existing `projectide-continuation-20261102.md` ledger.

Remaining eligibility: authenticated producer, actual materialized evidence/body,
target-specific supported proof, evidence snapshot binding, current view/apply
authorization, offline availability and approved policy. No synthetic receipt or
metadata boolean can supply these. #1128 remains SUSPENDED / NOT_EVALUATED.
