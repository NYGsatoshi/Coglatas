# Duplication audit: first scoped reduction

Baseline: `main` at `165907995c06e29ad275212ee9206c38983898b1` (PR #849 already merged). This audit precedes the File grant query edit in this branch. No migrations, CI thresholds, detector exclusions, or generated files were changed.

## Method and limits

An exact, whitespace-normalized scan of 1,249 tracked C#, Python, shell, Ruby, MJS, and TypeScript files found 495 pairwise runs of at least 12 lines (8,578 pairwise line appearances). It excluded migrations, `wwwroot`, snapshots, build output, and dependency output. Pairwise appearances overlap and are **not** unique duplicated lines or a Sonar duplication percentage. The full tree also contains 119 migration/snapshot files (about 265,000 lines) and large generated/dependency assets; their size must not drive an application refactor. We inspected high-risk matches for meaning and call sites rather than treating the scanner rank as a fix list. The mass rename in PR #849 obscures ordinary path-based recent-change counts; future priority checks should follow pre-rename history and look at actual defects.

The repository configures SonarQube Cloud automatic analysis in `.sonarcloud.properties` and Qodana in `qodana.yaml`. A public Sonar measure request timed out from this environment and no comparable before/after Qodana duplication measure was available. Do not interpret the local scan as a Sonar/Qodana metric.

## Classification and disposition

| Class | Representative finding | Decision |
| --- | --- | --- |
| 1. Production logic | Effective File grant eligibility copied in `FileRepository`, `FileAccessGrantRepository`, and `DbSearchService` (roughly 50 predicate lines at each site). | **P0, changed here.** One reason to change: revoked grants, Tenant/User status, Workspace membership, File attachment, and external Project membership must agree across inventory, summaries, and search. |
| 2. Test fixture/setup | `AuditFindingReviewerMentionsServiceTests` and `AuditFindingsServiceTests` share 71 lines; Event/Form service tests share 58. PostgreSQL invite tests share 43. | **P2, separate PR.** Extract only the stable seed/builder and keep each scenario and assertion distinct. |
| 3. DTO/mapping/validation | `ProjectsController` repeats 22 lines within two response paths; `ProjectService` repeats a 20-line mapping segment. Task/Project creation paths also use similar validation. | **P1, review separately.** Compare error contracts and optional fields before extracting a typed mapper; changing status/error mapping is not a mechanical refactor. |
| 4. Controller/endpoint/handler | `AuthController` and `InvitesController` share 26 lines; `ProjectActivationController` and `WorkspaceProjectsController` share 20. | **P1 candidate only.** HTTP and authorization outcomes differ; retain controller ownership unless the same domain-neutral conversion is proved. |
| 5. EF Core/repository/query | Current/canonical authorization target resolvers share 79 lines; File grant query appears in three repositories; audit services have smaller shared projections. | **P0 File grant query here.** Defer resolver work: canonical Project/Task authorization intentionally differs from legacy target families and a careless merge can change delivery authority. |
| 6. CI/shell/Python | GCP deployment scripts share 65 lines; `install-syft.sh` and `install-grype.sh` share 28; CI acceptance runners share about 27. | **P2, separate PR.** Compare trust boundaries, tool pinning, inputs and workflow outputs before creating a shared script. |
| 7. Angular/TypeScript | Project/workspace create dialogs share 27 lines; audit result/severity badges share 28; multiple spec setups repeat 20–32 lines. | **P3, retained.** Angular is planned for replacement by Avalonia; no new frontend abstraction is justified for these matches. |
| 8. Intentional EF migration/snapshot | 119 migration/snapshot files, about 265,000 lines, including historical model snapshots and generated designers. | **Keep.** Do not rewrite migration history or manually edit generated snapshots. |
| 9. OpenAPI/generated/vendor assets | Hosted `src/Coglatas.Web/wwwroot`, browser snapshots, `package-lock.json`, build output and generated OpenAPI contract output. | **Keep.** Edit their source/generator only when behavior requires it. Existing analysis exclusions are unchanged. |
| 10. Similar syntax, different meaning | Event/Form authorization services share 47 lines but own different resource life cycles; canonical and legacy target resolvers share code but have separate Project visibility rules. | **Keep pending a behavioral comparison.** A generic cross-domain helper would obscure ownership and may weaken authorization. |

## First patch and follow-up order

`EffectiveFileAccessGrantQuery.For` in Infrastructure now owns the unchanged EF predicate. The File inventory, effective grant repository, and File search compose their own final projections over it. The query remains `AsNoTracking`; all predicates, including Tenant ID consistency and the active external Project path, are preserved. Before: three predicate copies. After: one predicate and three typed call sites. This removes roughly two copies (about 100 repeated predicate lines); the net code change should be assessed with the PR diff, not a repository-wide duplication percentage.

Priority considers potential unauthorized File discovery above raw block length. Next P0 investigation: compare the two current authorization target resolvers with their WPC-Final01 contract and PostgreSQL tests before touching them. P1: Project/Task mapping and validation, especially pagination and API error consistency. P2: test builders and CI scripts. P3: only small correctness fixes in Angular.

The active `__AIP_FEATURE_FLAGS__` runtime key remains in `Program.cs`, Angular, and UI tests. Rename guard passes because it does not flag that embedded token. Treat its contract migration as a separately tested rename follow-up; changing one side alone would break runtime feature flags. Historical logs and references to the external `AIPsiteNYGspec` repository remain evidence, not rename candidates.

## Verification needed for this patch

The new regression test checks valid access and verifies both grant revocation and membership suspension hide the private File from grant checks, summaries, and Workspace inventory. Run `dotnet restore`, Release solution build, this focused test, the File authorization and Tenant isolation/security suites, full .NET tests, migration pending-model check, architecture tests, and rename guard on the PR branch. PostgreSQL tests require `POSTGRES_TEST_CONNECTION_STRING`; an InMemory test alone does not prove SQL translation. Record CI results before merging.
