using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureOutboxReplayTransportTests
{
    private const string EventType = "Projects.ProjectChanged.v1";
    private const string Reason = "Synthetic application-to-transport replay control";

    [PostgreSqlFact]
    public async Task ActualReplayServiceDeliversOriginalEventAndCurrentGrantRevocationHasNoTransportOrAuditEffects()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            var alphaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var betaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            Assert.True(await member.SubscribeAsync("SubscribeProject", alphaScope.Project));
            Assert.True(await owner.SubscribeAsync("SubscribeProject", alphaScope.Project));
            Assert.True(await beta.SubscribeAsync("SubscribeProject", betaScope.Project));
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());

            // The durable envelope and replay actor are supplied by the test. Recipients
            // use the actual product password, cookie, session and Hub authorization.
            var original = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, alphaScope,
                SecurityCiFixtureSeed.TenantAMemberUserId, EventType);
            await member.WaitEventAsync(original);
            await owner.WaitEventAsync(original);
            await app.WaitDeliveredAsync(original);
            var betaPositive = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, betaScope,
                SecurityCiFixtureSeed.TenantBOwnerUserId, EventType);
            await beta.WaitEventAsync(betaPositive);
            await app.WaitDeliveredAsync(betaPositive);
            Assert.True(member.Received(original, EventType));
            Assert.True(owner.Received(original, EventType));
            Assert.True(beta.Received(betaPositive, EventType));
            Assert.Equal(1, member.DeliveryCount(original));
            Assert.Equal(1, owner.DeliveryCount(original));
            Assert.Equal(0, beta.DeliveryCount(original));
            controls.ObserveIsolation(EventType, RealtimeSubscriptionType.Project,
                "MANUAL_REPLAY_BASELINE_CROSS_TENANT", owner, original, beta, original);
            var actor = await SeedSuppliedReplayActorAsync(database, alphaScope.Tenant);
            var baseline = await SnapshotAsync(database, original, actor.UserId!.Value);
            Assert.Equal(0, baseline.ReplayAuditCount);
            var tenant = new CurrentTenantService();
            tenant.SetTenant(alphaScope.Tenant, SecurityCiFixtureSeed.TenantASlug);
            await using var request = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options, tenant);
            var replayEnvironment = await ObserveReplayDatabaseAsync(request);
            var service = Service(request, tenant, actor);
            var firstStarted = DateTimeOffset.UtcNow;
            var replayed = await service.ReplayAsync(original, Reason);
            Assert.True(replayed.IsSuccess, replayed.Error);
            await member.WaitEventAsync(original, minimumCount: 2);
            await owner.WaitEventAsync(original, minimumCount: 2);
            await app.WaitDeliveredAsync(original, firstStarted);
            var first = await SnapshotAsync(database, original, actor.UserId.Value);
            Assert.Equal(baseline.ImmutableDigest, first.ImmutableDigest);
            Assert.Equal(1, first.ReplayAuditCount);
            Assert.NotEqual(baseline.EventStateDigest, first.EventStateDigest);
            Assert.Equal(2, member.DeliveryCount(original));
            Assert.Equal(2, owner.DeliveryCount(original));
            Assert.Equal(0, beta.DeliveryCount(original));
            controls.ObservePositive(EventType, RealtimeSubscriptionType.Project,
                "ACTUAL_APPLICATION_MANUAL_REPLAY", member, original);

            // Keep the supplied actor, application service and DbContext unchanged.
            // The evaluator must observe a grant revoked through a separate connection.
            await SetReplayGrantRevokedAsync(database, actor.UserId.Value, revoked: true);
            var beforeDenied = await SnapshotAsync(database, original, actor.UserId.Value);
            var denied = await service.ReplayAsync(original, Reason);
            Assert.False(denied.IsSuccess);
            Assert.Equal("The realtime outbox replay capability is required.", denied.Error);
            Assert.Equal(beforeDenied, await SnapshotAsync(database, original, actor.UserId.Value));
            var alphaSentinel = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, alphaScope,
                SecurityCiFixtureSeed.TenantAMemberUserId, EventType);
            var betaSentinel = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, betaScope,
                SecurityCiFixtureSeed.TenantBOwnerUserId, EventType);
            await member.WaitEventAsync(alphaSentinel);
            await owner.WaitEventAsync(alphaSentinel);
            await beta.WaitEventAsync(betaSentinel);
            await app.WaitDeliveredAsync(alphaSentinel);
            await app.WaitDeliveredAsync(betaSentinel);
            Assert.True(member.Received(alphaSentinel, EventType));
            Assert.True(owner.Received(alphaSentinel, EventType));
            Assert.True(beta.Received(betaSentinel, EventType));
            Assert.Equal(2, member.DeliveryCount(original));
            Assert.Equal(2, owner.DeliveryCount(original));
            Assert.Equal(0, beta.DeliveryCount(original));
            var afterDenied = await SnapshotAsync(database, original, actor.UserId.Value);
            Assert.Equal(beforeDenied, afterDenied);

            await SetReplayGrantRevokedAsync(database, actor.UserId.Value, revoked: false);
            var restoredStarted = DateTimeOffset.UtcNow;
            var restored = await service.ReplayAsync(original, Reason);
            Assert.True(restored.IsSuccess, restored.Error);
            await member.WaitEventAsync(original, minimumCount: 3);
            await owner.WaitEventAsync(original, minimumCount: 3);
            await app.WaitDeliveredAsync(original, restoredStarted);
            var final = await SnapshotAsync(database, original, actor.UserId.Value);
            Assert.Equal(baseline.ImmutableDigest, final.ImmutableDigest);
            Assert.Equal(2, final.ReplayAuditCount);
            Assert.Equal(3, member.DeliveryCount(original));
            Assert.Equal(3, owner.DeliveryCount(original));
            Assert.Equal(0, beta.DeliveryCount(original));
            controls.ObservePositive(EventType, RealtimeSubscriptionType.Project,
                "CURRENT_GRANT_RESTORED_MANUAL_REPLAY", owner, original);
            await AssertReplayReasonsAsync(database, original, actor.UserId.Value);
            await controls.SaveAsync();
            await WriteReceiptAsync(baseline, first, beforeDenied, afterDenied, final, replayEnvironment);
        });
    }

    private static async Task<SuppliedReplayActor> SeedSuppliedReplayActorAsync(string database, Guid tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        var email = "replay-operator-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var user = new User { Email = email, NormalizedEmail = email.ToUpperInvariant(), SystemRole = SystemRole.PlatformAdmin };
        var session = new Session { UserId = user.Id, SessionKeyHash = "synthetic-" + Guid.NewGuid().ToString("N"), ExpiresAt = now.AddHours(1) };
        await using var setup = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        setup.AddRange(user, session, new TenantUser { TenantId = tenantId, UserId = user.Id,
            Role = TenantUserRole.Owner, Status = TenantUserStatus.Active, JoinedAt = now.AddDays(-1) },
            new CapabilityGrant { TenantId = tenantId, SubjectUserId = user.Id,
                CapabilityKey = CapabilityKeys.RealtimeOutboxReplay, ScopeType = CapabilityScopeType.Tenant,
                ScopeId = tenantId, GrantedByUserId = user.Id, GrantedAt = now.AddMinutes(-1),
                ExpiresAt = now.AddHours(1), VersionNo = 1 });
        await setup.SaveChangesAsync();
        return new SuppliedReplayActor(user.Id, session.Id);
    }

    private static OutboxReplayService Service(AppDbContext db, CurrentTenantService tenant, SuppliedReplayActor actor)
    {
        var clock = new SystemClock();
        return new OutboxReplayService(new OutboxEventRepository(db), actor, tenant,
            new DbAuditLogger(db, clock, actor, tenant), clock, new EfUnitOfWork(db),
            new SessionRepository(db), new CapabilityGrantEvaluator(new CapabilityGrantRepository(db),
                new TenantRepository(db), new WorkspaceRepository(db), tenant, clock));
    }

    private static async Task<ReplayEnvironment> ObserveReplayDatabaseAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT current_user, current_setting('server_version'), roles.rolsuper, roles.rolbypassrls,
                       (SELECT count(*)::integer FROM pg_class relations
                        JOIN pg_namespace namespaces ON namespaces.oid = relations.relnamespace
                        WHERE namespaces.nspname = 'public' AND relations.relrowsecurity)
                FROM pg_roles roles WHERE roles.rolname = current_user
                """, (NpgsqlConnection)db.Database.GetDbConnection());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var result = new ReplayEnvironment(reader.GetString(0), reader.GetString(1),
                reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt32(4));
            Assert.False(string.IsNullOrWhiteSpace(result.DatabaseRole));
            Assert.False(string.IsNullOrWhiteSpace(result.DatabaseVersion));
            Assert.Equal(0, result.ProductRlsAppliedCount);
            Assert.False(await reader.ReadAsync());
            return result;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task SetReplayGrantRevokedAsync(string database, Guid actorId, bool revoked)
    {
        await using var change = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var grant = await change.Set<CapabilityGrant>().SingleAsync(item => item.SubjectUserId == actorId);
        grant.RevokedAt = revoked ? DateTimeOffset.UtcNow : null;
        grant.VersionNo++;
        await change.SaveChangesAsync();
        Assert.Equal(revoked, grant.RevokedAt.HasValue);
    }

    private static async Task<ReplaySnapshot> SnapshotAsync(string database, Guid eventId, Guid actorId)
    {
        await using var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var item = await observed.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == eventId);
        Assert.Equal(OutboxEventStatus.Delivered, item.Status);
        var immutable = new { item.Id, item.TenantId, item.EventType, item.PayloadSchemaVersion,
            item.AggregateType, item.AggregateId, item.AggregateVersion, item.OccurredAt,
            payloadDigest = Digest(item.PayloadJson), routingDigest = Digest(item.RoutingJson),
            item.CorrelationId, item.CausationId, item.CreatedAt };
        var state = new { immutable, item.Status, item.AttemptCount, item.NextAttemptAt,
            item.LockedAt, item.LockOwner, item.LockToken, item.DeliveredAt, item.LastErrorCode,
            errorSummaryDigest = item.LastErrorSummary is null ? null : Digest(item.LastErrorSummary),
            item.DeadLetteredAt, item.UpdatedAt };
        var audits = await observed.AuditLogs.AsNoTracking().Where(row => row.ActorUserId == actorId &&
            row.EntityId == eventId && row.Action == "RealtimeOutboxReplay").OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.TenantId, row.ActorUserId, row.Action, row.EntityType,
                row.EntityId, row.CreatedAt, row.MetadataJson }).ToArrayAsync();
        return new ReplaySnapshot(Digest(JsonSerializer.Serialize(immutable)),
            Digest(JsonSerializer.Serialize(state)), Digest(JsonSerializer.Serialize(audits)), audits.Length);
    }

    private static async Task AssertReplayReasonsAsync(string database, Guid eventId, Guid actorId)
    {
        await using var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var audits = await observed.AuditLogs.AsNoTracking().Where(row => row.ActorUserId == actorId &&
            row.EntityId == eventId && row.Action == "RealtimeOutboxReplay").ToArrayAsync();
        Assert.Equal(2, audits.Length);
        foreach (var audit in audits)
        {
            Assert.Equal("OutboxEvent", audit.EntityType);
            using var metadata = JsonDocument.Parse(audit.MetadataJson!);
            Assert.Equal(Reason, metadata.RootElement.GetProperty("reason").GetString());
        }
    }

    private static async Task WriteReceiptAsync(ReplaySnapshot baseline, ReplaySnapshot first, ReplaySnapshot beforeDenied,
        ReplaySnapshot afterDenied, ReplaySnapshot final, ReplayEnvironment environment,
        [CallerFilePath] string sourceFile = "")
    {
        Assert.Equal(beforeDenied, afterDenied);
        Assert.Equal(baseline.ImmutableDigest, final.ImmutableDigest);
        Assert.Equal(first, beforeDenied);
        Assert.NotEqual(first.EventStateDigest, final.EventStateDigest);
        Assert.NotEqual(first.ReplayAuditDigest, final.ReplayAuditDigest);
        var assemblies = new[] { typeof(SecurityArchitectureOutboxReplayTransportTests).Assembly,
            typeof(AuthController).Assembly, typeof(OutboxReplayService).Assembly,
            typeof(AppDbContext).Assembly, typeof(OutboxEvent).Assembly,
            typeof(Coglatas.SecurityArchitecture.SpecRegistryValidator).Assembly };
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is not null) Assert.Matches("^[0-9a-f]{40}$", candidate);
        await SecurityArchitectureInventoryTests.WritePrivateInventoryAsync("outbox-manual-replay-transport.json", new
        {
            schemaVersion = 2, approvalStatus = "DRAFT", ownerApproval = (string?)null,
            candidateSha = candidate, assemblyBindingScope = "SIX_ASSEMBLIES_WITH_LOADED_COPIES",
            assemblyDigests = assemblies.ToDictionary(assembly => assembly.GetName().Name!,
                assembly => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant()),
            verifierMethod = typeof(SecurityArchitectureOutboxReplayTransportTests).FullName + "." +
                nameof(ActualReplayServiceDeliversOriginalEventAndCurrentGrantRevocationHasNoTransportOrAuditEffects),
            sourcePath = "tests/Coglatas.Tests/SecurityArchitecture/SecurityArchitectureOutboxReplayTransportTests.cs",
            sourceDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourceFile))).ToLowerInvariant(),
            eventType = EventType, payloadSchemaVersion = 1,
            executionScope = "ACTUAL_APPLICATION_REPLAY_TO_PRODUCT_POSTGRES_OUTBOX_AND_REAL_WEBSOCKET",
            replayActorAuthority = "SUPPLIED_TEST_ACTOR_WITH_CURRENT_PERSISTED_AUTHORITY_CHECKS",
            recipientAuthentication = "ACTUAL_PRODUCT_PASSWORD_COOKIE_SESSION",
            durableEnvelopeProducer = "SYNTHETIC_TEST_ENQUEUE", businessProducerCoverage = "UNVERIFIED",
            authenticatedHttpReplayAdapter = "UNVERIFIED", operationalCliReplayAdapter = "UNVERIFIED",
            operatorIssuanceAuthority = "UNVERIFIED", productRlsAppliedCount = environment.ProductRlsAppliedCount,
            replayDatabaseRole = environment.DatabaseRole, databaseVersion = environment.DatabaseVersion,
            replayDatabaseRoleIsSuperuser = environment.IsSuperuser,
            replayDatabaseRoleHasBypassRls = environment.HasBypassRls,
            operationalDatabaseIdentity = "UNVERIFIED",
            originalPositiveDeliveryCount = 1, firstManualReplayDeliveryCount = 2,
            deniedManualReplayAdditionalDeliveryCount = 0, restoredManualReplayDeliveryCount = 3,
            verifiedPositiveRecipientCount = 2, verifiedCrossTenantPeerCount = 1,
            verifiedRevocationSentinelRecipientCount = 3, finalReplayAuditCount = final.ReplayAuditCount,
            originalIdentityPayloadRoutingPreserved = baseline.ImmutableDigest == final.ImmutableDigest,
            deniedEventStatePreserved = beforeDenied.EventStateDigest == afterDenied.EventStateDigest,
            deniedReplayAuditPreserved = beforeDenied.ReplayAuditDigest == afterDenied.ReplayAuditDigest,
            immutableDigest = baseline.ImmutableDigest, deniedEventStateDigest = beforeDenied.EventStateDigest,
            deniedReplayAuditDigest = beforeDenied.ReplayAuditDigest,
            snapshotDigests = new Dictionary<string, object>
            {
                ["baseline"] = SnapshotReceipt(baseline), ["firstReplay"] = SnapshotReceipt(first),
                ["beforeDeniedReplay"] = SnapshotReceipt(beforeDenied),
                ["afterDeniedReplay"] = SnapshotReceipt(afterDenied), ["restoredReplay"] = SnapshotReceipt(final)
            },
            recordedAtUtc = DateTimeOffset.UtcNow,
            runtimeOutcome = "PASS", normativeContractCompletion = "UNVERIFIED", preAvaloniaVerdict = "BLOCKED"
        });
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static object SnapshotReceipt(ReplaySnapshot snapshot) => new
    {
        immutableDigest = snapshot.ImmutableDigest, eventStateDigest = snapshot.EventStateDigest,
        replayAuditDigest = snapshot.ReplayAuditDigest, replayAuditCount = snapshot.ReplayAuditCount
    };

    private sealed record ReplaySnapshot(string ImmutableDigest, string EventStateDigest, string ReplayAuditDigest, int ReplayAuditCount);

    private sealed record ReplayEnvironment(string DatabaseRole, string DatabaseVersion, bool IsSuperuser,
        bool HasBypassRls, int ProductRlsAppliedCount);

    private sealed record SuppliedReplayActor(Guid? UserId, Guid? SessionId) : ICurrentUser
    {
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.PlatformAdmin;
        public bool IsAuthenticated => true;
        public string? Email => null;
    }
}
