using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// The current-state access boundary for persisted Workspace File grants.
/// Keep all consumers on the same recipient, File, and scope predicates.
/// </summary>
internal static class EffectiveFileAccessGrantQuery
{
    internal static IQueryable<FileAccessGrant> For(AppDbContext dbContext)
    {
        return dbContext.FileAccessGrants
            .AsNoTracking()
            .Where(grant =>
                grant.RevokedAt == null &&
                dbContext.FileObjects.Any(file =>
                    file.Id == grant.FileObjectId &&
                    file.TenantId == grant.TenantId &&
                    file.WorkspaceId == grant.WorkspaceId &&
                    file.DeletedAt == null &&
                    file.Status != FileObjectStatus.Deleted) &&
                dbContext.Attachments.Any(attachment =>
                    attachment.FileObjectId == grant.FileObjectId &&
                    attachment.WorkspaceId == grant.WorkspaceId &&
                    attachment.OwnerType == AttachmentOwnerType.Workspace &&
                    attachment.OwnerId == grant.WorkspaceId &&
                    attachment.DeletedAt == null) &&
                dbContext.Workspaces.Any(workspace =>
                    workspace.Id == grant.WorkspaceId &&
                    workspace.TenantId == grant.TenantId &&
                    workspace.DeletedAt == null &&
                    workspace.Status == WorkspaceStatus.Active) &&
                dbContext.TenantUsers.Any(tenantUser =>
                    tenantUser.TenantId == grant.TenantId &&
                    tenantUser.UserId == grant.RecipientUserId &&
                    tenantUser.Status == TenantUserStatus.Active) &&
                dbContext.Users.Any(user =>
                    user.Id == grant.RecipientUserId &&
                    user.Status == UserStatus.Active &&
                    user.DeletedAt == null) &&
                ((grant.RecipientKind == FileAccessGrantRecipientKind.WorkspaceMember &&
                  dbContext.WorkspaceMembers.Any(member =>
                      member.TenantId == grant.TenantId &&
                      member.WorkspaceId == grant.WorkspaceId &&
                      member.UserId == grant.RecipientUserId &&
                      member.Status == MembershipStatus.Active)) ||
                 (grant.RecipientKind == FileAccessGrantRecipientKind.ExternalProjectMember &&
                  !dbContext.WorkspaceMembers.Any(member =>
                      member.TenantId == grant.TenantId &&
                      member.WorkspaceId == grant.WorkspaceId &&
                      member.UserId == grant.RecipientUserId &&
                      member.Status == MembershipStatus.Active) &&
                  dbContext.ProjectMembers.Any(member =>
                      member.TenantId == grant.TenantId &&
                      member.UserId == grant.RecipientUserId &&
                      dbContext.Projects.Any(project =>
                          project.Id == member.ProjectId &&
                          project.TenantId == grant.TenantId &&
                          project.WorkspaceId == grant.WorkspaceId &&
                          project.DeletedAt == null &&
                          project.Status != ProjectStatus.Archived &&
                          project.Status != ProjectStatus.Deleted)))));
    }
}
