using Coglatas.Application.Common;

namespace Coglatas.Application.Channels;

public interface IChannelService
{
    Task<Result<IReadOnlyList<ChannelListItemResponse>>> ListByGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Result<ChannelResponse>> CreateAsync(Guid groupId, CreateChannelRequest request, CancellationToken cancellationToken = default);
    Task<Result<ChannelResponse>> GetAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<Result<ChannelResponse>> UpdateAsync(Guid channelId, UpdateChannelRequest request, CancellationToken cancellationToken = default);
    Task<Result> ArchiveAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ChannelMemberResponse>>> ListMembersAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<Result<ChannelMemberResponse>> AddMemberAsync(Guid channelId, AddChannelMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveMemberAsync(Guid channelId, Guid userId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<PostResponse>>> ListPostsAsync(Guid channelId, PostListQuery query, CancellationToken cancellationToken = default);
    Task<Result<PostResponse>> CreatePostAsync(Guid channelId, CreatePostRequest request, CancellationToken cancellationToken = default);
    Task<Result<PostResponse>> GetPostAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<Result<PostResponse>> UpdatePostAsync(Guid postId, UpdatePostRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeletePostAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<ThreadReplyResponse>>> ListThreadsAsync(Guid postId, ThreadListQuery query, CancellationToken cancellationToken = default);
    Task<Result<ThreadReplyResponse>> CreateThreadAsync(Guid postId, CreateThreadReplyRequest request, CancellationToken cancellationToken = default);
    Task<Result> PinPostAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<Result> UnpinPostAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PostResponse>>> ListPinnedPostsAsync(Guid channelId, CancellationToken cancellationToken = default);
}
