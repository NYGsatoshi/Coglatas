using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Files;
using Coglatas.Application.Tenancy;
using Coglatas.Application.Notifications;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.TaskExecution;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Reflection;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Actual adapter execution under test-owned draft context; operational authority remains unverified.</summary>
public sealed class SecurityArchitectureRlsAdapterTests
{
    [PostgreSqlFact]
    public async Task ActualRawAdaptersUseOwnedContextAndPreserveRollbackBeforeForeignScopeNegatives()
    {
        await WithFixtureAsync(async fixture =>
        {
            var alpha = Tenant(fixture.Alpha.Tenant);
            var beta = Tenant(fixture.Beta.Tenant);
            var project = (Project)fixture.Alpha.Entities["projects"];
            var betaProject = (Project)fixture.Beta.Entities["projects"];
            var user = (User)fixture.Alpha.Entities["users"];
            var betaUser = (User)fixture.Beta.Entities["users"];
            var file = (FileObject)fixture.Alpha.Entities["file_objects"];
            var betaFile = (FileObject)fixture.Beta.Entities["file_objects"];
            var run = (TaskExecutionRun)fixture.Alpha.Entities["task_execution_runs"];
            var betaRun = (TaskExecutionRun)fixture.Beta.Entities["task_execution_runs"];
            var unchangedPreference = await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(fixture.Database,
                "SELECT \"MessageNotificationsEnabled\" FROM tenant_users WHERE \"TenantId\"=@tenant", ("tenant", alpha.TenantId));
            // The same valid row inputs fail closed when no context transaction is owned.
            await using (var unscoped = Context(fixture, alpha))
            {
                Assert.Null(await new ConfiguredProjectTaskWorkflowSource(unscoped).FindWorkspaceTemplateAsync(project));
                Assert.Null(await new MessageNotificationPreferenceStore(unscoped).GetEnabledAsync(alpha.TenantId, user.Id));
                Assert.Empty(await new FileRepository(unscoped).ListFileVersionsAsync(alpha.TenantId, file.Id, 10));
                Assert.Null(await new TaskExecutionResultRepository(unscoped, alpha).GetByRunAsync(run.Id));
                Assert.Null(await new TaskExecutionScopeRepository(unscoped).GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Project, project.Id));
            }
            // Prove every foreign target is valid under its own context before using non-delivery as an assertion.
            await using (var foreignPositive = Context(fixture, beta))
            {
                await using var transaction = await foreignPositive.Database.BeginTransactionAsync();
                Assert.NotNull(await new ConfiguredProjectTaskWorkflowSource(foreignPositive).FindWorkspaceTemplateAsync(betaProject));
                Assert.NotNull(await new MessageNotificationPreferenceStore(foreignPositive).GetEnabledAsync(beta.TenantId, betaUser.Id));
                Assert.Single(await new FileRepository(foreignPositive).ListFileVersionsAsync(beta.TenantId, betaFile.Id, 10));
                Assert.NotNull(await new TaskExecutionResultRepository(foreignPositive, beta).GetByRunAsync(betaRun.Id));
                Assert.NotNull(await new TaskExecutionScopeRepository(foreignPositive).GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Project, betaProject.Id));
                await transaction.RollbackAsync();
            }
            await using (var context = Context(fixture, alpha))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var workflows = new ConfiguredProjectTaskWorkflowSource(context);
                Assert.Single((await workflows.FindWorkspaceTemplateAsync(project))!.Stages);
                Assert.Single((await workflows.FindTenantDefaultAsync(project))!.Stages);
                Assert.Null(await workflows.FindWorkspaceTemplateAsync(betaProject));
                Assert.Null(await workflows.FindTenantDefaultAsync(betaProject));
                var preferences = new MessageNotificationPreferenceStore(context);
                Assert.Equal(unchangedPreference, await preferences.GetEnabledAsync(alpha.TenantId, user.Id));
                Assert.True(await preferences.SetEnabledAsync(alpha.TenantId, user.Id, !unchangedPreference, DateTimeOffset.UtcNow));
                Assert.Equal(!unchangedPreference, await preferences.GetEnabledAsync(alpha.TenantId, user.Id));
                Assert.Null(await preferences.GetEnabledAsync(beta.TenantId, betaUser.Id));
                Assert.False(await preferences.SetEnabledAsync(beta.TenantId, betaUser.Id, false, DateTimeOffset.UtcNow));
                Assert.Single(await new FileRepository(context).ListFileVersionsAsync(alpha.TenantId, file.Id, 10));
                Assert.Empty(await new FileRepository(context).ListFileVersionsAsync(beta.TenantId, betaFile.Id, 10));
                var results = new TaskExecutionResultRepository(context, alpha);
                var result = await results.GetByRunAsync(run.Id);
                Assert.NotNull(result);
                Assert.Single(await results.ListSourceReferencesAsync(result.Id));
                Assert.Null(await results.GetByRunAsync(betaRun.Id));
                var policies = new TaskExecutionScopeRepository(context);
                Assert.NotNull(await policies.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Project, project.Id));
                // This current raw adapter queries OwnerType/OwnerId without a Tenant predicate: the prototype policy must filter it.
                Assert.Null(await policies.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Project, betaProject.Id));
                await transaction.RollbackAsync();
            }
            Assert.Equal(unchangedPreference, await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(fixture.Database,
                "SELECT \"MessageNotificationsEnabled\" FROM tenant_users WHERE \"TenantId\"=@tenant", ("tenant", alpha.TenantId)));
            await AssertResetAsync(fixture);
            await WritePrivateAsync(fixture, "raw-adapters", ["ConfiguredWorkflow", "MessageNotificationPreference",
                "FileVersionLedger", "TaskExecutionResultAndProvenance", "SourcePolicyOwnerLookup", "OwnedTransactionRollback",
                "ForeignPositiveBeforeNegative", "UnscopedAdaptersFailClosed", "PhysicalConnectionReuseAndReset"],
                ["SharedIdentityDisplayProjectionRequiresOwnerReview", "SameTenantResourceAndSubjectAuthorityUnverified", "ProductTransactionOwnershipUnverified"]);
        });
    }

    [PostgreSqlFact]
    public async Task ActualAnnouncementAndDigestClaimAdaptersRespectBoundedWorkerContextAndCurrentClaimTokens()
    {
        await WithFixtureAsync(async fixture =>
        {
            var alpha = Tenant(fixture.Alpha.Tenant);
            var beta = Tenant(fixture.Beta.Tenant);
            var alphaDraft = (AnnouncementDraft)fixture.Alpha.Entities["announcement_drafts"];
            var betaDraft = (AnnouncementDraft)fixture.Beta.Entities["announcement_drafts"];
            var now = DateTimeOffset.UtcNow;
            await using (var unscoped = Context(fixture, alpha))
                Assert.Empty(await new AnnouncementDraftRepository(unscoped, alpha).ClaimDueAsync("synthetic", now, 10, TimeSpan.FromMinutes(2)));
            Guid betaToken;
            await using (var context = Context(fixture, beta))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var repository = new AnnouncementDraftRepository(context, beta);
                var claim = Assert.Single(await repository.ClaimDueAsync("synthetic-beta", now, 10, TimeSpan.FromMinutes(2)));
                Assert.Equal(betaDraft.Id, claim.DraftId);
                betaToken = claim.ClaimToken;
                Assert.NotNull(await repository.GetClaimedAsync(claim.DraftId, claim.ClaimToken));
                await transaction.CommitAsync();
            }
            await using (var context = Context(fixture, alpha))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var repository = new AnnouncementDraftRepository(context, alpha);
                var claim = Assert.Single(await repository.ClaimDueAsync("synthetic-alpha", now, 10, TimeSpan.FromMinutes(2)));
                Assert.Equal(alphaDraft.Id, claim.DraftId);
                Assert.NotNull(await repository.GetClaimedAsync(claim.DraftId, claim.ClaimToken));
                Assert.Null(await repository.GetClaimedAsync(betaDraft.Id, betaToken));
                Assert.Null(await repository.GetClaimedAsync(claim.DraftId, Guid.NewGuid()));
                await transaction.CommitAsync();
            }
            var alphaWorkspace = (Workspace)fixture.Alpha.Entities["workspaces"];
            var betaWorkspace = (Workspace)fixture.Beta.Entities["workspaces"];
            var alphaUser = (User)fixture.Alpha.Entities["users"];
            var betaUser = (User)fixture.Beta.Entities["users"];
            var newJob = Guid.NewGuid();
            TaskDeadlineDigestScheduleWrite Schedule(Guid id, Workspace workspace, User user) =>
                new(id, workspace.Id, user.Id, new DateOnly(2026, 10, 11), 1, now);
            await using (var context = Context(fixture, alpha))
            {
                var repository = new TaskDeadlineDigestRepository(context, alpha);
                Assert.Equal(0, await repository.UpsertSchedulesAsync([Schedule(newJob, alphaWorkspace, alphaUser)], now));
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.Equal(1, await repository.UpsertSchedulesAsync([Schedule(newJob, alphaWorkspace, alphaUser)], now));
                Assert.Equal(0, await repository.UpsertSchedulesAsync([Schedule(Guid.NewGuid(), betaWorkspace, betaUser)], now));
                await transaction.RollbackAsync();
            }
            Assert.Equal(0L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
                "SELECT count(*) FROM task_deadline_digest_jobs WHERE \"Id\"=@id", ("id", newJob)));
            TaskDeadlineDigestClaim betaDigest;
            await using (var context = Context(fixture, beta))
            {
                // The real digest repository owns its claim transaction, so the test interceptor supplies that transaction's context.
                var repository = new TaskDeadlineDigestRepository(context, beta);
                betaDigest = Assert.Single(await repository.ClaimDueAsync("synthetic-beta", now, 10, TimeSpan.FromMinutes(2)));
                Assert.Equal(beta.TenantId, betaDigest.TenantId);
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.NotNull(await repository.GetClaimedAsync(betaDigest.JobId, betaDigest.ClaimToken, true));
                Assert.True((await repository.MarkFailureAsync(betaDigest.JobId, betaDigest.ClaimToken, "Synthetic", now, now.AddMinutes(5))).Changed);
                await transaction.RollbackAsync();
            }
            await using (var context = Context(fixture, alpha))
            {
                var repository = new TaskDeadlineDigestRepository(context, alpha);
                var claim = Assert.Single(await repository.ClaimDueAsync("synthetic-alpha", now, 10, TimeSpan.FromMinutes(2)));
                Assert.Equal(alpha.TenantId, claim.TenantId);
                context.ChangeTracker.Clear();
                Assert.False((await repository.MarkFailureAsync(claim.JobId, claim.ClaimToken, "Synthetic", now, now.AddMinutes(5))).Changed);
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.NotNull(await repository.GetClaimedAsync(claim.JobId, claim.ClaimToken, true));
                Assert.Null(await repository.GetClaimedAsync(betaDigest.JobId, betaDigest.ClaimToken, true));
                Assert.Null(await repository.GetClaimedAsync(claim.JobId, Guid.NewGuid(), true));
                Assert.False((await repository.MarkFailureAsync(betaDigest.JobId, betaDigest.ClaimToken, "Synthetic", now, now.AddMinutes(5))).Changed);
                Assert.True((await repository.MarkFailureAsync(claim.JobId, claim.ClaimToken, "Synthetic", now, now.AddMinutes(5))).Changed);
                await transaction.RollbackAsync();
            }
            await AssertResetAsync(fixture);
            await WritePrivateAsync(fixture, "worker-claims", ["AnnouncementClaimAndCurrentToken", "DigestClaimOwnedTransaction",
                "DigestRawScheduleUpsert", "ScheduleRollback", "DigestFailureTransitionWithOwnedTransaction", "ForeignPositiveBeforeNegative", "CrossTenantClaimIsolation",
                "UnscopedAnnouncementAndScheduleFailClosed", "PhysicalConnectionReuseAndReset"],
                ["PlatformWorkerDiscoveryAuthorityUnverified", "PublicationGenerationDeliveryAndFailureAdaptersUnverified", "ClaimTokenChecksAreApplicationFences"]);
        });
    }

    [PostgreSqlFact]
    public async Task ActualAuditQueueAndStaleRecoveryMethodsRequireOwnedContextAndPreserveForeignJobs()
    {
        await WithFixtureAsync(async fixture =>
        {
            var alpha = Tenant(fixture.Alpha.Tenant);
            var beta = Tenant(fixture.Beta.Tenant);
            var now = DateTimeOffset.UtcNow;
            var staleBefore = now.AddMinutes(-10);
            var betaJob = (ExportJob)fixture.Beta.Entities["export_jobs"];
            await using (var unscoped = Context(fixture, alpha))
            {
                Assert.Empty(await Processor(unscoped).ListQueuedJobIdsAsync(10));
                Assert.Empty(await Processor(unscoped).ListQueuedTenantIdsAsync(10, staleBefore));
                Assert.Equal(0, await Processor(unscoped).RecoverStaleRunningAsync(staleBefore, now));
            }
            await using (var context = Context(fixture, beta))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.Single(await Processor(context).ListQueuedJobIdsAsync(10));
                Assert.Equal(beta.TenantId, Assert.Single(await Processor(context).ListQueuedTenantIdsAsync(10, staleBefore)));
                await transaction.RollbackAsync();
            }
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE export_jobs SET \"Status\"='Running',\"UpdatedAt\"=now()-interval '1 hour' WHERE \"Id\"=@id", ("id", betaJob.Id));
            await using (var context = Context(fixture, beta))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.Equal(1, await Processor(context).RecoverStaleRunningAsync(staleBefore, now));
                await transaction.RollbackAsync();
            }
            await using (var context = Context(fixture, alpha))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.Single(await Processor(context).ListQueuedJobIdsAsync(10));
                Assert.Equal(alpha.TenantId, Assert.Single(await Processor(context).ListQueuedTenantIdsAsync(10, staleBefore)));
                Assert.Equal(0, await Processor(context).RecoverStaleRunningAsync(staleBefore, now));
                await transaction.CommitAsync();
            }
            Assert.Equal("Running", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
                "SELECT \"Status\" FROM export_jobs WHERE \"Id\"=@id", ("id", betaJob.Id)));
            await using (var context = Context(fixture, beta))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.Equal(1, await Processor(context).RecoverStaleRunningAsync(staleBefore, now));
                await transaction.CommitAsync();
            }
            Assert.Equal("Failed", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
                "SELECT \"Status\" FROM export_jobs WHERE \"Id\"=@id", ("id", betaJob.Id)));
            await AssertResetAsync(fixture);
            await WritePrivateAsync(fixture, "audit-queue", ["ActualQueuedJobQuery", "ActualBoundedTenantDiscoveryQuery",
                "ActualStaleRecoveryExecuteUpdate", "ForeignPositiveBeforeNegative", "RecoveryRollback", "RecoveryCommit",
                "ForeignJobPreserved", "UnscopedMethodsFailClosed", "PhysicalConnectionReuseAndReset"],
                ["OperationalPlatformDiscoveryAuthorityUnverified", "PackageAuthorizationStorageAndDeliveryNotComposed"]);
        });
    }

    private static AuditPackageExportProcessor Processor(AppDbContext context) => new(context,
        new ArtifactRepository(context), new ArtifactEvidenceRepository(context), Unused<IArtifactAuthorizationService>(),
        new FileRepository(context), Unused<IFileAuthorizationService>(), Unused<ITenantAuthorizationService>(),
        Unused<ICapabilityGrantEvaluator>(), Unused<IFileStorageService>(), Unused<IAuditLogger>(), new EfUnitOfWork(context),
        new Coglatas.Infrastructure.Security.SystemClock());

    // Queue/recovery methods have no package or authorization dependencies. Any accidental invocation fails the fixture.
    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedDependency>();
    public class UnusedDependency : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("The isolated queue fixture does not compose " + targetMethod?.DeclaringType?.Name + "." + targetMethod?.Name);
    }

    private static readonly string[] Tables = ["task_workflow_templates", "task_workflow_template_stages",
        "workspace_task_workflow_defaults", "tenant_task_workflow_defaults", "tenant_users", "file_versions",
        "task_execution_results", "task_execution_result_sources", "task_execution_materialized_sources",
        "task_execution_source_policy_documents", "announcement_drafts", "task_deadline_digest_jobs", "task_deadline_digest_attempts", "workspaces", "export_jobs"];
    private sealed record Fixture(string Database, string Connection, string Role, SecurityArchitectureRlsRowFixtures.Seed Alpha,
        SecurityArchitectureRlsRowFixtures.Seed Beta, SecurityArchitectureRlsRuntimeContext.Recorder Recorder);

    private static async Task WithFixtureAsync(Func<Fixture, Task> operation)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_adapter_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var beta = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var policy = JsonSerializer.Serialize(TaskExecutionSourcePolicyV2.FromLegacy(false, true),
                    new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    UPDATE task_execution_source_policy_documents SET "PolicyJson"=CAST(@policy AS jsonb);
                    UPDATE tenant_users SET "Status"='Active';
                    UPDATE export_jobs SET "ExportType"='AuditPackage',"Status"='Queued';
                    UPDATE announcement_drafts SET "Status"='Scheduled',"ScheduledForUtc"=now()-interval '1 minute';
                    UPDATE task_deadline_digest_jobs SET "AttemptSequence"=1,"ScheduledForUtc"=now()-interval '1 minute',"NextAttemptAt"=now()-interval '1 minute';
                    UPDATE task_deadline_digest_attempts SET "Status"='Expired',"CompletedAt"=now(),"LastErrorCode"='DigestClaimExpired';
                    """, ("policy", policy));
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT ("Id","Status","DeletedAt") ON tenants TO "{role}";
                    GRANT SELECT ("Id","DisplayName") ON users TO "{role}";
                    """);
                foreach (var table in Tables)
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        GRANT SELECT,INSERT,UPDATE,DELETE ON {table} TO "{role}";
                        ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_adapter ON {table} TO "{role}"
                            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                            WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                        """);
                Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                    SELECT count(*) FROM pg_roles r WHERE rolname=@role AND NOT rolsuper AND NOT rolbypassrls
                        AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit
                        AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                        AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
                    """, ("role", role)));
                var fixture = new Fixture(database,
                    new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false }.ConnectionString,
                    role, alpha, beta, new());
                using var pooled = new NpgsqlConnection(fixture.Connection);
                try { await operation(fixture); }
                finally { NpgsqlConnection.ClearPool(pooled); }
            });
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\""); }
    }

    private static CurrentTenantService Tenant(Guid id)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(id, "synthetic-bounded-adapter");
        return tenant;
    }
    private static AppDbContext Context(Fixture fixture, CurrentTenantService tenant) => SecurityArchitectureRlsRuntimeContext.Create(
        fixture.Connection, new(tenant.TenantId, "syntheticBoundedAdapter", null, null), fixture.Recorder);

    private static async Task AssertResetAsync(Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        Assert.Contains(fixture.Recorder.Transactions, item => item.BackendProcessId == connection.ProcessID);
        await using var identity = new NpgsqlCommand("SELECT current_user", connection);
        Assert.Equal(fixture.Role, await identity.ExecuteScalarAsync());
        await using var command = new NpgsqlCommand("SELECT count(*) FROM tenant_users", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    private static async Task WritePrivateAsync(Fixture fixture, string adapter, string[] controls, string[] limits)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is not null && (candidate.Length != 40 || candidate.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidOperationException("The candidate SHA must be a full hexadecimal identity.");
        var environment = new { dotnetVersion = Environment.Version.ToString(), npgsqlVersion = typeof(NpgsqlConnection).Assembly.GetName().Version!.ToString(),
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database, "SHOW server_version"), fixture = "isolated-migrated-postgresql" };
        var roles = await PostgreSqlMigrationTestDatabase.QueryAsync(fixture.Database, """
            SELECT rolname,rolsuper,rolbypassrls,rolcreatedb,rolcreaterole,rolinherit,
                (SELECT count(*)::int FROM pg_auth_members WHERE member=r.oid),
                (SELECT count(*)::int FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
            FROM pg_roles r WHERE rolname=@role
            """, reader => new { roleKind = "syntheticBoundedAdapter", databaseRole = reader.GetString(0),
                isSuperuser = reader.GetBoolean(1), bypassRls = reader.GetBoolean(2), canCreateDb = reader.GetBoolean(3), canCreateRole = reader.GetBoolean(4),
                inheritsRoles = reader.GetBoolean(5), membershipCount = reader.GetInt32(6), protectedTableOwnershipCount = reader.GetInt32(7) }, ("role", fixture.Role));
        var observed = Assert.Single(roles);
        Assert.False(observed.isSuperuser || observed.bypassRls || observed.canCreateDb || observed.canCreateRole || observed.inheritsRoles);
        Assert.Equal(0, observed.membershipCount);
        Assert.Equal(0, observed.protectedTableOwnershipCount);
        var schemas = new List<SecurityArchitectureRlsSchemaIdentity.Snapshot>();
        foreach (var table in Tables) schemas.Add(await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(fixture.Database, table));
        var digest = static (byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-runtime-" + adapter + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, new { schemaVersion = 1, approval = "DRAFT", ownerApproval = (string?)null, candidateSha = candidate,
            testAssemblyDigest = digest(await File.ReadAllBytesAsync(typeof(SecurityArchitectureRlsAdapterTests).Assembly.Location)),
            environment, environmentFingerprint = digest(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(environment))),
            executionScope = "ISOLATED_ACTUAL_ADAPTER_METHODS", productRlsAppliedCount = 0,
            applicationRoleEquivalence = "UNVERIFIED", workerRoleEquivalence = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED",
            adapter, roles, verifiedControls = controls, observedLimits = limits, sourceSchemaIdentities = schemas,
            transactionCount = fixture.Recorder.Transactions.Count, physicalBackendCount = fixture.Recorder.Transactions.Select(item => item.BackendProcessId).Distinct().Count(),
            blindSpots = new[] { "DraftCrudAndDisplayIdentityGrantsAreNotApprovedAuthority", "TenantGucDoesNotContainArbitrarySql", "HostedWorkerLoopsAndProductStartupAreNotComposed", "AllRemainingAdapterPathsRequireQualification" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
