namespace Coglatas.Application.Groups;

public interface IGroupAuthorizationService
{
    Task<bool> CanViewGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

    Task<bool> CanManageGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

    Task<bool> CanCreateGroup(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
}
