# SEC-ARCH Wave 1 structural verification

Scope: #1145/#1146, reusing #835/#836 infrastructure and the existing Advisory
#841/#1152 integration. This implementation does not close those broader issues
or qualify runtime security. **PRE-AVALONIA SEC-ARCH: BLOCKED.**

## Gap audit and resulting behavior

| Requirement | Baseline classification | Result |
| --- | --- | --- |
| W1-01 versioned schema | IMPLEMENTATION_REQUIRED | Reuse version-1 contract, registry, history, verifier, evidence and owner-review records; add optional independent mitigation references without changing legacy digests. |
| W1-02 deterministic validation | IMPLEMENTATION_REQUIRED | Reject composite enums, trailing-newline IDs/digests, wildcard service audiences and implicit local times; support only the additional established MSG/SEARCH families. |
| W1-03 inventory diff | IMPLEMENTATION_REQUIRED | Retain existing rejection rules; add specific change categories and optional history comparison through the existing pinned-source registry validator. |
| W1-04 approval/history boundaries | ALREADY_IMPLEMENTED_AND_VERIFIED | Reuse immutable history, exact artifact/source/version bindings and scoped live owner-review resolution. Synthetic relation counts cannot approve mappings. |
| W1-05 evidence | IMPLEMENTATION_REQUIRED | Add optional exact contract digest binding and explicit UNVERIFIED runtime/attestation/approval results. Preserve existing negative evidence checks. |
| W1-06 CLI | TEST_REQUIRED | Preserve seven original command forms, exit codes and existing fields; add only the history-aware form and assurance fields. Inputs remain read-only. |
| Formal canonical projection, SPEC applicability and mapping approval | BLOCKED_BY_APPROVAL | No registry transcription, identity allocation, mapping approval or history rewriting. SPEC applicability is separate from contract activation and needs its approved canonical projection. |
| Runtime acceptance, infrastructure and gate promotion | OUT_OF_SCOPE | No product authorization, DB, migration, role, policy, activation, Required Check or ruleset change. |

Baseline verification ran the four existing structural test classes: 179 passed,
zero failed and zero skipped. New controls extend those classes and
`SecurityArchitectureWaveOneTests`; a failed synthetic uniqueness setup was
corrected without relaxing the validator. Test counts are execution evidence for
these structural controls, not normative coverage or runtime acceptance.

The focused suite adds 51 synthetic cases, including mitigation-reference and sanitized invalid-repository
CLI handling. The existing sanitized TRX producer catalogue now includes
those cases, increasing its explicit expected total from 288 to 339. Catalogue
maintenance preserves existing runtime entries and does not grant runtime
coverage credit to structural fixture results.

The completed focused suite passed 230 tests with zero failures or skips.
The local solution run passed 2,057 backend tests and 12 architecture tests,
with zero test failures and 386 explicit backend skips. PostgreSQL configuration
was absent; skipped and any legacy early-returning database cases provide no
database execution evidence. The first solution attempt lacked the architecture
project's restored assets; normal solution restore repaired that build setup.
The final TRX producer unit suite passed 112 tests, and the existing CI routing
regression script passed. Hosted PR/Main results must be checked at their own
exact commits and cannot be inferred from these local runs.

The first hosted ReSharper check reported four changed-file warnings: three
redundant null guards against non-nullable schema fields and a synthetic record
replacement expressed as an all-member `with`. The repair uses the existing
non-nullable/strict JSON contract and an explicit fixture constructor; all 230
structural controls still pass. The original hosted failure remains in PR history.

## Deliberate-invalid coverage

All cases use synthetic statements, identities, sources, contracts and execution
metadata. Names below refer to tests under `tests/Coglatas.Tests/SecurityArchitecture`.

