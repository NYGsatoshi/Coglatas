# WPC-Final02 Migration / Legacy Compatibility Audit

Status: implementation complete on the audit branch; PostgreSQL 18 acceptance pending this PR's CI result.

## Audited baselines

- Implementation repository: `NYGsatoshi/Coglatas`
- Implementation `main`: `31a5d2986d8a83c31fbaee22a679042a17b4335c`
- Normative specification repository: `NYGsatoshi/Coglatas-Spec`
- Specification `main`: `38339ba2964587f225c4c4151f643abb5523e862`
- Normative acceptance: `docs/specs/coglatas-core-v4/03-acceptance/workspace-project-canonical-completion-acceptance.md`
- Owner decisions: `docs/specs/coglatas-core-v4/01-core/17-workspace-project-canonical-completion-owner-decision-resolution.md`

This audit is intentionally separate from WPC-Final01. WPC-Final01 owns the integrated A-through-F business acceptance gate; WPC-Final02 owns schema evolution, legacy-row compatibility, rollback boundaries, and migration-time tenant isolation.

## Audited migration chain

| Order | Migration | Upgrade behavior | Downgrade behavior |
|---:|---|---|---|
| 1 | `20260816041835_Wpc02AProjectVisibilityAndActivationProvenance` | Adds nullable Project Visibility and explicit activation/recovery provenance. Existing Projects are assigned `ActivationState = LegacyUnknown`; Visibility and historical provenance remain unknown. | Drops all WPC-02A Visibility and activation/provenance columns, indexes, and constraints. Any canonical classification or activation provenance written after upgrade is lost. |
| 2 | `20260817023749_Wpc02BCapabilityGrantWorkspaceGeneral` | Adds nullable Conversation `DefaultKind` and `Visibility`, generic `capability_grants`, canonical default-shape checks, and partial uniqueness indexes. Existing Conversations remain unclassified; the migration does not infer `WorkspaceGeneral` or `ProjectGeneral` from names or relationships. | Drops the grant table and both Conversation columns. Delegated grants and canonical default identity/visibility written after upgrade are lost. |
| 3 | `20260819070000_Wpc02DTaskWorkflowTemplates` | Adds Tenant/Workspace workflow-template configuration tables with composite tenant-bound foreign keys. It does not replace or regenerate existing per-Project workflow definitions or stages. | Drops all template/default configuration tables. Configured Tenant/Workspace workflow defaults written after upgrade are lost. Existing per-Project workflow definitions remain because they predate WPC-02D. |

The timestamp ordering is canonical and must remain A, then B, then D. These migrations share one `AppDbContextModelSnapshot`; parallel or reordered schema histories are unsupported.

## Legacy compatibility result

The required PostgreSQL acceptance seeds data at the WPC-01 schema boundary and then applies the complete WPC chain. It verifies all of the following:

1. Historical Projects become `ActivationState = LegacyUnknown` with `Visibility = NULL` and no fabricated activation timestamp, version, suspended source, or archived source.
2. A legacy Project-channel Conversation named `general` remains `DefaultKind = NULL` and `Visibility = NULL`. Its name, type, and Project relationship are not used to invent canonical default identity.
3. A compatible legacy Project workflow retains the same definition ID, Tenant/Workspace/Project tuple, name, policy, version, stage IDs, categories, order, terminal/initial flags, and Task-to-stage reference.
4. Upgrade does not synthesize capability grants, WorkspaceGeneral/ProjectGeneral identities, workflow templates, or Tenant/Workspace defaults for legacy rows.
5. Two independent Tenant graphs remain bound to their original Tenant, Workspace, Project, Conversation, workflow, stages, and Task references.
6. The final EF Core model has no pending migration or model-snapshot change.

## Tenant-isolation and uniqueness result

WPC-Final02 pins two distinct enforcement boundaries:

- WPC-02D Tenant and Workspace defaults are protected by composite foreign keys. A default row cannot bind one Tenant or Workspace to another Tenant's workflow template.
- WorkspaceGeneral and ProjectGeneral identities are protected by partial unique indexes. Display-name changes cannot create a second canonical default in the same scope.

