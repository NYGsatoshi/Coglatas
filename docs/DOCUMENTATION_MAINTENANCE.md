# Documentation maintenance

Use [the catalogue](catalog.yml) to identify a document's role and recorded
constraints before reorganizing it. Use [the disposition and move ledger](DOCUMENTATION_MOVES.md)
for proposed moves, unresolved classifications, and baseline synchronization.
Reader navigation starts at [the documentation index](README.md).

## Authority and scope

[AGENTS.md](../AGENTS.md) defines the existing authority order: current source,
tests, CI, and root deployment configuration; active status documentation;
the roadmap; then historical archives. Product requirements retain their
separate specification authority. Follow [the language policy](PROJECT_LANGUAGE.md)
and keep private specification content in its access-controlled repository.

The catalogue describes documentation. Its lifecycle values do not establish
implementation, an execution result, owner approval, release readiness, or
permission to activate a candidate. Preserve those claims and their exact
scope in the governing source. An active policy may require a feature to stay
disabled; a historical record may contain a failure that must remain visible.

The inventory is tied to its recorded source baseline. Reconcile additions,
removals, and changed classifications when the documentation branch advances.
Complete path coverage means every scoped Markdown file has an entry; it does
not mean every product claim has been independently verified. A
`needs-review` entry remains unfinished classification work.

## Catalogue fields

`version: 1` identifies the metadata format. The `scope` block records the
pre-change Git commit and tree, its tracked `.md` count, the Markdown paths
added by the change, and the resulting expected count. This avoids embedding
a self-referential commit or file digest. The `documents` array covers that
resulting set; files found only on other branches remain outside it.
When refreshing the catalogue, update the source baseline and declared
additions together and compare the full resulting path set with Git.

Required record fields and the optional `canonical_for`, `source_ref`, and
`superseded_by` values are nonempty strings. `integrity` and `related_assets`
are arrays of unique strings when present. Omit unestablished optional values;
do not fill them with a guessed approval, successor, or source revision.

Keep one entry per repository-relative Markdown path, with exact case. Sort
entries by path so additions and removals are reviewable. Related non-Markdown
assets are dependencies, not additional Markdown entries. The existing
`coglatas-frontend/src/stories/Configure.mdx` file is outside the `.md` inventory.

| Field | Meaning and update rule |
| --- | --- |
| `path` | Current repository-relative path. A planned destination belongs in the move ledger until the move occurs. |
| `kind` | Primary role: `entrance`, `guide`, `reference`, `contract`, `decision`, `evidence`, `report`, or `generated-view`. Record mixed responsibilities in the classification basis. |
| `domain` | Stable subject grouping for navigation. Reuse an existing domain where it fits. |
| `lifecycle` | Documentation lifecycle from the definitions below. |
| `classification_basis` | Short source-grounded reason for the classification; describe unresolved or mixed scope explicitly. |
| `canonical_for` | Optional narrow scope for which the source establishes maintained authority. Omit when no such authority is established. |
| `source_ref` | Optional exact source commit or governing decision when the document declares one unambiguous source. Retain a multi-run ledger's individual identities in the document instead of assigning one misleading commit. |
| `superseded_by` | Optional concrete successor established by source review. Filename similarity is insufficient. |
| `integrity` | Composable flags for recorded generated, fixed-path, or immutable-evidence dependencies. |
| `related_assets` | Proven companion files or bundles, including JSON, logs, screenshots, archives, and receipts. Recheck the complete bundle before a move. |

An omitted optional field means no value was established in this classification.
In particular, absence of an integrity flag or related asset does not prove
that a file is movable or has no consumers. Derive file sizes, Git blob IDs,
and discovered references from the inspected tree; do not maintain redundant
manual hashes for ordinary guides. An existing evidence digest remains part
of its own evidence contract.

