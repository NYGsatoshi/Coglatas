using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

// Real service/persistence controls; no HTTP, dispatcher, application-role RLS or Pilot qualification.
public sealed class TenantMembershipLifecyclePostgreSqlTests(ITestOutputHelper output)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task AdditionAndReactivationPersistCurrentMembershipAuditAndUserInvalidation()
    {
        foreach (var status in new TenantUserStatus?[] { null, TenantUserStatus.Invited, TenantUserStatus.Suspended, TenantUserStatus.Archived, TenantUserStatus.Left })
        {
            await RunAsync("add-" + (status?.ToString() ?? "new"), async fixture =>
            {
                if (status is null) fixture.Db.TenantUsers.Remove(fixture.TargetMembership);
                else fixture.TargetMembership.Status = status.Value;
                await fixture.Db.SaveChangesAsync();
                var oldId = fixture.TargetMembership.Id;
                var oldJoinedAt = fixture.TargetMembership.JoinedAt;
                var before = DateTimeOffset.UtcNow;

                var result = await fixture.Service.AddCurrentTenantUserAsync(new AddTenantUserRequest(fixture.Target.Id, TenantUserRole.Staff));

                Assert.True(result.IsSuccess, result.Error);
                await using var read = fixture.OpenContext();
                var saved = await read.TenantUsers.SingleAsync(row => row.UserId == fixture.Target.Id);
                Assert.Equal(TenantUserStatus.Active, saved.Status);
                Assert.Equal(TenantUserRole.Staff, saved.Role);
                Assert.Equal(fixture.Owner.Tenant.Id, saved.TenantId);
                Assert.Equal(fixture.Owner.Actor.Id, saved.InvitedByUserId);
                if (status is null)
                {
                    Assert.NotEqual(oldId, saved.Id);
                    Assert.InRange(saved.JoinedAt, before, DateTimeOffset.UtcNow);
                }
                else
                {
                    Assert.Equal(oldId, saved.Id);
                    Assert.Equal(oldJoinedAt, saved.JoinedAt);
                }
                Assert.Equal(saved.Id, result.Value!.Id);
                Assert.Equal(fixture.Target.Id, result.Value.UserId);
                var audit = await read.AuditLogs.SingleAsync();
                Assert.Equal(status is null ? "TenantUserAdded" : "TenantUserReactivated", audit.Action);
                Assert.Equal(saved.Id, audit.EntityId);
                Assert.Equal(fixture.Owner.Actor.Id, audit.ActorUserId);
                Assert.Equal(fixture.Owner.Tenant.Id, audit.TenantId);
                using var metadata = JsonDocument.Parse(audit.MetadataJson!);
                Assert.Equal(fixture.Target.Id, metadata.RootElement.GetProperty("targetUserId").GetGuid());
                Assert.Equal(nameof(TenantUserRole.Staff), metadata.RootElement.GetProperty("role").GetString());
                await fixture.AssertInvalidationAsync(read, "granted");
                await fixture.AssertSessionsAsync(read, revoked: false);
                await fixture.AssertForeignMembershipAsync(read);
            });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task MembershipChangesPersistAuditAndRevocationWithoutChangingForeignMembership()
    {
        foreach (var scenario in new[] { "role", "suspended", "archived", "left", "both", "reactivated", "same-active", "empty", "same-inactive" })
        {
            await RunAsync(scenario, async fixture =>
            {
                if (scenario is "reactivated" or "same-inactive")
                {
                    fixture.TargetMembership.Status = TenantUserStatus.Suspended;
                    await fixture.Db.SaveChangesAsync();
                }
                var oldRole = fixture.TargetMembership.Role;
                var oldStatus = fixture.TargetMembership.Status;
                UpdateTenantUserRequest request = scenario switch
                {
                    "role" => new(TenantUserRole.Admin, null),
                    "suspended" or "same-inactive" => new(null, TenantUserStatus.Suspended),
                    "archived" => new(null, TenantUserStatus.Archived),
                    "left" => new(null, TenantUserStatus.Left),
                    "both" => new(TenantUserRole.Guest, TenantUserStatus.Suspended),
                    "reactivated" => new(null, TenantUserStatus.Active),
                    "same-active" => new(TenantUserRole.Member, TenantUserStatus.Active),
                    _ => new(null, null)
                };
                var result = await fixture.Service.UpdateCurrentTenantUserAsync(fixture.Target.Id, request);

                Assert.True(result.IsSuccess, result.Error);
                await using var read = fixture.OpenContext();
                var saved = await read.TenantUsers.SingleAsync(row => row.UserId == fixture.Target.Id);
                var expectedRole = request.Role ?? oldRole;
                var expectedStatus = request.Status ?? oldStatus;
                Assert.Equal(expectedRole, saved.Role);
                Assert.Equal(expectedStatus, saved.Status);
                Assert.Equal(saved.Role, result.Value!.Role);
                Assert.Equal(saved.Status, result.Value.Status);
                var changed = expectedRole != oldRole || expectedStatus != oldStatus;
                var audit = await read.AuditLogs.Where(row => row.EntityType == "TenantUser").ToListAsync();
                if (changed)
                {
                    var entry = Assert.Single(audit);
                    Assert.Equal(expectedStatus != oldStatus ? "TenantUserStatusChanged" : "TenantUserRoleChanged", entry.Action);
                    Assert.Equal(saved.Id, entry.EntityId);
                    Assert.Equal(fixture.Owner.Actor.Id, entry.ActorUserId);
                    using var metadata = JsonDocument.Parse(entry.MetadataJson!);
                    Assert.Equal(fixture.Target.Id, metadata.RootElement.GetProperty("targetUserId").GetGuid());
                    Assert.Equal(oldRole.ToString(), metadata.RootElement.GetProperty("oldRole").GetString());
                    Assert.Equal(expectedRole.ToString(), metadata.RootElement.GetProperty("newRole").GetString());
                    Assert.Equal(oldStatus.ToString(), metadata.RootElement.GetProperty("oldStatus").GetString());
                    Assert.Equal(expectedStatus.ToString(), metadata.RootElement.GetProperty("newStatus").GetString());
                    await fixture.AssertInvalidationAsync(read, expectedStatus == TenantUserStatus.Active ? "membershipChanged" : "suspended");
                }
                else
                {
                    Assert.Empty(audit);
                    Assert.Empty(await read.OutboxEvents.ToListAsync());
                }
                var revoked = expectedRole != oldRole || (request.Status.HasValue && expectedStatus != TenantUserStatus.Active);
                await fixture.AssertSessionsAsync(read, revoked);
                var sessionAudits = await read.AuditLogs.Where(row => row.EntityType == "Session" && row.Action == "SessionRevoked").ToListAsync();
                if (revoked)
                {
                    var entry = Assert.Single(sessionAudits);
                    using var metadata = JsonDocument.Parse(entry.MetadataJson!);
                    Assert.Equal("TenantMembershipChanged", metadata.RootElement.GetProperty("reason").GetString());
                    Assert.Equal(2, metadata.RootElement.GetProperty("count").GetInt32());
                }
                else Assert.Empty(sessionAudits);
                await fixture.AssertForeignMembershipAsync(read);
            });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task RemovalRetainsMembershipAndForeignRowsWhileRevokingCurrentUserSessions()
    {
        foreach (var status in new[] { TenantUserStatus.Active, TenantUserStatus.Suspended, TenantUserStatus.Left })
        {
            await RunAsync("remove-" + status, async fixture =>
            {
                fixture.TargetMembership.Status = status;
                await fixture.Db.SaveChangesAsync();
                var result = await fixture.Service.RemoveCurrentTenantUserAsync(fixture.Target.Id);

                Assert.True(result.IsSuccess, result.Error);
                await using var read = fixture.OpenContext();
                var saved = await read.TenantUsers.SingleAsync(row => row.Id == fixture.TargetMembership.Id);
                Assert.Equal(TenantUserStatus.Left, saved.Status);
                Assert.Equal(TenantUserRole.Member, saved.Role);
                Assert.Equal(fixture.TargetMembership.JoinedAt, saved.JoinedAt);
                Assert.Equal(fixture.Owner.Tenant.Id, saved.TenantId);
                var audit = await read.AuditLogs.SingleAsync(row => row.EntityType == "TenantUser");
                Assert.Equal("TenantUserRemoved", audit.Action);
                Assert.Equal(saved.Id, audit.EntityId);
                Assert.Equal(fixture.Owner.Actor.Id, audit.ActorUserId);
                Assert.Equal(fixture.Owner.Tenant.Id, audit.TenantId);
                using var membershipMetadata = JsonDocument.Parse(audit.MetadataJson!);
                Assert.Equal(fixture.Target.Id, membershipMetadata.RootElement.GetProperty("targetUserId").GetGuid());
                var revoked = await read.AuditLogs.SingleAsync(row => row.EntityType == "Session");
                Assert.Equal("SessionRevoked", revoked.Action);
                Assert.Equal(fixture.Owner.Actor.Id, revoked.ActorUserId);
                using var sessionMetadata = JsonDocument.Parse(revoked.MetadataJson!);
                Assert.Equal("TenantMembershipRemoved", sessionMetadata.RootElement.GetProperty("reason").GetString());
                Assert.Equal(2, sessionMetadata.RootElement.GetProperty("count").GetInt32());
                await fixture.AssertInvalidationAsync(read, "revoked");
                await fixture.AssertSessionsAsync(read, revoked: true);
                await fixture.AssertForeignMembershipAsync(read);
            });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task UnavailableOrNonCurrentManagersCannotReadOrMutateMemberships()
    {
        foreach (var scenario in new[] { "unavailable", "platform-scope", "anonymous", "missing-actor",
                     "member", "invited", "suspended-membership", "archived-membership", "left-membership", "missing-membership",
                     "suspended-account", "archived-account", "deleted-account", "suspended-tenant", "archived-tenant",
                     "deleted-tenant", "deleted-status", "foreign-scope" })
        {
            await RunAsync("denied-" + scenario, async fixture =>
            {
                switch (scenario)
                {
                    case "unavailable": fixture.Owner.CurrentTenant.Clear(); break;
                    case "platform-scope": fixture.Owner.CurrentTenant.SetPlatformScope(); break;
                    case "anonymous": fixture.Owner.CurrentUser.IsAuthenticated = false; break;
                    case "missing-actor": fixture.Owner.CurrentUser.UserId = null; break;
                    case "member": fixture.Owner.Membership.Role = TenantUserRole.Member; break;
                    case "invited": fixture.Owner.Membership.Status = TenantUserStatus.Invited; break;
                    case "suspended-membership": fixture.Owner.Membership.Status = TenantUserStatus.Suspended; break;
                    case "archived-membership": fixture.Owner.Membership.Status = TenantUserStatus.Archived; break;
                    case "left-membership": fixture.Owner.Membership.Status = TenantUserStatus.Left; break;
                    case "missing-membership": fixture.Db.TenantUsers.Remove(fixture.Owner.Membership); break;
                    case "suspended-account": fixture.Owner.Actor.Status = UserStatus.Suspended; break;
                    case "archived-account": fixture.Owner.Actor.Status = UserStatus.Archived; break;
                    case "deleted-account": fixture.Owner.Actor.MarkDeleted(fixture.Owner.Clock.UtcNow); break;
                    case "suspended-tenant": fixture.Owner.Tenant.Status = TenantStatus.Suspended; break;
                    case "archived-tenant": fixture.Owner.Tenant.Status = TenantStatus.Archived; break;
                    case "deleted-tenant": fixture.Owner.Tenant.MarkDeleted(fixture.Owner.Clock.UtcNow); break;
                    case "deleted-status": fixture.Owner.Tenant.Status = TenantStatus.Deleted; break;
                    case "foreign-scope": fixture.Owner.CurrentTenant.SetTenant(fixture.Foreign.Id, fixture.Foreign.Slug); break;
                }
                await fixture.Db.SaveChangesAsync();
                var beforeCount = await fixture.Db.TenantUsers.IgnoreQueryFilters().CountAsync();
                var expected = scenario is "unavailable" or "platform-scope" ? "ValidationFailed"
                    : scenario is "anonymous" or "missing-actor" ? "AuthenticationRequired" : "Forbidden";
                var list = await fixture.Service.ListCurrentTenantUsersAsync();
                var add = await fixture.Service.AddCurrentTenantUserAsync(new AddTenantUserRequest(fixture.Target.Id, TenantUserRole.Owner));
                var update = await fixture.Service.UpdateCurrentTenantUserAsync(fixture.Target.Id, new UpdateTenantUserRequest(TenantUserRole.Owner, TenantUserStatus.Left));
                var remove = await fixture.Service.RemoveCurrentTenantUserAsync(fixture.Target.Id);
                foreach (var result in new[] { (list.IsSuccess, list.ErrorDetail), (add.IsSuccess, add.ErrorDetail),
                             (update.IsSuccess, update.ErrorDetail), (remove.IsSuccess, remove.ErrorDetail) })
                {
                    Assert.False(result.IsSuccess);
                    Assert.Equal(expected, result.ErrorDetail!.Code);
                }
                Assert.Null(list.Value);
                Assert.Null(add.Value);
                Assert.Null(update.Value);
                fixture.Owner.CurrentTenant.SetTenant(fixture.Owner.Tenant.Id, fixture.Owner.Tenant.Slug);
                await using var read = fixture.OpenContext();
                Assert.Equal(beforeCount, await read.TenantUsers.IgnoreQueryFilters().CountAsync());
                var saved = await read.TenantUsers.SingleAsync(row => row.Id == fixture.TargetMembership.Id);
                Assert.Equal(TenantUserRole.Member, saved.Role);
                Assert.Equal(TenantUserStatus.Active, saved.Status);
                Assert.Equal(fixture.TargetMembership.JoinedAt, saved.JoinedAt);
                Assert.Empty(await read.AuditLogs.ToListAsync());
                Assert.Empty(await read.OutboxEvents.ToListAsync());
                await fixture.AssertSessionsAsync(read, revoked: false);
                await fixture.AssertForeignMembershipAsync(read);
            });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task InvalidTargetsAndCurrentTenantListingPreserveMembershipSessionAuditAndOutboxState()
    {
        foreach (var scenario in new[] { "add-missing", "add-deleted", "add-active", "update-missing", "remove-missing", "list" })
        {
            await RunAsync(scenario, async fixture =>
            {
                if (scenario == "add-deleted") fixture.Target.MarkDeleted(fixture.Owner.Clock.UtcNow);
                await fixture.Db.SaveChangesAsync();
                if (scenario == "list")
                {
                    var result = await fixture.Service.ListCurrentTenantUsersAsync();
                    Assert.True(result.IsSuccess, result.Error);
                    Assert.Equal(new[] { fixture.Owner.Actor.Id, fixture.Target.Id }.Order(), result.Value!.Select(row => row.UserId).Order());
                    Assert.All(result.Value!, row => Assert.Equal(fixture.Owner.Tenant.Id, row.TenantId));
                }
                else if (scenario.StartsWith("add-", StringComparison.Ordinal))
                {
                    var target = scenario == "add-missing" ? Guid.NewGuid() : fixture.Target.Id;
                    var result = await fixture.Service.AddCurrentTenantUserAsync(new AddTenantUserRequest(target, TenantUserRole.Owner));
                    Assert.False(result.IsSuccess);
                    Assert.Equal(scenario == "add-active" ? "User is already an active member of this tenant." : "User not found.", result.Error);
                    Assert.Null(result.Value);
                }
                else if (scenario == "update-missing")
                {
                    var result = await fixture.Service.UpdateCurrentTenantUserAsync(Guid.NewGuid(), new UpdateTenantUserRequest(TenantUserRole.Owner, TenantUserStatus.Left));
                    Assert.False(result.IsSuccess);
                    Assert.Equal("Tenant user not found.", result.Error);
                }
                else
                {
                    var result = await fixture.Service.RemoveCurrentTenantUserAsync(Guid.NewGuid());
                    Assert.False(result.IsSuccess);
                    Assert.Equal("Tenant user not found.", result.Error);
                }
                await using var read = fixture.OpenContext();
                var saved = await read.TenantUsers.SingleAsync(row => row.Id == fixture.TargetMembership.Id);
                Assert.Equal(TenantUserRole.Member, saved.Role);
                Assert.Equal(TenantUserStatus.Active, saved.Status);
                Assert.Equal(3, await read.TenantUsers.IgnoreQueryFilters().CountAsync());
                Assert.Empty(await read.AuditLogs.ToListAsync());
                Assert.Empty(await read.OutboxEvents.ToListAsync());
                await fixture.AssertSessionsAsync(read, revoked: false);
                await fixture.AssertForeignMembershipAsync(read);
            });
        }
    }

    private Task RunAsync(string scenario, Func<MembershipFixture, Task> verify) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await MembershipFixture.CreateAsync(database);
                Assert.True(fixture.Db.Database.IsNpgsql());
                await verify(fixture);
                output.WriteLine("Completed synthetic PostgreSQL membership case: " + scenario);
            });

    private sealed class MembershipFixture(AdministrationFixture owner) : IAsyncDisposable
    {
        public AdministrationFixture Owner { get; } = owner;
        public AppDbContext Db => Owner.Db;
        public User Target { get; } = NewUser();
        public Tenant Foreign { get; } = new() { Name = "Synthetic foreign tenant", Slug = "foreign-" + Guid.NewGuid().ToString("N") };
        public TenantUser TargetMembership { get; private set; } = null!;
        private TenantUser ForeignMembership { get; set; } = null!;
        private Session[] TargetSessions { get; set; } = [];
        private Session ActorSession { get; set; } = null!;
        private Session OldRevokedSession { get; set; } = null!;

        public TenantService Service
        {
            get
            {
                var tenants = new TenantRepository(Db);
                var audit = new DbAuditLogger(Db, Owner.Clock, Owner.CurrentUser, Owner.CurrentTenant);
                return new TenantService(tenants, new TenantAuthorizationService(tenants), Owner.CurrentTenant,
                    Owner.CurrentUser, audit, new EfUnitOfWork(Db),
                    new UserSessionService(new SessionRepository(Db), tenants, audit, Owner.Clock, new EfUnitOfWork(Db)),
                    new TenancyOptions(), new AuthorizationStateChangePublisher(
                        new TransactionalOutbox(new OutboxEventRepository(Db), Owner.CurrentTenant, Owner.Clock), Owner.CurrentTenant, Owner.Clock));
            }
        }

        public static async Task<MembershipFixture> CreateAsync(string database)
        {
            var fixture = new MembershipFixture(await AdministrationFixture.CreateAsync(database));
            fixture.Owner.CurrentTenant.SetPlatformScope();
            fixture.Target.PasswordHash = fixture.Owner.Actor.PasswordHash;
            fixture.TargetMembership = new TenantUser { TenantId = fixture.Owner.Tenant.Id, UserId = fixture.Target.Id,
                Role = TenantUserRole.Member, Status = TenantUserStatus.Active, JoinedAt = fixture.Owner.Clock.UtcNow.AddDays(-30), InvitedByUserId = fixture.Owner.Actor.Id };
            fixture.ForeignMembership = new TenantUser { TenantId = fixture.Foreign.Id, UserId = fixture.Target.Id,
                Role = TenantUserRole.Owner, Status = TenantUserStatus.Active, JoinedAt = fixture.Owner.Clock.UtcNow.AddDays(-60) };
            fixture.TargetSessions = [fixture.NewSession(fixture.Target.Id), fixture.NewSession(fixture.Target.Id)];
            fixture.ActorSession = fixture.NewSession(fixture.Owner.Actor.Id);
            fixture.OldRevokedSession = fixture.NewSession(fixture.Target.Id);
            fixture.OldRevokedSession.RevokedAt = fixture.Owner.Clock.UtcNow.AddDays(-1);
            fixture.Db.AddRange(fixture.Target, fixture.Foreign, fixture.TargetMembership, fixture.ForeignMembership,
                fixture.ActorSession, fixture.OldRevokedSession);
            fixture.Db.Sessions.AddRange(fixture.TargetSessions);
            await fixture.Db.SaveChangesAsync();
            fixture.Owner.CurrentTenant.SetTenant(fixture.Owner.Tenant.Id, fixture.Owner.Tenant.Slug);
            return fixture;
        }

        private static User NewUser()
        {
            var email = Guid.NewGuid().ToString("N") + "@example.invalid";
            return new User { DisplayName = "Synthetic target", Email = email, NormalizedEmail = email.ToUpperInvariant(), Status = UserStatus.Active };
        }

        private Session NewSession(Guid userId) => new() { UserId = userId, SessionKeyHash = Guid.NewGuid().ToString("N"), ExpiresAt = Owner.Clock.UtcNow.AddHours(1) };
        public AppDbContext OpenContext() => Owner.OpenContext();
        public ValueTask DisposeAsync() => Owner.DisposeAsync();

        public async Task AssertForeignMembershipAsync(AppDbContext read)
        {
            var foreign = await read.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == ForeignMembership.Id);
            Assert.Equal(Foreign.Id, foreign.TenantId);
            Assert.Equal(Target.Id, foreign.UserId);
            Assert.Equal(TenantUserRole.Owner, foreign.Role);
            Assert.Equal(TenantUserStatus.Active, foreign.Status);
            Assert.Equal(ForeignMembership.JoinedAt, foreign.JoinedAt);
        }

        public async Task AssertSessionsAsync(AppDbContext read, bool revoked)
        {
            var sessions = await read.Sessions.AsNoTracking().Where(row => row.UserId == Target.Id).ToListAsync();
            Assert.Equal(3, sessions.Count);
            foreach (var original in TargetSessions)
                Assert.Equal(revoked ? Owner.Clock.UtcNow : (DateTimeOffset?)null, sessions.Single(row => row.Id == original.Id).RevokedAt);
            Assert.Equal(Owner.Clock.UtcNow.AddDays(-1), sessions.Single(row => row.Id == OldRevokedSession.Id).RevokedAt);
            Assert.Null((await read.Sessions.AsNoTracking().SingleAsync(row => row.Id == ActorSession.Id)).RevokedAt);
        }

        public async Task AssertInvalidationAsync(AppDbContext read, string change)
        {
            var row = await read.OutboxEvents.AsNoTracking().SingleAsync();
            Assert.Equal(Owner.Tenant.Id, row.TenantId);
            Assert.Equal("Security.AuthorizationStateChanged.v1", row.EventType);
            Assert.Equal("AuthorizationState", row.AggregateType);
            Assert.Equal(Target.Id, row.AggregateId);
            Assert.Equal(OutboxEventStatus.Pending, row.Status);
            Assert.Equal(Owner.Clock.UtcNow.UtcDateTime.Ticks, row.AggregateVersion);
            using var json = JsonDocument.Parse(row.PayloadJson);
            Assert.Equal(row.Id, json.RootElement.GetProperty("eventId").GetGuid());
            Assert.Equal(row.TenantId, json.RootElement.GetProperty("tenantId").GetGuid());
            Assert.Equal(row.EventType, json.RootElement.GetProperty("eventType").GetString());
            var payload = json.RootElement.GetProperty("payload");
            Assert.Equal(Target.Id, payload.GetProperty("affectedUserId").GetGuid());
            Assert.Equal("tenant", payload.GetProperty("scopeType").GetString());
            Assert.Equal(Owner.Tenant.Id, payload.GetProperty("scopeId").GetGuid());
            Assert.Equal(change, payload.GetProperty("change").GetString());
            Assert.Equal(Owner.Clock.UtcNow.UtcDateTime.Ticks, payload.GetProperty("authorizationVersion").GetInt64());
            Assert.Equal(new[] { "affectedUserId", "authorizationVersion", "change", "scopeId", "scopeType" }, payload.EnumerateObject().Select(property => property.Name).Order().ToArray());
            var routing = JsonSerializer.Deserialize<RealtimeRoutingTarget[]>(row.RoutingJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var target = Assert.Single(routing!);
            Assert.Equal(RealtimeSubscriptionType.User, target.SubscriptionType);
            Assert.Equal(Target.Id, target.ResourceId);
        }
    }
}
