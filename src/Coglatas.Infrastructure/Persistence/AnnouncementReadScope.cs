using Coglatas.Application.Announcements;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Canonical SQL-translatable Announcement list/detail/search scope. Legacy
/// announcements continue to use their live single-scope authorization. A
/// durable #388 publication records an atomic frozen-cohort audit marker and
/// one logical Announcement notification per dispatch-time recipient. The
/// notification is immutable cohort membership even if the recipient later
/// soft-deletes its visible notification; the marker also represents an empty
/// cohort without allowing future scope members to drift into it.
/// Tenant membership remains a hard outer boundary.
/// </summary>
public static class AnnouncementReadScope
{
    public static IQueryable<Announcement> VisibleAnnouncementsFor(
        this AppDbContext dbContext,
        Guid userId,
        bool isSystemAdmin,
        DateTimeOffset now)
    {
        var baseQuery = dbContext.Announcements
            .AsNoTracking()
            .Where(announcement =>
                announcement.DeletedAt == null &&
                announcement.PublishedAt <= now &&
                (!announcement.ExpiresAt.HasValue || announcement.ExpiresAt.Value > now));

        if (isSystemAdmin)
        {
            // Preserve the existing SystemAdmin announcement-read exception.
            // Tenant isolation still comes from the AppDbContext query filter.
            return baseQuery;
        }

        var activeTenantUserIds = dbContext.TenantUsers
            .AsNoTracking()
            .Where(tenantUser => tenantUser.Status == TenantUserStatus.Active)
            .Select(tenantUser => tenantUser.UserId);

        var readableWorkspaceIds = dbContext.Workspaces
            .AsNoTracking()
            .Where(workspace =>
                workspace.DeletedAt == null &&
                (workspace.Status == WorkspaceStatus.Active ||
                 workspace.Status == WorkspaceStatus.Archived) &&
                dbContext.WorkspaceMembers.Any(member =>
                    member.WorkspaceId == workspace.Id &&
                    member.UserId == userId &&
                    member.Status == MembershipStatus.Active))
            .Select(workspace => workspace.Id);

        var frozenCohortAnnouncementIds = dbContext.AuditLogs
            .AsNoTracking()
            .Where(log =>
                log.Action == AnnouncementDistributionContract.FrozenCohortAuditAction &&
                log.EntityType == "Announcement" &&
                log.EntityId.HasValue)
            .Select(log => log.EntityId!.Value);

        var deliveredAnnouncementIds = dbContext.Notifications
            .AsNoTracking()
            .Where(notification =>
                notification.UserId == userId &&
                notification.NotificationType == NotificationType.Announcement &&
                notification.RelatedEntityType == "Announcement" &&
                notification.RelatedEntityId.HasValue &&
                notification.LogicalKey != null &&
                notification.LogicalKey.StartsWith(AnnouncementDistributionContract.DeliveryLogicalKeyPrefix))
            .Select(notification => notification.RelatedEntityId!.Value);

        return baseQuery.Where(announcement =>
            activeTenantUserIds.Contains(userId) &&
            (
                // Durable multi/single target publication: dispatch-time
                // membership has already been re-authorized and frozen.
                (frozenCohortAnnouncementIds.Contains(announcement.Id) &&
                 deliveredAnnouncementIds.Contains(announcement.Id)) ||

                // Legacy announcements without a frozen cohort retain the
                // historical live single-scope visibility contract.
                (!frozenCohortAnnouncementIds.Contains(announcement.Id) &&
                 (
                    // Tenant-global announcement.
                    (!announcement.WorkspaceId.HasValue &&
                     !announcement.GroupId.HasValue &&
                     !announcement.ChannelId.HasValue) ||

                    // Channel-scoped announcement. Scope links, parent lifecycle,
                    // current Workspace access, and the channel audience must all hold.
                    (announcement.ChannelId.HasValue &&
                     announcement.GroupId.HasValue &&
                     announcement.WorkspaceId.HasValue &&
                     dbContext.Channels.Any(channel =>
                         channel.Id == announcement.ChannelId.Value &&
                         channel.GroupId == announcement.GroupId.Value &&
                         channel.WorkspaceId == announcement.WorkspaceId.Value &&
                         channel.DeletedAt == null &&
                         channel.Status == ChannelStatus.Active &&
                         readableWorkspaceIds.Contains(channel.WorkspaceId) &&
                         dbContext.Groups.Any(group =>
                             group.Id == channel.GroupId &&
                             group.WorkspaceId == channel.WorkspaceId &&
                             group.DeletedAt == null &&
                             group.Status == GroupStatus.Active) &&
                         (((channel.Type == ChannelType.Public ||
                            channel.Type == ChannelType.Announcement) &&
                           dbContext.GroupMembers.Any(member =>
                               member.GroupId == channel.GroupId &&
                               member.UserId == userId)) ||
                          ((channel.Type == ChannelType.Private ||
                            channel.Type == ChannelType.Confidential) &&
                           dbContext.ChannelMembers.Any(member =>
                               member.ChannelId == channel.Id &&
                               member.UserId == userId))))) ||

                    // Group-scoped announcement. A stale GroupMember row must never
                    // survive loss of the parent Workspace authorization boundary.
                    (!announcement.ChannelId.HasValue &&
                     announcement.GroupId.HasValue &&
                     announcement.WorkspaceId.HasValue &&
                     dbContext.Groups.Any(group =>
                         group.Id == announcement.GroupId.Value &&
                         group.WorkspaceId == announcement.WorkspaceId.Value &&
                         group.DeletedAt == null &&
                         group.Status == GroupStatus.Active &&
                         readableWorkspaceIds.Contains(group.WorkspaceId) &&
                         dbContext.GroupMembers.Any(member =>
                             member.GroupId == group.Id &&
                             member.UserId == userId))) ||

                    // Workspace-only announcement.
                    (!announcement.ChannelId.HasValue &&
                     !announcement.GroupId.HasValue &&
                     announcement.WorkspaceId.HasValue &&
                     readableWorkspaceIds.Contains(announcement.WorkspaceId.Value))
                 ))
            ));
    }
}