`CapabilityGrant.ScopeId` is polymorphic and is not a direct Workspace foreign key. Therefore, a malformed Workspace-scoped grant can be persisted through direct SQL with a ScopeId belonging to another Tenant. This is not accepted as authorization evidence. The authoritative `CapabilityGrantEvaluator` re-resolves the current Workspace, verifies its Tenant, and rejects that grant. Consequences:

- application code must not authorize from direct `capability_grants` table reads;
- every current authorization decision must use the canonical evaluator;
- future consumers or reporting paths that interpret grants must preserve the same Tenant and current-resource validation;
- bypassing the evaluator is a security defect.

The Final02 PostgreSQL test persists this malformed row deliberately and requires the production evaluator to return `false`.

## Rollback boundary

All three WPC `Down()` paths are schema downgrades, not lossless operational rollback mechanisms.

### Allowed rollback

An application-version rollback may keep the upgraded database schema when the older application version is confirmed to tolerate the additive columns/tables. This is the preferred emergency rollback shape.

A database downgrade is allowed only when all of the following are true:

1. writes using the affected WPC schema have been stopped;
2. a verified database backup or snapshot exists;
3. operators accept the data-loss boundary listed in the migration table above;
4. the downgrade is executed in a maintenance window;
5. reapplication and post-restore verification are prepared.

### Prohibited rollback

Do not automatically migrate down after any of these have become authoritative:

- explicit Project Visibility or activation provenance;
- delegated capability grants;
- canonical Conversation identity or visibility;
- Tenant or Workspace workflow-template defaults.

A code rollback that requires those values must use backup/restore or a corrective forward migration. Running `Down()` and then reapplying cannot reconstruct lost WPC-owned values.

### What the automated rollback test proves

The automated test uses legacy rows created before the WPC schema and deliberately does not create WPC-owned post-upgrade state. It proves that:

- D can roll back to B and reapply;
- B can roll back to A and reapply;
- A can roll back to the WPC-01 boundary and reapply;
- pre-existing Project, Conversation, workflow, stage, and Task references survive each boundary;
- reapplication restores unknown/null classifications rather than inventing history;
- the latest model is clean after the full cycle.

It does not claim that post-upgrade WPC data survives schema downgrade. The migration definitions show that it does not.

## CI evidence

`.github/workflows/wpc-final02-migration-legacy-acceptance.yml` runs on PostgreSQL 18 and:

1. validates the fixed three-test manifest;
2. restores and builds the solution;
3. applies the latest schema to a real PostgreSQL database;
4. rejects pending EF Core model changes;
5. executes only the `Scope=WPCFinal02` PostgreSQL acceptance cases;
6. requires all named tests to appear in the TRX;
7. uploads the TRX as retained evidence.

Required tests are pinned in `scripts/ci/wpc-final02-migration-required-tests.txt`.

## Local reproduction

Use a PostgreSQL role that can create and drop temporary databases because each scenario creates an isolated database:

```bash
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=replace-me'
export POSTGRES_TEST_CONNECTION_STRING="$ConnectionStrings__DefaultConnection"

dotnet restore Coglatas.slnx --disable-parallel
dotnet build Coglatas.slnx --configuration Release --no-restore -m:1
mkdir -p artifacts/wpc-final02-migration-results
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj \
  --configuration Release \
  --no-build \
  -m:1 \
  --filter 'Scope=WPCFinal02' \
  --logger 'trx;LogFileName=wpc-final02-migration.trx' \
  --results-directory artifacts/wpc-final02-migration-results
bash scripts/ci/verify-trx-results.sh \
  artifacts/wpc-final02-migration-results/wpc-final02-migration.trx \
  --minimum-total 3 \
  --required-tests scripts/ci/wpc-final02-migration-required-tests.txt \
  --label 'WPC-Final02 migration and legacy compatibility'
```

The tests are not valid evidence when `POSTGRES_TEST_CONNECTION_STRING` is absent.

## Merge gate and dependency

WPC-Final02 remains draft until:

- WPC-Final01 PR #321 is merged or this branch is rebased onto its final merged result;
- the dedicated PostgreSQL 18 workflow passes;
- standard repository build, test, and security checks pass;
- the branch has no pending EF Core model change;
- no unresolved review thread remains;
- no unrelated production or migration change is introduced;
- auto-merge remains disabled.

No corrective production migration is proposed by this audit unless the runtime acceptance reveals a defect.
