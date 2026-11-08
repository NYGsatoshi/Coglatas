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

## Classification coverage

Every one of the 475 scoped Markdown paths has one catalogue entry. The
initial lifecycle assessment is:

| Lifecycle | Documents |
| --- | ---: |
| `active` | 106 |
| `candidate` | 81 |
| `historical` | 20 |
| `superseded` | 15 |
| `needs-review` | 253 |

The `needs-review` entries explicitly retain unresolved or mixed scope; they
are not silently treated as obsolete. Candidate classifications preserve the
source's stated adoption boundary, and an active entrance may link to inactive
implementation. These counts describe document roles, not passing tests or
completed features. Recalculate them when refreshing the catalogue.

## Recorded disposition

`Retain` is the current path decision. `Review scope` identifies specific
follow-up work; it is not a move authorization or an assertion that a source
has become obsolete. These entries inherit the source baseline above and
require a refreshed PR-overlap check before implementation.

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
| DOC-M12 | [CI cache strategy](../.github/CI_CACHE_STRATEGY.md) | Needs review; retain path | Its opening describes self-hosted persistent caches, while [the runner-pool guide](ci/self-hosted-runner-pool.md) explicitly scopes its self-hosted material as historical private-operation context and [Main CI](../.github/workflows/ci.yml) uses `ubuntu-latest`. Reconcile the cache guide's audience and runner scope against the actual workflow/cache setup; do not silently discard still-applicable cache guidance. |
| DOC-M13 | [Dependency update fast lane](ci/dependency-update-fastlane.md) | Needs review; retain path | The guide declares Dependabot-only operation and names `.github/dependabot.yml`; the inspected tree instead contains [Renovate configuration](../.github/renovate.json) and lacks that named Dependabot file. Reconcile configured producers, schedules, and constraints with the owning workstream in #1749. Config presence alone does not establish live bot execution or a successful update. |
| DOC-M14 | [Dependency security automation](verification/dependency-security-automation-20261026.md) | Needs review; retain source-bound evidence | The reusable constraints still prescribe a Node 24 Docker baseline, while [Renovate configuration](../.github/renovate.json) explicitly retains approved Node 26 LTS. Reconcile the maintained version guidance with its current source; preserve the earlier verification appendices and their original runtimes. |
| DOC-M15 | [SEC-ARCH preflight](verification/security-architecture-preflight.md) | Needs review; retain effective owner gates | Its implementation walkthrough describes five-assembly reconciliation, while [versioned assembly binding](verification/security-architecture-six-assembly-binding.md) defines version 2 six-assembly scope and preserves version 1 as historical. Reconcile the walkthrough while retaining BLOCKED/Advisory, unresolved mappings, and separate owner decisions. |

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

