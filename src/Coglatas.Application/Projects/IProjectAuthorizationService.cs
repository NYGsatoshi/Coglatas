using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public interface IProjectAuthorizationService
{
    Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default);
    Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates current Project content-mutation authority. Read/discovery
    /// visibility is never sufficient. Implementations that have not adopted
    /// the explicit contribution boundary fail closed to Project management.
    /// </summary>
    Task<bool> CanContributeProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) =>
        CanManageProject(userId, projectId, cancellationToken);

    Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default);
}

public interface ITaskAuthorizationService
{
    /// <summary>Combines read-time capability projection without caching command authorization.</summary>
    async Task<TaskReadCapabilities> GetReadCapabilitiesAsync(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default) => new(
        await CanUpdateTask(userId, taskItemId, cancellationToken),
        await CanAssignTask(userId, taskItemId, cancellationToken),
        await CanDeleteTask(userId, taskItemId, cancellationToken),
        await CanReviewTask(userId, taskItemId, cancellationToken),
        await CanOverrideTaskReview(userId, taskItemId, cancellationToken));

    Task<bool> CanCreateTask(Guid userId, Guid projectId, CancellationToken cancellationToken = default);
    Task<bool> CanUpdateTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default);
    Task<bool> CanAssignTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default);
    Task<bool> CanReviewTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default);
    Task<bool> CanOverrideTaskReview(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default);
}

public interface ICommentAuthorizationService
{
    Task<bool> CanCommentOnTarget(Guid userId, CommentTargetType targetType, Guid targetId, CancellationToken cancellationToken = default);
}

public sealed record TaskReadCapabilities(bool CanUpdate, bool CanAssign, bool CanDelete, bool CanReview, bool CanOverrideReview);
