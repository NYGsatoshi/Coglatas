# Verification documentation

[Documentation](../README.md) · [Testing guide](../TESTING.md) ·
[Evidence bundles](../evidence/README.md) · [Historical archive](../archive/README.md)

This directory contains verification contracts, execution guidance, review
candidates, source-bound results, and implementation ledgers. Use the topic index
to find a record, then check its stated scope and source before using its claims.
The [repository authority order](../../AGENTS.md#start-here) and the
[testing qualifications](../TESTING.md) continue to apply.

The initial index accounts for the 295 Markdown records present here at
[`b82e34878f94d78f39e72c1076db65e54004de82`](https://github.com/NYGsatoshi/Coglatas/tree/b82e34878f94d78f39e72c1076db65e54004de82/docs/verification).
That inventory identifies the starting documents; it does not qualify their
product behavior, execute a gate, or establish the state of a later Main commit.

Two additional bounded PostgreSQL migration records were imported byte-for-byte
from Main at `9cf3ed8eec0d46ebe0d841a533dee097ec007a4d` (DOC-08).
This index now covers 303 records in `docs/verification/` on the documentation
branch. The two migration records and one historical testing snapshot were
added in earlier isolated PRs; five source-bound pagination records were copied
byte-for-byte from Main `a851cc3a8bb977f08605097a719fa186c8f37e32` in DOC-09.
The topic index does not cover Main-only records excluded from this branch, or
assert runtime, official Gate, release, authorization or PostgreSQL acceptance.

## Find a topic

| Topic | Contents |
| --- | --- |
| [Governance, Functional CI, and acceptance](#governance-functional-ci-and-acceptance) | Coverage definitions, gate ownership, owner journeys, and acceptance evidence. |
| [Build artifacts, diagnostics, and quality coverage](#build-artifacts-diagnostics-and-quality-coverage) | Image identity, evidence retention, diagnostics, and original-source coverage collection. |
| [Dependencies and platform upgrades](#dependencies-and-platform-upgrades) | Version-specific dependency and platform qualification records. |
| [Security architecture and specification contracts](#security-architecture-and-specification-contracts) | Exit requirements, isolated fixtures, registry bindings, and their public review boundaries. |
| [Security controls and supply chain](#security-controls-and-supply-chain) | Finding attribution, scanner behavior, browser privacy, and dependency controls. |
| [Authentication, tenancy, and administration](#authentication-tenancy-and-administration) | Session, account, membership, and administrative validation boundaries. |
| [Workspaces, Groups, and Projects](#workspaces-groups-and-projects) | Creation, activation, membership, scoped reads, and related browser interactions. |
| [Tasks, Gantt, and notifications](#tasks-gantt-and-notifications) | Task contracts, execution, scheduling, notification delivery, and implementation ledgers. |
| [Files, artifacts, and download authorization](#files-artifacts-and-download-authorization) | Storage outcomes, browser operations, grants, and retained-credential authorization. |
| [Messaging, Threads, and search](#messaging-threads-and-search) | Conversation and Message behavior, composer lifecycles, search, and Thread diagnostics. |
| [Realtime and Outbox](#realtime-and-outbox) | Transport authorization, connection lifecycles, durable claims, and dispatch failures. |
| [Announcements, forms, and audit](#announcements-forms-and-audit) | Publication, form admission, audit views, and their authorization and validation boundaries. |
| [Frontend and design reviews](#frontend-and-design-reviews) | Cross-domain browser foundations, design comparisons, and scoped UI previews. |
| [Release and Student Council pilot](#release-and-student-council-pilot) | Pilot scope, owner decisions, source-bound release snapshots, and pilot feature workstreams. |
| [Deployment, recovery, and isolated environments](#deployment-recovery-and-isolated-environments) | Target-environment prerequisites, synthetic recovery, and isolated configuration records. |
| [Performance](#performance) | Measurement declarations, retained cohorts, and structural verification. |
| [ProjectIDE and Security Foundation](#projectide-and-security-foundation) | Core and Security Foundation verification, with each record retaining its own admission and activation scope. |
| [MVP-A verification](#mvp-a-verification) | Phase-specific checklists, environment notes, blockers, and execution records. |

## Read the record in context

Document role, implementation state, execution result, and approval state are
separate. These entry points illustrate the different roles already present:

| Role | Start here | Interpretation |
| --- | --- | --- |
| Canonical Functional CI definitions | [Test policy](functional-ci/functional-test-policy.md), [gate topology](functional-ci/functional-gate-topology.md), [journey matrix](functional-ci/functional-journey-matrix.md) | Definitions, target ownership, and journey inventory; source mappings do not establish exact-candidate hosted execution. |
| Required-check contract | [Required-check governance](governance-required-checks.md) | Read the authoritative policy/registry references and the distinction between static topology, live rulesets, and exact-head results. |
| Security qualification requirements | [SEC-ARCH preflight](security-architecture-preflight.md) | Required exit evidence and approval boundaries; indexing the contract does not satisfy its conditions. |
| Execution guidance | [Functional execution evidence](functional-ci/functional-execution-evidence.md), [public HTTPS Golden Path](p0-public-https-golden-path.md) | Follow the recorded prerequisites and source/artifact binding. Target-environment execution evidence is separate from a gate implementation. |
| Guidance with a verification ledger | [SEC-ARCH fixtures](security-architecture-fixtures.md), [Gantt ledger](task-v1-pr06-gantt.md) | Read reusable instructions or contracts alongside their recorded limits, chronology, and source-specific outcomes. |
| Source-bound release evidence | [Student Council release gate](student-council-release-gate.md) | The opening assessment explicitly belongs to its dated source and attempt. A later Main needs its own receipt; effective owner restrictions have their separate authority. |
| Candidate or review-only work | [Mobile acceptance candidate](real-backend-mobile-acceptance.md), [restore dependency advisor](synthetic-restore-dependency-advisor.md) | Preserve the candidate/diagnostic scope and any unexecuted qualification. |
| Configuration decision packet | [P4 isolated environment packet](p4-isolated-environment-owner-packet-20261018.md) | Preserve its decision identity and referenced digest. A documentation change is not an infrastructure approval receipt. |

Before citing a result, check the source commit or branch, run and attempt,
environment, artifact identity, and what was actually executed. Keep failure,
skip, blocked, and unexecuted outcomes distinct; a later success does not rewrite
an earlier result. Check effective restrictions against their governing record
rather than inferring expiration from a filename or date.

Topic lists below retain each record's title. Terms such as `current`, `Main`,
`final`, and `candidate` keep the scope defined inside that record. Grouping a
record by subject does not classify it as approved, obsolete, or ready to deploy.

## Governance, Functional CI, and acceptance

Coverage definitions, gate ownership, owner journeys, and acceptance evidence.

- [FCI-06 fresh-request authorization probe](fci06-fresh-request-correlation.md)
- [Authorized Thread projection failure evidence](functional-ci/authorized-thread-projection-diagnostics.md)
- [FCI-05 Files owner acceptance](functional-ci/fci05-files-owner.md)
- [Functional domain execution and exact-candidate evidence](functional-ci/functional-execution-evidence.md)
- [Functional CI gate topology](functional-ci/functional-gate-topology.md)
- [Functional CI journey matrix](functional-ci/functional-journey-matrix.md)
- [Functional CI test policy](functional-ci/functional-test-policy.md)
- [Required-check governance contract](governance-required-checks.md)
- [Retained testing verification snapshot (2026-08-02)](testing-historical-snapshot-20260802.md)
- [MBJ-02 fixed PostgreSQL result receipt](mbj02-named-postgresql-receipt-20261026.md)
- [Group fixture Workspace restoration readiness](mobile-group-entry-readiness-20261029.md)
- [Independent actual-backend MVP0 Mobile qualification](mobile-mvp0-group-dm-20261029.md)
- [Opt-in real-backend mobile acceptance](real-backend-mobile-acceptance.md)
- [Real-backend P0 authorization fixture handoff](real-backend-p0-authorization-handoff.md)

## Build artifacts, diagnostics, and quality coverage

Image identity, evidence retention, diagnostics, and original-source coverage collection.

- [Acceptance image receipt retention and completeness](acceptance-image-receipt-retention-20261019.md)
- [Acceptance wrapper prebuilt image continuity](acceptance-prebuilt-image-reuse-20261019.md)
- [Isolated CI PostgreSQL diagnostic privacy](ci-postgresql-diagnostic-privacy.md)
- [Existing Python operational control collection](existing-python-controls-coverage.md)
- [Original MJS coordinates in Functional coverage](functional-native-representation.md)
- [Explicit Node test registration completion ownership](node-test-promise-ownership.md)
- [Reviewed roster diagnostics and Sonar coverage integration](pilot-roster-sonar-integration-20261028.md)
- [Private default Playwright output](private-playwright-output-20261030.md)
- [Native coverage for existing quality-evidence controls](quality-evidence-native-coverage.md)
- [ReadOnly roster Compose startup observations](readonly-roster-compose-observations.md)
- [ReadOnly roster qualification finalization](readonly-roster-qualification-finally-20261029.md)
- [Real-backend Compose cleanup exit propagation](real-backend-compose-cleanup-exit.md)
- [Runtime image identity across Docker image stores](runtime-image-identity.md)
- [SDK image digest coordination](sdk-image-digest-coordination.md)
- [Security operational Node coverage collection](security-operational-node-coverage.md)
- [Source-bound Python coverage for acceptance image receipts](sonar-acceptance-image-receipt-coverage.md)
- [Angular config strict JSON analysis repair](sonar-angular-config-json-20261029.md)
- [Announcement Sonar pragma compatibility](sonar-announcement-pragma-20261029.md)
- [Sonar .NET coverage import](sonar-dotnet-coverage-20261029.md)
- [Existing regression coverage collection — 2026-10-29](sonar-existing-regression-coverage-20261029.md)
- [Native Node original-source coverage mapping](sonar-native-node-source-maps-20261029.md)
- [Native Node coverage record validation refactor](sonar-node-record-validation-20261029.md)
- [Node regression test coverage classification](sonar-node-test-classification-20261029.md)
- [Qualified operational raw-range conversion](sonar-operational-c8-20261101.md)
- [Original native coverage: diagnostic evidence and import boundary](sonar-operational-coverage-20261101.md)
- [Existing Pilot helper measurement](sonar-pilot-helper-coverage.md)
- [Python measurement for existing privacy and setup-probe controls](sonar-privacy-probe-coverage.md)
- [Audited Python unit-body coverage classification](sonar-python-unit-classification-20261029.md)
- [Native Node coverage for the offline restore advisor](sonar-restore-advisor-coverage-20261029.md)
- [Native coverage for existing SEC-17 execution verifiers](sonar-sec17-execution-coverage-20261029.md)
- [Lone-surrogate AST compatibility](sonar-surrogate-ast-20261029.md)
- [Measuring existing ZAP report and DB-CI controls](sonar-zap-db-ci-measurement.md)
- [Shared ordering support for observation tests](string-order-test-support.md)

## Dependencies and platform upgrades

Version-specific dependency and platform qualification records.

- [ANG22-06 — reviewed third-party pin refresh (PR #1259)](ang22-06-third-party-pins-20261019.md)
- [Angular 22 official migration verification](angular-22-official-migrations.md)
- [Angular 22.2.5 build dependency admission](angular-225-optional-peer-tree-20261029.md)
- [Lucide1.64 compatible update qualification](lucide-164-compatible-update-20261029.md)
- [Node24 production builder digest qualification](node24-production-image-e7add2a-20261029.md)
- [Node.js 26 LTS migration candidate (2026-10-30)](node26-lts-candidate-20261030.md)
- [Compatible npm dependency qualification — 2026-10-29](npm-compatible-qualification-20261029.md)
- [Compatible npm update qualification — 2026-10-31](npm-compatible-renovate-20261031.md)
- [.NET SDK 10.0.302 Upgrade Verification](platform/dotnet-sdk-10.0.302-upgrade.md)
- [Coordinated .NET SDK 10.0.402 upgrade — candidate](platform/dotnet-sdk-10.0.402-upgrade.md)
- [Renovate PR CI triage and coordinated remediation — 2026-10-27](renovate-ci-remediation-20261027.md)

## Security architecture and specification contracts

Exit requirements, isolated fixtures, registry bindings, and their public review boundaries.

- [SEC-ARCH isolated fixture status](security-architecture-fixtures.md)
- [Pinned isolated Kafka launcher qualification](security-architecture-kafka-launcher.md)
- [SEC-ARCH isolated Kafka fixture](security-architecture-kafka.md)
- [SEC-ARCH Phase 2 integration qualification](security-architecture-phase2-integration.md)
- [SEC-ARCH PR producer source binding](security-architecture-pr-producer-sidecar.md)
- [SEC-ARCH pre-Avalonia exit contract](security-architecture-preflight.md)
- [SEC-ARCH registry quality repair](security-architecture-registry-quality-repair.md)
- [Isolated PostgreSQL preparation and recovery controls](security-architecture-rls-recovery.md)
- [Versioned SEC-ARCH assembly binding](security-architecture-six-assembly-binding.md)
- [SEC-ARCH Wave 1 structural verification](security-architecture-wave1.md)
- [specification-contract Advisory adapter](specification-contract-advisory.md)
- [SPEC registry and SEC-ARCH traceability](specification-contract-registry.md)

## Security controls and supply chain

Finding attribution, scanner behavior, browser privacy, and dependency controls.

- [Dependabot / dependency alert remediation audit — 2026-08-01](dependabot-alert-remediation-20260801.md)
- [Dependency automation and independent security scanning](dependency-security-automation-20261026.md)
- [Stable rate-limit response privacy qualification](rate-limit-privacy-trace-fixture-20261031.md)
- [SEC-04 caller main-path controls](sec04-caller-main-controls-20261029.md)
- [SEC-04 deep caller execution boundary](sec04-deep-execution.md)
- [SEC-06 historical attribution disposition (#1080)](sec06-historical-attribution-disposition.md)
- [SEC-08 finding-policy preparation (#582)](sec08-finding-policy-preparation.md)
- [SEC-14 isolated native browser controls](sec14-native-browser-controls.md)
- [SEC-14 original Main API scanner observation](sec14-native-main-observation.md)
- [SEC-14 release evidence consistency and dependency feasibility](sec14-release-evidence-consistency.md)
- [SEC-14 release image graph consistency](sec14-release-image-graph.md)
- [SEC-14 same-subject Advisory evidence](sec14-same-subject-advisory.md)
- [Private local inputs in Docker build contexts](security-build-context-private-inputs.md)
- [SEC-04 integration input attribution repair](security-disclosure-input-attribution.md)
- [Main Security Boolean query contract repair](security-main-boolean-query-repair.md)
- [SEC-04 Workspace icon input attribution candidate](security-workspace-icon-attribution-20261029.md)
- [Socket SCA installation integrity](socket-sca-locked-install-20261101.md)
- [Trivy canonical database acquisition](trivy-canonical-database-source.md)

## Authentication, tenancy, and administration

Session, account, membership, and administrative validation boundaries.

- [Administrative profile and setting contract tests](admin-lifecycle-contract-tests.md)
- [Administrative profile validation order](admin-profile-validation-order.md)
- [Same-document actor replacement: canonical producer qualification](auth-actor-replacement.md)
- [Session hydration response boundaries](auth-session-read-boundary.md)
- [Issue #527 invite acceptance verification](issue-527-invite-acceptance.md)
- [Tenant membership persistence and invalidation controls](tenant-membership-lifecycle-postgresql.md)
- [Tenant platform profile validation](tenant-platform-profile-validation.md)
- [Tenant settings persistence limits](tenant-settings-text-limits.md)

## Workspaces, Groups, and Projects

Creation, activation, membership, scoped reads, and related browser interactions.

- [Group roster keyboard evidence retention](group-keyboard-evidence-retention.md)
- [Group roster keyboard lifecycle observation, version 2](group-keyboard-lifecycle-diagnostics-20261026.md)
- [Group roster immediate boundary keys](group-roster-boundary-keys-20261026.md)
- [Group roster Home and End across nested scrollports](group-roster-home-outer-scroll.md)
- [Group roster viewport reauthorization candidate](group-roster-viewport-reauthorization-20261028.md)
- [P0 Project create and activation verification](p0-project-create-activation.md)
- [Project child list pagination overflow — Main source-bound](project-child-pagination-overflow.md)
- [P0 Workspace sharing header verification](p0-workspace-sharing-header.md)
- [Legacy Project comment mutation authority](project-comment-current-authority.md)
- [Project detail initial response Workspace binding](project-detail-initial-workspace-response.md)
- [Project overview follow-up request scope](project-overview-scope-dispatch.md)
- [Workspace channel directory and authoring candidate](workspace-channel-directory.md)
- [Workspace member detail during routine catch-up](workspace-member-detail-catchup-20261026.md)
- [Workspace member management reads](workspace-member-management-options.md)
- [Workspace search: preserve results during IME Escape](workspace-search-ime-escape-20261029.md)
- [WPC-01 Workspace / Project creation backend foundation](wpc-01-workspace-project-creation-foundation.md)
- [WPC-Final01 Integration Audit](wpc-final01-integration-audit.md)
- [WPC-Final02 Migration / Legacy Compatibility Audit](wpc-final02-migration-legacy-audit.md)
- [WPC-Final03 Security No-Go Audit](wpc-final03-security-no-go-audit.md)
- [WS-01 Announcement parent-scope hardening](ws-01-announcement-parent-scope-hardening.md)
- [WS-01 Workspace dashboard projection verification](ws-01-workspace-dashboard-backend-projection.md)
- [WS-02 Workspace context header verification](ws-02-workspace-context-header.md)
- [WS-03 Workspace creation verification](ws-03-workspace-create.md)

## Tasks, Gantt, and notifications

Task contracts, execution, scheduling, notification delivery, and implementation ledgers.

- [Issue #134 Notification / ReadState closure audit](issue-134-notification-readstate-closure.md)
- [Milestone update validation order](milestone-validation-order.md)
- [My Tasks saved filters verification](my-tasks-saved-filters.md)
- [P0 structured Task Brief verification](p0-task-brief.md)
- [P0 Task-create verification](p0-task-create.md)
- [P0 Task execution Active Source Scope completion](p0-task-execution-scope-completion.md)
- [Issue #357 Task execution source-scope foundation verification](p0-task-execution-scope-foundation.md)
- [P0 Task progress and Activity verification](p0-task-progress-activity.md)
- [P0 Task-create quality checklist verification](p0-task-quality-checklist.md)
- [P0 Task state-list verification](p0-task-state-list.md)
- [P1 Task context summary verification](p1-task-context-summary.md)
- [Optimized Project Task query pagination overflow — Main source-bound](project-task-query-pagination-overflow.md)
- [Task deadline digest hosted lifecycle controls](task-deadline-worker-host-lifecycle.md)
- [Task denial and Project refresh regression](task-denial-project-refresh-regression.md)
- [Task execution intervention service controls](task-intervention-service-controls.md)
- [TASK-V1-PR03C final-head acceptance verification](task-v1-pr03c-detail-contract.md)
- [TASK-V1-PR04 My Tasks verification](task-v1-pr04-my-tasks.md)
- [TASK-V1-PR05 verification record](task-v1-pr05-kanban.md)
- [TASK-V1-PR06 Canonical Gantt implementation and verification ledger](task-v1-pr06-gantt.md)
- [TASK-V1-PR07-A persistence, preferences, and dedupe foundation verification](task-v1-pr07-a-foundation.md)
- [TASK-V1-PR07-B immediate notifications verification](task-v1-pr07-b-immediate-notifications.md)
- [TASK-V1-PR07-C Workspace deadline digest verification](task-v1-pr07-c-deadline-digest.md)
- [TASK-V1-PR07 CI acceptance gate](task-v1-pr07-ci-acceptance-gate.md)
- [TASK-V1-PR07-D — authorized delivery and Angular reconciliation](task-v1-pr07-d-authorized-delivery-angular.md)
- [TASK-V1-PR07 notification, digest, Outbox, and realtime gap audit](task-v1-pr07-gap-audit.md)

## Files, artifacts, and download authorization

Storage outcomes, browser operations, grants, and retained-credential authorization.

- [Artifact version upload compensation](artifact-upload-compensation.md)
- [File upload commit outcomes](file-upload-commit-outcomes.md)
- [Files download failure feedback](files-download-failure-feedback.md)
- [Interrupted local file storage](files-interrupted-storage.md)
- [Files paging and feedback](files-paging-and-feedback.md)
- [Files row input-lifecycle regression controls](files-row-input-lifecycle-20261029.md)
- [Files upload cancellation state and scope invalidation](files-upload-cancellation-state.md)
- [Files upload retry state](files-upload-retry-state.md)
- [Non-enabled Files upload presentation preparation](files-upload-ui-preparation.md)
- [P0 Files contextual actions verification](p0-files-contextual-actions.md)
- [P0 Files inspector tabs verification](p0-files-inspector-tabs.md)
- [P0 Files search and Filter Chips verification](p0-files-search-filter-chips.md)
- [P0 Files sharing-state verification](p0-files-sharing-state.md)
- [Pilot expanded image preview](pilot-expanded-image-preview.md)
- [Pilot image preview controls](pilot-file-image-viewport.md)
- [Files download operation lifetime — 2026-10-26 JST](pilot-files-download-operation-lifetime-20261026.md)
- [SEC-16 selected Attachment grant actor and scope controls](sec16-attachment-grant-scope-20261024.md)
- [SEC-16 — File download-grant real-stack P0 candidate](sec16-file-download-grant-p0.md)
- [SEC-16 selected download-grant lifecycles over real HTTP](sec16-grant-http-lifecycle.md)
- [SEC-16 ordinary-host initial-version read authority](sec16-history-current-authority.md)
- [Retained download credentials across Workspace folder placement](sec16-placement-retained-credentials.md)
- [SEC-16 retained File reads across a Workspace ReadOnly transition](sec16-readonly-retained-credentials.md)
- [SEC-16 residual requirement, test and execution traceability](sec16-residual-traceability-20261023.md)
- [SEC-16 server-owned selection scope and retained-handle control](sec16-selection-scope-retained-handle.md)
- [SEC-16 sharing-policy PUT and retained download credentials](sec16-sharing-policy-retained-credentials.md)
- [SEC-17 current File sharing policy transport candidate](sec17-file-sharing-policy-transport.md)
- [Files folder interaction reliability — 2026-10-18](web-files-folder-reliability-20261018.md)
- [Web Files v2 — verified API/interaction contract matrix](web-p2-files-contract-matrix-20261018.md)
- [Browser-local Starred files — 2026-10-19](web-wave-local-starred-files-20261019.md)

## Messaging, Threads, and search

Conversation and Message behavior, composer lifecycles, search, and Thread diagnostics.

- [Advanced-search drawer lifecycle diagnostics](advanced-search-drawer-lifecycle-diagnostics-20261026.md)
- [Workspace Channel create-click reliability during current-scope refresh — 2026-10-25](channel-create-refresh-click-reliability-20261025.md)
- [Composer comparator evidence: 2026-10-26](composer-comparator-20261026.md)
- [Composer native fill and DOM generation chronology — 2026-10-25](composer-fill-generation-chronology-20261025.md)
- [Composer input chronology: private structural diagnostics — 2026-10-25](composer-input-lifecycle-observation-20261025.md)
- [Conversation-create dialog during routine inbox catch-up](direct-dialog-routine-catchup.md)
- [Legacy Channel/Post edit, soft-delete, pin/unpin persistence — Issue #538](legacy-channel-post-mutation-persistence-20261024.md)
- [Completed Message action feedback during catch-up](message-action-feedback-catchup.md)
- [Message catch-up settlement race](message-catchup-settlement-race.md)
- [Conversation load denial normalization](message-normalized-load-denials.md)
- [Message search mobile opening focus](message-search-mobile-focus.md)
- [Message confirmation and uncertain POST recovery](message-send-confirmation-recovery.md)
- [P0 Message Actions: Issue #343](p0-message-actions.md)
- [Issue #359 Message search and quick-filter verification](p1-message-search-filters.md)
- [Issue #362 Message thread context verification](p1-message-thread-context.md)
- [Issue #367 advanced Message-filter verification](p2-message-advanced-filters.md)
- [Issue #368 saved Message follow-up verification](p2-message-follow-ups.md)
- [Workspace channel create-dialog reauthorization — 2026-10-25](pilot-channel-create-reauthorization-20261025.md)
- [First live Message in an empty conversation](pilot-first-live-message.md)
- [Messaging create-dialog scope-bound asynchronous callbacks](pilot-messaging-create-identity-boundary-20261024.md)
- [Advanced search opener render readiness](search-opener-render-readiness-20261026.md)
- [Inbox search presentation after routine catch-up](search-routine-presentation-20261026.md)
- [Real Thread authorized-root/catch-up ordering — isolated candidate](thread-authorized-catchup-order-20261024.md)
- [Messaging catch-up completion coordination](thread-catchup-completion-coordination-20261026.md)
- [Thread intent during routine catch-up](thread-catchup-local-intent.md)
- [SEC-17 / FCI-06: pre-action denied Thread GET interleaving (diagnostic only)](thread-denial-preaction-interleaving-20261027.md)
- [Held Thread response mechanism investigation](thread-held-response-investigation.md)
- [Thread native navigation during routine catch-up](thread-native-navigation-catchup-gap-20261026.md)
- [Versioned finite Thread observation capacity](thread-observation-budget-20261029.md)
- [Web Thread Workspace — existing API vs multi-root lifecycle](web-p2-thread-contract-matrix-20261018.md)
- [Dedicated Thread Workspace candidate](web-p2-thread-workspace-20261018.md)

## Realtime and Outbox

Transport authorization, connection lifecycles, durable claims, and dispatch failures.

- [Canonical Realtime resolver boundaries](canonical-realtime-resolver-boundaries.md)
- [Realtime authenticated-actor lifecycle boundary](realtime-actor-boundary.md)
- [Scoped Realtime connection invalidation controls](realtime-connection-invalidation-tests.md)
- [Scoped Outbox dispatch failure controls](realtime-outbox-failure-boundaries.md)
- [PostgreSQL Outbox claim boundaries](realtime-outbox-postgresql-claims.md)
- [SEC-17 bounded deep reconnect and concurrent Direct scopes](sec17-bounded-deep-reconnect.md)
- [SEC-17 bounded Group and session transport controls](sec17-group-session-churn.md)
- [SEC-17 ReadOnly reconnect and Thread-write controls](sec17-readonly-thread-reconnect-20261027.md)

## Announcements, forms, and audit

Publication, form admission, audit views, and their authorization and validation boundaries.

- [Announcement current-authority boundary](announcement-current-authority.md)
- [Legacy Announcement service contract tests](announcement-legacy-lifecycle.md)
- [Audit grid native bounds regression](audit-grid-native-bounds-20261029.md)
- [Audit pagination overflow - independent Main repair](audit-pagination-security-main-repair.md)
- [Event list pagination overflow — Main source-bound](event-list-pagination-overflow.md)
- [Internal Form parent authority](form-parent-authority.md)
- [Form list pagination overflow — Main source-bound](form-list-pagination-overflow.md)
- [Form response pagination overflow — Main source-bound](form-response-pagination-overflow.md)
- [Forms answer admission and normalization tests](forms-answer-contract-tests.md)
- [P0 announcement mobile details verification](p0-announcement-mobile-details.md)
- [P0 announcement publish confirmation and durable delivery verification](p0-announcement-publish-confirmation.md)
- [P0 Audit filters and saved views verification](p0-audit-filters-saved-views.md)
- [P0 Audit exact-event sensitive metadata (Issue #349)](p0-audit-sensitive-metadata.md)
- [Issue #382 — Announcement live preview actions and linked attachment](p1-announcement-live-preview-actions.md)
- [P1 announcement local-preview verification](p1-announcement-local-preview.md)
- [P1 Audit UI accessibility hardening (Issue #386)](p1-audit-ui-accessibility.md)
- [P2 Audit loading, empty, error, and recovery states (Issue #389)](p2-audit-load-retry.md)

## Frontend and design reviews

Cross-domain browser foundations, design comparisons, and scoped UI previews.

- [Current Web inventory addendum and architecture guard](current-web-wave-inventory-20261019.md)
- [Dormant Storybook compiler root](dormant-storybook-root-directory.md)
- [P0 Continue-working verification](p0-continue-working.md)
- [P0 structured Report reader and Evidence Inspector](p0-report-evidence-inspector.md)
- [P0 Research Plan (`#364`)](p0-research-plan.md)
- [P1 local Draft and Links workbench](p1-local-draft-links-workbench.md)
- [P1 Research Plan Diff + Impact (`#366`)](p1-research-plan-diff-impact.md)
- [Angular Main CI reliability investigation — 2026-10-18](web-ci-reliability-20261018.md)
- [Web frontend P0/P1 wave, 2026-10-19](web-frontend-wave-20261019.md)
- [Angular integration qualification — 2026-10-18](web-integration-qa-20261018.md)
- [Angular Web P1 independent source audit — 2026-10-18](web-p1-independent-audit-20261018.md)
- [Penpot Web communication audit](web-penpot-communication-audit.md)
- [Penpot Web UI audit: Files and Artifacts](web-penpot-files-audit.md)
- [Angular Web localization and Penpot integration audit](web-penpot-integration-audit.md)
- [Angular Project and Task Penpot audit](web-penpot-projects-audit.md)
- [Web Penpot audit: AppShell, design system, authentication and Workspace](web-penpot-shell-audit.md)
- [Penpot revision 339: Web visual comparison](web-penpot-visual-comparison.md)
- [PR #1234 post-merge Main CI verification](web-pr1234-postmerge-main-ci.md)
- [Web wave independent frontend quality review — 2026-10-19](webwave-independent-quality-20261019.md)
- [Web wave: current HTTP authorization and Unicode boundaries](webwave-runtime-boundaries-20261019.md)
- [Web frontend wave: navigation and messaging foundation](webwave-ui-foundation-20261019.md)

## Release and Student Council pilot

Pilot scope, owner decisions, source-bound release snapshots, and pilot feature workstreams.

- [Pilot SEC-16 / SEC-17 regression integration — 2026-10-24](pilot-sec16-sec17-integration-20261024.md)
- [Pilot SEC-16 / SEC-17 / Thread reauthorization integration](pilot-sec16-thread-combined-20261024.md)
- [Student council pilot: channel and thread workstream](student-council-channel-thread.md)
- [Student Council DM and Group DM implementation boundary](student-council-dm-group-boundary.md)
- [Student Council explicit read action](student-council-explicit-read.md)
- [Student Council Files and preview candidate](student-council-files-preview.md)
- [Student Council Group DM frontend candidate](student-council-group-dm-ui.md)
- [Student Council pilot Home](student-council-home-g04.md)
- [Main381 Messaging diagnostics](student-council-main381-messaging-diagnostics.md)
- [Student Council mobile header follow-up](student-council-mobile-header-polish.md)
- [Student Council Owner decision packet — 2026-10-26 JST](student-council-owner-decisions-20261026.md)
- [Student Council pilot: Penpot traceability](student-council-penpot-traceability.md)
- [Student Council pilot scope](student-council-pilot-scope.md)
- [Student Council source-bound readiness — 2026-10-26 JST](student-council-readiness-20261026.md)
- [Student Council pilot release gate](student-council-release-gate.md)
- [Student Council Web release readiness — 2026-10-23](student-council-release-readiness-20261023.md)
- [Workspace attention-card width follow-up](student-council-workspace-container-polish.md)
- [Workspace member-management presentation](student-council-workspace-member-management.md)
- [Student council pilot: Workspace membership scope audit](student-council-workspace-membership-scope.md)

## Deployment, recovery, and isolated environments

Target-environment prerequisites, synthetic recovery, and isolated configuration records.

- [Logical recovery expression stability — #1355](logical-recovery-schema-stability-20261101.md)
- [Issue #481 public HTTPS production Golden Path](p0-public-https-golden-path.md)
- [P4 isolated environment configuration decision packet](p4-isolated-environment-owner-packet-20261018.md)
- [Post-repair PostgreSQL migration logical-catalog round trip — Main source-bound](postgresql-postrepair-migration-roundtrip.md)
- [Pre-repair PostgreSQL migration-prefix round trip — Main source-bound](postgresql-prerepair-migration-roundtrip.md)
- [Procon on-prem Compose migration verification](procon-onprem-compose-migration.md)
- [Procon on-prem reverse-proxy topology](procon-onprem-reverse-proxy-topology.md)
- [Startup configuration boundary controls](startup-configuration-boundaries.md)
- [Synthetic logical recovery mechanism](synthetic-logical-recovery.md)
- [Synthetic data-only restore FK dependency advisor — issue #1355](synthetic-restore-dependency-advisor.md)

## Performance

Measurement declarations and structural verification. Interpret numerical evidence using the [performance assurance policy](../../performance/ASSURANCE-SUSPENSION.md).

- [API source audit for the rejected bb9 cohort](perf05-api-bb9-source-audit.md)
- [API clock accumulation precision](perf05-api-clock-precision.md)
- [PERF-05 preserved evidence enrollment](perf05-environment-class-enrollment.md)
- [Prospective Medium Main32 environment assignment declaration](perf05-medium-main32-assignment-declaration.md)
- [Authorized medium Main32 public-digest declaration](perf05-medium-main32-declaration.md)
- [PERF-05 Medium zero-capture recovery declaration](perf05-medium-zero-capture-recovery-declaration.md)
- [PERF-05 public compatibility digest naming](perf05-public-digest-schema.md)
- [Authorized small Main32 public-digest declaration](perf05-small-main32-declaration.md)
- [Exact-main DB producer prerequisite correction](performance-db-producer-prerequisite.md)
- [PostgreSQL structural Main integration and indefinite numerical suspension](performance-structural-main-integration.md)

## ProjectIDE and Security Foundation

Core and Security Foundation verification, with each record retaining its own admission and activation scope.

- [Licensed Task source-policy refresh repair](pre-avalonia-licensed-scope-refresh.md)
- [Project IDE Core — #905 Slice 1](project-ide-core-slice-1.md)
- [Security Foundation deterministic binding — SEC-FND-02](security-foundation-binding.md)
- [SEC-FND-01 Domain contracts and runtime boundary](security-foundation-domain-contracts.md)
- [Security Foundation evaluation — SEC-FND-03](security-foundation-evaluation.md)
- [Security Evaluation persistence verification](security-foundation-persistence.md)
- [Pre-Avalonia Security Foundation verification](security-foundation-pre-avalonia.md)
- [Pre-Avalonia Security Foundation Shadow integration](security-foundation-shadow-integration.md)

## MVP-A verification

Phase-specific checklists, environment notes, blockers, and execution records.

- [MVP-A P0 Frontend Accessibility Checklist](mvp-a/frontend-accessibility-checklist.md)
- [MVP-A P0 Frontend CI Stages](mvp-a/frontend-ci-stages.md)
- [MVP-A Environment Notes](mvp-a/mvp-a-environment-notes.md)
- [MVP-A Evidence Log](mvp-a/mvp-a-evidence-log.md)
- [MVP-A Implementation Verification Report](mvp-a/mvp-a-implementation-verification-report.md)
- [MVP-A P0 Blockers](mvp-a/mvp-a-p0-blockers.md)

## Preserve evidence and maintain the index

Keep records with their referenced JSON, screenshots, logs, and artifact receipts.
The initial directory inventory also includes six JSON files and fifteen PNGs;
their owning records retain the links and interpretation. Evidence bundles under
[`docs/evidence/`](../evidence/README.md) and the
[performance contract](../../performance/README.md) have separate entry points.

Some document paths are CI inputs or digest-bound subjects. Check their consumers
before renaming, moving, reformatting, or adding metadata. Record classification
in an index or catalogue when original bytes must remain unchanged. Preserve
historical commit links, original outcomes, and private-source boundaries.

When adding a record, link it from the appropriate topic and state its own scope,
source, and evidence limits. Keep the three canonical Functional CI documents
and other applicable policies discoverable as policies. Review a document's
successor, effective obligations, path consumers, and companion assets before
classifying it as historical or moving it to the [archive](../archive/README.md).