| Lifecycle | Interpretation |
| --- | --- |
| `active` | Maintained instructions, contracts, references, or an entrance currently used for its stated purpose. The document can still describe incomplete work. |
| `candidate` | A proposal or bounded candidate awaiting its governing adoption or activation decision. Presence on a branch or in an index does not adopt it. |
| `historical` | The source explicitly designates historical status or clearly delimits a prior context to retain. A date or pinned source alone is insufficient; a current contract can also be source-bound. This does not require a physical move to `archive/`. |
| `superseded` | The source explicitly marks the document superseded or establishes a replacement. Record a concrete successor when known; preserve any unique obligations or evidence. |
| `needs-review` | A single lifecycle or authority boundary is not established, including mixed current instructions and historical/candidate sections. Retain the source while resolving it. |

Use `generated`, `fixed-path-consumer`, and `immutable-evidence` only for
established dependencies, and allow more than one flag. `generated` identifies
a view owned by another source; `fixed-path-consumer` identifies discovery,
policy, or code that uses its path; `immutable-evidence` identifies a concrete
byte/subject retention contract. Ordinary source-bound records should still
retain their observations, outcomes, and attribution during organization.

## Recorded protected paths and consumers

The following dependencies were inspected at
`b82e34878f94d78f39e72c1076db65e54004de82`. This is a minimum recorded set, not
a complete proof that every other path is unreferenced. Re-read these consumers
at the owning branch's proposed head before any affected move.

| Path or collection | Established dependency | Handling |
| --- | --- | --- |
| `AGENTS.md`, `.github/copilot-instructions.md`, `.github/ISSUE_TEMPLATE/engineering-task.md`, `.github/pull_request_template.md`, and root/component README entrances | Tool and contributor entry points; [repository rules](../AGENTS.md) and [tool instructions](../.github/copilot-instructions.md) establish shared guidance. | Retain discovery paths and useful component-local entrances. A path migration must preserve its consumer's discovery contract. |
| `COPYRIGHT.md`, `CONTRIBUTING.md`, `THIRD_PARTY_NOTICES.md`, `.github/SECURITY.md`, `docs/PUBLICATION_RUNBOOK.md`, and `README.md` | [Publication validator](../scripts/ci/check-publication-readiness.py) requires the first five Markdown paths and checks the root README public-source notice. It also requires `.github/CODEOWNERS` and `.gitleaksignore`. | Keep the paths and policy meaning. Recheck the validator for a separately scoped contract migration. |
| `docs/SECURITY.md`, `docs/SECURITY_MODEL.md`, the publication policy files, and deployment guidance including `deploy/gcp/README.md` | [.github/CODEOWNERS](../.github/CODEOWNERS) and [governance policy](../governance/policy.json) contain path and ownership expectations. | Preserve ownership and policy references. A prose reorganization does not authorize changing the expected controls. |
| `governance/controls.md` | [Governance validator](../scripts/ci/validate-governance-policy.py) renders the complete view from `governance/policy.json` and compares it with the checked-in text. | Classify externally. Change the owning policy/generator and regenerate only when the underlying contract change is required. |
| `docs/verification/governance-required-checks.md` | [Governance policy](../governance/policy.json) lists this path in its references; [the validator](../scripts/ci/validate-governance-policy.py) checks that referenced files exist. | Preserve or explicitly migrate the policy reference and validate the resulting contract. |
| `performance/baseline-campaigns/evidence/**` | [Campaign validation](../scripts/performance/db_campaign.py) compares retained evidence and policy files with base Git blob bytes when a base is supplied. [Approval validation](../scripts/ci/verify-performance-db-campaign-baselines.py) also retains the exact files of historical approved evidence and baselines. | Preserve paths and bytes, including Markdown and rejected evidence. Keep each JSON/ZIP/review bundle together. |
| `docs/verification/p4-isolated-environment-owner-packet-20261018.md` | [Companion review JSON](verification/assets/p4-isolated-environment-review-20261018.json) binds this exact subject path and its SHA-256 over Git blob bytes. | Preserve the subject and its bytes. A revised review subject requires its own explicit evidence procedure, not a formatter or move. |
| `docs/verification/functional-ci/*` and `docs/ci/functional-compose-harness.md` | [Main CI router](../scripts/ci/route-main-ci-changes.sh) selects Functional coverage for these paths before its ordinary documentation exemption. | Preserve selection semantics. A move must update and verify the affected routing contract. |
| `docs/migration/avalonia/*` | [Main CI router](../scripts/ci/route-main-ci-changes.sh) selects AV-MIG contract/security work for this directory. | Retain its path binding until a scoped routing migration is verified. |
| `docs/ci/compat-critical-suite.md` and `docs/TESTING.md` | [Compatibility preflight](../.github/workflows/compat-critical-preflight.yml) names both in `pull_request.paths`. | Preserve trigger coverage for any extracted or moved contract. |
| `docs/verification/functional-ci/authorized-thread-projection-diagnostics.md` | [Thread typecheck workflow](../.github/workflows/thread-functional-typecheck.yml) names the file in `pull_request.paths`. | Include this trigger in a proposed path migration. |
| `docs/verification/composer-input-lifecycle-observation-20261025.md` | [Composer diagnostics workflow](../.github/workflows/composer-lifecycle-diagnostics.yml) names the file in `pull_request.paths`. | Preserve the diagnostic relationship and source-bound observations. |
| `docs/verification/synthetic-restore-dependency-advisor.md` | [Restore-advisor workflow](../.github/workflows/synthetic-restore-dependency-advisor.yml) names the file in `pull_request.paths`. | Include this trigger in a proposed path migration. |
| `docs/ci/qodana-packet-verification.md` | [Packet verifier](../scripts/ci/qodana-packets/verify.py) and [R2 verifier](../scripts/ci/qodana-packets/r2_verify.py) allow this exact auxiliary path in their bounded-diff checks. | Preserve the path or separately review and verify the affected packet boundary. |

