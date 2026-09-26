using Coglatas.Application.Common;

namespace Coglatas.Application.Groups;

public interface IGroupService
{
    Task<Result<IReadOnlyList<GroupListItemResponse>>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Result<GroupDetailResponse>> CreateAsync(Guid workspaceId, CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result<GroupDetailResponse>> GetAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Result<GroupDetailResponse>> UpdateAsync(Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result> ArchiveAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GroupMemberResponse>>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Result<GroupMemberResponse>> AddMemberAsync(Guid groupId, AddGroupMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result<GroupMemberResponse>> UpdateMemberAsync(Guid groupId, Guid userId, UpdateGroupMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
}
