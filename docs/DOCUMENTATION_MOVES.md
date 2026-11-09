# Documentation disposition and move ledger

This ledger records concrete organization decisions and unresolved work.
Read [the maintenance procedure](DOCUMENTATION_MAINTENANCE.md) and
[the catalogue](catalog.yml) before proposing a path change.

## Baseline and completed scope

The documentation integration branch starts from
`b82e34878f94d78f39e72c1076db65e54004de82`, whose Markdown inventory contains
470 paths. The three navigation indexes introduced by
[PR #1795](https://github.com/NYGsatoshi/Coglatas/pull/1795) add reader routes;
[PR #1796](https://github.com/NYGsatoshi/Coglatas/pull/1796) corrects verified
guidance and moves AI task navigation earlier within its existing file.

Both PRs are normally merged into `docs/integration-20261107` at
`f4a8c7577a757a68cd5472cab548b78534229e69`, with 473 Markdown paths. Adding
this ledger and the maintenance guide brings the DOC-03 catalogue scope to
475 Markdown paths. `docs/catalog.yml` is metadata, outside that `.md` count.

DOC-03 adds the catalogue, maintenance guide, and this ledger. Its complete
path inventory does not settle every content classification. Use
`needs-review` and the basis recorded in each entry to find remaining work.
No original document is physically moved, deleted, or newly archived by these
initial organization changes.

## DOC-04 validation checkpoint

[PR #1811](https://github.com/NYGsatoshi/Coglatas/pull/1811) implemented the
committed-tree reference/catalogue checker, independent controls, fixed
dependencies, qualification workflow, and operating guide on the test
workstream. It normally merged into `SecurityTestImplimation` as
`1dc64c38390208e45c4496c685cd4c37de00607c`. The documentation branch records
this result without importing the test branch's contents.

| Qualified input | Identity |
| --- | --- |
| Checker PR head | `78206c0ecf9b934d302ece7a6fd502b314363ece` |
| Checker head and merge tree | `4581f78ae13f161c929b88fefb563276bc6ff47c` |
| DOC-03 corpus and trusted retention baseline | `f849313aecf480948a6fe9176d73a490f2bec93f` |
| Documentation corpus tree | `9001024215a6cf2dae75123296e9c020c21b538e` |

[Qualification run 42429664858, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/42429664858)
passed 48 independent positive/negative controls and the complete pinned
corpus: 475 Markdown documents/catalogue entries, 779 local references,
24 validated fragments, 122 companion references to 118 files, 33 protected
documents, and 38 retained immutable files, with zero findings. A separate
navigation-only run checked the 451-document checker branch; it did not
claim catalogue or protection coverage for that different source.

All six registered required contexts passed on the exact checker PR head.
[PR CI 42429664893/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/42429664893)
passed all 17 jobs, including the four Functional domains and actor journey;
[Publication Readiness 42429664980/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/42429664980)
passed its owning trust-boundary checks. The
[exact-merge qualification 42430210440/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/42430210440)
also passed. These results retain their separate checker/corpus identities
and do not grant product, numerical-assurance, or owner-approval credit.

This checkpoint adds no physical moves, deletions, or archive admissions.
The classifications and unresolved dispositions below remain in force.
For each next move, follow the [maintenance procedure](DOCUMENTATION_MAINTENANCE.md#procedure-for-a-documentation-change)
and run the qualified checker against that move's own committed candidate
and trusted ancestor before recording its result. The initial corpus receipt
does not substitute for candidate-specific validation.

## DOC-05 guidance reconciliation

This batch resolves three specific content conflicts against documentation
base `74ab2a603b499fcd9319046bb21664f5f9b33aa3`. It retains each existing
path and separates maintained instructions from the historical material they
still explain. The [coordination snapshot](#doc-05-coordination-snapshot-2026-11-09-0046-jst)
records the observed branch identities and PR overlap.

| Disposition | Reconciled scope | Retained boundary |
| --- | --- | --- |
| DOC-M12 | Cache locations, invalidation and cleanup are explained according to the checked-in workflows and host/container lifetime. | The private self-hosted runner context remains historical; a cache directory or volume does not establish persistence across GitHub-hosted jobs. |
| DOC-M14 | Reusable dependency guidance follows the configured Node 26 Docker baseline. | The 2026-10-26 npm audit appendix, its Node 24 runtime, original false-green evidence, digests and issue qualifications remain source-bound history. Configuration is not proof of a fresh hosted bot or scanner run. |
| DOC-M15 | SEC-ARCH reconciliation distinguishes version 2 six-assembly binding from version 1 historical five-assembly evidence. | All fifteen exit conditions, original outcomes, `BLOCKED`/Advisory status and separate owner approvals remain in force. Historical bytes do not acquire version 2 qualification. |

The catalogue's inventory provenance advances to this 475-document base,
with no Markdown additions declared for DOC-05. The earlier DOC-03 baseline
and its two additions remain recorded above.

These three catalogue records become `active` for their maintained guidance
or contract purpose. This does not change product acceptance or approval
state. Other unresolved records remain `needs-review`; the change adds no
physical moves, deletions or archive admissions.

Qualify this batch's exact committed candidate with the DOC-04 checker and
the independently selected trusted baseline above, and record the result in
the implementing PR. Retain catalogue coverage, local targets/fragments,
companion assets, fixed paths and immutable bytes. The earlier DOC-04 receipt
does not qualify this later tree, and local integrity checks do not establish
external URL reachability or new product execution evidence.

## Classification coverage

Every one of the 475 scoped Markdown paths has one catalogue entry. The
initial lifecycle assessment and the DOC-05 reconciliation counts are:

| Lifecycle | Initial DOC-03 | After DOC-05 |
| --- | ---: | ---: |
| `active` | 106 | 109 |
| `candidate` | 81 | 81 |
| `historical` | 20 | 20 |
| `superseded` | 15 | 15 |
| `needs-review` | 253 | 250 |

The `needs-review` entries explicitly retain unresolved or mixed scope; they
are not silently treated as obsolete. Candidate classifications preserve the
source's stated adoption boundary, and an active entrance may link to inactive
implementation. These counts describe document roles, not passing tests or
completed features. Recalculate them when refreshing the catalogue.

## Recorded disposition

`Retain` is the current path decision. `Review scope` identifies specific
follow-up work; it is not a move authorization or an assertion that a source
has become obsolete. Initial entries inherit the source baseline above; the
three DOC-05 dispositions use the separately recorded reconciliation baseline.
Further implementation requires a refreshed PR-overlap check.

| ID | Existing document or group | Current disposition | Concrete follow-up and completion condition |
| --- | --- | --- | --- |
| DOC-M01 | [AI context](AI_CONTEXT.md) | Retain entrance; review mixed responsibilities | Keep task routes and effective constraints at the entrance. Link detailed historical executions to their existing source records only after identifying every unique requirement and active restriction. The initial navigation relocation is not a full ledger extraction. |
| DOC-M02 | [Testing](TESTING.md) | Retain CI-bound path; review scoped extraction | Separate reusable execution guidance from dated results where that reduces duplication. Account for its compatibility-workflow trigger and existing anchors before any extraction. |
| DOC-M03 | [API contracts](API_CONTRACTS.md) and [detailed contracts](contracts/) | Retain; review ownership | Identify the maintained home of each repeated contract and preserve compatibility/error semantics. Extract one coherent contract only after checking implementation and inbound references. |
| DOC-M04 | [Security model](SECURITY_MODEL.md) and [known issues](KNOWN_ISSUES.md) | Retain; review mixed status records | Preserve the distinction between implementation, open issues, dated qualification, and approval. Maintain CODEOWNERS/governance references for the security model. A dated failure or HOLD is not removed solely because newer work exists. |
| DOC-M05 | [Task compatibility](TASK_V1_PR01_COMPATIBILITY.md), `TASK_V1_PR02.md`, `TASK_V1_PR04.md`, `TASK_V1_PR05.md`, `TASK_V1_PR06.md`, and `TASK_V1_PR07_PLAN.md` | Retain; classify lasting contracts and plans | Inspect each document's actual scope. A numbered PR filename does not establish historical-only purpose. Retain unique acceptance criteria and explicitly identify any successor before archival. |
| DOC-M06 | [Gantt ledger](verification/task-v1-pr06-gantt.md) and [PR07 gap audit](verification/task-v1-pr07-gap-audit.md) | Retain source chronology; review current guidance separately | The Gantt ledger preserves a temporary overflow contract alongside merge-time history; the gap audit preserves an original NO-GO and later owner-decision context. Keep these distinctions and source identities when adding a current summary. |
| DOC-M07 | `docs/verification/functional-ci/*`, `docs/ci/functional-compose-harness.md`, `docs/migration/avalonia/*`, and the exact workflow-bound paths in [the dependency table](DOCUMENTATION_MAINTENANCE.md#recorded-protected-paths-and-consumers) | Retain fixed paths | A future migration must preserve routing/trigger semantics and verify relevant consumers. Directory naming alone is insufficient grounds to relocate them. |
| DOC-M08 | [Generated governance view](../governance/controls.md), campaign evidence, and the [P4 owner packet](verification/p4-isolated-environment-owner-packet-20261018.md) | Preserve generated/subject integrity | Classify and navigate externally. Apply the existing owning generator or evidence-revision contract if a substantive change is required; retain original protected bytes during organization. |
| DOC-M09 | [Release gate](verification/student-council-release-gate.md), readiness JSON, and other verification/evidence bundles | Retain source-bound records | Keep dates, commits, runs, attempts, outcomes, and approvals together. Check JSON/log/image/archive companions before any future move. Later success does not replace earlier failure records. |
| DOC-M10 | [Numerical-assurance suspension](../performance/ASSURANCE-SUSPENSION.md) and [U-22 fixture guide](u22/demo-data.md) | Retain governing purpose | The suspension explicitly has no automatic expiry; the fixture guide is test-only guidance. Neither is eligible for automatic archival based on its date or surrounding package name. |
| DOC-M11 | [Existing archive](archive/README.md) | Retain current categories and historical scope | Add new entries only after successor or bounded-history review, consumer checks, asset accounting, and deliberate old-link treatment. Existing archive placement does not resolve every outstanding obligation described in a record. |
| DOC-M12 | [CI cache strategy](../.github/CI_CACHE_STRATEGY.md) | Retain active guide; DOC-05 scope reconciled | Current cache behavior is tied to checked-in workflows and explicit host/container lifetimes. Keep [the runner-pool guide](ci/self-hosted-runner-pool.md)'s historical private-operation boundary and recheck implementation before changing persistence or cleanup guidance. |
| DOC-M13 | [Dependency update fast lane](ci/dependency-update-fastlane.md) | Needs review; retain path | The guide declares Dependabot-only operation and names `.github/dependabot.yml`; the inspected tree instead contains [Renovate configuration](../.github/renovate.json) and lacks that named Dependabot file. Reconcile configured producers, schedules, and constraints with the owning workstream in #1749. Config presence alone does not establish live bot execution or a successful update. |
| DOC-M14 | [Dependency security automation](verification/dependency-security-automation-20261026.md) | Retain active guide and dated evidence; DOC-05 scope reconciled | The reusable Node Docker constraint follows [Renovate configuration](../.github/renovate.json). Keep original audit runtimes and outcomes within their dated scope; require fresh execution evidence for bot, scanner or runtime qualification. |
| DOC-M15 | [SEC-ARCH preflight](verification/security-architecture-preflight.md) | Retain active contract; DOC-05 scope reconciled | The walkthrough now follows [versioned assembly binding](verification/security-architecture-six-assembly-binding.md). Preserve version 1 evidence, all fifteen exit conditions, BLOCKED/Advisory, unresolved mappings and separate owner decisions when updating the contract. |
| DOC-M16 | [Acceptance prebuilt-image reuse](verification/acceptance-prebuilt-image-reuse-20261019.md) | Retain historical repair/report; DOC-06 classified | Preserve original provenance-gap audit, bounded local controls and pending candidate checks. The later receipt-retention failure confirms this is not a current completeness attestation; current v1/v2 requirements remain in [runtime image identity](verification/runtime-image-identity.md). |
| DOC-M17 | [Acceptance image receipt retention](verification/acceptance-image-receipt-retention-20261019.md) | Retain failed historical audit; DOC-06 classified | Keep Main 39150862060/1's three-of-four receipt failure and original repair evidence; distinguish classic schema v1 from conditional proof-bound v2 only by referencing current Main validator and active runtime identity contract. Do not upgrade either audit to a current hosted PASS. |
| DOC-M18 | [Real-backend P0 authorization handoff](verification/real-backend-p0-authorization-handoff.md) | Retain historical handoff log; DOC-06 classified | The final exact-Main 37838083775/1 qualification closes the documented candidate chronology but not independent dependency/release gates. Current Test-only probe scope belongs to [security model](SECURITY_MODEL.md) and implementation. |
| DOC-M19 | [Real-backend Compose cleanup exit](verification/real-backend-compose-cleanup-exit.md) | Retain historical issue repair; DOC-06 classified | The #1601 candidate record has synthetic child-event evidence, not fresh hosted Docker/OS proof. Current behavior is in [the Compose adapter](../tests/ui/run-real-backend-smoke-compose.mjs), Node controls and [harness guide](ci/functional-compose-harness.md); retain all original failure precedence and authorization limits. |
| DOC-M20 | [SDK image digest coordination](verification/sdk-image-digest-coordination.md) | Retain historical digest-drift repair; DOC-06 classified | Keep original same-version digest mismatch and test-only negative mutations. Current canonical pin is [the Dockerfile](../Dockerfile) and the [image parity guard](../scripts/ci/test-dependency-update-policy.rb); do not convert pin parity into runtime or recovery approval. |
| DOC-M21 | [MBJ-02 named PostgreSQL receipt](verification/mbj02-named-postgresql-receipt-20261026.md) | Retain historical receipt repair; DOC-06 classified | Preserve initial eight-fact source proof, the original seven failed synthetic fixture cases, and later corrections; current receipt authority is [the producer](../scripts/ci/mbj02-trx-receipt.py), wrapper and licensed workflow. `VERIFIED` is not wrapper `PASS`. |

## Branch and PR coordination

Maintain document ownership across `docs/integration-20261107`, `main`,
`SecurityTestImplimation`, and `projectide/integration-20261031`. A document
exclusive to a candidate branch remains a candidate-branch record until its
own integration decision. A branch with fewer Markdown files does not prove
that missing Main documents were intentionally retired.

Before implementing a move, record the observed branch heads and the head/base
SHA of every open PR that changes the path or its live consumer. Recheck those
identities immediately before publication. Resolve overlap with the owning
workstream or leave the current path in place. A dated overlap inventory is
coordination evidence, not a promise that the PR list is still current.

### Coordination snapshot: 2026-11-08 01:16:54 JST

| Owning branch | Observed commit |
| --- | --- |
| `docs/integration-20261107` | `f4a8c7577a757a68cd5472cab548b78534229e69` |
| `main` | `9cf3ed8eec0d46ebe0d841a533dee097ec007a4d` |
| `SecurityTestImplimation` | `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f` |
| `projectide/integration-20261031` | `7ada4c6f52e1dd5b4407de11e55bb521dce9eff7` |

The open-PR snapshot contains 57 other PRs, excluding the integrated #1795 and
#1796. Of those, 31 change 50 distinct Markdown paths. Only four of those
paths already exist in the 473-file documentation baseline; the other 46 are
proposed additions outside this catalogue's present source scope.

| Existing path affected | Open PR and observed target | Observed PR head | Disposition |
| --- | --- | --- | --- |
| `deploy/gcp/README.md` | [#1626](https://github.com/NYGsatoshi/Coglatas/pull/1626), `main` | `abe6d99d8be1789f2f6898451a6fd622acaaa6c7` | Retain path; recheck deployment work and policy consumers before editing or moving. |
| `docs/ci/dependency-update-fastlane.md` and `docs/ci/github-action-pinning.md` | [#1749](https://github.com/NYGsatoshi/Coglatas/pull/1749), `SecurityTestImplimation` | `fe059005a9e97af3f28db68120331b5b8a983e99` | Retain paths; coordinate with the owning test/CI workstream before moving either guide. |
| `docs/verification/functional-ci/authorized-thread-projection-diagnostics.md` | [#1378](https://github.com/NYGsatoshi/Coglatas/pull/1378), `main` | `91954aa3f91a407f7a5c4531d6e9514fe17bd55a` | Retain path, source attribution, and routing/typecheck consumers; recheck the candidate's own decision state. |

All three listed PRs were Draft in this observation. Their current lifecycle
and merge permission remain governed by the owning workstream. A path that
does not appear in this table still needs a complete consumer and overlap
search before a move; this table reports direct Markdown-path overlap only.

### DOC-05 coordination snapshot: 2026-11-09 00:46 JST

| Owning branch | Observed commit |
| --- | --- |
| `docs/integration-20261107` | `74ab2a603b499fcd9319046bb21664f5f9b33aa3` |
| `main` | `ad77fd995ac4d01d892183708964ea00c86c24c0` |
| `SecurityTestImplimation` | `1dc64c38390208e45c4496c685cd4c37de00607c` |
| `projectide/integration-20261031` | `aeee28f04529a232518281cd83811aa5cfaf57e8` |

The refreshed inventory covers all changed-file pages for 57 open PRs.
None directly changes the DOC-05 cache, dependency-security automation or
SEC-ARCH preflight guides, the catalogue, or this ledger. This is a direct
path-overlap observation, not a claim that their implementation sources have
no pending changes. The reconciliation uses the inspected committed source;
pending workflow, toolchain or verifier proposals are not adopted by updating
documentation.

[PR #1749](https://github.com/NYGsatoshi/Coglatas/pull/1749), observed at
`fe059005a9e97af3f28db68120331b5b8a983e99` against
`SecurityTestImplimation` base `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`,
still owns changes to `docs/ci/dependency-update-fastlane.md` and
`docs/ci/github-action-pinning.md`. Keep those guides with that workstream.
Recheck branch and overlapping PR identities before publishing a later batch.

### DOC-06 coordination snapshot: 2026-11-09 JST

| Owning branch | Observed commit |
| --- | --- |
| `docs/integration-20261107` | `6f6cc7495921887c5c14ce043f03e8412666919c` |
| `main` | `508f5a565b4bf6d6942163fb1447eaff9b241ecb` |
| `SecurityTestImplimation` | `1dc64c38390208e45c4496c685cd4c37de00607c` |
| `projectide/integration-20261031` | `aeee28f04529a232518281cd83811aa5cfaf57e8` |

All changed-file pages were inspected for 56 open PRs. None directly edits
the three DOC-06 report paths, this catalogue or this ledger. Separate
unmerged source proposals touch image reuse or its licensed workflow:
[#1599](https://github.com/NYGsatoshi/Coglatas/pull/1599),
[#1633](https://github.com/NYGsatoshi/Coglatas/pull/1633),
[#1502](https://github.com/NYGsatoshi/Coglatas/pull/1502) and
[#1456](https://github.com/NYGsatoshi/Coglatas/pull/1456). Their proposed
behavior is not adopted as documentation authority. The current source
comparison for classic schema v1 and conditional binding schema v2 uses
the inspected `508f5a565b4bf6d6942163fb1447eaff9b241ecb` Main verifier, shell helper,
licensed acceptance workflow and existing active runtime-image-identity
contract. This is classification/navigation only, not a physical move,
archive admission, replay qualification or approval change.

### DOC-06 evidence-chronology coordination snapshot: 2026-11-09 JST

| Owning branch | Observed commit |
| --- | --- |
| `docs/integration-20261107` | `6a12a6416910b11af12d2157581234c34adc1c55` |
| `main` | `54e63db2efc7030f3f08b9d85bb95e9ad94ab5ae` |
| `SecurityTestImplimation` | `1dc64c38390208e45c4496c685cd4c37de00607c` |

The all-pages changed-file inventory covered 57 open PRs. None directly
modifies the three source reports, this catalogue, or this move ledger.
Related but unadopted source proposals exist: Dockerfile changes in #1826,
#1787 and #1556; Compose adapter changes in #1632 and #1633;
MBJ-02 receipt producer and SDK pin guard changes in #1556;
and licensed acceptance workflow changes in #1633, #1502 and #1456.
These candidates are not imported. Current source observations are pinned
to `54e63db2efc7030f3f08b9d85bb95e9ad94ab5ae`. Each document's original evidence remains
source- and attempt-bound; this classification is not a permission to
archive a path or to apply a held code/test candidate.
### Pending Main synchronization

The documentation base intentionally retains its initial product/test
baseline. Record subsequently added Main documents here until they exist on
the documentation branch; do not put absent paths into the active catalogue
or create relative links to files that have not been imported.

The following exact-source additions await a future deliberate baseline
synchronization. Recompute the inventory and add them to the verification
index and catalogue when their owning content is available locally.

| Observed Main | Document and source | Disposition |
| --- | --- | --- |
| `9cf3ed8eec0d46ebe0d841a533dee097ec007a4d` | [Post-repair PostgreSQL migration roundtrip](https://github.com/NYGsatoshi/Coglatas/blob/9cf3ed8eec0d46ebe0d841a533dee097ec007a4d/docs/verification/postgresql-postrepair-migration-roundtrip.md), introduced by [PR #1794](https://github.com/NYGsatoshi/Coglatas/pull/1794) | Pending baseline synchronization; keep current source-specific evidence intact. |
| `9cf3ed8eec0d46ebe0d841a533dee097ec007a4d` | [Pre-repair PostgreSQL migration roundtrip](https://github.com/NYGsatoshi/Coglatas/blob/9cf3ed8eec0d46ebe0d841a533dee097ec007a4d/docs/verification/postgresql-prerepair-migration-roundtrip.md), introduced by [PR #1797](https://github.com/NYGsatoshi/Coglatas/pull/1797) | Pending baseline synchronization; import and index only through its deliberate owning-content integration. |

## Record for an executed move

No physical moves have been executed in this ledger. Add a separate record for
each coherent move after the applicable validation is ready. Include:

- The owning branch, inspected base, resulting commit, and implementing PR.
- Exact old and new paths, lifecycle basis, and a concrete successor when relevant.
- Live consumers and companion assets, with refreshed open-PR overlap.
- The treatment of old paths, heading anchors, and source-qualified historical references.
- Actual validation results, protected-byte comparison, and unresolved exceptions.
- The inverse move or revert procedure and any compatibility pointer to retain.

Keep original outcome and approval records reachable. Update the catalogue,
affected indexes, and this ledger in the same reviewed move.

