using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.ProjectIde.Evaluations;
using Coglatas.Application.Projects;
using Coglatas.Application.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Coglatas.Ui.Core.Interaction;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Current native lifecycles under isolated prototype authority, not approved applicability or product integration.</summary>
public sealed class SecurityArchitectureRlsNativeLifecycleTests
{
    [PostgreSqlFact]
    public async Task ActualCanonicalAppendAndPersistenceLifecyclesPreserveImmutableRowsUnderAllDraftPolicies()
    {
        await WithFixtureAsync(allPolicies: true, async fixture =>
        {
            var project = (Project)fixture.Alpha.Entities["projects"];
            var foreignProject = (Project)fixture.Beta.Entities["projects"];
            var task = (TaskItem)fixture.Alpha.Entities["task_items"];
            var file = (FileObject)fixture.Alpha.Entities["file_objects"];
            var foreignFile = (FileObject)fixture.Beta.Entities["file_objects"];
            var user = (User)fixture.Alpha.Entities["users"];
            var scope = new ContextScope(fixture.Alpha.Tenant.ToString(), project.WorkspaceId.ToString(), project.Id.ToString());
            var foreignScope = new ContextScope(fixture.Beta.Tenant.ToString(), foreignProject.WorkspaceId.ToString(), foreignProject.Id.ToString());
            var key = CoglatasUiCanonicalChangeJournalCoordinator.CreateScopeKey(scope);
            var foreignKey = CoglatasUiCanonicalChangeJournalCoordinator.CreateScopeKey(foreignScope);
            // Each coordinator owns its real transaction; the test interceptor supplies only that transaction's scope.
            await using (var foreign = Context(fixture, fixture.Beta.Tenant))
                Assert.Equal(1, await new CoglatasUiCanonicalChangeJournalCoordinator(foreign).ExecuteAuthoritativeMutationAsync(
                    foreignScope, CanonicalDependencyDomain.WorkflowMetadata, static (_, _, _) => Task.CompletedTask));
            await using (var context = Context(fixture, fixture.Alpha.Tenant))
            {
                var coordinator = new CoglatasUiCanonicalChangeJournalCoordinator(context);
                Assert.Equal(1, await coordinator.ExecuteAuthoritativeMutationAsync(scope,
                    CanonicalDependencyDomain.WorkflowMetadata, static (_, _, _) => Task.CompletedTask));
                Assert.Equal(2, await coordinator.ExecuteAuthoritativeMutationAsync(scope,
                    CanonicalDependencyDomain.Auxiliary, static (_, _, _) => Task.CompletedTask));
                var denied = await Assert.ThrowsAsync<PostgresException>(() => coordinator.ExecuteAuthoritativeMutationAsync(foreignScope,
                    CanonicalDependencyDomain.Auxiliary, static (_, _, _) => Task.CompletedTask));
                Assert.Equal("RLS_WITH_CHECK", SecurityArchitectureRlsOperationTests.Classify(denied));
                var snapshot = await JournalStateAsync(fixture.Database, key);
                await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAuthoritativeMutationAsync(scope,
                    CanonicalDependencyDomain.Auxiliary, static (_, _, _) => throw new InvalidOperationException("Synthetic staged failure")));
                Assert.Equal(snapshot, await JournalStateAsync(fixture.Database, key));
            }
            Assert.Equal((2L, 2L), await JournalStateAsync(fixture.Database, key));
            Assert.Equal((1L, 1L), await JournalStateAsync(fixture.Database, foreignKey));
            var versions = await VersionStateAsync(fixture.Database, file.Id);
            var sourcePolicy = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
                "SELECT \"VersionNo\" FROM project_execution_scopes WHERE \"ProjectId\"=@project", ("project", project.Id));
            await using (var context = Context(fixture, fixture.Alpha.Tenant))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var repository = new TaskExecutionScopeRepository(context);
                var current = Assert.IsType<ProjectExecutionScope>(await repository.GetProjectScopeForUpdateAsync(project.Id));
                current.ProjectFilesEnabled = !current.ProjectFilesEnabled;
                current.VersionNo++;
                Assert.Null(await repository.GetProjectScopeForUpdateAsync(foreignProject.Id));
                var localOverride = Assert.IsType<TaskExecutionScopeOverride>(await repository.GetTaskOverrideForUpdateAsync(task.Id));
                repository.RemoveTaskOverride(localOverride);
                var files = new FileRepository(context);
                var currentFile = Assert.IsType<FileObject>(await files.GetFileObjectAsync(file.Id));
                currentFile.MarkDeleted(DateTimeOffset.UtcNow, user.Id, "Synthetic lifecycle preservation");
                Assert.Null(await files.GetFileObjectAsync(foreignFile.Id));
                Assert.Equal(3, await context.SaveChangesAsync());
                Assert.Single(await files.ListFileVersionsAsync(fixture.Alpha.Tenant, file.Id, 10));
                await transaction.RollbackAsync();
            }
            Assert.Equal(versions, await VersionStateAsync(fixture.Database, file.Id));
            Assert.Equal(sourcePolicy, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
                "SELECT \"VersionNo\" FROM project_execution_scopes WHERE \"ProjectId\"=@project", ("project", project.Id)));
            Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
                "SELECT count(*) FROM task_execution_scope_overrides WHERE \"TaskItemId\"=@task", ("task", task.Id)));
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(fixture.Database,
                "SELECT \"DeletedAt\" IS NOT NULL FROM file_objects WHERE \"Id\"=@file", ("file", file.Id)));
            await WritePrivateAsync(fixture, "canonical-and-persistence", 105,
                ["ActualCanonicalAppend", "ActualRevisionAdvance", "ForeignCoordinatorPositiveBeforeWithCheckDenial", "CanonicalStagedRollback",
                 "PersistentProjectPolicyUpdate", "TaskOverrideClearMeansInheritance", "FileSoftDeletePreservesImmutableVersions", "PersistenceRollback"],
                ["AppendOnlyRowsHaveNoDirectUpdateOrDeleteLifecycle", "ConcreteApplicationResourceAuthorityUnverified", "FullComposedHostStartupUnverified"]);
        });
    }

    [PostgreSqlFact]
    public async Task ActualSecurityStoreTerminalizesOnceWithCurrentAuthorizationAndScopedRuleAppend()
    {
        // Authorization reads precede this adapter's owned transaction. Its identity/member/project dependencies
        // deliberately retain the inactive product semantics; this proves two selected policies, not all105 startup.
        await WithFixtureAsync(allPolicies: false, async fixture =>
        {
            foreach (var seed in new[] { fixture.Alpha, fixture.Beta })
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
                    UPDATE projects SET "Status"='Active',"ActivationState"='Activated',"ActivatedAtUtc"=now(),"ActivationVersion"=1 WHERE "TenantId"=@tenant;
                    UPDATE workspace_members SET "Status"='Active',"Role"='Owner' WHERE "TenantId"=@tenant;
                    UPDATE project_members SET "Role"='Owner' WHERE "TenantId"=@tenant;
                    """, ("tenant", seed.Tenant));
            var first = Binding(fixture.Alpha);
            var foreign = Binding(fixture.Beta);
            var decision = new SecurityDecision(SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified,
                [new("synthetic.a", SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified)]);
            await using (var context = Context(fixture, fixture.Beta.Tenant))
            {
                var store = Store(context, fixture.Beta);
                Assert.True(await store.CreatePendingAsync(foreign));
                Assert.Equal(SecurityTerminalizationResult.Terminalized, await store.TerminalizeAsync(foreign, decision));
            }
            await using (var context = Context(fixture, fixture.Alpha.Tenant))
            {
                var store = Store(context, fixture.Alpha);
                Assert.True(await store.CreatePendingAsync(first));
                Assert.Equal(SecurityTerminalizationResult.Terminalized, await store.TerminalizeAsync(first, decision));
                Assert.Equal(SecurityTerminalizationResult.AlreadyTerminal, await store.TerminalizeAsync(first, decision));
                Assert.False(await store.CreatePendingAsync(foreign));
                Assert.Equal(SecurityTerminalizationResult.Unavailable, await store.TerminalizeAsync(foreign, decision));
            }
            Assert.Equal(("Completed", 1L), await EvaluationStateAsync(fixture.Database, first.Request.EvaluationId));
            Assert.Equal(("Completed", 1L), await EvaluationStateAsync(fixture.Database, foreign.Request.EvaluationId));
            var revoked = Binding(fixture.Alpha);
            await using (var context = Context(fixture, fixture.Alpha.Tenant))
            {
                var store = Store(context, fixture.Alpha);
                Assert.True(await store.CreatePendingAsync(revoked));
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                    "UPDATE workspace_members SET \"Status\"='Suspended' WHERE \"TenantId\"=@tenant", ("tenant", fixture.Alpha.Tenant));
                Assert.Equal(SecurityTerminalizationResult.Unavailable, await store.TerminalizeAsync(revoked, decision));
            }
            Assert.Equal(("Pending", 0L), await EvaluationStateAsync(fixture.Database, revoked.Request.EvaluationId));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE workspace_members SET \"Status\"='Active' WHERE \"TenantId\"=@tenant", ("tenant", fixture.Alpha.Tenant));
            await using (var context = Context(fixture, fixture.Alpha.Tenant))
                Assert.Equal(SecurityTerminalizationResult.Terminalized, await Store(context, fixture.Alpha).TerminalizeAsync(revoked, decision));
            Assert.Equal(("Completed", 1L), await EvaluationStateAsync(fixture.Database, revoked.Request.EvaluationId));
            await WritePrivateAsync(fixture, "security-terminalization", 2,
                ["ActualPendingRunCreation", "ActualScopedRuleAppendBeforeTerminalization", "TerminalizeOnce", "ForeignPositiveBeforeScopeDenial",
                 "CurrentWorkspaceMembershipRevocationPreservesPendingRun", "RestoredMembershipPositive"],
                ["PreTransactionIdentityAndProjectAuthorizationRemainUnprotected", "All105PolicyStartupUnverified", "FutureRetentionOperatorAuthorityUnverified"]);
        });
    }

    private sealed record Fixture(string Database, string Connection, string Role, SecurityArchitectureRlsRowFixtures.Seed Alpha,
        SecurityArchitectureRlsRowFixtures.Seed Beta, SecurityArchitectureRlsRuntimeContext.Recorder Recorder);

    private static async Task WithFixtureAsync(bool allPolicies, Func<Fixture, Task> scenario)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_lifecycle_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var beta = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    """);
                var tables = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                    SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_attribute a ON a.attrelid=c.oid
                    WHERE n.nspname='public' AND c.relkind IN ('r','p') AND a.attname='TenantId' AND NOT a.attisdropped ORDER BY c.relname
                    """, reader => reader.GetString(0));
                Assert.Equal(103, tables.Count);
                if (!allPolicies) tables = ["security_evaluation_runs", "security_evaluation_rule_results"];
                foreach (var table in tables)
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_lifecycle ON public."{table}" TO "{role}"
                            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                            WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                        """);
                if (allPolicies)
                {
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        GRANT SELECT ON coglatas_ui_canonical_revision_heads,coglatas_ui_canonical_change_journal,
                            project_execution_scopes,task_execution_scope_overrides,file_objects,file_versions TO "{role}";
                        GRANT SELECT ("Id","DisplayName") ON users TO "{role}";
                        GRANT SELECT ("Id","Status","DeletedAt") ON tenants TO "{role}";
                        ALTER TABLE file_selection_snapshot_items ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE file_selection_snapshot_items FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_lifecycle ON file_selection_snapshot_items TO "{role}"
                            USING (EXISTS(SELECT 1 FROM file_selection_snapshots parent WHERE parent."Id"="SelectionSnapshotId"));
                        ALTER TABLE coglatas_ui_canonical_change_journal ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE coglatas_ui_canonical_change_journal FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_lifecycle ON coglatas_ui_canonical_change_journal TO "{role}"
                            USING (EXISTS(SELECT 1 FROM coglatas_ui_canonical_revision_heads parent WHERE parent."ScopeKey"=coglatas_ui_canonical_change_journal."ScopeKey"));
                        GRANT INSERT,UPDATE ON coglatas_ui_canonical_revision_heads TO "{role}";
                        GRANT INSERT ON coglatas_ui_canonical_change_journal TO "{role}";
                        GRANT UPDATE ON project_execution_scopes,file_objects TO "{role}";
                        GRANT DELETE ON task_execution_scope_overrides TO "{role}";
                        """);
                }
                else
                {
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        GRANT SELECT ON security_evaluation_runs,security_evaluation_rule_results,projects,project_members,
                            workspaces,workspace_members,groups,group_members,tenant_users,tenants,users TO "{role}";
                        GRANT INSERT,UPDATE ON security_evaluation_runs TO "{role}";
                        GRANT INSERT ON security_evaluation_rule_results TO "{role}";
                        """);
                }
                var connection = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1 }.ConnectionString;
                try { await scenario(new(database, connection, role, alpha, beta, new())); }
                finally { using var pooled = new NpgsqlConnection(connection); NpgsqlConnection.ClearPool(pooled); }
            });
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\""); }
    }

    private static AppDbContext Context(Fixture fixture, Guid tenant) => SecurityArchitectureRlsRuntimeContext.Create(fixture.Connection,
        new(tenant, "syntheticSuppliedLifecycleAuthority", null, null), fixture.Recorder);

    private static SecurityEvaluationStore Store(AppDbContext context, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(seed.Tenant, "synthetic-current-authority");
        var workspaces = new WorkspaceRepository(context);
        var groups = new GroupRepository(context);
        var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(context), workspaces,
            new TenantAuthorizationService(new TenantRepository(context)));
        var projectAuthorization = new ProjectAuthorizationService(new ProjectRepository(context), workspaceAuthorization,
            new GroupAuthorizationService(groups, workspaces, workspaceAuthorization), groups);
        return new(context, tenant, new Actor(((User)seed.Entities["users"]).Id), projectAuthorization, new Clock());
    }

    private static SecurityBinding Binding(SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        var revision = SourceRevisionContext.Committed(new(new(new(seed.Tenant), new(((Project)seed.Entities["projects"]).Id), BranchId.New()), RevisionId.New()));
        var document = SourceDocument.Create(DocumentId.New(), "project", SourceJson.Parse("""
            {"entityId":"00000000-0000-4000-8000-000000000006","kind":"coglatas.project","schemaVersion":1,"payload":{}}
            """));
        var source = ProjectSource.Create(revision, [document]);
        return SecurityBinding.Create(new(Guid.NewGuid(), new(new(seed.Tenant), ((User)seed.Entities["users"]).Id), new("projectide.analyze"),
            new(revision, source.Digest), SecurityEnforcementMode.Shadow, source), new(source));
    }

    private static async Task<(long Revision, long Entries)> JournalStateAsync(string database, string key) => (
        await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT \"Revision\" FROM coglatas_ui_canonical_revision_heads WHERE \"ScopeKey\"=@key", ("key", key)),
        await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT count(*) FROM coglatas_ui_canonical_change_journal WHERE \"ScopeKey\"=@key", ("key", key)));
    private static Task<string> VersionStateAsync(string database, Guid file) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
        "SELECT encode(sha256(convert_to(string_agg(to_jsonb(v)::text,'|' ORDER BY \"Id\"),'UTF8')),'hex') FROM file_versions v WHERE \"FileObjectId\"=@file", ("file", file));
    private static async Task<(string Status, long Rules)> EvaluationStateAsync(string database, Guid evaluation) => (
        await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, "SELECT \"Status\" FROM security_evaluation_runs WHERE \"Id\"=@id", ("id", evaluation)),
        await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT count(*) FROM security_evaluation_rule_results WHERE \"EvaluationId\"=@id", ("id", evaluation)));

    private sealed class Actor(Guid user) : ICurrentUser
    {
        public Guid? UserId => user;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private static async Task WritePrivateAsync(Fixture fixture, string name, int policyCount, string[] controls, string[] holds)
    {
        await using var observed = new NpgsqlConnection(fixture.Connection);
        await observed.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_user,r.rolsuper,r.rolbypassrls FROM pg_roles r WHERE r.rolname=current_user", observed);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var role = reader.GetString(0);
        Assert.Equal(fixture.Role, role);
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database, """
            SELECT count(*) FROM pg_roles r WHERE rolname=@role AND NOT rolsuper AND NOT rolbypassrls
                AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit
                AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relowner=r.oid)
            """, ("role", role)));
        var readTables = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database, """
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p') AND has_table_privilege(@role,c.oid,'SELECT')
            """, ("role", role));
        Assert.Equal(policyCount == 105 ? 6L : 11L, readTables);
        Assert.NotEmpty(fixture.Recorder.Transactions);
        Assert.All(fixture.Recorder.Transactions, observation => Assert.Equal("syntheticSuppliedLifecycleAuthority", observation.AuthorityKind));
        var actualPolicies = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relrowsecurity AND c.relforcerowsecurity");
        Assert.Equal(policyCount, actualPolicies);
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsNativeLifecycleTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-native-lifecycle-" + name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, candidateSha = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA"), testAssemblyDigest = assemblyDigest,
            approval = "DRAFT", ownerApproval = (string?)null, executedAtUtc = DateTimeOffset.UtcNow,
            executionScope = "ISOLATED_CURRENT_NATIVE_ADAPTER_LIFECYCLES", observedDraftPolicyCount = actualPolicies,
            databaseRole = role, isSuperuser = false, bypassRls = false, authorityKind = "syntheticSuppliedLifecycleAuthority",
            observedSelectTableCount = readTables, membershipCount = 0, protectedTableOwnershipCount = 0,
            transactionCount = fixture.Recorder.Transactions.Count, controls, holds,
            operationalRoleEquivalence = "UNVERIFIED", identityProjectionAuthority = "UNVERIFIED",
            approvedOperationApplicability = "UNVERIFIED", productRlsAppliedCount = 0,
            preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
