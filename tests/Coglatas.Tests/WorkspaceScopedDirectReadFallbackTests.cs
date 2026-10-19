using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests;

public sealed class WorkspaceScopedDirectReadFallbackTests
{
    [Theory]
    [InlineData(WorkspaceStatus.Active)]
    [InlineData(WorkspaceStatus.Archived)]
    public async Task NonPostgreSqlReadRelationRequiresCurrentWorkspaceMembership(WorkspaceStatus status)
    {
        var tenant = new CurrentTenantService();
        var tenantId = Guid.NewGuid();
        tenant.SetPlatformScope();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"direct-read-{Guid.NewGuid():N}").Options;
        await using var db = new AppDbContext(options, tenant);
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Fallback control", DisplayName = "Fallback control", Slug = "direct-read-fallback" });
        await db.SaveChangesAsync();
        tenant.SetTenant(tenantId, "direct-read-fallback");
        var user = new User { Email = "reader@example.test", NormalizedEmail = "READER@EXAMPLE.TEST", DisplayName = "Reader" };
        var workspace = new Workspace { TenantId = tenantId, Name = "Read control", Slug = "read-control", Status = status, CreatedByUserId = user.Id };
        var workspaceMember = new WorkspaceMember { TenantId = tenantId, WorkspaceId = workspace.Id, UserId = user.Id, Status = MembershipStatus.Active };
        var direct = new Conversation { TenantId = tenantId, WorkspaceId = workspace.Id, Type = ConversationType.DirectMessage, CreatedByUserId = user.Id };
        var thread = new Conversation { TenantId = tenantId, WorkspaceId = workspace.Id, Type = ConversationType.Thread, ParentConversationId = direct.Id, RootConversationId = direct.Id, CreatedByUserId = user.Id };
        db.Users.Add(user);
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(workspaceMember);
        db.Conversations.AddRange(direct, thread);
        foreach (var conversation in new[] { direct, thread })
            db.ConversationMembers.Add(new ConversationMember { TenantId = tenantId, ConversationId = conversation.Id, UserId = user.Id, CanRead = true, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var repository = new MessagingRepository(db);
        Assert.Null(repository.QueryReadableConversationIds(user.Id));
        var ids = new[] { direct.Id, thread.Id };
        Assert.Equal(2, (await repository.FilterReadableConversationIdsAsync(user.Id, ids)).Count);
        Assert.Equal(2, (await repository.ListForUserAsync(user.Id, 1, 20)).TotalCount);

        workspaceMember.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        Assert.Empty(await repository.FilterReadableConversationIdsAsync(user.Id, ids));
        Assert.Empty((await repository.ListForUserAsync(user.Id, 1, 20)).Items);
    }
}
