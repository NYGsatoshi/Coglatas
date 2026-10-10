# SPEC registry and SEC-ARCH traceability

Tracks #835, #836 and the Advisory foundation of #841. Status: mechanical
infrastructure implemented; canonical allocation/interpretation, owner-approved
mapping, complete verifier linkage and hosted exact-candidate qualification
remain pending. **PRE-AVALONIA SEC-ARCH: BLOCKED.**

The repository-owned version-1 schemas are the records in
[`SpecRegistry.cs`](../../tools/Coglatas.SecurityArchitecture/SpecRegistry.cs).
`ContractJson` rejects unknown properties, duplicate JSON properties, unknown
enums, null required values and unsupported constructor shapes. No parser or
production dependency is added. Real statements, source excerpts, authority
references and mappings belong in the private specification repository. Public
fixtures contain only synthetic requirements.

## Identity and authority

`SPEC-ARCH-*`, `SPEC-AUTH-*`, `SPEC-RT-*`, `SPEC-STATE-*`, `SPEC-API-*` and
`SPEC-UI-*` form one repository-wide namespace. Each allocated identity retains
one ordered history, starting at version 1. Allocation occurs through a reviewed
registry diff citing an exact existing normative source. An identity is never a
new requirement simply because it appears in a registry. Explanatory prose,
implementation inventories, diagrams and historical plans do not independently
establish normative authority.

Every version includes source revision/path/optional explicit anchor/SHA256,
an exact source excerpt and its UTF-8 digest, owner, severity, closed verification
classes, explicit known limitations, related `ARCH-*` identities and a change
review reference. `Active`, `Deprecated` and `Retired` are lifecycle states.
Retirement retains its reason and optional registered successors. It removes
neither the identity nor its old versions. Retired identities cannot be reused;
deprecated identities cannot be silently reactivated.

`registry-validate` can compare an independently retrieved baseline with the
candidate. It rejects removal or rewriting of earlier versions, registry-version
rollback and changed allocations without a version increment. The current
conservative rule gives a different normative statement a new identity and
reviewed successor relationship. Whether wording-only corrections may retain
an identity requires owner interpretation before relaxing this rule. The caller
must retrieve the real baseline independently; passing a chosen empty baseline
cannot establish non-reuse across repository history.

The supplied baseline must itself pass the supported registry schema, identity,
source and ordered-history checks before comparison. Invalid baseline diagnostics
use the `SPEC_BASELINE_*` prefix; empty, duplicate, unsupported or malformed
histories do not establish comparison integrity. The baseline's historical
source revision may differ from the candidate's current source revision. These
mechanical checks do not authenticate which baseline the owner approved.

`traceability-transition-check` applies the same retained-baseline validation
while reconciling the registry, contracts, verifiers and optional execution
links. The ordinary `traceability-check` command remains compatible for an
individual snapshot. It does not establish historical allocation integrity.
The baseline is an independently supplied governance dependency; neither a
chosen baseline nor a structurally valid comparison authenticates its approval.

Current obligations pin the independently supplied canonical source revision;
historical versions retain their own immutable revisions. The source commit can
precede the registry review commit, avoiding a self-referential commit hash.
Validation reads exact Git blobs with a 16 MiB size limit and a 30-second process
deadline, not mutable working-tree files. Paths cannot escape a repository.
Each source digest, anchor and statement check uses one byte snapshot. Verifier
digest and declaration checks likewise share their source snapshot. A reader
cannot splice a declaration or statement from a second read into earlier
digest evidence. CLI documents and review-artifact reads have a 32 MiB stream
limit and a 30-second read deadline; oversized inputs produce a sanitized
`INPUT_ERROR` without echoing their path or contents.
Explicit `<a id="..."></a>` or `<span id="..."></span>`
anchors, when used, must occur exactly once. Heading slug inference and line
numbers are deliberately unsupported. Paths and exact excerpts locate source;
owner review establishes normative interpretation.

## Verification and evidence

Closed classes are `archunit`, `roslyn`, `static-custom`, `unit-test`,
`integration-test`, `e2e-test`, `contract-test`, `generated-evidence` and `manual`.
`Blocking` marks an obligation that would block a qualified consuming gate;
it does not promote this Advisory rollout or change Required Checks.
`Advisory` remains visible. `Manual` severity uses only the manual class and
never counts as automatic PASS.

The SEC-ARCH manifest pins the registry's canonical serialization digest/version
and maps each included current requirement version to current security contract
digest and complementary verifiers. Each verifier pins its class, version,
current candidate source blob, declaration identity and distinct execution-link
IDs. All current contract `SpecIds` must resolve through the manifest. Removed,
retired, stale or duplicated mappings, missing complementary classes and changed
contract/source bytes produce deterministic diagnostics.

