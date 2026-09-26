using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Additional deterministic canaries used by the SEC-05 authorization-negative
/// matrix. This seed deliberately layers on top of <see cref="SecurityCiFixtureSeed"/>
/// instead of widening the ordinary application seed path.
/// </summary>
public static class SecurityCiAuthorizationMatrixSeed
{
    public const string AlphaShadowConversationTitle = "SEC05 ALPHA SHADOW CONVERSATION CANARY";
    public const string AlphaShadowMessageBody = "SEC05_ALPHA_SHADOW_MESSAGE_DO_NOT_LEAK";

    public const string AlphaAnnouncementTitle = "SEC05 ALPHA ANNOUNCEMENT CANARY";
    public const string AlphaAnnouncementBody = "SEC05_ALPHA_ANNOUNCEMENT_DO_NOT_LEAK";
    public const string BetaAnnouncementTitle = "SEC05 BETA ANNOUNCEMENT CANARY";
    public const string BetaAnnouncementBody = "SEC05_BETA_ANNOUNCEMENT_DO_NOT_LEAK";

    public const string AlphaNotificationLogicalKey = "sec05-alpha-task-open-canary";
    public const string AlphaNotificationTitle = "SEC05 ALPHA NOTIFICATION CANARY";
    public const string AlphaNotificationBody = "SEC05_ALPHA_NOTIFICATION_DO_NOT_LEAK";
    public const string BetaNotificationLogicalKey = "sec05-beta-task-open-canary";
    public const string BetaNotificationTitle = "SEC05 BETA NOTIFICATION CANARY";
    public const string BetaNotificationBody = "SEC05_BETA_NOTIFICATION_DO_NOT_LEAK";

    public static async Task SeedAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var alpha = await ResolveGraphAsync(
            dbContext,
            SecurityCiFixtureSeed.TenantASlug,
            SecurityCiFixtureSeed.TenantAOwnerEmail,
            SecurityCiFixtureSeed.TenantAWorkspaceSlug,
            SecurityCiFixtureSeed.TenantAProjectSlug,
            SecurityCiFixtureSeed.TenantATaskTitle,
            cancellationToken);
        var beta = await ResolveGraphAsync(
            dbContext,
            SecurityCiFixtureSeed.TenantBSlug,
            SecurityCiFixtureSeed.TenantBOwnerEmail,
            SecurityCiFixtureSeed.TenantBWorkspaceSlug,
            SecurityCiFixtureSeed.TenantBProjectSlug,
            SecurityCiFixtureSeed.TenantBTaskTitle,
            cancellationToken);

        var alphaMember = await ResolveUserAsync(
            dbContext,
            SecurityCiFixtureSeed.TenantAMemberEmail,
            cancellationToken);
        var alphaRestricted = await ResolveUserAsync(
            dbContext,
            SecurityCiFixtureSeed.TenantARestrictedEmail,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await EnsureShadowConversationAsync(
            dbContext,
            alpha,
            alphaMember,
            alphaRestricted,
            now,
            cancellationToken);
        await EnsureAnnouncementAsync(
            dbContext,
            alpha,
            AlphaAnnouncementTitle,
            AlphaAnnouncementBody,
            now,
            cancellationToken);
        await EnsureAnnouncementAsync(
            dbContext,
            beta,
            BetaAnnouncementTitle,
            BetaAnnouncementBody,
            now,
            cancellationToken);
        await EnsureNotificationAsync(
            dbContext,
            alpha,
            AlphaNotificationLogicalKey,
            AlphaNotificationTitle,
            AlphaNotificationBody,
            now,
            cancellationToken);
        await EnsureNotificationAsync(
            dbContext,
            beta,
            BetaNotificationLogicalKey,
            BetaNotificationTitle,
            BetaNotificationBody,
            now,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<SecurityGraph> ResolveGraphAsync(
        AppDbContext dbContext,
        string tenantSlug,
        string ownerEmail,
        string workspaceSlug,
        string projectSlug,
        string taskTitle,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Slug == tenantSlug, cancellationToken);
        var owner = await ResolveUserAsync(dbContext, ownerEmail, cancellationToken);
        var workspace = await dbContext.Workspaces
            .IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.TenantId == tenant.Id && candidate.Slug == workspaceSlug,
                cancellationToken);
        var project = await dbContext.Projects
            .IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.TenantId == tenant.Id &&
                             candidate.WorkspaceId == workspace.Id &&
                             candidate.Slug == projectSlug,
                cancellationToken);
        var task = await dbContext.TaskItems
            .IgnoreQueryFilters()
            .SingleAsync(
                candidate => candidate.TenantId == tenant.Id &&
                             candidate.ProjectId == project.Id &&
                             candidate.Title == taskTitle,
                cancellationToken);

