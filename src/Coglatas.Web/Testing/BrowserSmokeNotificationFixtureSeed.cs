using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Web.Testing;

internal static class BrowserSmokeNotificationFixtureSeed
{
    private const string RevocationGroupSlug = "browser-smoke-pr04-queue";

    public static async Task EnsureRevocableProjectAccessAsync(
        AppDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var group = await dbContext.Groups.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.Slug == RevocationGroupSlug &&
            candidate.Status == GroupStatus.Active &&
            candidate.DeletedAt == null,
            cancellationToken);
        if (group is null)
        {
            throw new InvalidOperationException(
                "The PR07-D browser-smoke revocation Group fixture is missing or inactive.");
        }

        var project = await dbContext.Projects.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.Slug == BrowserSmokeNotificationFixture.ProjectSlug &&
            candidate.Status == ProjectStatus.Active &&
            candidate.DeletedAt == null,
            cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException(
                "The PR07-D browser-smoke notification Project fixture is missing or inactive.");
        }

        var normalizedRecipientEmail = BrowserSmokeNotificationFixture.RecipientEmail.ToUpperInvariant();
        var recipient = await dbContext.Users.SingleOrDefaultAsync(candidate =>
            candidate.NormalizedEmail == normalizedRecipientEmail &&
            candidate.Status == UserStatus.Active &&
            candidate.DeletedAt == null,
            cancellationToken);
        if (recipient is null)
        {
            throw new InvalidOperationException(
                "The PR07-D browser-smoke notification recipient fixture is missing or inactive.");
        }

        var hasDirectProjectAccess = await dbContext.ProjectMembers.AnyAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.ProjectId == project.Id &&
            candidate.UserId == recipient.Id,
            cancellationToken);
        if (!hasDirectProjectAccess)
        {
            throw new InvalidOperationException(
                "The PR07-D browser-smoke recipient must start with direct Project membership.");
        }

        // Browser-smoke Projects are normalized to canonical Activated provenance
        // by the Test-only persistence interceptor. Keep the fixture graph equally
        // canonical: an Activated Project must have ProjectGeneral and participant
        // state before production membership mutation can be exercised.
        await EnsureCanonicalProjectGeneralAsync(dbContext, project, cancellationToken);

        // Ungrouped Projects intentionally inherit Workspace visibility. The
        // PR07-D revocation scenario must therefore be group-scoped so deleting
        // the recipient's direct ProjectMember is a real authorization loss.
        project.GroupId = group.Id;

        var recipientGroupMember = await dbContext.GroupMembers.FirstOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.GroupId == group.Id &&
            candidate.UserId == recipient.Id,
            cancellationToken);
        if (recipientGroupMember is not null)
        {
            dbContext.GroupMembers.Remove(recipientGroupMember);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureCanonicalProjectGeneralAsync(
        AppDbContext dbContext,
        Project project,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.FirstOrDefaultAsync(candidate =>
            candidate.TenantId == project.TenantId &&
            candidate.WorkspaceId == project.WorkspaceId &&
            candidate.ProjectId == project.Id &&
            (candidate.DefaultKind == ConversationDefaultKind.ProjectGeneral ||
             (candidate.Type == ConversationType.ProjectChannel && candidate.Title == "general")),
            cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = project.TenantId,
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                Type = ConversationType.ProjectChannel,
                Title = "general",
                Visibility = ConversationVisibility.PublicWithinScope,
                DefaultKind = ConversationDefaultKind.ProjectGeneral,
                CreatedByUserId = project.CreatedByUserId
            };
            await dbContext.Conversations.AddAsync(conversation, cancellationToken);
        }
        else
        {
            conversation.TenantId = project.TenantId;
            conversation.WorkspaceId = project.WorkspaceId;
            conversation.ProjectId = project.Id;
            conversation.Type = ConversationType.ProjectChannel;
            conversation.Title = "general";
            conversation.Visibility = ConversationVisibility.PublicWithinScope;
            conversation.DefaultKind = ConversationDefaultKind.ProjectGeneral;
            conversation.CreatedByUserId = project.CreatedByUserId;
        }

        var members = await dbContext.ProjectMembers
            .Where(candidate =>
                candidate.TenantId == project.TenantId &&
                candidate.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        foreach (var member in members)
        {
            var role =
                member.Role == ProjectRole.Owner ||
                member.UserId == project.OwnerUserId ||
                member.UserId == project.CreatedByUserId
                    ? ConversationMemberRole.Admin
                    : member.Role == ProjectRole.Viewer
                        ? ConversationMemberRole.ReadOnly
                        : ConversationMemberRole.Member;
            var readOnly = role == ConversationMemberRole.ReadOnly;
            var admin = role == ConversationMemberRole.Admin;
            var participant = await dbContext.ConversationMembers.FirstOrDefaultAsync(candidate =>
                candidate.TenantId == project.TenantId &&
                candidate.ConversationId == conversation.Id &&
                candidate.UserId == member.UserId,
                cancellationToken);

            if (participant is null)
            {
                await dbContext.ConversationMembers.AddAsync(new ConversationMember
                {
                    TenantId = project.TenantId,
                    ConversationId = conversation.Id,
                    UserId = member.UserId,
                    Role = role,
                    CanRead = true,
                    CanPost = !readOnly,
                    CanManageMembers = admin,
                    CanCreateThread = !readOnly,
                    JoinedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
                continue;
            }

            participant.Role = role;
            participant.CanRead = true;
            participant.CanPost = !readOnly;
            participant.CanManageMembers = admin;
            participant.CanCreateThread = !readOnly;
            participant.LeftAt = null;
            participant.RemovedAt = null;
            participant.RemovedByUserId = null;
        }
    }
}
