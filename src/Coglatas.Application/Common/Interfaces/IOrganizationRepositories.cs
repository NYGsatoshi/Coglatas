using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<Workspace>> ListForUserAsync(Guid userId, bool includeAll, CancellationToken cancellationToken = default);
    Task<Workspace?> GetByIdAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
    Task<WorkspaceMember?> GetMemberWithWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) =>
        GetMemberAsync(workspaceId, userId, cancellationToken);
    Task<IReadOnlyList<WorkspaceMember>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task AddMemberAsync(WorkspaceMember member, CancellationToken cancellationToken = default);
}

public interface IGroupRepository
{
    Task<IReadOnlyList<Group>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Group>> ListManagedByUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
    Task<Group?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GroupMember>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task AddAsync(Group group, CancellationToken cancellationToken = default);
    Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken = default);
}

public interface IChannelRepository
{
    Task<IReadOnlyList<Channel>> ListByGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Channel?> GetByIdAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<ChannelMember?> GetMemberAsync(Guid channelId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChannelMember>> ListMembersAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Post>> ListPinnedPostsAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<PagedResponse<Post>> ListPostsAsync(Guid channelId, int page, int pageSize, DateTimeOffset? before, DateTimeOffset? after, CancellationToken cancellationToken = default);
    Task<Post?> GetPostByIdAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<PagedResponse<PostThread>> ListThreadsAsync(Guid postId, int page, int pageSize, DateTimeOffset? before, DateTimeOffset? after, CancellationToken cancellationToken = default);
    Task AddAsync(Channel channel, CancellationToken cancellationToken = default);
    Task AddMemberAsync(ChannelMember member, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(ChannelMember member, CancellationToken cancellationToken = default);
    Task AddPostAsync(Post post, CancellationToken cancellationToken = default);
    Task AddThreadAsync(PostThread thread, CancellationToken cancellationToken = default);
}
