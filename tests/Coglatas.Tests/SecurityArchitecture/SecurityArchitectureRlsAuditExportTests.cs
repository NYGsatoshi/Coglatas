using System.IO.Compression;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Audit;
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
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Actual export adapters in a caller-owned isolated transaction; no product worker authority is approved.</summary>
public sealed class SecurityArchitectureRlsAuditExportTests
{
    private static readonly string[] ProtectedTables = ["export_jobs", "file_objects", "file_versions", "audit_logs"];
    private static readonly string[] ReadTables =
    [
        "export_jobs", "file_objects", "file_versions", "audit_logs", "users", "tenants", "tenant_users", "capability_grants",
        "workspaces", "workspace_members", "projects", "project_members", "groups", "group_members",
        "artifacts", "artifact_versions", "attachments", "artifact_claims", "artifact_evidence", "artifact_findings", "audit_finding_decisions"
    ];

    [PostgreSqlFact]
    public Task ActualAuditPackageAdaptersPersistZipAndRecheckCurrentAuthorityBeforeStorage() => WithFixtureAsync(async fixture =>
    {
        var stages = new List<State>();
        var beta = await QueueAsync(fixture, fixture.Beta);
        await ProcessAsync(fixture, fixture.Beta, beta);
        stages.Add(await AssertPackageAsync(fixture, beta));
        var alpha = await QueueAsync(fixture, fixture.Alpha);
        await ProcessAsync(fixture, fixture.Alpha, alpha);
        stages.Add(await AssertPackageAsync(fixture, alpha));
        var foreign = await QueueAsync(fixture, fixture.Beta);
        var before = await StateAsync(fixture, foreign);
        await ProcessAsync(fixture, fixture.Alpha, foreign);
        Assert.Equal(before, await StateAsync(fixture, foreign));
        Assert.False(await fixture.Storage.ExistsAsync(await StorageKeyAsync(fixture, foreign)));
        stages.Add(before);

        var revoked = await QueueAsync(fixture, fixture.Alpha);
        await SetExportGrantAsync(fixture, revoked: true);
        await ProcessAsync(fixture, fixture.Alpha, revoked);
        stages.Add(await AssertDeniedAsync(fixture, revoked));
        await SetExportGrantAsync(fixture, revoked: false);
        var restored = await QueueAsync(fixture, fixture.Alpha);
        await ProcessAsync(fixture, fixture.Alpha, restored);
        stages.Add(await AssertPackageAsync(fixture, restored));

        var midBuild = await QueueAsync(fixture, fixture.Alpha);
        var projectionObserved = false;
        await ProcessAsync(fixture, fixture.Alpha, midBuild, async () =>
        {
            projectionObserved = true;
            await SetExportGrantAsync(fixture, revoked: true);
        });
        Assert.True(projectionObserved);
        stages.Add(await AssertDeniedAsync(fixture, midBuild));
        await SetExportGrantAsync(fixture, revoked: false);
        var final = await QueueAsync(fixture, fixture.Alpha);
        await ProcessAsync(fixture, fixture.Alpha, final);
        stages.Add(await AssertPackageAsync(fixture, final));

        var membershipChange = await QueueAsync(fixture, fixture.Alpha);
        await ProcessAsync(fixture, fixture.Alpha, membershipChange, () => SetMembershipAsync(fixture, active: false));
        stages.Add(await AssertDeniedAsync(fixture, membershipChange));
        await SetMembershipAsync(fixture, active: true);
        var membershipRestored = await QueueAsync(fixture, fixture.Alpha);
        await ProcessAsync(fixture, fixture.Alpha, membershipRestored);
        stages.Add(await AssertPackageAsync(fixture, membershipRestored));
        Assert.Equal(9, stages.Count);
        Assert.Equal(5, stages.Count(stage => stage.Status == "Completed"));
        Assert.Equal(3, stages.Count(stage => stage.ErrorCode == "AuthorizationChanged"));
        Assert.Equal(1, stages.Count(stage => stage.Status == "Queued"));
        Assert.Equal(18, fixture.Recorder.Transactions.Count);
        Assert.Equal(5, fixture.Storage.TotalSaveAttemptCount);
        await WritePrivateAsync(fixture, "draft-rls-audit-export-adapters.json", new
        {
            stages,
            stageControls = new[]
            {
                "betaAuthorizedPackage", "alphaAuthorizedPackage", "foreignTenantClaimLeavesQueuedStateUnchanged",
                "revokedGrantBeforeClaimProducesFailureWithoutPackage", "restoredGrantAuthorizedPackage",
                "grantRevokedDuringProjectionProducesFailureWithoutPackage", "restoredAfterProjectionRevocationAuthorizedPackage",
                "membershipSuspendedDuringProjectionProducesFailureWithoutPackage", "restoredMembershipAuthorizedPackage"
            },
            positivePackageCount = 5, deniedBeforeStorageCount = 3, unchangedForeignQueuedJobCount = 1,
            fixture.Storage.TotalSaveAttemptCount,
            actualClaimsReadBeforeCurrentGrantRevocation = projectionObserved,
            suppliedActorAuthority = "SYNTHETIC_CURRENT_PERSISTED_MEMBER_AND_CAPABILITY",
            transactionOwnership = "EXPLICIT_CALLER_OWNED_TEST_TRANSACTION",
            productProcessorTransactionCompatibility = "UNVERIFIED", httpIssuance = "UNVERIFIED"
        });
    });