The P4 subject's audited SHA-256 is
`fdb7eeb821c8b5438947ae3833ff76922e9b5d9e625398c693cf84c4518e1f54`, matching
the companion JSON at the stated baseline. This establishes a subject binding;
it is not a claim that every CI run checks that digest. Preserve the review's
`OWNER_DECISION_REQUIRED` state and null approval as recorded.

### Evidence bundles and historical identities

The baseline evidence collection has 17 Markdown records and 30 companion logs
under `docs/evidence/mvp-a/frontend-final-2026-07-03/`. The verification
collection also has six JSON files and 15 PNGs, including P4 review metadata,
readiness inventories, and the Penpot comparison set. Use the
[evidence index](evidence/README.md) and [verification index](verification/README.md)
to locate them, then inspect exact file references before editing a bundle.

[The Student Council release-gate record](verification/student-council-release-gate.md)
explicitly scopes its opening assessment to a dated commit and links
[the readiness JSON](verification/student-council-readiness.json). Keep the
historical outcome and run/attempt identity. A later Main result requires its
own source-qualified record; it does not rewrite earlier failure evidence.

Leave commit-qualified historical links and `.gitleaksignore` finding
fingerprints attached to their original source. A historical path component
inside a fingerprint is not a live navigation link to replace. Classify
protected records in the catalogue or an index instead of adding metadata to
their original bytes.

## Procedure for a documentation change

1. Refresh the owning branch and relevant PR heads. Record overlapping changed
   paths in the [move ledger](DOCUMENTATION_MOVES.md) before moving them.
2. Establish the document's purpose and any successor from its content and
   governing source. Update its catalogue entry without transferring product
   approval or silently resolving an unknown classification.
3. For a proposed move, inspect Markdown links and anchors, code-formatted path
   mentions, workflow/script/test/config references, CODEOWNERS, governance,
   digest subjects, and companion assets. Treat examples and historical
   references according to their actual role.