        return new SecurityGraph(tenant, owner, workspace, project, task);
    }

    private static Task<User> ResolveUserAsync(
        AppDbContext dbContext,
        string email,
        CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return dbContext.Users
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.NormalizedEmail == normalized, cancellationToken);
    }

    private static async Task EnsureShadowConversationAsync(
        AppDbContext dbContext,
        SecurityGraph graph,
        User alphaMember,
        User alphaRestricted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.FirstOrDefaultAsync(
            candidate => candidate.TenantId == graph.Tenant.Id &&
                         candidate.WorkspaceId == graph.Workspace.Id &&
                         candidate.ProjectId == graph.Project.Id &&
                         candidate.Title == AlphaShadowConversationTitle,
            cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = graph.Tenant.Id,
                WorkspaceId = graph.Workspace.Id,
                ProjectId = graph.Project.Id,
                Type = ConversationType.ProjectLinked,
                Title = AlphaShadowConversationTitle,
                IsArchived = false,
                IsLocked = false,
                CreatedByUserId = graph.Owner.Id
            };
            await dbContext.Conversations.AddAsync(conversation, cancellationToken);
        }
        else
        {
            conversation.Type = ConversationType.ProjectLinked;
            conversation.IsArchived = false;
            conversation.IsLocked = false;
            conversation.CreatedByUserId = graph.Owner.Id;
        }

        await EnsureConversationMemberAsync(
            dbContext,
            graph.Tenant.Id,
            conversation.Id,
            graph.Owner.Id,
            ConversationMemberRole.Admin,
            canPost: true,
            now,
            cancellationToken);
        await EnsureConversationMemberAsync(
            dbContext,
            graph.Tenant.Id,
            conversation.Id,
            alphaMember.Id,
            ConversationMemberRole.Member,
            canPost: true,
            now,
            cancellationToken);
        await EnsureConversationMemberAsync(
            dbContext,
            graph.Tenant.Id,
            conversation.Id,
            alphaRestricted.Id,
            ConversationMemberRole.ReadOnly,
            canPost: false,
            now,
            cancellationToken);

        var message = await dbContext.Messages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == graph.Tenant.Id &&
                             candidate.ConversationId == conversation.Id &&
                             candidate.Body == AlphaShadowMessageBody,
                cancellationToken);
        if (message is null)
        {
            await dbContext.Messages.AddAsync(new Message
            {
                TenantId = graph.Tenant.Id,
                WorkspaceId = graph.Workspace.Id,
                ConversationId = conversation.Id,
                AuthorUserId = graph.Owner.Id,
                Body = AlphaShadowMessageBody,
                Version = 1
            }, cancellationToken);
        }
        else
        {
            message.WorkspaceId = graph.Workspace.Id;
            message.AuthorUserId = graph.Owner.Id;
            message.Version = Math.Max(1, message.Version);
            if (message.IsDeleted)
            {
                message.Restore();
            }
        }
    }

    private static async Task EnsureConversationMemberAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid conversationId,
        Guid userId,
        ConversationMemberRole role,
        bool canPost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var membership = await dbContext.ConversationMembers.FirstOrDefaultAsync(
            candidate => candidate.TenantId == tenantId &&
                         candidate.ConversationId == conversationId &&
                         candidate.UserId == userId,
            cancellationToken);

        if (membership is null)
        {
            await dbContext.ConversationMembers.AddAsync(new ConversationMember
            {
                TenantId = tenantId,
                ConversationId = conversationId,
                UserId = userId,
                Role = role,
                CanRead = true,
                CanPost = canPost,
                CanManageMembers = role == ConversationMemberRole.Admin,
                CanCreateThread = canPost,
                JoinedAt = now
            }, cancellationToken);
            return;
        }

        membership.Role = role;
        membership.CanRead = true;
        membership.CanPost = canPost;
        membership.CanManageMembers = role == ConversationMemberRole.Admin;
        membership.CanCreateThread = canPost;
        membership.LeftAt = null;
        membership.RemovedAt = null;
        membership.RemovedByUserId = null;
    }

    private static async Task EnsureAnnouncementAsync(
        AppDbContext dbContext,
        SecurityGraph graph,
        string title,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var announcement = await dbContext.Announcements
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == graph.Tenant.Id && candidate.Title == title,
                cancellationToken);

        if (announcement is null)
        {
            announcement = new Announcement
            {
                TenantId = graph.Tenant.Id,
                WorkspaceId = graph.Workspace.Id,
                AuthorUserId = graph.Owner.Id,
                Title = title,
                Body = body,
                Priority = AnnouncementPriority.Important,
                IsPinned = false,
                RequiresReadConfirmation = true,
                PublishedAt = now
            };
            await dbContext.Announcements.AddAsync(announcement, cancellationToken);
            return;
        }

        announcement.WorkspaceId = graph.Workspace.Id;
        announcement.GroupId = null;
        announcement.ChannelId = null;
        announcement.AuthorUserId = graph.Owner.Id;
        announcement.Body = body;
        announcement.Priority = AnnouncementPriority.Important;
        announcement.IsPinned = false;
        announcement.RequiresReadConfirmation = true;
        announcement.ExpiresAt = null;
        if (announcement.IsDeleted)
        {
            announcement.Restore();
        }
    }

    private static async Task EnsureNotificationAsync(
        AppDbContext dbContext,
        SecurityGraph graph,
        string logicalKey,
        string title,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == graph.Tenant.Id &&
                             candidate.UserId == graph.Owner.Id &&
                             candidate.LogicalKey == logicalKey,
                cancellationToken);

        if (notification is null)
        {
            notification = new Notification
            {
                TenantId = graph.Tenant.Id,
                UserId = graph.Owner.Id,
                LogicalKey = logicalKey,
                NotificationType = NotificationType.TaskAssigned,
                Title = title,
                Body = body,
                RelatedEntityType = "TaskItem",
                RelatedEntityId = graph.Task.Id,
                IsRead = false,
                CreatedAt = now,
                StateVersion = 1
            };
            await dbContext.Notifications.AddAsync(notification, cancellationToken);
            return;
        }

        notification.NotificationType = NotificationType.TaskAssigned;
        notification.Title = title;
        notification.Body = body;
        notification.RelatedEntityType = "TaskItem";
        notification.RelatedEntityId = graph.Task.Id;
        notification.IsRead = false;
        notification.ReadAt = null;
        notification.DeletedAt = null;
        notification.StateVersion = Math.Max(1, notification.StateVersion);
    }

    private sealed record SecurityGraph(
        Tenant Tenant,
        User Owner,
        Workspace Workspace,
        Project Project,
        TaskItem Task);
}