    [PostgreSqlFact]
    public Task ActualAuditExportWorkerRetainsGlobalDiscoveryContextCompatibilityHold() => WithFixtureAsync(async fixture =>
    {
        var alpha = await QueueAsync(fixture, fixture.Alpha);
        var beta = await QueueAsync(fixture, fixture.Beta);
        var initialAlpha = await StateAsync(fixture, alpha);
        var initialBeta = await StateAsync(fixture, beta);
        await AssertOwnedDiscoveryAsync(fixture, fixture.Alpha);
        await AssertOwnedDiscoveryAsync(fixture, fixture.Beta);
        var discovery = new DiscoveryObservations();
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantService>();
        services.AddScoped<ICurrentTenantAccessor>(provider => provider.GetRequiredService<CurrentTenantService>());
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<CurrentTenantService>());
        services.AddScoped(provider => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Connection).Options,
            provider.GetRequiredService<ICurrentTenant>()));
        services.AddScoped<IAuditPackageExportProcessor>(scope =>
        {
            var tenant = scope.GetRequiredService<ICurrentTenant>();
            var processor = Compose(scope.GetRequiredService<AppDbContext>(), tenant, fixture.Storage, new Actor(null)).Processor;
            return new ObservedDiscovery(processor, tenant, discovery);
        });
        await using var provider = services.BuildServiceProvider();
        using var worker = new AuditPackageExportWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AuditPackageExportWorkerOptions()), NullLogger<AuditPackageExportWorker>.Instance);
        await worker.RunOnceAsync();
        Assert.Equal(1, discovery.InvocationCount);
        Assert.Equal(0, discovery.ReturnedTenantCount);
        Assert.True(discovery.PlatformScopeObserved);
        Assert.Equal(initialAlpha, await StateAsync(fixture, alpha));
        Assert.Equal(initialBeta, await StateAsync(fixture, beta));
        Assert.False(await fixture.Storage.ExistsAsync(await StorageKeyAsync(fixture, alpha)));
        Assert.False(await fixture.Storage.ExistsAsync(await StorageKeyAsync(fixture, beta)));
        await AssertResetAsync(fixture);
        Assert.Equal(4, fixture.Recorder.Transactions.Count);
        Assert.Equal(0, fixture.Storage.TotalSaveAttemptCount);
        await WritePrivateAsync(fixture, "draft-rls-audit-export-worker-discovery.json", new
        {
            actualWorker = typeof(AuditPackageExportWorker).FullName, actualRunOnceInvoked = true,
            discovery.InvocationCount, discovery.ReturnedTenantCount, discovery.PlatformScopeObserved,
            positiveCallerOwnedDiscoveryCount = 2,
            fixture.Storage.TotalSaveAttemptCount,
            queuedBefore = new[] { initialAlpha, initialBeta }, queuedAfterUnchanged = true,
            observedCompatibility = "GLOBAL_DISCOVERY_WITHOUT_BOUNDED_DATABASE_CONTEXT",
            actualWorkerCompletion = "UNVERIFIED", discoveryAuthority = "REQUIRES_OWNER_REVIEW",
            transactionOwnership = "CURRENT_WORKER_NO_TEST_AMBIENT_TRANSACTION"
        });
    });

    private sealed record Fixture(string Database, string Connection, string Role, SecurityArchitectureRlsRowFixtures.Seed Alpha,
        SecurityArchitectureRlsRowFixtures.Seed Beta, ObservedStorage Storage, SecurityArchitectureRlsRuntimeContext.Recorder Recorder);
    private sealed record Composition(AuditPackageExportService Service, AuditPackageExportProcessor Processor);
    private sealed record State(string Status, string? ErrorCode, long SizeBytes, long AuditCount, string RowsDigest,
        string? StoredBytesDigest = null, int PackageEntryCount = 0, int PackageSaveAttemptCount = 0);

    private static async Task WithFixtureAsync(Func<Fixture, Task> scenario)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_export_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-rls-export-" + Guid.NewGuid().ToString("N"));
        var storage = new ObservedStorage(new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = directory })));
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var beta = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                foreach (var seed in new[] { alpha, beta })
                {
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                        UPDATE tenant_users SET "Status"='Active',"Role"='Member' WHERE "TenantId"=@tenant;
                        UPDATE workspace_members SET "Status"='Active',"Role"='Owner' WHERE "TenantId"=@tenant;
                        UPDATE project_members SET "Role"='Owner' WHERE "TenantId"=@tenant;
                        UPDATE projects SET "Status"='Active',"Visibility"='MembersOnly' WHERE "TenantId"=@tenant;
                        UPDATE artifact_evidence SET "SourceKind"='ArtifactVersion',"SourceReference"=@version WHERE "TenantId"=@tenant;
                        """, ("tenant", seed.Tenant), ("version", ((ArtifactVersion)seed.Entities["artifact_versions"]).Id.ToString()));
                    await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                    foreach (var capability in new[] { CapabilityKeys.AuditView, CapabilityKeys.AuditExport })
                    {
                        context.Set<CapabilityGrant>().Add(new CapabilityGrant
                        {
                            TenantId = seed.Tenant, SubjectUserId = ((User)seed.Entities["users"]).Id,
                            GrantedByUserId = ((User)seed.Entities["users"]).Id, CapabilityKey = capability,
                            ScopeType = CapabilityScopeType.Tenant, ScopeId = seed.Tenant, GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                        });
                    }
                    await context.SaveChangesAsync();
                }
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT ON export_jobs,file_objects,file_versions,audit_logs,users,tenants,tenant_users,capability_grants,
                        workspaces,workspace_members,projects,project_members,groups,group_members,
                        artifacts,artifact_versions,attachments,artifact_claims,artifact_evidence,artifact_findings,audit_finding_decisions TO "{role}";
                    GRANT INSERT ON export_jobs,file_objects,file_versions,audit_logs TO "{role}";
                    GRANT UPDATE ON export_jobs,file_objects TO "{role}";
                    """);
                foreach (var table in ProtectedTables)
                {
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY; ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_export ON public."{table}" TO "{role}"
                            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                            WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                        """);
                }
                var connection = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1 }.ConnectionString;
                try
                {
                    var fixture = new Fixture(database, connection, role, alpha, beta, storage, new());
                    await AssertRoleAuthorityAsync(fixture);
                    await scenario(fixture);
                }
                finally
                {
                    using var pooled = new NpgsqlConnection(connection);
                    NpgsqlConnection.ClearPool(pooled);
                }
            });
        }
        finally
        {
            try
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\"");
            }
            finally
            {
                var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.StartsWith(prefix + "coglatas-sec-arch-rls-export-", Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    private static Composition Compose(AppDbContext context, ICurrentTenant tenant, IFileStorageService storage, Actor actor,
        Func<Task>? afterProjection = null)
    {
        var clock = new Clock();
        var unit = new EfUnitOfWork(context);
        var tenantRepository = new TenantRepository(context);
        var tenantAuthorization = new TenantAuthorizationService(tenantRepository);
        var workspaces = new WorkspaceRepository(context);
        var groups = new GroupRepository(context);
        var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(context), workspaces, tenantAuthorization);
        var projects = new ProjectAuthorizationService(new ProjectRepository(context), workspaceAuthorization,
            new GroupAuthorizationService(groups, workspaces, workspaceAuthorization), groups);
        var artifacts = new ArtifactRepository(context);
        var artifactAuthorization = new ArtifactAuthorizationService(artifacts, projects);
        var files = new FileRepository(context);
        var fileAuthorization = new FileAuthorizationService(files, projects, Unused<IConversationAuthorizationService>(),
            Unused<IChannelAuthorizationService>(), workspaceAuthorization);
        var capability = new CapabilityGrantEvaluator(new CapabilityGrantRepository(context), tenantRepository, workspaces, tenant, clock);
        var audit = new DbAuditLogger(context, clock, actor, tenant);
        var auditAuthorization = new AuditAuthorizationService(actor, tenant, tenantAuthorization, capability, audit, unit);
        IArtifactEvidenceRepository evidence = new ArtifactEvidenceRepository(context);
        if (afterProjection is not null)
        {
            evidence = new ObservedEvidence(evidence, afterProjection);
        }
        var claims = new DbAuditClaimsEvidenceService(context, artifacts, evidence, artifactAuthorization, files, fileAuthorization, auditAuthorization, actor);
        return new(new(context, artifacts, claims, auditAuthorization, actor, storage, audit, unit, clock),
            new(context, artifacts, evidence, artifactAuthorization, files, fileAuthorization, tenantAuthorization, capability, storage, audit, unit, clock));
    }

    private static async Task<Guid> QueueAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        Guid job;
        await using (var context = Context(fixture, seed))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await AssertOperationIdentityAsync(context, fixture.Role, seed.Tenant);
            var response = await Compose(context, Tenant(seed), fixture.Storage, new Actor(((User)seed.Entities["users"]).Id)).Service
                .QueueAsync(new(((ArtifactVersion)seed.Entities["artifact_versions"]).Id));
            Assert.True(response.IsSuccess, response.Error);
            job = response.Value!.JobId;
            await transaction.CommitAsync();
        }
        await AssertResetAsync(fixture);
        return job;
    }

    private static async Task ProcessAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed, Guid job, Func<Task>? afterProjection = null)
    {
        await using (var context = Context(fixture, seed))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await AssertOperationIdentityAsync(context, fixture.Role, seed.Tenant);
            await Compose(context, Tenant(seed), fixture.Storage, new Actor(((User)seed.Entities["users"]).Id), afterProjection).Processor.ProcessAsync(job);
            await transaction.CommitAsync();
        }
        await AssertResetAsync(fixture);
    }

    private static async Task AssertOwnedDiscoveryAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        await using (var context = Context(fixture, seed))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await AssertOperationIdentityAsync(context, fixture.Role, seed.Tenant);
            var processor = Compose(context, Tenant(seed), fixture.Storage, new Actor(((User)seed.Entities["users"]).Id)).Processor;
            var tenants = await processor.ListQueuedTenantIdsAsync(50, DateTimeOffset.UtcNow.AddMinutes(-10));
            Assert.Equal(seed.Tenant, Assert.Single(tenants));
            await transaction.CommitAsync();
        }
        await AssertResetAsync(fixture);
    }

    private static AppDbContext Context(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed) => SecurityArchitectureRlsRuntimeContext.Create(
        fixture.Connection, new(seed.Tenant, "syntheticSuppliedAuditExportActor", ((User)seed.Entities["users"]).Id, null), fixture.Recorder);
    private static CurrentTenantService Tenant(SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(seed.Tenant, "synthetic-export");
        return tenant;
    }
    private static Task SetExportGrantAsync(Fixture fixture, bool revoked) => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
        "UPDATE capability_grants SET \"RevokedAt\"=CASE WHEN @revoked THEN now() ELSE NULL END,\"VersionNo\"=\"VersionNo\"+1 WHERE \"TenantId\"=@tenant AND \"CapabilityKey\"=@key",
        ("revoked", revoked), ("tenant", fixture.Alpha.Tenant), ("key", CapabilityKeys.AuditExport));
    private static Task SetMembershipAsync(Fixture fixture, bool active) => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
        "UPDATE tenant_users SET \"Status\"=@status WHERE \"TenantId\"=@tenant AND \"UserId\"=@user",
        ("status", active ? "Active" : "Suspended"), ("tenant", fixture.Alpha.Tenant), ("user", ((User)fixture.Alpha.Entities["users"]).Id));
    private static Task<string> StorageKeyAsync(Fixture fixture, Guid job) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
        "SELECT f.\"StorageKey\" FROM export_jobs j JOIN file_objects f ON f.\"Id\"=j.\"FileObjectId\" WHERE j.\"Id\"=@job", ("job", job));
    private static async Task<State> StateAsync(Fixture fixture, Guid job)
    {
        var rows = await PostgreSqlMigrationTestDatabase.QueryAsync(fixture.Database, """
            SELECT j."Status",j."ErrorMessage",f."SizeBytes",(SELECT count(*) FROM audit_logs a WHERE a."EntityId"=j."Id"),
                encode(sha256(convert_to(to_jsonb(j)::text||to_jsonb(f)::text||coalesce((SELECT string_agg(to_jsonb(a)::text,'|' ORDER BY a."Id")
                    FROM audit_logs a WHERE a."EntityId"=j."Id"),'')||coalesce((SELECT string_agg(to_jsonb(v)::text,'|' ORDER BY v."Id")
                    FROM file_versions v WHERE v."FileObjectId"=f."Id"),''),'UTF8')),'hex')
            FROM export_jobs j JOIN file_objects f ON f."Id"=j."FileObjectId" WHERE j."Id"=@job
            """, reader => new State(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4)), ("job", job));
        var state = Assert.Single(rows);
        Assert.Matches("^[a-f0-9]{64}$", state.RowsDigest);
        return state with { PackageSaveAttemptCount = fixture.Storage.SaveAttemptCount(await StorageKeyAsync(fixture, job)) };
    }
    private static async Task<State> AssertPackageAsync(Fixture fixture, Guid job)
    {
        var state = await StateAsync(fixture, job);
        Assert.Equal("Completed", state.Status);
        Assert.Null(state.ErrorCode);
        Assert.InRange(state.SizeBytes, 1L, 25L * 1024 * 1024);
        Assert.Equal(2L, state.AuditCount);
        Assert.Equal(1, state.PackageSaveAttemptCount);
        await using var stream = await fixture.Storage.OpenReadAsync(await StorageKeyAsync(fixture, job));
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        Assert.Equal(state.SizeBytes, bytes.Length);
        var digest = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
        var storedDigest = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
            "SELECT f.\"HashSha256\" FROM export_jobs j JOIN file_objects f ON f.\"Id\"=j.\"FileObjectId\" WHERE j.\"Id\"=@job", ("job", job));
        Assert.Equal(storedDigest, digest);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        Assert.Equal(new[] { "audit-report.json", "claim-evidence.json", "risk-decisions.json", "run-metadata.json", "source-manifest.json" },
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.All(zip.Entries, entry => Assert.True(entry.Length > 0));
        await using var projectionStream = zip.GetEntry("claim-evidence.json")!.Open();
        using var projection = await JsonDocument.ParseAsync(projectionStream);
        var claims = projection.RootElement.GetProperty("claims").EnumerateArray().ToArray();
        Assert.NotEmpty(claims);
        Assert.All(claims, claim => Assert.NotEmpty(claim.GetProperty("evidence").EnumerateArray()));
        var observation = state with { StoredBytesDigest = digest, PackageEntryCount = zip.Entries.Count };
        Assert.Matches("^[a-f0-9]{64}$", observation.StoredBytesDigest!);
        Assert.Equal(5, observation.PackageEntryCount);
        return observation;
    }
    private static async Task<State> AssertDeniedAsync(Fixture fixture, Guid job)
    {
        var state = await StateAsync(fixture, job);
        Assert.Equal("Failed", state.Status);
        Assert.Equal("AuthorizationChanged", state.ErrorCode);
        Assert.Equal(0L, state.SizeBytes);
        Assert.Equal(2L, state.AuditCount);
        Assert.Equal(0, state.PackageSaveAttemptCount);
        Assert.False(await fixture.Storage.ExistsAsync(await StorageKeyAsync(fixture, job)));
        return state;
    }
    private static async Task AssertResetAsync(Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_user,r.rolsuper,r.rolbypassrls,current_setting('coglatas.tenant_id',true) FROM pg_roles r WHERE r.rolname=current_user", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(fixture.Role, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.True(reader.IsDBNull(3) || reader.GetString(3).Length == 0);
    }

    private static async Task AssertOperationIdentityAsync(AppDbContext context, string role, Guid tenant)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT current_user,current_setting('coglatas.tenant_id',true)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(role, reader.GetString(0));
        Assert.Equal(tenant.ToString("D"), reader.GetString(1));
    }

    private static async Task AssertRoleAuthorityAsync(Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("""
            SELECT current_user,r.rolsuper,r.rolbypassrls,r.rolcreatedb,r.rolcreaterole,r.rolinherit,
                (SELECT count(*) FROM pg_auth_members WHERE member=r.oid),
                (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relowner=r.oid)
            FROM pg_roles r WHERE r.rolname=current_user
            """, connection))
        {
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(fixture.Role, reader.GetString(0));
            for (var index = 1; index <= 5; index++)
            {
                Assert.False(reader.GetBoolean(index));
            }
            Assert.Equal(0L, reader.GetInt64(6));
            Assert.Equal(0L, reader.GetInt64(7));
        }

        await using var privileges = new NpgsqlCommand("""
            SELECT c.relname,
                has_table_privilege(current_user,c.oid,'SELECT'),has_table_privilege(current_user,c.oid,'INSERT'),
                has_table_privilege(current_user,c.oid,'UPDATE'),has_table_privilege(current_user,c.oid,'DELETE'),
                has_table_privilege(current_user,c.oid,'TRUNCATE'),has_table_privilege(current_user,c.oid,'REFERENCES'),
                has_table_privilege(current_user,c.oid,'TRIGGER')
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname
            """, connection);
        await using var tables = await privileges.ExecuteReaderAsync();
        var reads = new List<string>();
        var inserts = new List<string>();
        var updates = new List<string>();
        while (await tables.ReadAsync())
        {
            var name = tables.GetString(0);
            if (tables.GetBoolean(1))
            {
                reads.Add(name);
            }
            if (tables.GetBoolean(2))
            {
                inserts.Add(name);
            }
            if (tables.GetBoolean(3))
            {
                updates.Add(name);
            }
            for (var index = 4; index <= 7; index++)
            {
                Assert.False(tables.GetBoolean(index));
            }
        }
        Assert.Equal(ReadTables.Order(StringComparer.Ordinal), reads.Order(StringComparer.Ordinal));
        Assert.Equal(ProtectedTables.Order(StringComparer.Ordinal), inserts.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "export_jobs", "file_objects" }, updates);
    }

    private static async Task WritePrivateAsync(Fixture fixture, string name, object observations)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsAuditExportTests).Assembly.Location);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        foreach (var transaction in fixture.Recorder.Transactions)
        {
            Assert.Equal("syntheticSuppliedAuditExportActor", transaction.AuthorityKind);
            Assert.NotEqual(Guid.Empty, transaction.TransactionId);
            Assert.True(transaction.BackendProcessId > 0);
        }
        await using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is not null)
        {
            Assert.Matches("^[a-f0-9]{40}$", candidate);
        }
        await JsonSerializer.SerializeAsync(output, new { schemaVersion = 1, candidateSha = candidate,
            testAssemblyDigest = digest, approval = "DRAFT", ownerApproval = (string?)null, observations, databaseRole = fixture.Role,
            selectedDraftPolicyCount = ProtectedTables.Length, transactionCount = fixture.Recorder.Transactions.Count,
            selectPrivilegeCount = ReadTables.Length, insertPrivilegeCount = ProtectedTables.Length, updatePrivilegeCount = 2,
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database, "SHOW server_version"),
            productRlsAppliedCount = 0, operationalRoleEquivalence = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
    private sealed class Actor(Guid? user) : ICurrentUser
    {
        public Guid? UserId => user;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => user.HasValue;
    }
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
    private sealed class ObservedEvidence(IArtifactEvidenceRepository inner, Func<Task> afterProjection) : IArtifactEvidenceRepository
    {
        public Task<bool> HasClaimsAsync(Guid id, CancellationToken cancellationToken = default) => inner.HasClaimsAsync(id, cancellationToken);
        public async Task<IReadOnlyList<ArtifactClaim>> ListClaimsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var rows = await inner.ListClaimsAsync(id, cancellationToken);
            Assert.NotEmpty(rows);
            await afterProjection();
            return rows;
        }
        public Task AddClaimsAsync(IReadOnlyCollection<ArtifactClaim> claims, CancellationToken cancellationToken = default) => inner.AddClaimsAsync(claims, cancellationToken);
    }

    private sealed class DiscoveryObservations
    {
        public int InvocationCount { get; set; }
        public int ReturnedTenantCount { get; set; }
        public bool PlatformScopeObserved { get; set; }
    }

    private sealed class ObservedStorage(IFileStorageService inner) : IFileStorageService
    {
        private readonly ConcurrentDictionary<string, int> _saveAttempts = new(StringComparer.Ordinal);
        public int TotalSaveAttemptCount => _saveAttempts.Values.Sum();
        public int SaveAttemptCount(string key) => _saveAttempts.GetValueOrDefault(key);

        public Task<Result> SaveAsync(string storageKey, Stream stream, string contentType, CancellationToken cancellationToken = default)
        {
            _saveAttempts.AddOrUpdate(storageKey, 1, (_, count) => count + 1);
            return inner.SaveAsync(storageKey, stream, contentType, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            inner.OpenReadAsync(storageKey, cancellationToken);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(storageKey, cancellationToken);
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(storageKey, cancellationToken);
        public Task<string?> CreateSignedReadUrlAsync(string storageKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            inner.CreateSignedReadUrlAsync(storageKey, expiresIn, cancellationToken);
    }

    private sealed class ObservedDiscovery(IAuditPackageExportProcessor inner, ICurrentTenant tenant,
        DiscoveryObservations observations) : IAuditPackageExportProcessor
    {
        public async Task<IReadOnlyList<Guid>> ListQueuedTenantIdsAsync(int take, DateTimeOffset staleBefore,
            CancellationToken cancellationToken = default)
        {
            observations.InvocationCount++;
            observations.PlatformScopeObserved = tenant.IsPlatformScope;
            var result = await inner.ListQueuedTenantIdsAsync(take, staleBefore, cancellationToken);
            observations.ReturnedTenantCount = result.Count;
            return result;
        }

        public Task<int> RecoverStaleRunningAsync(DateTimeOffset staleBefore, DateTimeOffset now,
            CancellationToken cancellationToken = default) => inner.RecoverStaleRunningAsync(staleBefore, now, cancellationToken);
        public Task<IReadOnlyList<Guid>> ListQueuedJobIdsAsync(int take,
            CancellationToken cancellationToken = default) => inner.ListQueuedJobIdsAsync(take, cancellationToken);
        public Task ProcessAsync(Guid job, CancellationToken cancellationToken = default) => inner.ProcessAsync(job, cancellationToken);
    }

    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedDependency>();
    public class UnusedDependency : DispatchProxy
    {
        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("This artifact-version export fixture does not compose " + targetMethod?.Name);
    }
}
