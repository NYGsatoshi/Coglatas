using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// SQL-translatable Project read scopes. The normal scope mirrors
/// ProjectAuthorizationService.CanViewProject. The list scope additionally
/// preserves explicitly-authorized archived Project history without making
/// archived Project-derived content searchable or navigable.
/// </summary>
public static class ProjectReadScope
{
    public static IQueryable<Project> VisibleProjectsFor(this AppDbContext dbContext, Guid userId)
    {
        var activeSystemAdminIds = dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.Id == userId &&
                user.SystemRole == SystemRole.SystemAdmin &&
                user.Status == UserStatus.Active &&
                user.DeletedAt == null)
            .Select(user => user.Id);

        return dbContext.Projects
            .AsNoTracking()
            .Where(project =>
                project.DeletedAt == null &&
                project.Status != ProjectStatus.Archived &&
                project.Status != ProjectStatus.Deleted &&
                dbContext.Workspaces.Any(workspace =>
                    workspace.Id == project.WorkspaceId &&
                    workspace.DeletedAt == null &&
                    ((workspace.Status == WorkspaceStatus.Active &&
                      (activeSystemAdminIds.Contains(userId) ||
                       dbContext.WorkspaceMembers.Any(member =>
                           member.WorkspaceId == project.WorkspaceId &&
                           member.UserId == userId &&
                           member.Status == MembershipStatus.Active))) ||
                     (workspace.Status == WorkspaceStatus.Archived &&
                      dbContext.WorkspaceMembers.Any(member =>
                          member.WorkspaceId == project.WorkspaceId &&
                          member.UserId == userId &&
                          member.Status == MembershipStatus.Active)))) &&
                (project.Members.Any(member => member.UserId == userId) ||
                 (project.Visibility.HasValue
                     ? project.Visibility == ProjectVisibility.WorkspaceVisible &&
                       project.ActivationState == ProjectActivationState.Activated &&
                       (project.Status == ProjectStatus.Active ||
                        project.Status == ProjectStatus.Review ||
                        project.Status == ProjectStatus.Completed)
                     : (project.Status == ProjectStatus.Active ||
                        project.Status == ProjectStatus.Review ||
                        project.Status == ProjectStatus.Completed) &&
                       (!project.GroupId.HasValue ||
                        dbContext.Workspaces.Any(workspace =>
                            workspace.Id == project.WorkspaceId &&
                            workspace.DeletedAt == null &&
                            workspace.Status == WorkspaceStatus.Active &&
                            activeSystemAdminIds.Contains(userId)) ||
                        dbContext.WorkspaceMembers.Any(member =>
                            member.WorkspaceId == project.WorkspaceId &&
                            member.UserId == userId &&
                            member.Status == MembershipStatus.Active &&
                            (member.Role == WorkspaceRole.Owner || member.Role == WorkspaceRole.Admin)) ||
                        dbContext.GroupMembers.Any(member =>
                            member.GroupId == project.GroupId.Value &&
                            member.UserId == userId)))));
    }

    public static IQueryable<Project> ListableProjectsFor(this AppDbContext dbContext, Guid userId)
    {
        var archivedHistory = dbContext.Projects
            .AsNoTracking()
            .Where(project =>
                project.DeletedAt == null &&
                project.Status == ProjectStatus.Archived &&
                dbContext.Workspaces.Any(workspace =>
                    workspace.Id == project.WorkspaceId &&
                    workspace.DeletedAt == null &&
                    (workspace.Status == WorkspaceStatus.Active || workspace.Status == WorkspaceStatus.Archived)) &&
                dbContext.WorkspaceMembers.Any(member =>
                    member.WorkspaceId == project.WorkspaceId &&
                    member.UserId == userId &&
                    member.Status == MembershipStatus.Active) &&
                project.Members.Any(member => member.UserId == userId));

        return dbContext.VisibleProjectsFor(userId).Concat(archivedHistory);
    }

    public static IQueryable<Guid> CurrentReaderUserIdsForProject(this AppDbContext dbContext, Guid projectId)
    {
        var project = dbContext.Projects
            .AsNoTracking()
            .Where(item =>
                item.Id == projectId &&
                item.DeletedAt == null &&
                item.Status != ProjectStatus.Deleted);

        return dbContext.Users
            .AsNoTracking()
            .Where(user => user.Status == UserStatus.Active && user.DeletedAt == null)
            .Where(user => project.Any(item =>
                item.Status == ProjectStatus.Archived
                    ? dbContext.Workspaces.Any(workspace =>
                          workspace.Id == item.WorkspaceId &&
                          workspace.DeletedAt == null &&
                          (workspace.Status == WorkspaceStatus.Active || workspace.Status == WorkspaceStatus.Archived)) &&
                      dbContext.WorkspaceMembers.Any(member =>
                          member.WorkspaceId == item.WorkspaceId &&
                          member.UserId == user.Id &&
                          member.Status == MembershipStatus.Active) &&
                      item.Members.Any(member => member.UserId == user.Id)
                    : dbContext.Workspaces.Any(workspace =>
                          workspace.Id == item.WorkspaceId &&
                          workspace.DeletedAt == null &&
                          ((workspace.Status == WorkspaceStatus.Active &&
                            (user.SystemRole == SystemRole.SystemAdmin ||
                             dbContext.WorkspaceMembers.Any(member =>
                                 member.WorkspaceId == item.WorkspaceId &&
                                 member.UserId == user.Id &&
                                 member.Status == MembershipStatus.Active))) ||
                           (workspace.Status == WorkspaceStatus.Archived &&
                            dbContext.WorkspaceMembers.Any(member =>
                                member.WorkspaceId == item.WorkspaceId &&
                                member.UserId == user.Id &&
                                member.Status == MembershipStatus.Active)))) &&
                      (item.Members.Any(member => member.UserId == user.Id) ||
                       (item.Visibility.HasValue
                           ? item.Visibility == ProjectVisibility.WorkspaceVisible &&
                             item.ActivationState == ProjectActivationState.Activated &&
                             (item.Status == ProjectStatus.Active ||
                              item.Status == ProjectStatus.Review ||
                              item.Status == ProjectStatus.Completed)
                           : (item.Status == ProjectStatus.Active ||
                              item.Status == ProjectStatus.Review ||
                              item.Status == ProjectStatus.Completed) &&
                             (!item.GroupId.HasValue ||
                              dbContext.Workspaces.Any(workspace =>
                                  workspace.Id == item.WorkspaceId &&
                                  workspace.DeletedAt == null &&
                                  workspace.Status == WorkspaceStatus.Active &&
                                  user.SystemRole == SystemRole.SystemAdmin) ||
                              dbContext.WorkspaceMembers.Any(member =>
                                  member.WorkspaceId == item.WorkspaceId &&
                                  member.UserId == user.Id &&
                                  member.Status == MembershipStatus.Active &&
                                  (member.Role == WorkspaceRole.Owner || member.Role == WorkspaceRole.Admin)) ||
                              dbContext.GroupMembers.Any(member =>
                                  member.GroupId == item.GroupId.Value &&
                                  member.UserId == user.Id))))))
            .Select(user => user.Id);
    }
}
