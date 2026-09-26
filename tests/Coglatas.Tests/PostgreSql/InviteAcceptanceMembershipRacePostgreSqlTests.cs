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

[Trait("Category", "PostgreSQLIntegration")]
[Trait("Scope", "Issue527")]
public sealed class InviteAcceptanceMembershipRacePostgreSqlTests
{
    private const string Token = "issue-527-membership-race-token";
    private const string InviteEmail = "race-invitee@example.com";
    private const string InvitePassword = "InvitePassword123";
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 7, 30, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task ConcurrentTenantSuspensionWinsOverInviteAcceptance()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();

        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedAsync(database);

            await using var adminContext = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            await using var adminTransaction = await adminContext.Database.BeginTransactionAsync();
            var administrativeMembership = await adminContext.TenantUsers.SingleAsync(item =>
                item.TenantId == graph.Tenant.Id && item.UserId == graph.User.Id);
            administrativeMembership.Status = TenantUserStatus.Suspended;
            await adminContext.SaveChangesAsync();

            await using var acceptanceScope = CreateTenantScope(database, graph.Tenant);
            var service = CreateService(acceptanceScope.Context, acceptanceScope.CurrentTenant);
            var acceptance = service.AcceptInviteAsync(new AcceptInviteRequest(Token, "Ignored", InvitePassword));

            var completedBeforeAdministrativeCommit = await Task.WhenAny(
                acceptance,
                Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.NotSame(acceptance, completedBeforeAdministrativeCommit);

            await adminTransaction.CommitAsync();

            var result = await acceptance.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(result.IsSuccess);
            Assert.Equal("Invite is invalid or expired.", result.Error);

            await using var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var tenantMembership = await verify.TenantUsers.SingleAsync(item =>
                item.TenantId == graph.Tenant.Id && item.UserId == graph.User.Id);
            Assert.Equal(TenantUserStatus.Suspended, tenantMembership.Status);
            Assert.False(await verify.Sessions.AnyAsync(item => item.UserId == graph.User.Id));
            Assert.Null((await verify.Invites.SingleAsync(item => item.Id == graph.Invite.Id)).AcceptedAt);

            var denialMetadata = await verify.AuditLogs
                .IgnoreQueryFilters()
                .Where(item => item.Action == "InviteAcceptanceDenied")
                .Select(item => item.MetadataJson)
                .ToListAsync();
            Assert.Contains(denialMetadata, metadata =>
                metadata?.Contains("TenantMembershipUnavailable", StringComparison.Ordinal) == true);
        });
    }

    private static async Task<RaceGraph> SeedAsync(string connectionString)
    {
        var passwordHasher = new Pbkdf2PasswordHasher();
        var tokenHasher = new Sha256TokenHasher();
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);

        var tenant = new Tenant
        {
            Name = "Issue 527 Race Tenant",
            DisplayName = "Issue 527 Race Tenant",
            Slug = $"issue-527-race-{Guid.NewGuid():N}",
            Status = TenantStatus.Active
        };
        var inviter = new User
        {
            DisplayName = "Race Inviter",
            Email = $"race-inviter-{Guid.NewGuid():N}@example.com",
            PasswordHash = passwordHasher.HashPassword("InviterPassword123"),
            SystemRole = SystemRole.User,
            Status = UserStatus.Active
        };
        inviter.NormalizedEmail = inviter.Email.ToUpperInvariant();

        var user = new User
        {
            DisplayName = "Race Invitee",
            Email = InviteEmail,
            NormalizedEmail = InviteEmail.ToUpperInvariant(),
            PasswordHash = passwordHasher.HashPassword("ExistingPassword123"),
            SystemRole = SystemRole.User,
            Status = UserStatus.Active
        };
        var workspace = new Workspace
        {
            TenantId = tenant.Id,
            Name = "Race Workspace",
            Slug = $"race-workspace-{Guid.NewGuid():N}",
            Status = WorkspaceStatus.Active,
            CreatedByUserId = inviter.Id
        };
        var invite = new Invite
        {
            TenantId = tenant.Id,
            WorkspaceId = workspace.Id,
            Email = InviteEmail,
            NormalizedEmail = InviteEmail.ToUpperInvariant(),
            TokenHash = tokenHasher.HashToken(Token),
            Role = WorkspaceRole.Member,
            ExpiresAt = Now.AddDays(1),
            InvitedByUserId = inviter.Id
        };

        context.Tenants.Add(tenant);
        context.Users.AddRange(inviter, user);
        context.Workspaces.Add(workspace);
        context.Invites.Add(invite);
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = TenantUserRole.Member,
            Status = TenantUserStatus.Active,
            JoinedAt = Now.AddDays(-2),
            InvitedByUserId = inviter.Id
        });
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            TenantId = tenant.Id,
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = WorkspaceRole.Member,
            Status = MembershipStatus.Active,
            JoinedAt = Now.AddDays(-2)
        });

        await context.SaveChangesAsync();
        return new RaceGraph(tenant, user, invite);
    }

    private static AuthService CreateService(AppDbContext context, CurrentTenantService currentTenant)
    {
        var currentUser = new AnonymousCurrentUser();
        var clock = new FixedClock(Now);
        return new AuthService(
            new UserRepository(context),
            new InviteRepository(context),
            new TenantRepository(context),
            new WorkspaceRepository(context),
            new SessionRepository(context),
            new NoopUserSessionService(),
            new Pbkdf2PasswordHasher(),
            new Sha256TokenHasher(),
            new DbAuditLogger(context, clock, currentUser, currentTenant),
            currentUser,
            clock,
            new EfUnitOfWork(context),
            new AuthSecurityOptions());
    }

    private static TenantScope CreateTenantScope(string connectionString, Tenant tenant)
    {
        var currentTenant = new CurrentTenantService();
        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new TenantScope(new AppDbContext(options, currentTenant), currentTenant);
    }

    private sealed record RaceGraph(Tenant Tenant, User User, Invite Invite);

    private sealed class TenantScope(AppDbContext context, CurrentTenantService currentTenant) : IAsyncDisposable
    {
        public AppDbContext Context { get; } = context;
        public CurrentTenantService CurrentTenant { get; } = currentTenant;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => false;
    }

    private sealed class NoopUserSessionService : IUserSessionService
    {
        public Task<SessionValidationResult> ValidateSessionAsync(
            Guid userId,
            Guid sessionId,
            Guid? tenantId,
            bool requireActiveTenantMembership,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SessionValidationResult.Success());

        public Task<Result> RevokeSessionAsync(
            Guid sessionId,
            Guid? actorUserId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<int>> RevokeUserSessionsAsync(
            Guid userId,
            Guid? actorUserId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<int>.Success(0));
    }
}
