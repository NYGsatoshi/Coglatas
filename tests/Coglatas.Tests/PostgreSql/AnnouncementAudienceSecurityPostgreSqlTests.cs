using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class AnnouncementAudienceSecurityPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task GlobalAudienceTargetsOnlyActiveUsersInAnnouncementTenant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgreSqlTestEnvironment.RequireConnectionString())
            .Options;
        var currentTenant = new CurrentTenantService();
        var runId = Guid.NewGuid().ToString("N");

        await using var dbContext = new AppDbContext(options, currentTenant);
        var tenantA = new Tenant { Name = $"Announcement A {runId}", DisplayName = "Announcement A", Slug = $"ann-a-{runId}" };
        var tenantB = new Tenant { Name = $"Announcement B {runId}", DisplayName = "Announcement B", Slug = $"ann-b-{runId}" };
        var userA = NewUser($"ann-a-{runId}@example.test", "Tenant A User");
        var userB = NewUser($"ann-b-{runId}@example.test", "Tenant B User");

        currentTenant.SetPlatformScope();
        dbContext.Tenants.AddRange(tenantA, tenantB);
        dbContext.Users.AddRange(userA, userB);
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenantA.Id, tenantA.Slug);
        dbContext.TenantUsers.Add(new TenantUser
        {
            UserId = userA.Id,
            Status = TenantUserStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenantB.Id, tenantB.Slug);
        dbContext.TenantUsers.Add(new TenantUser
        {
            UserId = userB.Id,
            Status = TenantUserStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenantA.Id, tenantA.Slug);
        var repository = new AnnouncementRepository(dbContext, new FixedClock(DateTimeOffset.UtcNow), currentTenant);
        var targets = await repository.ListTargetUsersAsync(new Announcement { TenantId = tenantA.Id });

        Assert.Contains(targets, target => target.UserId == userA.Id);
        Assert.DoesNotContain(targets, target => target.UserId == userB.Id);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ScopedAudienceRequiresCurrentTenantAndWorkspaceMembership()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgreSqlTestEnvironment.RequireConnectionString())
            .Options;
        var currentTenant = new CurrentTenantService();
        var runId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        await using var dbContext = new AppDbContext(options, currentTenant);
        var tenant = new Tenant { Name = $"Announcement Scope {runId}", DisplayName = "Announcement Scope", Slug = $"ann-scope-{runId}" };
        var user = NewUser($"ann-scope-{runId}@example.test", "Scoped User");

        currentTenant.SetPlatformScope();
        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        dbContext.TenantUsers.Add(new TenantUser
        {
            UserId = user.Id,
            Status = TenantUserStatus.Active,
            JoinedAt = now
        });
        var workspace = new Workspace
        {
            Name = "Announcement Workspace",
            Slug = $"ann-workspace-{runId}",
            CreatedByUserId = user.Id,
            Status = WorkspaceStatus.Active
        };
        dbContext.Workspaces.Add(workspace);
        dbContext.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = WorkspaceRole.Member,
            Status = MembershipStatus.Active,
            JoinedAt = now
        });
        var group = new Group
        {
            WorkspaceId = workspace.Id,
            Name = "Announcement Group",
            Slug = $"ann-group-{runId}",
            CreatedByUserId = user.Id,
            Status = GroupStatus.Active
        };
        dbContext.Groups.Add(group);
        dbContext.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = user.Id,
            Role = GroupRole.Member,
            JoinedAt = now
        });
        var channel = new Channel
        {
            WorkspaceId = workspace.Id,
            GroupId = group.Id,
            Name = "private-announcements",
            Slug = $"ann-channel-{runId}",
            CreatedByUserId = user.Id,
            Type = ChannelType.Private,
            Status = ChannelStatus.Active
        };
        dbContext.Channels.Add(channel);
        dbContext.ChannelMembers.Add(new ChannelMember
        {
            ChannelId = channel.Id,
            UserId = user.Id,
            Role = ChannelRole.Member,
            JoinedAt = now
        });
        var announcement = new Announcement
        {
            WorkspaceId = workspace.Id,
            GroupId = group.Id,
            ChannelId = channel.Id,
            AuthorUserId = user.Id,
            Title = "Private announcement",
            Body = "Audience boundary regression",
            PublishedAt = now.AddMinutes(-1)
        };
        dbContext.Announcements.Add(announcement);
        await dbContext.SaveChangesAsync();

        var repository = new AnnouncementRepository(dbContext, new FixedClock(now), currentTenant);

        var workspaceMembership = await dbContext.WorkspaceMembers.SingleAsync(member =>
            member.WorkspaceId == workspace.Id && member.UserId == user.Id);
        workspaceMembership.Status = MembershipStatus.Suspended;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Empty(await repository.ListTargetUsersAsync(announcement));
        Assert.False(await repository.IsVisibleToUserAsync(announcement.Id, user.Id, false));

        workspaceMembership = await dbContext.WorkspaceMembers.SingleAsync(member =>
            member.WorkspaceId == workspace.Id && member.UserId == user.Id);
        workspaceMembership.Status = MembershipStatus.Active;
        var tenantMembership = await dbContext.TenantUsers.SingleAsync(member =>
            member.TenantId == tenant.Id && member.UserId == user.Id);
        tenantMembership.Status = TenantUserStatus.Suspended;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Empty(await repository.ListTargetUsersAsync(announcement));
        Assert.False(await repository.IsVisibleToUserAsync(announcement.Id, user.Id, false));
    }

    private static User NewUser(string email, string displayName) => new()
    {
        DisplayName = displayName,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "test-hash",
        Status = UserStatus.Active
    };

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
