using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.Messaging;
using Coglatas.Application.Realtime;
using Coglatas.Application.TenantAdministration;
using Coglatas.Application.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using AttachmentResponse = Coglatas.Application.Files.AttachmentResponse;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Actual file adapters with selected test-only policies; current product authority remains separate.</summary>
public sealed class SecurityArchitectureRlsFileVersionAdapterTests
{
    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("Synthetic SEC-ARCH file bytes.");
    private static readonly string[] ProtectedTables = ["file_objects", "file_versions", "attachments", "audit_logs", "outbox_events"];
    private static readonly string[] ReadTables =
    [
        "file_objects", "file_versions", "attachments", "audit_logs", "outbox_events", "users", "tenants", "tenant_users",
        "workspaces", "workspace_members", "projects", "project_members", "groups", "group_members", "task_items",
        "tenant_settings", "subscriptions", "plans", "usage_records", "file_access_grants"
    ];

    [PostgreSqlFact]
    public Task ActualFileUploadCreatesNativeVersionAndPreservesCurrentAdmissionAndSoftDeleteBoundaries() => WithFixtureAsync(async fixture =>
    {
        var initial = await SnapshotAsync(fixture);
        var beta = await UploadAsync(fixture, fixture.Beta);
        var betaObservation = await AssertUploadedAsync(fixture, fixture.Beta, beta);
        var alpha = await UploadAsync(fixture, fixture.Alpha);
        var alphaObservation = await AssertUploadedAsync(fixture, fixture.Alpha, alpha);
        AssertAdded(initial, await SnapshotAsync(fixture), 2);

        var beforeForeign = await SnapshotAsync(fixture);
        var saveCount = fixture.Storage.SaveCount;
        var foreign = await InScopeAsync(fixture, fixture.Alpha, (_, composition) => UploadAsync(composition.Service, fixture.Beta));
        Assert.False(foreign.IsSuccess);
        Assert.Equal("You are not allowed to upload an attachment for this resource.", foreign.Error);
        Assert.Equal(saveCount, fixture.Storage.SaveCount);
        Assert.Equal(beforeForeign, await SnapshotAsync(fixture));

        await SetMembershipAsync(fixture, active: false);
        var beforeRevocation = await SnapshotAsync(fixture);
        var revoked = await InScopeAsync(fixture, fixture.Alpha, (_, composition) => UploadAsync(composition.Service, fixture.Alpha));
        Assert.False(revoked.IsSuccess);
        Assert.Equal("You are not allowed to upload an attachment for this resource.", revoked.Error);
        Assert.Equal(saveCount, fixture.Storage.SaveCount);
        Assert.Equal(beforeRevocation, await SnapshotAsync(fixture));
        var openCount = fixture.Storage.OpenCount;
        var deniedVersion = await InScopeAsync(fixture, fixture.Alpha, (_, composition) =>
            composition.Activity.ViewVersionAsync(alpha.FileObjectId, alpha.FileObjectId));
        Assert.False(deniedVersion.IsSuccess);
        Assert.Equal("FILE_NOT_FOUND", deniedVersion.ErrorDetail?.Code);
        Assert.Equal(openCount, fixture.Storage.OpenCount);
        var deniedDelete = await InScopeAsync(fixture, fixture.Alpha, (_, composition) => composition.Service.DeleteAsync(alpha.Id));
        Assert.False(deniedDelete.IsSuccess);
        Assert.Equal(beforeRevocation, await SnapshotAsync(fixture));
        await SetMembershipAsync(fixture, active: true);
        var restored = await UploadAsync(fixture, fixture.Alpha);
        var restoredObservation = await AssertUploadedAsync(fixture, fixture.Alpha, restored);

        var ledger = await VersionDigestAsync(fixture, alpha.FileObjectId);
        var deletion = await InScopeAsync(fixture, fixture.Alpha, (_, composition) => composition.Service.DeleteAsync(alpha.Id));
        Assert.True(deletion.IsSuccess, deletion.Error);
        Assert.Equal(ledger, await VersionDigestAsync(fixture, alpha.FileObjectId));
        Assert.Equal("Deleted", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
            "SELECT \"Status\" FROM file_objects WHERE \"Id\"=@id", ("id", alpha.FileObjectId)));
        Assert.True(await fixture.Storage.ExistsAsync(await StorageKeyAsync(fixture, alpha.FileObjectId)));
        var afterDeleteOpenCount = fixture.Storage.OpenCount;
        var deletedVersion = await InScopeAsync(fixture, fixture.Alpha, (_, composition) =>
            composition.Activity.ViewVersionAsync(alpha.FileObjectId, alpha.FileObjectId));
        Assert.False(deletedVersion.IsSuccess);
        Assert.Equal(afterDeleteOpenCount, fixture.Storage.OpenCount);

        var authorityChangedDuringStorage = false;
        fixture.Storage.AfterSave = async () =>
        {
            authorityChangedDuringStorage = true;
            await SetMembershipAsync(fixture, active: false);
        };
        var duringStorage = await UploadAsync(fixture, fixture.Alpha);
        fixture.Storage.AfterSave = null;
        Assert.True(authorityChangedDuringStorage);
        var currentCanUpload = await InScopeAsync(fixture, fixture.Alpha, (_, composition) =>
            composition.Authorization.CanUploadAttachment(UserId(fixture.Alpha), AttachmentOwnerType.Workspace, WorkspaceId(fixture.Alpha)));
        Assert.False(currentCanUpload);
        var duringStorageObservation = await AssertNativeAndStorageAsync(fixture, duringStorage);
        await SetMembershipAsync(fixture, active: true);
        var final = await UploadAsync(fixture, fixture.Alpha);
        var finalObservation = await AssertUploadedAsync(fixture, fixture.Alpha, final);
        Assert.Equal(5, fixture.Storage.SaveCount);
        Assert.Equal(0, fixture.Storage.DeleteCount);
        await WritePrivateAsync(fixture, "draft-rls-file-version-admission.json", new
        {
            authorizedPositiveUploads = new[] { betaObservation, alphaObservation, restoredObservation, finalObservation },
            foreignAdmissionNoEffects = true, currentSuspensionAdmissionNoEffects = true,
            currentSuspensionVersionReadBeforeStorageDenied = true, currentSuspensionDeleteNoEffects = true,
            softDeletePreservesImmutableVersionDigest = ledger, softDeletedBytesRetained = true,
            duringStorageCurrentAuthority = new
            {
                committedSuspensionObserved = authorityChangedDuringStorage, currentCanUpload,
                observedPersistedUpload = duringStorageObservation, successfulAuthorizationDenialCredit = false,
                requiredRevalidation = "UNVERIFIED", currentSourceBoundary = "NO_REAUTHORIZATION_AFTER_STORAGE_RETURNS"
            },
            fixture.Storage.SaveCount, fixture.Storage.DeleteCount,
            fileVersionReplacementProducer = "NOT_PRESENT_IN_CURRENT_ACTIVE_INTERFACE",
            authenticatedHttpIssuance = "UNVERIFIED", operationalWorkerDiscovery = "UNVERIFIED"
        });
    });

    [PostgreSqlFact]
    public Task ActualFileUploadCompensatesDatabaseFailureButCallerRollbackRetainsStorageCompatibilityHold() => WithFixtureAsync(async fixture =>
    {
        var positive = await UploadAsync(fixture, fixture.Alpha);
        await AssertUploadedAsync(fixture, fixture.Alpha, positive);
        var baseline = await SnapshotAsync(fixture);
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, $"REVOKE INSERT ON attachments FROM \"{fixture.Role}\"");
        try
        {
            await using (var context = Context(fixture, fixture.Alpha))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                await AssertIdentityAsync(context, fixture, fixture.Alpha.Tenant);
                Assert.False(await HasInsertAsync(context, "attachments"));
                var failure = await Assert.ThrowsAsync<DbUpdateException>(() => UploadAsync(Compose(context, fixture, fixture.Alpha).Service, fixture.Alpha));
                var databaseFailure = Assert.IsType<PostgresException>(failure.InnerException);
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, databaseFailure.SqlState);
                Assert.Contains("attachments", databaseFailure.MessageText, StringComparison.Ordinal);
                Assert.Equal(2, fixture.Storage.SaveCount);
                Assert.Equal(1, fixture.Storage.DeleteCount);
                Assert.False(await fixture.Storage.ExistsAsync(fixture.Storage.LastSavedKey!));
                await AssertIdentityAsync(context, fixture, fixture.Alpha.Tenant);
                await transaction.RollbackAsync();
            }
        }
        finally
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, $"GRANT INSERT ON attachments TO \"{fixture.Role}\"");
        }
        await AssertResetAsync(fixture);
        Assert.Equal(baseline, await SnapshotAsync(fixture));
        var restored = await UploadAsync(fixture, fixture.Alpha);
        await AssertUploadedAsync(fixture, fixture.Alpha, restored);
        var beforeRollback = await SnapshotAsync(fixture);
        Guid rollbackFile;
        await using (var context = Context(fixture, fixture.Alpha))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await AssertIdentityAsync(context, fixture, fixture.Alpha.Tenant);
            var response = await UploadAsync(Compose(context, fixture, fixture.Alpha).Service, fixture.Alpha);
            Assert.True(response.IsSuccess, response.Error);
            rollbackFile = response.Value!.FileObjectId;
            Assert.Single(await new FileRepository(context).ListFileVersionsAsync(fixture.Alpha.Tenant, rollbackFile, 10));
            await transaction.RollbackAsync();
        }
        await AssertResetAsync(fixture);
        Assert.Equal(beforeRollback, await SnapshotAsync(fixture));
        var rollbackKey = fixture.Storage.LastSavedKey!;
        Assert.True(await fixture.Storage.ExistsAsync(rollbackKey));
        Assert.Equal(0L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
            "SELECT count(*) FROM file_objects WHERE \"Id\"=@id", ("id", rollbackFile)));
        Assert.Equal(4, fixture.Storage.SaveCount);
        Assert.Equal(1, fixture.Storage.DeleteCount);
        await WritePrivateAsync(fixture, "draft-rls-file-version-compensation.json", new
        {
            actualPersistenceFailure = new { mechanism = "GRANT", sqlState = "42501", objectKind = "attachments", rlsDenialCredit = false },
            databaseAndNativeLedgerRollbackDigest = baseline.RowsDigest,
            applicationCompensationDeleteCount = fixture.Storage.DeleteCount, restoredPositiveUpload = true,
            successfulOuterTransactionRollback = true, rollbackRowsDigest = beforeRollback.RowsDigest,
            persistedRollbackFileCount = 0, retainedStorageObjectCount = 1,
            callerRollbackStorageCompensation = "UNVERIFIED", fixture.Storage.SaveCount,
            transactionOwnership = "EXPLICIT_CALLER_OWNED_TEST_TRANSACTION"
        });
        // Owned fixture cleanup is separate from the actual application's observed compensation.
        await fixture.Storage.DeleteAsync(rollbackKey);
    });

    private sealed record Fixture(string Database, string Connection, string Role, SecurityArchitectureRlsRowFixtures.Seed Alpha,
        SecurityArchitectureRlsRowFixtures.Seed Beta, ObservedStorage Storage, FileStorageOptions StorageOptions,
        SecurityArchitectureRlsRuntimeContext.Recorder Recorder);
    private sealed record Composition(FileService Service, FileActivityService Activity, FileAuthorizationService Authorization);
    private sealed record Snapshot(long Files, long Versions, long Attachments, long Audits, long Events, string RowsDigest);
    private sealed record UploadedObservation(int NativeVersionNumber, long StoredSizeBytes, string StoredBytesDigest,
        string NativeLedgerDigest, long UploadAuditCount, long OutboxEventCount);

    private static async Task WithFixtureAsync(Func<Fixture, Task> scenario)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_file_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-rls-file-" + Guid.NewGuid().ToString("N"));
        var options = new FileStorageOptions { RootPath = directory, AllowedExtensions = [".txt"], AllowedContentTypes = ["text/plain"] };
        var storage = new ObservedStorage(new LocalFileStorageService(Options.Create(options)));
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var beta = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    UPDATE tenant_users SET "Status"='Active',"Role"='Member';
                    UPDATE workspaces SET "Status"='Active';
                    UPDATE workspace_members SET "Status"='Active',"Role"='Owner';
                    UPDATE plans SET "EnabledFeaturesJson"='["FileSharing"]';
                    UPDATE tenant_settings SET "FeatureFlagsJson"='{"FileSharing":true}';
                    """);
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT ON {string.Join(',', ReadTables)} TO "{role}";
                    GRANT INSERT ON {string.Join(',', ProtectedTables)} TO "{role}";
                    GRANT UPDATE ON file_objects,attachments TO "{role}";
                    """);
                foreach (var table in ProtectedTables)
                {
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_file ON public."{table}" TO "{role}"
                            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                            WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                        """);
                }
                var connection = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1 }.ConnectionString;
                try
                {
                    var fixture = new Fixture(database, connection, role, alpha, beta, storage, options, new());
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
                Assert.StartsWith(prefix + "coglatas-sec-arch-rls-file-", Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Composition Compose(AppDbContext context, Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(seed.Tenant, "synthetic-file");
        var actor = new Actor(UserId(seed));
        var clock = new Clock();
        var repository = new FileRepository(context);
        var tenantPlans = new TenantPlanRepository(context);
        var workspaces = new WorkspaceAuthorizationService(new UserRepository(context), new WorkspaceRepository(context),
            new TenantAuthorizationService(new TenantRepository(context)));
        var authorization = new FileAuthorizationService(repository, Unused<Coglatas.Application.Projects.IProjectAuthorizationService>(),
            Unused<IConversationAuthorizationService>(), Unused<IChannelAuthorizationService>(), workspaces);
        var unit = new EfUnitOfWork(context);
        var invalidations = new BusinessInvalidationPublisher(new TransactionalOutbox(new OutboxEventRepository(context), tenant, clock), tenant, clock);
        return new(new(repository, new FileDownloadGrantRepository(context), fixture.Storage, authorization,
                new ConfiguredFileUploadPolicy(Options.Create(fixture.StorageOptions)), new FeatureFlagService(tenantPlans, tenant),
                new QuotaService(tenantPlans), actor, tenant, clock, new DbAuditLogger(context, clock, actor, tenant),
                new Sha256TokenHasher(), invalidations, unit),
            new(repository, new FileAccessGrantRepository(context), authorization, fixture.Storage, actor, tenant), authorization);
    }

    private static AppDbContext Context(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed) => SecurityArchitectureRlsRuntimeContext.Create(
        fixture.Connection, new(seed.Tenant, "syntheticSuppliedFileActor", UserId(seed), null), fixture.Recorder);
    private static Guid UserId(SecurityArchitectureRlsRowFixtures.Seed seed) => ((User)seed.Entities["users"]).Id;
    private static Guid WorkspaceId(SecurityArchitectureRlsRowFixtures.Seed seed) => ((Workspace)seed.Entities["workspaces"]).Id;

    private static async Task<T> InScopeAsync<T>(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed,
        Func<AppDbContext, Composition, Task<T>> action)
    {
        T result;
        await using (var context = Context(fixture, seed))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await AssertIdentityAsync(context, fixture, seed.Tenant);
            result = await action(context, Compose(context, fixture, seed));
            await transaction.CommitAsync();
        }
        await AssertResetAsync(fixture);
        return result;
    }

    private static async Task<Result<AttachmentResponse>> UploadAsync(FileService service, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        await using var bytes = new MemoryStream(FileBytes, writable: false);
        return await service.UploadAsync(new(AttachmentOwnerType.Workspace, WorkspaceId(seed), "synthetic.txt", "text/plain", bytes.Length, bytes));
    }
    private static async Task<AttachmentResponse> UploadAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed)
    {
        var response = await InScopeAsync(fixture, seed, (_, composition) => UploadAsync(composition.Service, seed));
        Assert.True(response.IsSuccess, response.Error);
        return response.Value!;
    }
    private static Task SetMembershipAsync(Fixture fixture, bool active) => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
        "UPDATE workspace_members SET \"Status\"=@status WHERE \"TenantId\"=@tenant AND \"UserId\"=@user",
        ("status", active ? "Active" : "Suspended"), ("tenant", fixture.Alpha.Tenant), ("user", UserId(fixture.Alpha)));
    private static Task<string> StorageKeyAsync(Fixture fixture, Guid file) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
        "SELECT \"StorageKey\" FROM file_objects WHERE \"Id\"=@id", ("id", file));
    private static Task<string> VersionDigestAsync(Fixture fixture, Guid file) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
        "SELECT encode(sha256(convert_to(string_agg(to_jsonb(v)::text,'|' ORDER BY v.\"Id\"),'UTF8')),'hex') FROM file_versions v WHERE v.\"FileObjectId\"=@id", ("id", file));

    private static async Task<UploadedObservation> AssertUploadedAsync(Fixture fixture, SecurityArchitectureRlsRowFixtures.Seed seed, AttachmentResponse file)
    {
        var native = await InScopeAsync(fixture, seed, async (_, composition) =>
        {
            var activity = await composition.Activity.GetAsync(file.FileObjectId);
            Assert.True(activity.IsSuccess, activity.Error);
            var entry = Assert.Single(activity.Value!.Items);
            Assert.Equal("uploaded", entry.Kind);
            Assert.Equal(1, entry.Version!.VersionNumber);
            Assert.Equal(file.FileObjectId, entry.Version.VersionId);
            var response = await composition.Activity.ViewVersionAsync(file.FileObjectId, entry.Version.VersionId);
            Assert.True(response.IsSuccess, response.Error);
            await using var content = response.Value!.Content;
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes);
            Assert.Equal(FileBytes, bytes.ToArray());
            return entry.Version.VersionNumber;
        });
        var observation = await AssertNativeAndStorageAsync(fixture, file);
        Assert.Equal(native, observation.NativeVersionNumber);
        return observation;
    }

    private static async Task<UploadedObservation> AssertNativeAndStorageAsync(Fixture fixture, AttachmentResponse file)
    {
        var rows = await PostgreSqlMigrationTestDatabase.QueryAsync(fixture.Database, """
            SELECT v."VersionNumber",v."SizeBytes",v."StorageKey"=f."StorageKey",v."TenantId"=f."TenantId",
                v."CreatedByUserId"=f."UploadedByUserId",v."HashSha256" IS NOT DISTINCT FROM f."HashSha256",
                (SELECT count(*) FROM audit_logs a WHERE a."EntityId"=f."Id" AND a."Action"='FileUploaded'),
                (SELECT count(*) FROM outbox_events e WHERE e."AggregateId"=f."Id" AND e."EventType"='Files.FileChanged.v1')
            FROM file_versions v JOIN file_objects f ON f."Id"=v."FileObjectId" WHERE f."Id"=@id
            """, reader => (Version: reader.GetInt32(0), Size: reader.GetInt64(1), KeyMatch: reader.GetBoolean(2),
                ScopeMatch: reader.GetBoolean(3), ActorMatch: reader.GetBoolean(4), HashMatch: reader.GetBoolean(5),
                Audits: reader.GetInt64(6), Events: reader.GetInt64(7)), ("id", file.FileObjectId));
        var row = Assert.Single(rows);
        Assert.Equal(1, row.Version);
        Assert.Equal((long)FileBytes.Length, row.Size);
        Assert.True(row.KeyMatch && row.ScopeMatch && row.ActorMatch && row.HashMatch);
        Assert.Equal(1L, row.Audits);
        Assert.Equal(1L, row.Events);
        await using var stream = await fixture.Storage.OpenReadAsync(await StorageKeyAsync(fixture, file.FileObjectId));
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        Assert.Equal(FileBytes, bytes.ToArray());
        var observation = new UploadedObservation(row.Version, bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(),
            await VersionDigestAsync(fixture, file.FileObjectId), row.Audits, row.Events);
        Assert.Equal(1, observation.NativeVersionNumber);
        Assert.Equal((long)FileBytes.Length, observation.StoredSizeBytes);
        Assert.Matches("^[a-f0-9]{64}$", observation.StoredBytesDigest);
        Assert.Matches("^[a-f0-9]{64}$", observation.NativeLedgerDigest);
        Assert.Equal(1L, observation.UploadAuditCount);
        Assert.Equal(1L, observation.OutboxEventCount);
        return observation;
    }

    private static async Task<Snapshot> SnapshotAsync(Fixture fixture)
    {
        var counts = new List<long>();
        var identities = new List<string>();
        foreach (var table in ProtectedTables)
        {
            var rows = await PostgreSqlMigrationTestDatabase.QueryAsync(fixture.Database, $"""
                SELECT count(*),encode(sha256(convert_to(coalesce(string_agg(to_jsonb(r)::text,'|' ORDER BY to_jsonb(r)::text COLLATE "C"),''),'UTF8')),'hex')
                FROM public."{table}" r
                """, reader => (Count: reader.GetInt64(0), Digest: reader.GetString(1)));
            var row = Assert.Single(rows);
            counts.Add(row.Count);
            identities.Add(table + ":" + row.Digest);
        }
        Assert.Equal(5, counts.Count);
        return new(counts[0], counts[1], counts[2], counts[3], counts[4],
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', identities)))).ToLowerInvariant());
    }
    private static void AssertAdded(Snapshot before, Snapshot after, long count)
    {
        Assert.Equal(count, after.Files - before.Files);
        Assert.Equal(count, after.Versions - before.Versions);
        Assert.Equal(count, after.Attachments - before.Attachments);
        Assert.Equal(count, after.Audits - before.Audits);
        Assert.Equal(count, after.Events - before.Events);
        Assert.NotEqual(before.RowsDigest, after.RowsDigest);
    }
    private static async Task AssertIdentityAsync(AppDbContext context, Fixture fixture, Guid tenant)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT current_user,current_setting('coglatas.tenant_id',true)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(fixture.Role, reader.GetString(0));
        Assert.Equal(tenant.ToString("D"), reader.GetString(1));
    }
    private static async Task<bool> HasInsertAsync(AppDbContext context, string table)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT has_table_privilege(current_user,@table,'INSERT')";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
    private static async Task AssertResetAsync(Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_user,current_setting('coglatas.tenant_id',true)", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(fixture.Role, reader.GetString(0));
        Assert.True(reader.IsDBNull(1) || reader.GetString(1).Length == 0);
    }
    private static async Task AssertRoleAuthorityAsync(Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("""
            SELECT current_user,r.rolsuper,r.rolbypassrls,r.rolcreatedb,r.rolcreaterole,r.rolinherit,
                (SELECT count(*) FROM pg_auth_members WHERE member=r.oid),
                (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
            FROM pg_roles r WHERE r.rolname=current_user
            """, connection))
        {
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(fixture.Role, reader.GetString(0));
            for (var index = 1; index <= 5; index++) Assert.False(reader.GetBoolean(index));
            Assert.Equal(0L, reader.GetInt64(6));
            Assert.Equal(0L, reader.GetInt64(7));
        }
        var reads = new List<string>();
        var inserts = new List<string>();
        var updates = new List<string>();
        await using var privileges = new NpgsqlCommand("""
            SELECT c.relname,has_table_privilege(current_user,c.oid,'SELECT'),has_table_privilege(current_user,c.oid,'INSERT'),
                has_table_privilege(current_user,c.oid,'UPDATE'),has_table_privilege(current_user,c.oid,'DELETE'),
                has_table_privilege(current_user,c.oid,'TRUNCATE'),has_table_privilege(current_user,c.oid,'REFERENCES'),has_table_privilege(current_user,c.oid,'TRIGGER')
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname
            """, connection);
        await using var tables = await privileges.ExecuteReaderAsync();
        while (await tables.ReadAsync())
        {
            if (tables.GetBoolean(1)) reads.Add(tables.GetString(0));
            if (tables.GetBoolean(2)) inserts.Add(tables.GetString(0));
            if (tables.GetBoolean(3)) updates.Add(tables.GetString(0));
            for (var index = 4; index <= 7; index++) Assert.False(tables.GetBoolean(index));
        }
        Assert.Equal(ReadTables.Order(StringComparer.Ordinal), reads.Order(StringComparer.Ordinal));
        Assert.Equal(ProtectedTables.Order(StringComparer.Ordinal), inserts.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "attachments", "file_objects" }, updates);
    }
    private static async Task WritePrivateAsync(Fixture fixture, string name, object observations)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is not null) Assert.Matches("^[a-f0-9]{40}$", candidate);
        Assert.All(fixture.Recorder.Transactions, transaction =>
        {
            Assert.Equal("syntheticSuppliedFileActor", transaction.AuthorityKind);
            Assert.NotEqual(Guid.Empty, transaction.TransactionId);
            Assert.True(transaction.BackendProcessId > 0);
        });
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsFileVersionAdapterTests).Assembly.Location);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        await using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, candidateSha = candidate, testAssemblyDigest = digest, approval = "DRAFT", ownerApproval = (string?)null,
            observations, databaseRole = fixture.Role, selectedDraftPolicyCount = ProtectedTables.Length,
            selectPrivilegeCount = ReadTables.Length, insertPrivilegeCount = ProtectedTables.Length, updatePrivilegeCount = 2,
            transactionCount = fixture.Recorder.Transactions.Count,
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database, "SHOW server_version"),
            productRlsAppliedCount = 0, operationalRoleEquivalence = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
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
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
    private sealed class ObservedStorage(IFileStorageService inner) : IFileStorageService
    {
        private readonly ConcurrentQueue<string> _saves = new();
        private readonly ConcurrentQueue<string> _deletes = new();
        public int SaveCount => _saves.Count;
        public int DeleteCount => _deletes.Count;
        public int OpenCount { get; private set; }
        public string? LastSavedKey => _saves.LastOrDefault();
        public Func<Task>? AfterSave { get; set; }
        public async Task<Result> SaveAsync(string storageKey, Stream stream, string contentType, CancellationToken cancellationToken = default)
        {
            _saves.Enqueue(storageKey);
            var result = await inner.SaveAsync(storageKey, stream, contentType, cancellationToken);
            if (result.IsSuccess && AfterSave is not null) await AfterSave();
            return result;
        }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return inner.OpenReadAsync(storageKey, cancellationToken);
        }
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            _deletes.Enqueue(storageKey);
            return inner.DeleteAsync(storageKey, cancellationToken);
        }
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) => inner.ExistsAsync(storageKey, cancellationToken);
        public Task<string?> CreateSignedReadUrlAsync(string storageKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            inner.CreateSignedReadUrlAsync(storageKey, expiresIn, cancellationToken);
    }
    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedDependency>();
    public class UnusedDependency : DispatchProxy
    {
        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("This direct-Workspace file fixture does not compose " + targetMethod?.Name);
    }
}
