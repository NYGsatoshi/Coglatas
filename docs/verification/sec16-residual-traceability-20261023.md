# SEC-16 residual requirement, test and execution traceability

Status: **IN_PROGRESS; SEC-08 finding-policy gap BLOCKED; release NO_GO**.
Source baseline: Main `4ae574ab567f003b91ce2e467f0fd3681880b918`.
This is an implementation/evidence reconciliation, not formal private SEC-ARCH
mapping, risk acceptance, RLS activation, target approval or release permission.

Issues [#689](https://github.com/NYGsatoshi/Coglatas/issues/689) and
[#1328](https://github.com/NYGsatoshi/Coglatas/issues/1328) were closed with
incomplete acceptance checklists. Their lifecycle state is preserved; closure
does not establish the remaining evidence. [#582](https://github.com/NYGsatoshi/Coglatas/issues/582)
remains open. This document neither changes those states nor assigns new
repository priorities or approves suppressions.

## Current route and contract inventory

Controllers are authoritative: `FilesController`, `AttachmentsController` and
`FileFoldersController`. SEC-05's `scripts/security/sec05-route-inventory.py`
checks selected route tokens; it is not a complete lifecycle inventory.

| Family | Current route classes | Boundary and applicability |
| --- | --- | --- |
| FileObject metadata | `GET /api/files?workspaceId=…`, `GET /api/files/{fileObjectId}` | List visibility does not grant direct-read authority. FCI-07 substitutes foreign and same-Tenant cross-Workspace identities. |
| FileObject credential | `POST /api/files/{fileObjectId}/download-grants`, `POST /api/file-download-grants/{grantId}/download` | Current actor/resource scope is selected server-side. Credential is an opaque POST-body token. Existing denial is 400. |
| Attachment credential | `POST /api/attachments/{attachmentId}/download-grants`, `POST /api/attachment-download-grants/{grantId}/download` | Denied issuance deliberately masks missing/unauthorized resources with 404 `FILE_DOWNLOAD_GRANT_NOT_FOUND`; denied use is 400. Do not replace this with a generic expected status. |
| Direct bytes | `GET /api/files/{fileObjectId}/download`, `GET /api/attachments/{attachmentId}/download` | Reauthorizes independently of a previously displayed detail or issued credential. |
| Upload/delete | `POST /api/files`, `POST /api/attachments`, `DELETE /api/files/{fileObjectId}`, `DELETE /api/attachments/{attachmentId}` | Synthetic upload is arrangement. Deletion is an implemented HTTP transition; the new retained-grant deletion facts are outside baseline Main. |
| Batch selection/delete | `POST /api/files/selection-snapshots`, `POST /api/files/selection-snapshots/{selectionSnapshotId}/delete` | Existing snapshot creation and batch deletion are separate current routes. The selected single-File HTTP facts do not establish retained-grant or mixed-scope batch deletion coverage. |
| Sharing | `GET/PUT /api/files/{fileObjectId}/sharing`, `POST /api/files/{fileObjectId}/sharing/recipients`, `DELETE /api/files/{fileObjectId}/sharing/recipients/{grantId}` | The DELETE identity is a sharing grant, distinct from an opaque download grant. Current sharing version enters the download-grant policy stamp. Regrant must not revive old credentials; old owner stamps also become stale. |
| History | `GET /api/files/{fileObjectId}/activity`, `GET /api/files/{fileObjectId}/versions/{versionId}/content` | Immutable historical-content reads exist and reauthorize download. A public replace-current-content command was not identified in these controllers; do not invent replacement semantics. |
| Logical placement | `GET /api/files/{fileObjectId}/location`, `POST /api/files/{fileObjectId}/move`, folder create/list/move routes | Current move is Workspace-scoped folder placement, not transfer of Tenant/Workspace ownership. A folder change does not itself imply revoked download authority. |

`FileService` uses a ten-minute grant lifetime, rechecks current scope, actor,
operation, expiry, revocation, target eligibility, classification and policy
stamp, and records successful use in `DownloadedAt`. It does not reject a grant
merely because it was previously downloaded. **One-use/consumed-token rejection
is NOT_APPLICABLE to this contract.** Positive repeated downloads are intentional.
The current grant routes return bytes rather than public signed URLs; a signed-URL
product lifecycle is NOT_APPLICABLE. Storage abstractions do not establish such
an endpoint. Expiry and grant revocation have no public writer: H1–H4 arrange
only synthetic persisted timestamps, then use real HTTP as the oracle.

## Exact test catalog

Backend paths below are relative to `tests/Coglatas.Tests`; other paths are
relative to the repository. Method names identify source controls, not proof
that each ran on every commit.

| ID | Class/file and exact owning test or helper | Kind |
| --- | --- | --- |
| B1 | `tests/functional/security-negative/cross-scope-negative-matrix.spec.ts`, owner `FUNC-AUTHZ-002`, calls `createSec16GrantJourney` in `sec16-file-grants.ts` before and after HTTP Workspace-member removal | Real Compose HTTP/Playwright; Alpha member/owner and Beta owner, retained in-memory credential |
| U1 | `Files/FileDownloadGrantBoundaryTests.FileDownloadGrantCanBeCreatedOnlyForAuthorizedActor` | Unit: stored actor/Tenant/Workspace/resource binding, denial creates no extra grant |
| U2 | `FileDownloadGrantDownloadRequiresCurrentAuthorization`; `TaskFileOpenReauthorizesAndDoesNotTreatDetailStateAsACapability` in the same class | Unit: revoked read/issue/use authority; zero storage opens |
| U3 | `ExpiredAndRevokedFileDownloadGrantsAreDenied`; `ActorMismatchAndTokenMismatchAreDeniedWithoutLeakingToken` | Unit: terminal state, identity/token mismatch and audit reasons |
| U4 | `TaskFileGrantRequiresCleanScan`; `CurrentFileObjectStateInvalidatesExistingTaskGrant` | Unit theories: Pending/Infected/Failed/Skipped task scan; Quarantined/Archived/Deleted target |
| U5 | `TenantWorkspaceAndTargetScopeMismatchAreDenied`; `StalePolicyAndClassificationChangesInvalidateOldFileGrant`; `InaccessibleFilesAndUnknownOrMissingClassificationFailClosed` | Unit: scope, classification and stale policy guards |
| U6 | `AuditDoesNotContainRawTokenSignedUrlStoragePathOrRestrictedValues` | Unit: allowlisted audit data, no credential/storage/content values |
| P1 | `PostgreSql/TaskV1FileOpenDownloadReauthorizationPostgreSqlTests.ActiveCleanTaskAssociationCanOpenIssueAndUseAGrant` | Persisted Application positive; storage spy, not HTTP or physical-byte proof |
| P2 | `MembershipLossDeniesOpenGrantIssueAndGrantUseBeforeStorageRead` in that class | Persisted membership suspension; read/issue/use fail and storage count remains zero |
| P3 | `AssociationFileStateAndScopeChangesInvalidateExistingGrantWithoutReadingStorage` in that class | Seven independently created fixtures: association deletion, Quarantined, Archived, File deletion, Failed scan, Workspace change, Project change |
| H1/H2 | `PostgreSql/FileDownloadGrantLifecycleHostedHttpTests.ExpiredFileObjectGrantRejectsRetainedCredentialThroughRealHttp`; `RevokedFileObjectGrantRejectsRetainedCredentialThroughRealHttp` | Real Web host, cookies/CSRF, physical file and PostgreSQL |
| H3/H4 | `ExpiredAttachmentGrantRejectsRetainedCredentialThroughRealHttp`; `RevokedAttachmentGrantRejectsRetainedCredentialThroughRealHttp` in that class | Same terminal-state proof through Attachment route family |
| S1/S2 | `FileObjectGrantRemainsInvalidAfterRealHttpSharingRevokeAndRegrant`; `AttachmentGrantRemainsInvalidAfterRealHttpSharingRevokeAndRegrant` in that class | PR #1334, **not baseline Main**; public sharing grant/revoke/regrant |
| D1/D2 | `DeletedFileObjectRejectsRetainedGrantAndFreshDownloadsThroughRealHttp`; `DeletedAttachmentRejectsRetainedGrantAndFreshDownloadsThroughRealHttp` in that class | Stacked PR #1337, **not baseline Main**; current HTTP delete routes |
| A1 | `Files/FileSharingServiceTests.RevokeRefreshesTheAuthoritativeProjectionAndInvalidatesPriorDownloadPolicy`; `ShareMutationFailsClosedWhenCallerLacksWorkspaceManagementAuthority`; `GrantRequiresServerEligibleRecipientRatherThanAClientSuppliedIdentity` | Application/unit sharing projection, authority and recipient controls |
| A2 | `Files/FileActivityServiceTests.HistoricalContentReauthorizesDownloadBeforeOpeningItsImmutableStorageKey`; `HistoricalContentDoesNotReadStorageAfterDownloadAuthorizationIsRevoked` | Unit history-byte positive/negative with storage spy |
| F1 | `PostgreSql/FileFolderHostedHttpTests.CurrentMemberPlacementOperationsRespectPersistedRoleScopeAndConcurrencyWhileIntakeIsClosed` | Real HTTP/PostgreSQL placement, stale versions, ReadOnly/suspended and cross-Workspace/Tenant denial, protected state and direct-byte positive; no retained opaque-grant assertion |

## Requirement-to-control reconciliation

| Requirement from #689/#1328 | Positive and mutation/negative controls | State and remaining evidence |
| --- | --- | --- |
| Current route inventory and applicability | Controller inventory above, SEC-05 token verification; U1/P1/H1–H4 | Inventory reconciled for these current families. Complete role × scope × route execution is **IN_PROGRESS**, not inferred from route existence. |
| Cross-scope identity substitution and direct-read isolation | B1: same-Tenant actor transfer, Beta actor transfer, invalid token, wrong grant identity, foreign issuance; byte-identical member positive. FCI-07 also substitutes metadata IDs; U1/U5 bind stored scope | Representative HTTP subset exists. Attachment grant actor/scope permutations and complete Owner/Member/ReadOnly/removed/unrelated role combinations still need explicit accounting. |
| Membership revocation with a live Tenant session | B1: retained grant, fresh FileObject issuance and direct download deny after real HTTP revoke; `/api/auth/me` remains 200; unaffected owner reads the same bytes. P2/U2 check zero storage opens | Bounded FileObject HTTP evidence exists. This does not prove every parent Project/Conversation/Attachment scope or concurrent revocation window. |
| Expired/explicitly revoked grant | H1–H4: successful member/owner physical-byte downloads before; two terminal denials; unchanged protected digest/use counts; exact audit reason; valid member session, fresh member grant and owner positive afterward | Implemented in baseline Main. Synthetic DB timestamp arrangement is explicit. Existing candidate receipts below do not substitute for new exact-Main qualification. |
| Real sharing policy transition | S1/S2: real HTTP private upload and recipient grant/revoke/regrant, missing-CSRF negative, old/fresh/direct denial after revoke; fresh owner positive; old grant still `policy_changed` after regrant and fresh member positive | Selected six-fact candidate qualified separately; **not merged/Main evidence**. General sharing-policy PUT, all recipient kinds and role combinations remain unqualified here. |
| HTTP deletion | D1/D2 extend the same real-host fixture; retained credential/fresh issuance/direct bytes must remain denied with unchanged protected state and current unaffected positive | Local exact eight-fact result supplied for stacked PR #1337; independent receipt review and hosted/resulting-Main evidence remain pending. No Main credit. |
| Archived/quarantined/ineligible/changed parent scope | U4/U5 and P3: each applicable persisted state/scope transition denies, zero storage reads; P1 establishes normal operation | **IMPLEMENTED_NOT_VERIFIED at full HTTP matrix level**. P3 is persisted Application plus storage spy; it is not seven independently named HTTP facts or a full role matrix. No HTTP status writer is invented. |
| Move/version behavior | F1 rejects forbidden Workspace/Tenant placement and keeps bytes/metadata stable; A2 reauthorizes historical content before storage. P3 mutates File Workspace/Project scope | Folder placement is not an ownership move. Retained grants around legitimate placement and historical-byte cross-scope HTTP cases are not proved by F1/A2. No public replacement workflow or one-use-token requirement is inferred. |
| Denial minimization and unchanged protected state | B1 checks bounded 400 bodies and absent Location/Content-Disposition plus private markers; H/S/D compare selected state digests/use counts and safe audits; U6 checks audit absence | New denials must retain route-specific 400/404, no 500/timeout-as-denial, no protected filename/body/storage/credential/identity echo. These checks do not establish complete success/failure logging coverage. |
| Isolated target/session/storage and safe evidence | SEC-02/03 target/auth/context guards; synthetic Alpha/Beta actors; physical fixture files; FCI-07 JUnit/owner verification; privacy boundary in PR #1327 | Reuse existing harnesses. No production/school/third-party storage active testing; source configuration alone is not target execution or HTTPS evidence. |
| Common normalized finding-policy path (#582) | Existing SEC-06 High/scanner-failure/redaction mutations and the File-route processor controls below | **BLOCKED**: Medium remains report-only; no complete approved inventory/expiring suppression/ratchet or SEC-16 assertion-to-common-finding bridge is established. |

## Actual execution allocation versus missing expansion

| Lane | Current source allocation | Evidence and limits |
| --- | --- | --- |
| PR backend | Routed .NET unit/provider scopes in `.github/workflows/ci.yml`, migrated real PostgreSQL and verified scoped TRX. Test traits select their owning scopes | A source change can select a subset; inspect the actual filter and named TRX cases. A green unrelated job is not these tests' execution. |
| PR Functional/security | `FUNC-AUTHZ-002` is a required FCI-07 owner; File/security runtime changes route the isolated security stack. B1 is outside the full-expansion conditional | Retain both owner/JUnit counts and exact checkout/merge source. Default Fast does not become a full File lifecycle matrix. |
| Trusted Main | `main-validation.yml` executes the complete backend projects with distinct verified TRX; Main Fast/Full/Extended consume same-run artifacts and include authz-negative | Full/Extended repeat B1's same bounded grant calls. They do **not** expand B1 into H/S/D or a new complete SEC-16 role matrix. Backend H1–H4 are separate tests. |
| Manual/deep | Existing isolated SEC-04/06 scanner/runtime workflows and Functional Extended retain target/auth/coverage failure gates | No dedicated complete long grant/revoke/regrant sequence or fuzzer-finding-to-SEC-16 replay inventory is established here. This is an unfilled acceptance requirement, not an approved exemption or claimed schedule. |
| This reconciliation PR | Five extra File-route mutations in the existing `test_security_zap_attribution.py` module; its owner is `test-zap-contract.sh` in selected security PR/Main lanes | Local focused execution is recorded below. A Python-test/document-only diff is not assumed to select the live PR scanner lane; inspect actual routing. No CI policy or routing change is made to claim extra execution. |

An adequate future matrix must explicitly record each current route family's
authorized actor, same-Tenant unauthorized actor, foreign Tenant, foreign
Workspace, removed member, ReadOnly where relevant, applicable parent scope and
implemented transition. Define actual additional cases and owners before claiming
Main/deep expansion; rerunning the representative subset is insufficient.

## Source-bound evidence ledger

| Receipt | Exact source and observed result | Qualification boundary |
| --- | --- | --- |
| #1325 historical bounded owner | [PR CI 39913066994](https://github.com/NYGsatoshi/Coglatas/actions/runs/39913066994), source `afd7be4591c554dd243a52c32cec75ee13485bf3`, recorded in `sec16-file-download-grant-p0.md`; #1325 merged as `babcf80f7babafe4087fb7cb5742edb2b98868c2` | Historical B1 receipt; not full #689 completion or proof for every later Main SHA. |
| Baseline Main | `4ae574ab567f003b91ce2e467f0fd3681880b918` contains H1–H4 from the reviewed #1333 integration | Source inclusion only in this ledger; this reconciliation does not rerun the provider/HTTP suite or transfer PR success to Main. |
| #1334 sharing candidate | Head `75c40d48fd0bf844161c840458f0c50a85537a63`; tested merge `357baa971e165412b78c01f2f413d120bd95c621`; [PR CI 39945361039](https://github.com/NYGsatoshi/Coglatas/actions/runs/39945361039) successful with 443 scoped cases, including all six H/S facts; local postcommit six-fact TRX also passes with zero skips | Separate candidate qualification. Existing first ReSharper failure 39943790004 and earlier limited receipts remain distinct. Not merged/Main evidence. |
| #1337 deletion candidate | Head `c5c49abf107ec68179ba045afc5dacc3699b283b`, stacked on #1334 head; supplied clean postcommit local result: eight executed/passed, zero failed/not-executed. TRX SHA-256 `8499b81197abab67ff5f98a3da6079ac24b2b36929d0f966a74da5cb73645564` | `artifacts/sec16-file-deletion/c5c49abf-qualified/` in its own worktree contains paired Tests/Web identities, TCP/database/version/privacy/loopback probes and cleanup. Independent source review passed; final receipt review and hosted/resulting-Main execution were pending when reconciled. Earlier wrong-reason failures and eight-test PASS/clean-tree-qualification FAIL remain retained. |

The #1334 local final six-fact TRX hash is
`7bcec0ce61e81c7a3fc59e20407c129e867b0e8704963d6373d6e0d01c5350f4`.
Both built Test/Web source metadata identify that postcommit head; paired hashes,
TCP expected-database/version results and owned cleanup were retained. These are
selected HTTP/provider receipts, not target HTTPS, all-table RLS or release evidence.

## #582 path: what is implemented and what is not

`scripts/security/process-zap-report.py` currently reduces ZAP findings to rule,
risk, confidence, method, parameter text, path and query **names**; request/response
evidence and query values are discarded. It checks a forbidden-value set before
writing sanitized evidence. It retains input-file hashes and attribution metadata.
These input hashes are not a common normalized-evidence fingerprint or a
requirement/scenario-to-finding identity.

`zap-policy.json` and `test-zap-contract.sh` explicitly preserve **High = block,
Medium = report**. Scanner nonzero/timeout, absent reports, malformed inputs and
unknown classifications fail closed. This SEC-06 policy is not #582's required
new-unapproved-Medium ratchet. Existing narrowly scoped passive filters are not
new risk acceptance and are not a substitute for the requested owned, reasoned,
expiring, tightly scoped suppression model. No policy/filter is changed here.

B1 and H/S/D currently fail the test/owner gate directly. This is useful immediate
blocking, but no common machine-readable SEC-16 failure fingerprint/triage bridge
was found connecting those assertion outcomes to #582's complete model. Do not
rename a direct assertion or a Code Scanning static review as that integration.
The full cross-scanner policy, approved initial Medium inventory, suppression
expiry/count ratchet and complete replay/normalization linkage remain open.

The added `Sec16ReportBoundaryTests` use the same actual SEC-06 processor as the
existing `AttributionEvidenceTests` and ZAP shell mutation fixtures. They cover:

- `test_file_route_high_blocks_after_retaining_only_safe_metadata`: both grant
  consumption route classes retain safe method/location metadata but High fails.
- `test_medium_is_visible_under_current_report_only_policy_not_a_sec08_ratchet`:
  Medium stays visible; the passing current processor status is explicitly not
  #582 ratchet acceptance.
- `test_scanner_timeout_cannot_pass_as_zero_findings`: exit 124 plus zero alerts
  still fails and records `scanner-failed`.
- `test_missing_report_cannot_pass_as_empty_file_findings`: absent report fails
  before evidence publication.
- `test_secret_bearing_retained_parameter_fails_before_publishing_evidence`:
  a canary in a retained field fails without echo or output files.

The synthetic reports use logical route placeholders, not real grant identifiers,
and perform no network scanning. They demonstrate processor behavior; they do not
create a SEC-16 production finding adapter or prove actual scanner auth/coverage.

## Verification of this reconciliation

Focused native Python execution: `python -B -m unittest discover -s tests/ci -p
test_security_zap_attribution.py -v` passed **19 tests, zero failures/skips**,
including the five new File-route controls and fourteen retained attribution
mutations. No policy/suppression, product, grant semantics, migration, role,
required threshold or Owner approval changed. No new PostgreSQL or browser runtime
execution is claimed by this documentation/test slice. Exact committed-source
local rechecks and hosted CI must be recorded separately from working-copy checks.

The full Ruby-dependent ZAP shell contract was not executed in this local slice;
the inspected local host and existing Playwright container lack Ruby. The focused
Python processor checks are not represented as the complete shell/scanner suite.
Existing SEC-ARCH/approved-target HOLDs, independent review and resulting-Main
qualification remain mandatory. None of these representative results authorizes
closing the residual security acceptance or declaring RELEASE_READY.
