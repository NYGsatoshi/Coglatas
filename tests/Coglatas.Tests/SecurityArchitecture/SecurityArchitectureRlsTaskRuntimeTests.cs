using System.Reflection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.Groups;
using Coglatas.Application.Messaging;
using Coglatas.Application.Projects;
using Coglatas.Application.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.TaskExecution;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Actual task adapter lifecycles under selected, test-owned authority. Product RLS remains inactive.</summary>
public sealed class SecurityArchitectureRlsTaskRuntimeTests
{
    private static readonly string[] ProtectedWrites =
        ["task_execution_materialized_sources", "task_execution_results", "task_execution_result_sources", "audit_logs"];

    [PostgreSqlFact]
    public async Task ActualTaskRuntimePersistsScopedResultAndAuditAndReauthorizesAfterMaterialization()
    {
        await WithFixtureAsync(async fixture =>
        {
            var stages = new List<Stage>();
            var foreign = await NewRunAsync(fixture, fixture.Beta);
            await ExecuteAsync(fixture, fixture.Beta, foreign);
            var foreignPositive = await StateAsync(fixture.Database, foreign);
            AssertSucceeded(foreignPositive);
            stages.Add(new("foreignPositive", foreignPositive));
            var local = await NewRunAsync(fixture, fixture.Alpha);
            await ExecuteAsync(fixture, fixture.Alpha, local);
            var localPositive = await StateAsync(fixture.Database, local);
            AssertSucceeded(localPositive);
            stages.Add(new("localPositive", localPositive));
            await ExecuteAsync(fixture, fixture.Alpha, local);
            Assert.Equal(localPositive, await StateAsync(fixture.Database, local));
            stages.Add(new("terminalReplayUnchanged", localPositive));

            // The foreign run was executable by its own supplied actor before the mismatched handle control.
            var foreignPending = await NewRunAsync(fixture, fixture.Beta);
            var pending = await StateAsync(fixture.Database, foreignPending);
            await ExecuteAsync(fixture, fixture.Alpha, foreignPending, handleTenant: fixture.Alpha.Tenant);
            Assert.Equal(pending, await StateAsync(fixture.Database, foreignPending));
            await ExecuteAsync(fixture, fixture.Alpha, foreignPending, handleTenant: fixture.Beta.Tenant);
            Assert.Equal(pending, await StateAsync(fixture.Database, foreignPending));
            stages.Add(new("foreignHandleApplicationDenial", pending));
            await ExecuteAsync(fixture, fixture.Beta, foreignPending);
            AssertSucceeded(await StateAsync(fixture.Database, foreignPending));

            var revoked = await NewRunAsync(fixture, fixture.Alpha);
            var committedRevocations = 0;
            var storage = new ObservedStorage(fixture.Storage, async () =>
            {
                Assert.Equal(0, committedRevocations++);
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                    "UPDATE workspace_members SET \"Status\"='Suspended' WHERE \"TenantId\"=@tenant",
                    ("tenant", fixture.Alpha.Tenant));
            });
            await ExecuteAsync(fixture, fixture.Alpha, revoked, storage);
            Assert.Equal(1, committedRevocations);
            Assert.Equal(1, storage.OpenCount);
            var revokedState = await StateAsync(fixture.Database, revoked);
            AssertFailedWithoutOutput(revokedState, "TASK_EXECUTION_RESULT_PERSISTENCE_FAILED");
            stages.Add(new("membershipRevokedDuringActualFileRead", revokedState));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE workspace_members SET \"Status\"='Active' WHERE \"TenantId\"=@tenant",
                ("tenant", fixture.Alpha.Tenant));
            var restored = await NewRunAsync(fixture, fixture.Alpha);
            await ExecuteAsync(fixture, fixture.Alpha, restored);
            var restoredState = await StateAsync(fixture.Database, restored);
            AssertSucceeded(restoredState);
            stages.Add(new("membershipRestoredNewRequestPositive", restoredState));
            await WritePrivateAsync(fixture, "selected-write-lifecycle", stages,
                ["ActualLocalFileRead", "ActualProjectAndAttachmentAuthorization", "ActualRawProvenanceResultAndReferenceInserts",
                 "ActualQueuedStartedAndSucceededAuditWrites", "TerminalReplayPreservesImmutableRowsAndAudit",
                 "ForeignPositiveBeforeApplicationHandleDenial", "CommittedMembershipRevocationBeforeFinalAuthorization",
                 "RevocationPreventsDurableOutput", "RestoredMembershipNewRequestPositive"],
                compatibilityOutcome: "SELECTED_WRITE_ADAPTER_OBSERVED", reasonCode: null);
        });
    }

    [PostgreSqlFact]
    public async Task ActualTaskRuntimeRunReadBetweenTransactionsRetainsMissingContextCompatibilityHold()
    {
        await WithFixtureAsync(async fixture =>
        {
            var stages = new List<Stage>();
            var positive = await NewRunAsync(fixture, fixture.Alpha);
            var observedPositiveStorage = new ObservedStorage(fixture.Storage);
            await ExecuteAsync(fixture, fixture.Alpha, positive, observedPositiveStorage);
            Assert.Equal(1, observedPositiveStorage.OpenCount);
            var positiveState = await StateAsync(fixture.Database, positive);
            AssertSucceeded(positiveState);
            stages.Add(new("fourSelectedPoliciesPositive", positiveState));

            await AddPolicyAsync(fixture.Database, fixture.Role, "task_execution_runs");
            var held = await NewRunAsync(fixture, fixture.Alpha);
            var storage = new ObservedStorage(fixture.Storage);
            await ExecuteAsync(fixture, fixture.Alpha, held, storage);
            Assert.Equal(0, storage.OpenCount);
            var heldState = await StateAsync(fixture.Database, held);
            AssertFailedWithoutOutput(heldState, "TASK_EXECUTION_NO_AUTHORIZED_TEXT_SOURCES");
            stages.Add(new("runReadWithoutTransactionLocalContext", heldState));

            // Observe both conditions directly on the same actual role/connection. No GRANT or trigger failure
            // explains the missing row; this is a legitimate adapter operation that remains unqualified.
            await using var context = Context(fixture, fixture.Alpha);
            Assert.False(await context.TaskExecutionRuns.AsNoTracking().AnyAsync(run => run.Id == held));
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                Assert.True(await context.TaskExecutionRuns.AsNoTracking().AnyAsync(run => run.Id == held));
                await transaction.CommitAsync();
            }
            Assert.False(await context.TaskExecutionRuns.AsNoTracking().AnyAsync(run => run.Id == held));
            await WritePrivateAsync(fixture, "between-transaction-run-read", stages,
                ["FourSelectedPoliciesPositiveBeforeCompatibilityMutation", "ActualRunBecomesRunningInOwnedTransaction",
                 "BetweenTransactionRunReadMissingContext", "NoSourceMaterialized", "FailedLifecycleAuditObserved",
                 "SameRoleRunVisibleInsideTransactionAndHiddenAfterCommit"],
                compatibilityOutcome: "UNVERIFIED", reasonCode: "MissingContextForBetweenTransactionRunRead");
        });
    }

    private sealed record Fixture(string Database, string Connection, string Role,
        SecurityArchitectureRlsRowFixtures.Seed Alpha, SecurityArchitectureRlsRowFixtures.Seed Beta,
        IFileStorageService Storage, SecurityArchitectureRlsRuntimeContext.Recorder Recorder,
        ConcurrentQueue<Invocation> Invocations);
    private sealed record Invocation(string DatabaseRole, int BackendProcessId, string AuthorityKind);

    private static async Task WithFixtureAsync(Func<Fixture, Task> scenario)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_task_runtime_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var storageDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-task-runtime-" + Guid.NewGuid().ToString("N")));
        var storage = new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = storageDirectory }));
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var beta = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                foreach (var seed in new[] { alpha, beta }) await PrepareSourceAsync(database, seed, storage);
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT ON task_execution_materialized_sources,task_execution_results,task_execution_result_sources,audit_logs,
                        task_execution_runs,projects,task_items,attachments,file_objects,users,workspaces,workspace_members,
                        project_members,tenant_users,tenants TO "{role}";
                    GRANT UPDATE ON task_execution_runs TO "{role}";
                    GRANT INSERT ON task_execution_materialized_sources,task_execution_results,task_execution_result_sources,audit_logs TO "{role}";
                    """);
                foreach (var table in ProtectedWrites)
                {
                    await AddPolicyAsync(database, role, table);
                }
                var connection = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1 }.ConnectionString;
                try { await scenario(new(database, connection, role, alpha, beta, storage, new(), new())); }
                finally { using var pooled = new NpgsqlConnection(connection); NpgsqlConnection.ClearPool(pooled); }
            });
        }
        finally
        {
            try { await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\""); }
            finally
            {
                var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.StartsWith(tempRoot + "coglatas-sec-arch-task-runtime-", storageDirectory, StringComparison.OrdinalIgnoreCase);
                if (Directory.Exists(storageDirectory)) Directory.Delete(storageDirectory, recursive: true);
            }
        }
    }

    private static Task AddPolicyAsync(string database, string role, string table) => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
        ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;
        CREATE POLICY sec_arch_draft_task_runtime ON public."{table}" TO "{role}"
            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
            WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
        """);

    private static async Task PrepareSourceAsync(string database, SecurityArchitectureRlsRowFixtures.Seed seed, IFileStorageService storage)
    {
        var bytes = "synthetic bounded text\nfor task execution"u8.ToArray();
        var key = "task-runtime/" + Guid.NewGuid().ToString("N") + ".txt";
        await using var stream = new MemoryStream(bytes, writable: false);
        Assert.True((await storage.SaveAsync(key, stream, "text/plain")).IsSuccess);
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            UPDATE projects SET "Status"='Active',"ActivationState"='Activated',"ActivatedAtUtc"=now(),"ActivationVersion"=1,"Visibility"='MembersOnly'
                WHERE "TenantId"=@tenant;
            UPDATE workspace_members SET "Status"='Active',"Role"='Owner' WHERE "TenantId"=@tenant;
            UPDATE project_members SET "Role"='Owner' WHERE "TenantId"=@tenant;
            UPDATE task_execution_runs SET "Status"='Succeeded',"FinishedAtUtc"=now(),"VersionNo"="VersionNo"+1 WHERE "TenantId"=@tenant;
            UPDATE file_objects SET "ContentType"='text/plain',"StorageKey"=@key,"SizeBytes"=@bytes,"HashSha256"=@hash WHERE "TenantId"=@tenant;
            """, ("tenant", seed.Tenant), ("key", key), ("bytes", bytes.LongLength),
            ("hash", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        Assert.Equal("NormalUser", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
            "SELECT \"SystemRole\" FROM users WHERE \"Id\"=@user", ("user", ((User)seed.Entities["users"]).Id)));
    }

    private static async Task<Guid> NewRunAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(seed.Tenant, "synthetic-request-seed");
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Database).Options, tenant);
        var run = new TaskExecutionRun
        {
            TenantId = seed.Tenant, WorkspaceId = ((Workspace)seed.Entities["workspaces"]).Id,
            ProjectId = ((Project)seed.Entities["projects"]).Id, TaskItemId = ((TaskItem)seed.Entities["task_items"]).Id,
            RequestedByUserId = ((User)seed.Entities["users"]).Id, RequestedAtUtc = DateTimeOffset.UtcNow,
            SnapshotSchemaVersion = TaskExecutionRun.SnapshotSchemaVersion1, SnapshotScopeOrigin = TaskExecutionScopeOrigin.ProjectDefault,
            SnapshotProjectScopeVersion = 1, SnapshotWebEnabled = false, SnapshotProjectFilesEnabled = true
        };
        context.TaskExecutionRuns.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static AppDbContext Context(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed) => SecurityArchitectureRlsRuntimeContext.Create(
        fixture.Connection, new(seed.Tenant, "syntheticSuppliedTaskRuntimeAuthority", ((User)seed.Entities["users"]).Id, null), fixture.Recorder);

    private static async Task ExecuteAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed, Guid run,
        IFileStorageService? storage = null, Guid? handleTenant = null)
    {
        await using var context = Context(fixture, seed);
        await context.Database.OpenConnectionAsync();
        var observed = (NpgsqlConnection)context.Database.GetDbConnection();
        await AssertConnectionAuthorityAsync(observed, fixture.Role);
        var tenant = new CurrentTenantService();
        tenant.SetTenant(seed.Tenant, "synthetic-current-task-authority");
        var workspaces = new WorkspaceRepository(context);
        var groups = new GroupRepository(context);
        var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(context), workspaces,
            new TenantAuthorizationService(new TenantRepository(context)));
        var projectAuthorization = new ProjectAuthorizationService(new ProjectRepository(context), workspaceAuthorization,
            new GroupAuthorizationService(groups, workspaces, workspaceAuthorization), groups);
        var files = new FileAuthorizationService(new FileRepository(context), projectAuthorization,
            Unused<IConversationAuthorizationService>(), Unused<IChannelAuthorizationService>(), workspaceAuthorization);
        var actor = new Actor(((User)seed.Entities["users"]).Id);
        var clock = new Clock();
        var runtime = new DurableTaskExecutionResultRuntime(context, tenant, projectAuthorization, files,
            storage ?? fixture.Storage, clock, new DbAuditLogger(context, clock, actor, tenant));
        await runtime.ExecuteAsync(new(run, handleTenant ?? seed.Tenant, TaskExecutionRun.RuntimeContractVersion1));
        await AssertConnectionAuthorityAsync(observed, fixture.Role);
        fixture.Invocations.Enqueue(new(fixture.Role, observed.ProcessID, "syntheticSuppliedTaskRuntimeAuthority"));
    }

    private static async Task AssertConnectionAuthorityAsync(NpgsqlConnection connection, string expectedRole)
    {
        await using var command = new NpgsqlCommand("SELECT current_user,r.rolsuper,r.rolbypassrls,current_setting('coglatas.tenant_id',true) FROM pg_roles r WHERE r.rolname=current_user", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedRole, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.True(reader.IsDBNull(3) || reader.GetString(3).Length == 0);
    }

    private sealed record State(string Status, string? FailureCode, long Sources, long Results, long References,
        string AuditActions, string NativeRowsDigest, string AuditRowsDigest);
    private sealed record Stage(string Name, State State);

    private static async Task<State> StateAsync(string database, Guid run)
    {
        var states = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT r."Status",r."FailureCode",
                (SELECT count(*) FROM task_execution_materialized_sources s WHERE s."TaskExecutionRunId"=r."Id"),
                (SELECT count(*) FROM task_execution_results s WHERE s."TaskExecutionRunId"=r."Id"),
                (SELECT count(*) FROM task_execution_result_sources s JOIN task_execution_results result ON result."Id"=s."TaskExecutionResultId"
                    WHERE result."TaskExecutionRunId"=r."Id"),
                coalesce((SELECT string_agg(a."Action",',' ORDER BY a."CreatedAt",a."Id") FROM audit_logs a WHERE a."EntityId"=r."Id"),''),
                encode(sha256(convert_to(coalesce((SELECT string_agg(item.value,'|' ORDER BY item.kind,item.id) FROM (
                    SELECT 'source' kind,s."Id" id,to_jsonb(s)::text value FROM task_execution_materialized_sources s WHERE s."TaskExecutionRunId"=r."Id"
                    UNION ALL SELECT 'result',s."Id",to_jsonb(s)::text FROM task_execution_results s WHERE s."TaskExecutionRunId"=r."Id"
                    UNION ALL SELECT 'reference',s."Id",to_jsonb(s)::text FROM task_execution_result_sources s
                        JOIN task_execution_results result ON result."Id"=s."TaskExecutionResultId" WHERE result."TaskExecutionRunId"=r."Id"
                    ) item),''),'UTF8')),'hex'),
                encode(sha256(convert_to(coalesce((SELECT string_agg(to_jsonb(a)::text,'|' ORDER BY a."Id") FROM audit_logs a WHERE a."EntityId"=r."Id"),''),'UTF8')),'hex')
            FROM task_execution_runs r WHERE r."Id"=@run
            """, reader => new State(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)), ("run", run));
        var state = Assert.Single(states);
        Assert.Matches("^[a-f0-9]{64}$", state.NativeRowsDigest);
        Assert.Matches("^[a-f0-9]{64}$", state.AuditRowsDigest);
        return state;
    }

    private static void AssertSucceeded(State state)
    {
        Assert.Equal("Succeeded", state.Status);
        Assert.Null(state.FailureCode);
        Assert.Equal(1L, state.Sources);
        Assert.Equal(1L, state.Results);
        Assert.Equal(1L, state.References);
        Assert.Equal("TaskExecutionRunQueued,TaskExecutionRunStarted,TaskExecutionRunSucceeded", state.AuditActions);
    }

    private static void AssertFailedWithoutOutput(State state, string failureCode)
    {
        Assert.Equal("Failed", state.Status);
        Assert.Equal(failureCode, state.FailureCode);
        Assert.Equal(0L, state.Sources);
        Assert.Equal(0L, state.Results);
        Assert.Equal(0L, state.References);
        Assert.Equal("TaskExecutionRunQueued,TaskExecutionRunStarted,TaskExecutionRunFailed", state.AuditActions);
    }

    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedDependency>();
    public class UnusedDependency : DispatchProxy
    {
        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("This task-owner fixture does not compose " + targetMethod?.Name);
    }

    private sealed class Actor(Guid user) : ICurrentUser
    {
        public Guid? UserId => user;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        private DateTimeOffset _value = DateTimeOffset.UtcNow.AddSeconds(1);
        public DateTimeOffset UtcNow { get { var current = _value; _value = _value.AddMilliseconds(1); return current; } }
    }

    private sealed class ObservedStorage(IFileStorageService inner, Func<Task>? beforeOpen = null) : IFileStorageService
    {
        public int OpenCount { get; private set; }
        public Task<Result> SaveAsync(string key, Stream stream, string contentType, CancellationToken cancellationToken = default) => inner.SaveAsync(key, stream, contentType, cancellationToken);
        public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            if (beforeOpen is not null) await beforeOpen();
            return await inner.OpenReadAsync(key, cancellationToken);
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);
        public Task<string?> CreateSignedReadUrlAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default) => inner.CreateSignedReadUrlAsync(key, expiresIn, cancellationToken);
    }

    private static async Task WritePrivateAsync(Fixture fixture, string name, IReadOnlyList<Stage> stages,
        string[] controls, string compatibilityOutcome, string? reasonCode)
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        await AssertConnectionAuthorityAsync(connection, fixture.Role);
        Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database, """
            SELECT count(*) FROM pg_roles r WHERE rolname=@role AND NOT rolsuper AND NOT rolbypassrls
                AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit
                AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
            """, ("role", fixture.Role)));
        var readTables = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database, """
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p') AND has_table_privilege(@role,c.oid,'SELECT')
            """, ("role", fixture.Role));
        Assert.Equal(15L, readTables);
        var permissionRows = await PostgreSqlMigrationTestDatabase.QueryAsync(fixture.Database, """
            SELECT count(*) FILTER(WHERE has_table_privilege(@role,c.oid,'INSERT')),
                count(*) FILTER(WHERE has_table_privilege(@role,c.oid,'UPDATE')),
                count(*) FILTER(WHERE has_table_privilege(@role,c.oid,'DELETE')),
                count(*) FILTER(WHERE has_table_privilege(@role,c.oid,'TRUNCATE'))
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p')
            """, reader => (Insert: reader.GetInt64(0), Update: reader.GetInt64(1), Delete: reader.GetInt64(2), Truncate: reader.GetInt64(3)), ("role", fixture.Role));
        var permissions = Assert.Single(permissionRows);
        Assert.Equal((4L, 1L, 0L, 0L), permissions);
        var policyCount = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relrowsecurity AND c.relforcerowsecurity");
        Assert.Equal(compatibilityOutcome == "UNVERIFIED" ? 5L : 4L, policyCount);
        Assert.NotEmpty(fixture.Recorder.Transactions);
        Assert.All(fixture.Recorder.Transactions, observation => Assert.Equal("syntheticSuppliedTaskRuntimeAuthority", observation.AuthorityKind));
        Assert.NotEmpty(fixture.Invocations);
        foreach (var invocation in fixture.Invocations)
        {
            Assert.Equal(fixture.Role, invocation.DatabaseRole);
            Assert.True(invocation.BackendProcessId > 0);
            Assert.Equal("syntheticSuppliedTaskRuntimeAuthority", invocation.AuthorityKind);
        }
        Assert.NotEmpty(stages);
        foreach (var stage in stages)
        {
            Assert.False(string.IsNullOrWhiteSpace(stage.Name));
            Assert.Contains(stage.State.Status, new[] { "Accepted", "Succeeded", "Failed" });
            Assert.InRange(stage.State.Sources, 0L, 1L);
            Assert.Equal(stage.State.Sources, stage.State.Results);
            Assert.Equal(stage.State.Results, stage.State.References);
            Assert.Matches("^[a-f0-9]{64}$", stage.State.NativeRowsDigest);
            Assert.Matches("^[a-f0-9]{64}$", stage.State.AuditRowsDigest);
        }
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var schemas = new List<SecurityArchitectureRlsSchemaIdentity.Snapshot>();
        foreach (var table in ProtectedWrites.Append("task_execution_runs")) schemas.Add(await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(fixture.Database, table));
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsTaskRuntimeTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        var environment = new
        {
            dotnetVersion = Environment.Version.ToString(), npgsqlVersion = typeof(NpgsqlConnection).Assembly.GetName().Version!.ToString(),
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database, "SHOW server_version"),
            fixture = "isolated-migrated-postgresql"
        };
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-task-runtime-" + name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, candidateSha = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA"), testAssemblyDigest = assemblyDigest,
            approval = "DRAFT", ownerApproval = (string?)null, executedAtUtc = DateTimeOffset.UtcNow, environment,
            environmentFingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(environment)))).ToLowerInvariant(),
            executionScope = "ISOLATED_ACTUAL_TASK_RUNTIME_WITH_SELECTED_DRAFT_WRITE_POLICIES", observedDraftPolicyCount = policyCount,
            databaseRole = fixture.Role, isSuperuser = false, bypassRls = false, membershipCount = 0, protectedTableOwnershipCount = 0,
            observedSelectTableCount = readTables, authorityKind = "syntheticSuppliedTaskRuntimeAuthority", transactionCount = fixture.Recorder.Transactions.Count,
            observedInsertTableCount = permissions.Insert, observedUpdateTableCount = permissions.Update,
            observedDeleteTableCount = permissions.Delete, observedTruncateTableCount = permissions.Truncate,
            invocations = fixture.Invocations.ToArray(), acceptedSnapshotSchemaVersion = TaskExecutionRun.SnapshotSchemaVersion1,
            observedRuntimeContractVersion = TaskExecutionRun.RuntimeContractVersion1,
            controls, stages, schemas, compatibilityOutcome, reasonCode, operationalRoleEquivalence = "UNVERIFIED", identityProjectionAuthority = "UNVERIFIED",
            authenticationAndRequestIssuance = "UNVERIFIED", allTableStartupCompatibility = "UNVERIFIED", approvedOperationApplicability = "UNVERIFIED",
            holds = new[] { "Run/identity/membership/project/attachment reads outside owned transactions require separate concrete authority design.",
                "A transaction-local mutable tenant setting cannot contain arbitrary SQL executed by a compromised role.",
                "Authenticated HTTP request issuance and controller-to-runtime invocation are not observed by supplied runtime handles.",
                "Selected runtime writes do not qualify direct append-only UPDATE/DELETE or native precheck-negative cells." },
            productRlsAppliedCount = 0, preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
