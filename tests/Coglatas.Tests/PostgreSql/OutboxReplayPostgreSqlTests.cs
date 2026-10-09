using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class OutboxReplayPostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task CurrentTenantCapabilityReplaysOriginalEventAndPersistsReasonInSameTransaction()
    {
        await WithFixtureAsync(async (connection, graph) =>
        {
            var tenant = TenantContext(graph.Alpha.Id);
            await using var db = Context(connection, tenant);
            var actor = Actor(graph);
            var result = await Service(db, tenant, actor).ReplayAsync(graph.Event.Id, "  Synthetic replay review  ");
            Assert.True(result.IsSuccess, result.Error);

            await using var observed = Context(connection, tenant);
            var item = await observed.OutboxEvents.SingleAsync();
            Assert.Equal(graph.Event.Id, item.Id);
            Assert.Equal(OutboxEventStatus.Pending, item.Status);
            Assert.Equal(Now, item.NextAttemptAt);
            Assert.Null(item.DeadLetteredAt);
            Assert.Null(item.LastErrorCode);
            Assert.Equal(graph.Event.PayloadJson, item.PayloadJson);
            Assert.Equal(graph.Event.RoutingJson, item.RoutingJson);
            Assert.Equal(graph.Event.AttemptCount, item.AttemptCount);
            var audit = await observed.AuditLogs.SingleAsync();
            Assert.Equal(graph.Alpha.Id, audit.TenantId);
            Assert.Equal(graph.User.Id, audit.ActorUserId);
            Assert.Equal(item.Id, audit.EntityId);
            Assert.Equal("RealtimeOutboxReplay", audit.Action);
            using var metadata = JsonDocument.Parse(audit.MetadataJson!);
            Assert.Equal("Synthetic replay review", metadata.RootElement.GetProperty("reason").GetString());
        });
    }

    [PostgreSqlFact]
    public async Task PersistedRevocationsScopeAndIdentityChangesDenyWithoutEventOrAuditEffects()
    {
        await WithFixtureAsync(async (connection, graph) =>
        {
            var changes = new (string Name, Action<Graph> Change)[]
            {
                ("revoked grant", g => g.Grant.RevokedAt = Now),
                ("expired grant", g => g.Grant.ExpiresAt = Now),
                ("future grant", g => g.Grant.GrantedAt = Now.AddMinutes(1)),
                ("wrong capability", g => g.Grant.CapabilityKey = CapabilityKeys.AuditView),
                ("wrong tenant grant", g => { g.Grant.TenantId = g.Beta.Id; g.Grant.ScopeId = g.Beta.Id; }),
                ("inactive membership", g => g.Membership.Status = TenantUserStatus.Suspended),
                ("inactive tenant", g => g.Alpha.Status = TenantStatus.Suspended),
                ("inactive user", g => g.User.Status = UserStatus.Suspended),
                ("deleted user", g => g.User.MarkDeleted(Now)),
                ("role downgrade with stale actor", g => g.User.SystemRole = SystemRole.User),
                ("revoked session", g => g.Session.RevokedAt = Now),
                ("expired session", g => g.Session.ExpiresAt = Now),
                ("wrong session subject", g => g.Session.UserId = g.OtherUser.Id),
                ("unsupported schema", g => g.Event.PayloadSchemaVersion = 999),
                ("processing event", g => g.Event.Status = OutboxEventStatus.Processing),
                ("cancelled event", g => g.Event.Status = OutboxEventStatus.Cancelled)
            };
            await using var setup = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            foreach (var (name, change) in changes)
            {
                setup.AttachRange(graph.Alpha, graph.Beta, graph.User, graph.OtherUser,
                    graph.Membership, graph.Session, graph.Grant, graph.Event);
                change(graph);
                await setup.SaveChangesAsync();
                var tenant = TenantContext(graph.Alpha.Id);
                await using (var request = Context(connection, tenant))
                {
                    var denied = await Service(request, tenant, Actor(graph)).ReplayAsync(graph.Event.Id, "Synthetic denial");
                    Assert.False(denied.IsSuccess, name);
                }
                await using (var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
                {
                    var item = await observed.OutboxEvents.SingleAsync();
                    Assert.Equal(graph.Event.Status, item.Status);
                    Assert.Equal(graph.Event.NextAttemptAt, item.NextAttemptAt);
                    Assert.Equal(graph.Event.DeadLetteredAt, item.DeadLetteredAt);
                    Assert.Equal(graph.Event.LastErrorCode, item.LastErrorCode);
                    Assert.Empty(await observed.AuditLogs.ToListAsync());
                }
                Reset(graph);
                await setup.SaveChangesAsync();
                setup.ChangeTracker.Clear();
            }

            var invalidActors = new[]
            {
                Actor(graph) with { IsAuthenticated = false },
                Actor(graph) with { UserId = null },
                Actor(graph) with { UserId = Guid.Empty },
                Actor(graph) with { SessionId = null },
                Actor(graph) with { SessionId = Guid.Empty },
                Actor(graph) with { SystemRole = SystemRole.User }
            };
            foreach (var actor in invalidActors)
            {
                var tenant = TenantContext(graph.Alpha.Id);
                await using var request = Context(connection, tenant);
                Assert.False((await Service(request, tenant, actor).ReplayAsync(graph.Event.Id, "Synthetic denial")).IsSuccess);
            }
            foreach (var tenant in new[] { TenantContext(graph.Beta.Id), new CurrentTenantService() })
            {
                await using var request = Context(connection, tenant);
                Assert.False((await Service(request, tenant, Actor(graph)).ReplayAsync(graph.Event.Id, "Synthetic denial")).IsSuccess);
            }
            var platform = new CurrentTenantService();
            platform.SetPlatformScope();
            await using (var request = Context(connection, platform))
                Assert.False((await Service(request, platform, Actor(graph)).ReplayAsync(graph.Event.Id, "Synthetic denial")).IsSuccess);

            var foreign = new OutboxEvent(Guid.NewGuid())
            {
                TenantId = graph.Beta.Id, EventType = graph.Event.EventType, PayloadSchemaVersion = 1,
                AggregateType = "Synthetic", AggregateId = Guid.NewGuid(), OccurredAt = Now,
                PayloadJson = "{}", RoutingJson = "{}", Status = OutboxEventStatus.DeadLetter,
                DeadLetteredAt = Now
            };
            setup.OutboxEvents.Add(foreign);
            await setup.SaveChangesAsync();
            await using (var request = Context(connection, TenantContext(graph.Alpha.Id)))
            {
                var service = Service(request, TenantContext(graph.Alpha.Id), Actor(graph));
                var foreignResult = await service.ReplayAsync(foreign.Id, "Synthetic denial");
                var missingResult = await service.ReplayAsync(Guid.NewGuid(), "Synthetic denial");
                Assert.False(foreignResult.IsSuccess);
                Assert.Equal(missingResult.Error, foreignResult.Error);
                foreach (var reason in new[] { "", new string('x', 501) })
                    Assert.False((await service.ReplayAsync(graph.Event.Id, reason)).IsSuccess);
            }
            await setup.Entry(foreign).ReloadAsync();
            Assert.Equal(OutboxEventStatus.DeadLetter, foreign.Status);
            var current = TenantContext(graph.Alpha.Id);
            await using var final = Context(connection, current);
            final.Remove(graph.Grant);
            await final.SaveChangesAsync();
            Assert.False((await Service(final, current, Actor(graph)).ReplayAsync(graph.Event.Id, "Synthetic denial")).IsSuccess);
            Assert.Equal(OutboxEventStatus.DeadLetter, (await final.OutboxEvents.SingleAsync()).Status);
            Assert.Empty(await final.AuditLogs.ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task PersistedWorkerClaimCannotBeRewoundByAnEarlierTrackedReplayState()
    {
        await WithFixtureAsync(async (connection, graph) =>
        {
            var tenant = TenantContext(graph.Alpha.Id);
            await using var request = Context(connection, tenant);
            var pending = await request.OutboxEvents.SingleAsync();
            pending.Status = OutboxEventStatus.Pending;
            pending.DeadLetteredAt = null;
            await request.SaveChangesAsync();
            await using (var worker = Context(connection, TenantContext(graph.Alpha.Id)))
            {
                var claim = Assert.Single(await new OutboxEventRepository(worker)
                    .ClaimDueAsync("synthetic-worker", Now, 1, TimeSpan.FromMinutes(1)));
                Assert.Equal(pending.Id, claim.Id);
                Assert.Equal(OutboxEventStatus.Processing, claim.Status);
            }

            Assert.Equal(OutboxEventStatus.Pending, pending.Status);
            Assert.False((await Service(request, tenant, Actor(graph)).ReplayAsync(pending.Id, "Synthetic stale read")).IsSuccess);
            await using var observed = Context(connection, tenant);
            var item = await observed.OutboxEvents.SingleAsync();
            Assert.Equal(OutboxEventStatus.Processing, item.Status);
            Assert.Equal("synthetic-worker", item.LockOwner);
            Assert.NotNull(item.LockToken);
            Assert.Empty(await observed.AuditLogs.ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task RequiredAuditFailureRollsBackTheRepositoryImmediateSave()
    {
        await WithFixtureAsync(async (connection, graph) =>
        {
            var tenant = TenantContext(graph.Alpha.Id);
            await using var request = Context(connection, tenant);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(connection,
                "ALTER TABLE audit_logs ADD CONSTRAINT sec_arch_replay_audit_failure CHECK (\"Action\" <> 'RealtimeOutboxReplay')");
            var service = Service(request, tenant, Actor(graph));
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ReplayAsync(graph.Event.Id, "Synthetic audit failure"));
            await using var observed = Context(connection, tenant);
            var item = await observed.OutboxEvents.SingleAsync();
            Assert.Equal(OutboxEventStatus.DeadLetter, item.Status);
            Assert.Equal(graph.Event.DeadLetteredAt, item.DeadLetteredAt);
            Assert.Equal(graph.Event.LastErrorCode, item.LastErrorCode);
            Assert.Null(item.NextAttemptAt);
            Assert.Empty(await observed.AuditLogs.ToListAsync());
        });
    }

    private static Task WithFixtureAsync(Func<string, Graph, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
            {
                await using var setup = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var alpha = new Tenant { Name = "Synthetic Alpha", DisplayName = "Synthetic Alpha", Slug = "replay-alpha" };
                var beta = new Tenant { Name = "Synthetic Beta", DisplayName = "Synthetic Beta", Slug = "replay-beta" };
                var user = new User { Email = "replay@example.invalid", NormalizedEmail = "REPLAY@EXAMPLE.INVALID", SystemRole = SystemRole.PlatformAdmin };
                var other = new User { Email = "other@example.invalid", NormalizedEmail = "OTHER@EXAMPLE.INVALID" };
                var membership = new TenantUser
                {
                    TenantId = alpha.Id, UserId = user.Id, Role = TenantUserRole.Owner,
                    Status = TenantUserStatus.Active, JoinedAt = Now.AddDays(-1)
                };
                var session = new Session { UserId = user.Id, SessionKeyHash = "synthetic-session-hash", ExpiresAt = Now.AddHours(1) };
                var grant = new CapabilityGrant
                {
                    TenantId = alpha.Id, SubjectUserId = user.Id, CapabilityKey = CapabilityKeys.RealtimeOutboxReplay,
                    ScopeType = CapabilityScopeType.Tenant, ScopeId = alpha.Id, GrantedByUserId = user.Id,
                    GrantedAt = Now.AddHours(-1), ExpiresAt = Now.AddHours(1), VersionNo = 1
                };
                var item = new OutboxEvent(Guid.NewGuid())
                {
                    TenantId = alpha.Id, EventType = "Security.AuthorizationStateChanged.v1", PayloadSchemaVersion = 1,
                    AggregateType = "Synthetic", AggregateId = Guid.NewGuid(), OccurredAt = Now.AddDays(-1),
                    PayloadJson = "{}", RoutingJson = "{}", Status = OutboxEventStatus.DeadLetter,
                    AttemptCount = 3, DeadLetteredAt = Now.AddMinutes(-5), LastErrorCode = "Synthetic"
                };
                setup.AddRange(alpha, beta, user, other, membership, session, grant, item);
                await setup.SaveChangesAsync();
                setup.ChangeTracker.Clear();
                await test(connection, new Graph(alpha, beta, user, other, membership, session, grant, item));
            });

    private static void Reset(Graph g)
    {
        g.Alpha.Status = TenantStatus.Active;
        g.Grant.RevokedAt = null;
        g.Grant.ExpiresAt = Now.AddHours(1);
        g.Grant.GrantedAt = Now.AddHours(-1);
        g.Grant.CapabilityKey = CapabilityKeys.RealtimeOutboxReplay;
        g.Grant.TenantId = g.Alpha.Id;
        g.Grant.ScopeId = g.Alpha.Id;
        g.Membership.Status = TenantUserStatus.Active;
        g.User.Status = UserStatus.Active;
        g.User.Restore();
        g.User.SystemRole = SystemRole.PlatformAdmin;
        g.Session.RevokedAt = null;
        g.Session.ExpiresAt = Now.AddHours(1);
        g.Session.UserId = g.User.Id;
        g.Event.PayloadSchemaVersion = 1;
        g.Event.Status = OutboxEventStatus.DeadLetter;
    }

    private static AppDbContext Context(string connection, CurrentTenantService tenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options, tenant);

    private static CurrentTenantService TenantContext(Guid tenantId)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(tenantId, "synthetic");
        return tenant;
    }

    private static SyntheticActor Actor(Graph graph) => new(graph.User.Id, graph.Session.Id, SystemRole.PlatformAdmin, true);

    private static OutboxReplayService Service(AppDbContext db, CurrentTenantService tenant, SyntheticActor actor)
    {
        var clock = new FixedClock();
        return new OutboxReplayService(new OutboxEventRepository(db), actor, tenant,
            new DbAuditLogger(db, clock, actor, tenant), clock, new EfUnitOfWork(db),
            new SessionRepository(db), new CapabilityGrantEvaluator(new CapabilityGrantRepository(db),
                new TenantRepository(db), new WorkspaceRepository(db), tenant, clock));
    }

    private sealed record Graph(Tenant Alpha, Tenant Beta, User User, User OtherUser,
        TenantUser Membership, Session Session, CapabilityGrant Grant, OutboxEvent Event);

    private sealed record SyntheticActor(Guid? UserId, Guid? SessionId, SystemRole? SystemRole, bool IsAuthenticated) : ICurrentUser
    {
        public string? Email => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

}
