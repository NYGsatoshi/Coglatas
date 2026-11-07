# Documentation

Use this index to find the guide, contract, or record for a task. Start with the
[project overview](../README.md) for product scope and the repository layout.

[AGENTS.md](../AGENTS.md) defines the authority order: current source, tests, CI,
and root deployment configuration; active status documentation; the
[roadmap](ROADMAP.md); then the [archive](archive/README.md).
A guide can contain dated evidence or candidate sections. Check their source
and scope before treating a statement as current implementation.

## Start a change

Read [AI context](AI_CONTEXT.md), [known issues](KNOWN_ISSUES.md), and
[coding rules](CODING_RULES.md), then choose the task-specific documents below.
Follow the [project language policy](PROJECT_LANGUAGE.md) and
[contribution terms](../CONTRIBUTING.md).

| Entrance | Purpose |
| --- | --- |
| This index | Find documentation by the work you need to do |
| [AI context](AI_CONTEXT.md) | Repository context, implementation qualifications, and agent reading routes |
| [Verification index](verification/README.md) | Find verification policies, instructions, and records by domain |
| [Evidence index](evidence/README.md) | Find retained baseline/failure pairs and their log bundles |
| [Archive index](archive/README.md) | Find historical plans, reports, and superseded explanations |

## Development and implementation

| Task | Read |
| --- | --- |
| Set up local development | [Development](DEVELOPMENT.md), [development modes](../README.dev-env.md), and the [Angular workspace README](../frontend/README.md) |
| Understand system responsibilities | [Architecture](ARCHITECTURE.md), [architecture documents](architecture/), and the [backend logic audit](BACKEND_LOGIC_AUDIT.md) |
| Change APIs or domain behavior | [API conventions](API_CONTRACTS.md), [detailed contracts](contracts/), and relevant [decisions](decisions/) |
| Work on schema or persistence | [Database](DATABASE.md) for schema sources and migration rules; [data model](DATA_MODEL.md) for the human-readable field inventory |
| Work on frontend behavior | [Frontend platform policy](frontend/platform-policy.md), [internationalization](FRONTEND_I18N.md), and [frontend documents](frontend/) |
| Work on Syncfusion integration | [License runbook](SYNCFUSION_LICENSE_RUNBOOK.md) and [adapter foundation](frontend/syncfusion-adapter-foundation.md) |
| Prepare deterministic demo data | [Demo dataset](demo-dataset.md) and the [U-22 fixture guide](u22/demo-data.md) |

## Security and operation

| Task | Read |
| --- | --- |
| Understand engineering security requirements | [Security guide](SECURITY.md), [security rules](SECURITY_RULES.md), and [authorization matrix](AUTHORIZATION_MATRIX.md) |
| Assess implementation boundaries | [Security model](SECURITY_MODEL.md), [known issues](KNOWN_ISSUES.md), and the [security baseline documents](security/) |
| Report a vulnerability | [Reporting policy](../SECURITY.md) and the [detailed reporting guidance](../.github/SECURITY.md) |
| Configure deployment or recovery | [Deployment](DEPLOYMENT.md), [operations](OPERATIONS.md), [GCP guide](../deploy/gcp/README.md), and [Sakura guide](../deploy/sakura/README.md) |
| Review repository publication or ownership controls | [Publication runbook](PUBLICATION_RUNBOOK.md), [contribution terms](../CONTRIBUTING.md), and the [generated governance control matrix](../governance/controls.md) |
| Inspect security-architecture tooling | [Tool README](../tools/Coglatas.SecurityArchitecture/README.md), [preflight contract](verification/security-architecture-preflight.md), and [fixture coverage record](verification/security-architecture-fixtures.md) |

Deployment instructions and structural tool validation do not establish
qualification on a target host. Read the applicable environment evidence and
approval conditions in the linked records.

## Testing and CI

| Task | Read |
| --- | --- |
| Choose and run tests | [Testing](TESTING.md) and [continuous integration](CONTINUOUS_INTEGRATION.md) |
| Work on real functional coverage | [Functional CI policy](verification/functional-ci/functional-test-policy.md), [journey matrix](verification/functional-ci/functional-journey-matrix.md), and [functional test architecture](../tests/functional/README.md) |
| Change execution or compatibility tooling | [CI documents](ci/), [Compose harness](ci/functional-compose-harness.md), and [critical compatibility suite](ci/compat-critical-suite.md) |
| Work on static analysis | [Static-analysis strategy](quality/static-analysis-strategy.md), [quality documents](quality/), and [frontend inspection tooling](../tools/frontend-inspections/README.md) |
| Interpret performance checks | [Performance contracts](../performance/README.md) and the [numerical-assurance suspension policy](../performance/ASSURANCE-SUSPENSION.md) |
| Locate a result or audit | [Verification index](verification/README.md), [evidence index](evidence/README.md), and [audit records](audit/) |

Use the source-defined scope of each check. Mocked browser behavior, real
functional coverage, structural validation, numerical assurance, and release
acceptance are different claims.

## Decisions, candidates, and history

- [Roadmap](ROADMAP.md): intended future scope.
- [Decision records](decisions/): the recorded decision, its authority, and any
  unresolved conditions.
- [Migration documents](migration/): scoped inventories, proposals, and
  migration boundaries; use the [frontend platform policy](frontend/platform-policy.md)
  when interpreting older frontend directions.
- [U-22 submission materials](u22/README.md): a bounded submission package,
  with its own fixture guidance and release requirements.
- [Archive index](archive/README.md): historical plans, status snapshots,
  reports, and superseded specifications.

Product requirements are maintained separately in the access-controlled
`Coglatas-Spec` repository. Authorized reviewers must verify private
requirements there. Keep public references limited to sanitized identifiers and
the minimum reviewable behavior; do not copy private specification text.

A document on a candidate branch or a Draft PR is scoped to that candidate.
Its presence in an index does not adopt it into `main` or approve activation.

## Read evidence in context

For a verification claim, retain the source commit, environment, workflow
run/attempt, and artifact identity supplied by the record. A historical result
describes that execution; it is not automatically a result for a later commit.

Document lifecycle, implementation state, execution result, and approval state
are separate. Preserve failed or blocked evidence, and check the governing
decision before changing an effective restriction. See the
[evidence index](evidence/README.md) for the retained bundles.

When adding a document, link it from the relevant entrance and state its scope.
Keep component and deployment instructions beside their owning assets.
Generated views and digest-bound evidence require their existing update
procedures; adding navigation does not change those contracts.

## Maintain documentation

Before adding, splitting, moving, or archiving a page, read the
[maintenance guide](DOCUMENTATION_MAINTENANCE.md), inspect its entry in the
[document catalogue](catalog.yml), and check the
[disposition and move ledger](DOCUMENTATION_MOVES.md).
The catalogue records document roles and known dependencies; it does not
change implementation, verification, or approval status. Keep generated
views, evidence subjects, and CI-bound paths under their existing contracts.