Supported mechanical declaration identities are fully qualified C# public
test methods and exact script lines `# verifier-id: <identity>` referenced as
`marker:<identity>`. Declaration matching does not prove runnable discovery or
test semantics. Trustworthy discovery/results must also resolve the identity.
The pinned source digest detects changed assertions and other source edits;
rebinding that digest requires review of the manifest's exact new bytes.

Optional execution links bind exact requirement/contract/verifier/source/candidate
identities to existing execution references and digests. Missing, disabled,
nonpassing, skipped/unverified, false `NOT_APPLICABLE` or differently bound
machine evidence fails. Without a links document, structural mode reports zero
executed links and explicit unresolved counts. Manual mappings remain separately
visible. The summary reports candidate/schema/registry versions, family,
severity and verifier-class counts, lifecycle counts, mappings, manual mappings,
passing links, unresolved links and limitation counts.

Passing-link accounting requires supported execution-link schema and valid
structural registry/contract/manifest/verifier source bindings. An otherwise
matching self-reported PASS record cannot retain passing-link credit when those
bindings fail; expected machine links remain visibly unresolved. A changed test
assertion, renamed/deleted declaration, missing source or stale mapping cannot
be hidden by retaining earlier execution metadata. These counters still describe
metadata consistency, not authenticated execution or normative coverage.

Execution-link JSON is metadata. The existing SEC-ARCH producer/TRX and independent
artifact reconciliation remain responsible for actual controls, run/attempt,
build/assembly/environment and trusted producer provenance. The registry does
not replace those controls or authenticate execution from a self-declared link.

## Personal approval reference

Offline registry and traceability results always retain `normativeReady=false`,
`approvalStatus=UNVERIFIED` and `executionAttestationStatus=UNVERIFIED`, even when
their structural diagnostics are empty. A JSON `ownerApproved` field is rejected.
An ordinary code-review approval does not supply SA-02 authority.

`registry-review-check` fetches current GitHub PR metadata, exact selected review,
complete bounded latest-owner decision history, and registry/mapping bytes at the
reviewed commit from the fixed HTTPS GitHub API. The independently supplied
personal owner must have an `APPROVED` review for that exact open, non-Draft head.
The review must contain the dedicated body generated by
`GitHubOwnerReviewVerifier.RequiredApprovalBody`: personal SPEC-registry/mapping
approval, exact reviewed commit and exact artifact digests, with the separate RLS,
activation, exception and gate-promotion holds explicitly retained. The input
reference's registry/mapping digests must match the actual supplied files before
live resolution. Each HTTP header/body read has a 30-second deadline and a 2 MB
response limit. Token lookup uses `GH_TOKEN` or `GITHUB_TOKEN`; neither is printed.

After resolving payloads the adapter re-fetches the head, selected review and
latest-owner decision. Dismissed, superseded, missing-scope or changed-head
approvals cannot qualify. Success is `REVIEW_REFERENCE_BOUND`, covering only that
scoped reference and observed snapshot. GitHub reads are not atomic and a later
revocation can occur; rerun immediately before consuming approval or merging.
Local receipt JSON cannot authenticate itself, a Draft PR is left Draft, and
this tool never submits reviews or changes a PR's approval/status.

## Execution

```text
dotnet run --project tools/Coglatas.SecurityArchitecture -- registry-validate <private-registry.json> <spec-repository-root> <independent-source-SHA> [private-baseline.json]
dotnet run --project tools/Coglatas.SecurityArchitecture -- traceability-check <private-registry.json> <private-manifest.json> <private-contracts.json> <spec-repository-root> <implementation-root> <independent-source-SHA> <exact-candidate-SHA> <as-of-UTC> [private-execution-links.json]
dotnet run --project tools/Coglatas.SecurityArchitecture -- traceability-transition-check <private-registry.json> <private-manifest.json> <private-contracts.json> <independent-retained-baseline.json> <spec-repository-root> <implementation-root> <independent-source-SHA> <exact-candidate-SHA> <as-of-UTC> [private-execution-links.json]
dotnet run --project tools/Coglatas.SecurityArchitecture -- registry-review-check <private-review-reference.json> <private-registry.json> <private-manifest.json> <independent-personal-owner-login> <as-of-UTC>
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "FullyQualifiedName~SecurityArchitectureSpecRegistryTests|FullyQualifiedName~SecurityArchitectureOwnerReviewTests|FullyQualifiedName~SecurityArchitectureCliTests"
```

Existing backend test discovery executes the synthetic positive and deliberate
invalid controls inside the unchanged CI architecture. No new Required Check,
ruleset, scanner baseline, policy, product infrastructure or activation is added.
Private registry execution requires an authorized private checkout and independent
review/source/candidate inputs; public CI must not upload the registry or detailed
private findings. #841's final canonical data/producer/gate integration remains
open until those dependencies qualify under SA-07 and #842/#614.

The [Advisory summary adapter](specification-contract-advisory.md) reuses existing
architecture/backend execution lanes inside their current jobs. Its public
default reports canonical inputs unavailable and qualified coverage unknown;
synthetic fixture counts cannot fill that dependency.