4. Record the exact old/new paths, affected consumers, old-link and anchor
   treatment, and rollback in the ledger. Preserve an old-path pointer when
   ordinary external references require compatibility. Preserve immutable
   originals rather than replacing them with a pointer.
5. Make a small coherent change. Keep movement separate from extensive prose
   rewriting or language migration. Update navigation and live consumers in
   the same reviewed change.
6. Check exact-case target paths and heading anchors, catalogue coverage,
   bundle completeness, and protected bytes. Run relevant existing policy and
   CI-routing checks when their contracts are affected, then record the actual
   execution scope and remaining limitations.
7. Update the ledger with the resulting commit and evidence. Revert through a
   new inverse-move or revert commit when needed; preserve published history.

### Archive admission

Use the existing `archive/status/`, `archive/plans/`, `archive/reports/`, and
`archive/old-specs/` categories. Record a concrete successor or explicit bounded
historical purpose, retain unique requirements and unresolved obligations,
account for consumers and assets, and explain old-link compatibility in the
[archive index](archive/README.md). Age, a closed PR, or a legacy filename alone
does not establish supersession.

For example, [the numerical-assurance suspension](../performance/ASSURANCE-SUSPENSION.md)
has no automatic expiry, and [the U-22 demo-data guide](u22/demo-data.md)
describes a deterministic test fixture. Assess their governing purpose before
proposing archival. Keep source-bound evidence discoverable even when it
remains in its original verification collection.

## Validation implementation boundary

At the audited baseline, [documentation CI](../.github/workflows/documentation_ci.yml)
is manually dispatched and checks UTF-8, NUL bytes, and conflict markers. It
does not establish link, anchor, catalogue, or move integrity. Local checks for
a particular documentation PR must report their actual scope separately.

DOC-04 implements the focused
[committed-tree checker](https://github.com/NYGsatoshi/Coglatas/blob/78206c0ecf9b934d302ece7a6fd502b314363ece/scripts/ci/documentation_integrity.py)
on `SecurityTestImplimation`, normally integrated through
[PR #1811](https://github.com/NYGsatoshi/Coglatas/pull/1811). Its
[operating guide](https://github.com/NYGsatoshi/Coglatas/blob/78206c0ecf9b934d302ece7a6fd502b314363ece/docs/ci/documentation-integrity.md)
defines dependency installation, the `check` and narrower `links` modes,
supported syntax, and source-bound reporting. The qualified implementation
uses an externally selected trusted baseline; candidate catalogue metadata
cannot remove an established protection obligation.

The [recorded qualification](DOCUMENTATION_MOVES.md#doc-04-validation-checkpoint)
passed 48 independent controls, including deliberate broken targets, wrong
case, catalogue errors, reference/fragment links, duplicate and custom
anchors, Unicode boundaries, complete asset bundles, and protected-byte or
metadata-removal mutations. The complete pinned DOC-03 corpus passed without
historical-link suppressions. Unsupported local addressing fails visibly;
the checker does not silently waive historical defects or rewrite evidence.

Before a physical move, run the qualified checker in full `check` mode against
the move's own committed candidate and an independently selected trusted
ancestor. Record both identities and actual results in the implementing PR.
The initial pinned-corpus result does not qualify a later tree. Catalogue
provenance remains descriptive, and generated or substantive evidence changes
still require their owning validation. The implementation workflow qualifies
checker changes on the test branch; it is not an automatic gate for every
documentation PR. A broader workflow promotion requires its own scoped change.

External/private URL reachability is separate from local repository integrity.
Keep existing required checks and routing unchanged during this work. In
particular, `docs/catalog.yml` is outside the Main router's ordinary
`docs/*.md`, `docs/*.rst`, and `docs/*.txt` exemption; classify its CI execution
according to the actual router result. Promote only reviewed checker changes
when needed; do not merge unrelated test-branch changes with documentation work.