| Required mutation | Regression coverage |
| --- | --- |
| 1. Duplicate SPEC ID | `DeliberateInvalidRegistryFixturesAreRejectedDeterministically` |
| 2. Duplicate contract ID | `InvalidContractsAreRejectedThroughCli` |
| 3. Duplicate JSON property | `MalformedAndManualPassDocumentsCannotValidate` |
| 4. Unknown field | `MalformedAndManualPassDocumentsCannotValidate` |
| 5. Unsupported schema | `MalformedAndManualPassDocumentsCannotValidate` |
| 6. Statement digest | `DeliberateInvalidRegistryFixturesAreRejectedDeterministically` |
| 7. Source revision | `DeliberateInvalidRegistryFixturesAreRejectedDeterministically` |
| 8. Unauthorized requirement version | `AllocationHistoryAndVersionGovernanceCannotBeBypassed`; `MissingStaleAndSemanticallyChangedVerifierBindingsFail` |
| 9. Missing/invalid approval reference | `InvalidContractsAreRejectedThroughCli`; `LocalApprovalDocumentCannotReplaceLiveAuthority`; `ActiveContractCannotHideMalformedOptionalApprovalReference` |
| 10. Historical replacement | `AllocationHistoryAndVersionGovernanceCannotBeBypassed`; history-aware inventory controls |
| 11. Wildcard expansion | `InvalidContractsAreRejectedThroughCli`; `StableIdentityAndTypedAudienceCannotHideUnboundedSuffixes` |
| 12. Contract removal | `InventoryDiffRejectsRemovedAndUnclassifiedSurfaces` |
| 13. Missing mapping | `MissingStaleAndSemanticallyChangedVerifierBindingsFail` |
| 14. Unapproved mapping promoted | `VersionedSyntheticRegistryValidatesWithoutGrantingNormativeAuthority`; `LocalApprovalDocumentCannotReplaceLiveAuthority` |
| 15. Seven links promoted to forty-one mappings | `SevenSyntheticExecutionLinksCannotApproveFortyOneMappings` |
| 16. Wrong candidate | `EvidenceMutationsCannotCreateFalseGreen` |
| 17. Missing evidence | `EvidenceMutationsCannotCreateFalseGreen` |
| 18. Disabled verifier | `EvidenceMutationsCannotCreateFalseGreen` |
| 19. Fabricated runtime PASS | `FabricatedRuntimePassRemainsStructurallyValidAndSecurityUnverified` |
| 20. Manual PASS | `ManualReviewCannotCountAsRuntimePass` |
| 21. Expired exception | `InvalidContractsAreRejectedThroughCli` |
| 22. Unsupported exemption | `ConditionalProductApplicabilityDoesNotExemptIsolatedRuntime` |
| 23. Unknown/wrong verifier | `ExecutionLinksCannotConflateMissingSkippedOrAnotherCandidateWithPass` |
| 24. Stale contract digest | `InvalidStructuralBindingsCannotKeepSelfReportedPassingLinkCredit`; `PinnedEvidenceCannotSurviveChangedContractScope` |
| 25. Missing positive/negative controls | `EvidenceMutationsCannotCreateFalseGreen` |
| 26. Malformed CLI input | `MalformedAndManualPassDocumentsCannotValidate`; `CliRejectsImplicitOrNonUtcValidationTime` |
| 27. Deterministic output | `InventoryDiffAcceptsSameScopeAndDeterministicallyRejectsRelaxation`; `InventoryChangesExposeSpecificOwnerReviewDiagnostics` |
| 28. Existing CLI compatibility | Existing CLI, registry and owner-review command tests, including `CliReadsPinnedGitSourceFromCleanSyntheticRepositoryAndRejectsForgedApprovalField` |

Additional regressions cover composite nonflags enums, trailing-newline
identities/digests/revisions, bounded families, exact scope replacement/shrinkage,
and unchanged input bytes. History-aware comparison rejects both invalid rewrites
and valid but review-requiring appends. No automatic baseline update is provided.

## Limits and next prerequisites

Source-byte equality, statement digest equality, structural validity, owner
approval, formal mapping approval, execution and runtime security acceptance are
separate claims. The offline tools cannot authenticate self-reported execution,
control counts, verifier names or approvals. Legacy standalone evidence without
`contractDigest` cannot establish a full scope binding. Traceability binds the
registered contract and verifier source/version; trusted execution provenance
remains a separate prerequisite even when those bindings validate.

Formal source/history projection must preserve existing approved revisions and
receive the applicable owner review. Individual relation approval cannot promote
its containing group, and Draft review material cannot become an approved
registry. Contract activation/activation approval is not canonical SPEC
applicability. The native registry does not yet project that private tuple; its
representation remains approval-blocked, and no exemption is inferred from it.
Optional contract mitigation references identify a distinct relationship from
threat references and expected policy, without approving any actual mapping.
Later implementation requires its own authorization and concrete
approved mappings, catalogues, identity/policy contracts and reproducible runtime
evidence. Wave 1 does not provide those approvals.

Existing Required Checks and Advisory status remain unchanged. Rollback is a
normal reviewed revert of the tooling/test/documentation commit; no persistent
data, baseline, registry or operational configuration is migrated.
