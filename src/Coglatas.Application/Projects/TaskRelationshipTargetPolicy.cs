using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.Projects;

/// <summary>
/// Authorizes a user as the active target of a Task relationship.  A historical
/// ProjectMember row alone is intentionally insufficient: the target must be
/// an active user with current Workspace and Project visibility.
/// </summary>
public interface ITaskRelationshipTargetPolicy
{
    Task<bool> IsEligibleAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class TaskRelationshipTargetPolicy(
    IProjectRepository projects,
    IUserRepository users,
    IProjectAuthorizationService projectAuthorization) : ITaskRelationshipTargetPolicy
{
    public async Task<bool> IsEligibleAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        var activeUsers = await users.GetActiveByIdsAsync([userId], cancellationToken);
        if (activeUsers.Count != 1 || activeUsers[0].Id != userId)
        {
            return false;
        }

        if (await projects.GetMemberAsync(projectId, userId, cancellationToken) is null)
        {
            return false;
        }

        return await projectAuthorization.CanViewProject(userId, projectId, cancellationToken);
    }
}
