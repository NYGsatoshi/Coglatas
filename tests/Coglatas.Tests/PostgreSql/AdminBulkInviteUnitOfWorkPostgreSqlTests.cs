using Coglatas.Application.Admin;
using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class AdminBulkInviteUnitOfWorkPostgreSqlTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public Task InvalidLastInvitePreservesCallerChangesWithoutStagingOrSaving() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connectionString =>
            {
                var ids = await SeedAsync(connectionString);
                await using var db = OpenTenant(connectionString, ids.TenantId, out var tenant);
                var workspace = await db.Workspaces.SingleAsync(item => item.Id == ids.WorkspaceId);
                workspace.Name = "Caller pending change";
                db.ChangeTracker.DetectChanges();
                await using var transaction = await db.Database.BeginTransactionAsync();

                var result = await Service(db, tenant, ids.ActorId).BulkCreateInvitesAsync(
                    new BulkCreateInviteRequest(ids.WorkspaceId, ["valid@example.invalid", "invalid"], WorkspaceRole.Member, NowUtc.AddDays(1)));

                Assert.False(result.IsSuccess);
                Assert.Equal("A valid email address is required.", result.Error);
                Assert.Equal(0, db.ChangeTracker.Entries<Invite>().Count());
                Assert.Equal(0, db.ChangeTracker.Entries<AuditLog>().Count());
                Assert.Equal(EntityState.Modified, db.Entry(workspace).State);
                Assert.Equal("Caller pending change", workspace.Name);
                Assert.Same(transaction, db.Database.CurrentTransaction);
                await using (var observer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString))
                {
                    Assert.Equal("Original workspace", await observer.Workspaces.Where(item => item.Id == ids.WorkspaceId).Select(item => item.Name).SingleAsync());
                    Assert.Equal(0, await observer.Invites.CountAsync(item => item.WorkspaceId == ids.WorkspaceId));
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                await using var committed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
                Assert.Equal("Caller pending change", await committed.Workspaces.Where(item => item.Id == ids.WorkspaceId).Select(item => item.Name).SingleAsync());
                Assert.Equal(0, await committed.Invites.CountAsync(item => item.WorkspaceId == ids.WorkspaceId));
                Assert.Equal(0, await committed.AuditLogs.CountAsync(item => item.TenantId == ids.TenantId));
            });

    [PostgreSqlFact]
    public Task ValidInvitesAndAuditFollowCallerCommitAndRollback() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connectionString =>
            {
                var ids = await SeedAsync(connectionString);
                foreach (var commit in new[] { false, true })
                {
                    await using var db = OpenTenant(connectionString, ids.TenantId, out var tenant);
                    var workspace = await db.Workspaces.SingleAsync(item => item.Id == ids.WorkspaceId);
                    workspace.Name = "Caller transaction change";
                    await using var transaction = await db.Database.BeginTransactionAsync();
                    var result = await Service(db, tenant, ids.ActorId).BulkCreateInvitesAsync(
                        new BulkCreateInviteRequest(ids.WorkspaceId,
                            ["first@example.invalid", "FIRST@example.invalid", "second@example.invalid"],
                            WorkspaceRole.Member, NowUtc.AddDays(1)));

                    Assert.True(result.IsSuccess, result.Error);
                    Assert.Equal(2, result.Value!.Count);
                    Assert.Same(transaction, db.Database.CurrentTransaction);
                    var stored = await db.Invites.AsNoTracking().Where(item => item.WorkspaceId == ids.WorkspaceId).OrderBy(item => item.NormalizedEmail).ToListAsync();
                    Assert.Equal(new[] { "FIRST@EXAMPLE.INVALID", "SECOND@EXAMPLE.INVALID" }, stored.Select(item => item.NormalizedEmail).ToArray());
                    Assert.Equal(result.Value.Select(item => item.Id).Order().ToArray(), stored.Select(item => item.Id).Order().ToArray());
                    foreach (var response in result.Value)
                    {
                        var invite = stored.Single(item => item.Id == response.Id);
                        Assert.Equal((ids.TenantId, ids.WorkspaceId, ids.ActorId, WorkspaceRole.Member, NowUtc.AddDays(1)),
                            (invite.TenantId, invite.WorkspaceId, invite.InvitedByUserId, invite.Role, invite.ExpiresAt));
                        Assert.True(response.InviteToken is { Length: > 0 });
                        Assert.True(new Sha256TokenHasher().HashToken(response.InviteToken!) == invite.TokenHash,
                            "The stored hash must match the returned synthetic token without rendering it.");
                    }
                    var audit = await db.AuditLogs.AsNoTracking().SingleAsync();
                    Assert.Equal((ids.TenantId, (Guid?)ids.ActorId, "BulkInviteCreated", "Invite", "2 invites created."),
                        (audit.TenantId, audit.ActorUserId, audit.Action, audit.EntityType, audit.Summary));
                    await using (var observer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString))
                    {
                        Assert.Equal(0, await observer.Invites.CountAsync(item => item.WorkspaceId == ids.WorkspaceId));
                        Assert.Equal(0, await observer.AuditLogs.CountAsync(item => item.TenantId == ids.TenantId));
                        Assert.Equal("Original workspace", await observer.Workspaces.Where(item => item.Id == ids.WorkspaceId).Select(item => item.Name).SingleAsync());
                    }

                    if (commit) await transaction.CommitAsync();
                    else await transaction.RollbackAsync();
                    await using var completed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
                    Assert.Equal(commit ? 2 : 0, await completed.Invites.CountAsync(item => item.WorkspaceId == ids.WorkspaceId));
                    Assert.Equal(commit ? 1 : 0, await completed.AuditLogs.CountAsync(item => item.TenantId == ids.TenantId));
                    Assert.Equal(commit ? "Caller transaction change" : "Original workspace",
                        await completed.Workspaces.Where(item => item.Id == ids.WorkspaceId).Select(item => item.Name).SingleAsync());
                }
            });

    private static async Task<FixtureIds> SeedAsync(string connectionString)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
        var tenant = new Tenant(Guid.NewGuid()) { Name = "Bulk native", DisplayName = "Bulk native", Slug = "bulk-native", Status = TenantStatus.Active };
        var actor = new User
        {
            DisplayName = "Synthetic administrator", Email = "native-admin@example.invalid",
            NormalizedEmail = "NATIVE-ADMIN@EXAMPLE.INVALID", Status = UserStatus.Active, SystemRole = SystemRole.SystemAdmin
        };
        var workspace = new Workspace
        {
            TenantId = tenant.Id, Name = "Original workspace", Slug = "bulk-native-workspace",
            Status = WorkspaceStatus.Active, CreatedByUserId = actor.Id
        };
        db.Tenants.Add(tenant);
        db.Users.Add(actor);
        db.Workspaces.Add(workspace);
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id, UserId = actor.Id, Role = TenantUserRole.Admin,
            Status = TenantUserStatus.Active, JoinedAt = NowUtc
        });
        await db.SaveChangesAsync();
        return new FixtureIds(tenant.Id, actor.Id, workspace.Id);
    }

    private static AppDbContext OpenTenant(string connectionString, Guid tenantId, out CurrentTenantService tenant)
    {
        tenant = new CurrentTenantService();
        tenant.SetTenant(tenantId, "bulk-native");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenant);
    }

    private static AdminService Service(AppDbContext db, CurrentTenantService tenant, Guid actorId)
    {
        var actor = new FixedActor(actorId);
        var clock = new FixedClock(NowUtc);
        return new AdminService(new AdminRepository(db), new Sha256TokenHasher(),
            new DbAuditLogger(db, clock, actor, tenant), actor, clock, new UnusedSessions(), new EfUnitOfWork(db), tenant);
    }

    private sealed record FixtureIds(Guid TenantId, Guid ActorId, Guid WorkspaceId);
    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
    private sealed record FixedActor(Guid ActorId) : ICurrentUser
    {
        public Guid? UserId => ActorId;
        public Guid? SessionId => null;
        public string Email => "native-admin@example.invalid";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.SystemAdmin;
        public bool IsAuthenticated => true;
    }

    private sealed class UnusedSessions : IUserSessionService
    {
        public Task<SessionValidationResult> ValidateSessionAsync(Guid userId, Guid sessionId, Guid? tenantId, bool requireActiveTenantMembership, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bulk invitation must not access user sessions.");
        public Task<Result> RevokeSessionAsync(Guid sessionId, Guid? actorUserId, string reason, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bulk invitation must not access user sessions.");
        public Task<Result<int>> RevokeUserSessionsAsync(Guid userId, Guid? actorUserId, string reason, Guid? exceptSessionId = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bulk invitation must not access user sessions.");
    }
}
