using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Channels;

public sealed class ChannelService(
    IChannelRepository channels,
    IGroupRepository groups,
    IUserRepository users,
    IGroupAuthorizationService groupAuthorization,
    IChannelAuthorizationService authorization,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork) : IChannelService
{
    public async Task<Result<IReadOnlyList<ChannelListItemResponse>>> ListByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await groupAuthorization.CanViewGroup(userId, groupId, cancellationToken))
        {
            return Result<IReadOnlyList<ChannelListItemResponse>>.Failure("Group not found.");
        }

        var items = await channels.ListByGroupAsync(groupId, cancellationToken);
        var visible = new List<ChannelListItemResponse>();
        foreach (var channel in items.Where(c => c.Status == ChannelStatus.Active))
        {
            if (await authorization.CanViewChannel(userId, channel.Id, cancellationToken))
            {
                visible.Add(ToListItem(channel));
            }
        }

        return Result<IReadOnlyList<ChannelListItemResponse>>.Success(visible);
    }

    public async Task<Result<ChannelResponse>> CreateAsync(Guid groupId, CreateChannelRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await groupAuthorization.CanManageGroup(userId, groupId, cancellationToken))
        {
            return Result<ChannelResponse>.Failure("You are not allowed to create channels.");
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            return Result<ChannelResponse>.Failure("Group not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<ChannelResponse>.Failure("Channel name is required.");
        }

        var channel = new Channel
        {
            WorkspaceId = group.WorkspaceId,
            GroupId = groupId,
            Name = request.Name.Trim(),
            Slug = SlugGenerator.FromName(request.Name),
            Description = request.Description?.Trim(),
            Type = request.ChannelType,
            Status = ChannelStatus.Active,
            CreatedByUserId = userId
        };

        await channels.AddAsync(channel, cancellationToken);
        await channels.AddMemberAsync(new ChannelMember
        {
            ChannelId = channel.Id,
            UserId = userId,
            Role = ChannelRole.Admin,
            JoinedAt = clock.UtcNow
        }, cancellationToken);
        await AuditAsync(userId, "ChannelCreated", channel.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ChannelResponse>.Success(await ToResponseAsync(channel, cancellationToken));
    }

    public async Task<Result<ChannelResponse>> GetAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanViewChannel(userId, channelId, cancellationToken))
        {
            return Result<ChannelResponse>.Failure("Channel not found.");
        }

        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        if (channel?.Type == ChannelType.Confidential)
        {
            await AuditAsync(userId, "ConfidentialChannelViewed", channel.Id, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return channel is null
            ? Result<ChannelResponse>.Failure("Channel not found.")
            : Result<ChannelResponse>.Success(await ToResponseAsync(channel, cancellationToken));
    }

    public async Task<Result<ChannelResponse>> UpdateAsync(Guid channelId, UpdateChannelRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanManageChannel(userId, channelId, cancellationToken))
        {
            return Result<ChannelResponse>.Failure("You are not allowed to manage this channel.");
        }

        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return Result<ChannelResponse>.Failure("Channel not found.");
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Result<ChannelResponse>.Failure("Channel name is required.");
            }

            channel.Name = request.Name.Trim();
            channel.Slug = SlugGenerator.FromName(channel.Name);
        }

        channel.Description = request.Description?.Trim() ?? channel.Description;
        channel.Type = request.ChannelType ?? channel.Type;
        channel.Status = request.Status ?? channel.Status;
        await AuditAsync(userId, "ChannelUpdated", channel.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ChannelResponse>.Success(await ToResponseAsync(channel, cancellationToken));
    }

    public async Task<Result> ArchiveAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanManageChannel(userId, channelId, cancellationToken))
        {
            return Result.Failure("You are not allowed to manage this channel.");
        }

        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return Result.Failure("Channel not found.");
        }

        channel.Status = ChannelStatus.Archived;
        channel.MarkDeleted(clock.UtcNow);
        await AuditAsync(userId, "ChannelArchived", channel.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ChannelMemberResponse>>> ListMembersAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanManageChannel(userId, channelId, cancellationToken))
        {
            return Result<IReadOnlyList<ChannelMemberResponse>>.Failure("You are not allowed to manage channel members.");
        }

        var members = await channels.ListMembersAsync(channelId, cancellationToken);
        return Result<IReadOnlyList<ChannelMemberResponse>>.Success(members.Select(ToMember).ToList());
    }

    public async Task<Result<ChannelMemberResponse>> AddMemberAsync(Guid channelId, AddChannelMemberRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) || !await authorization.CanManageChannel(actorUserId, channelId, cancellationToken))
        {
            return Result<ChannelMemberResponse>.Failure("You are not allowed to manage channel members.");
        }

        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (channel is null || user is null)
        {
            return Result<ChannelMemberResponse>.Failure("Channel or user not found.");
        }

        if (await groups.GetMemberAsync(channel.GroupId, request.UserId, cancellationToken) is null)
        {
            return Result<ChannelMemberResponse>.Failure("User must belong to the group before joining the channel.");
        }

        if (await channels.GetMemberAsync(channelId, request.UserId, cancellationToken) is not null)
        {
            return Result<ChannelMemberResponse>.Failure("User is already a channel member.");
        }

        var member = new ChannelMember
        {
            ChannelId = channelId,
            UserId = request.UserId,
            User = user,
            Role = request.Role,
            JoinedAt = clock.UtcNow
        };

        await channels.AddMemberAsync(member, cancellationToken);
        await AuditAsync(actorUserId, "ChannelMemberAdded", channelId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ChannelMemberResponse>.Success(ToMember(member));
    }

    public async Task<Result> RemoveMemberAsync(Guid channelId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) || !await authorization.CanManageChannel(actorUserId, channelId, cancellationToken))
        {
            return Result.Failure("You are not allowed to manage channel members.");
        }

        var member = await channels.GetMemberAsync(channelId, userId, cancellationToken);
        if (member is null)
        {
            return Result.Failure("Channel member not found.");
        }

        await channels.RemoveMemberAsync(member, cancellationToken);
        await AuditAsync(actorUserId, "ChannelMemberRemoved", channelId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PagedResponse<PostResponse>>> ListPostsAsync(Guid channelId, PostListQuery query, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanViewChannel(userId, channelId, cancellationToken))
        {
            return Result<PagedResponse<PostResponse>>.Failure("Channel not found.");
        }

        var page = await channels.ListPostsAsync(channelId, query.SafePage, query.SafePageSize, query.Before, query.After, cancellationToken);
        return Result<PagedResponse<PostResponse>>.Success(new PagedResponse<PostResponse>(
            page.Items.Select(ToPost).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount));
    }

    public async Task<Result<PostResponse>> CreatePostAsync(Guid channelId, CreatePostRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanPostToChannel(userId, channelId, cancellationToken))
        {
            return Result<PostResponse>.Failure("You are not allowed to post to this channel.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return Result<PostResponse>.Failure("Post body is required.");
        }

        var post = new Post { ChannelId = channelId, AuthorUserId = userId, Body = request.Body.Trim() };
        await channels.AddPostAsync(post, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        post.AuthorUser = await users.GetByIdAsync(userId, cancellationToken);
        return Result<PostResponse>.Success(ToPost(post));
    }

    public async Task<Result<PostResponse>> GetPostAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue || !TryCurrentUser(out var userId) || !await authorization.CanViewChannel(userId, post.ChannelId, cancellationToken))
        {
            return Result<PostResponse>.Failure("Post not found.");
        }

        return Result<PostResponse>.Success(ToPost(post));
    }

    public async Task<Result<PostResponse>> UpdatePostAsync(Guid postId, UpdatePostRequest request, CancellationToken cancellationToken = default)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue || !TryCurrentUser(out var userId))
        {
            return Result<PostResponse>.Failure("Post not found.");
        }

        if (post.AuthorUserId != userId && !await authorization.CanManageChannel(userId, post.ChannelId, cancellationToken))
        {
            return Result<PostResponse>.Failure("You are not allowed to edit this post.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return Result<PostResponse>.Failure("Post body is required.");
        }

        post.Body = request.Body.Trim();
        post.EditedAt = clock.UtcNow;
        await AuditAsync(userId, "PostEdited", post.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<PostResponse>.Success(ToPost(post));
    }

    public async Task<Result> DeletePostAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        if (post is null || !TryCurrentUser(out var userId))
        {
            return Result.Failure("Post not found.");
        }

        if (post.AuthorUserId != userId && !await authorization.CanManageChannel(userId, post.ChannelId, cancellationToken))
        {
            return Result.Failure("You are not allowed to delete this post.");
        }

        post.MarkDeleted(clock.UtcNow);
        await AuditAsync(userId, "PostDeleted", post.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PagedResponse<ThreadReplyResponse>>> ListThreadsAsync(Guid postId, ThreadListQuery query, CancellationToken cancellationToken = default)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        if (post is null || !TryCurrentUser(out var userId) || !await authorization.CanViewChannel(userId, post.ChannelId, cancellationToken))
        {
            return Result<PagedResponse<ThreadReplyResponse>>.Failure("Post not found.");
        }

        var replies = await channels.ListThreadsAsync(postId, query.SafePage, query.SafePageSize, query.Before, query.After, cancellationToken);
        return Result<PagedResponse<ThreadReplyResponse>>.Success(new PagedResponse<ThreadReplyResponse>(
            replies.Items.Select(ToThread).ToList(),
            replies.Page,
            replies.PageSize,
            replies.TotalCount));
    }

    public async Task<Result<ThreadReplyResponse>> CreateThreadAsync(Guid postId, CreateThreadReplyRequest request, CancellationToken cancellationToken = default)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        if (post is null || !TryCurrentUser(out var userId) || !await authorization.CanPostToChannel(userId, post.ChannelId, cancellationToken))
        {
            return Result<ThreadReplyResponse>.Failure("You are not allowed to reply to this post.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return Result<ThreadReplyResponse>.Failure("Reply body is required.");
        }

        var reply = new PostThread { PostId = postId, AuthorUserId = userId, Body = request.Body.Trim() };
        await channels.AddThreadAsync(reply, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        reply.AuthorUser = await users.GetByIdAsync(userId, cancellationToken);
        return Result<ThreadReplyResponse>.Success(ToThread(reply));
    }

    public async Task<Result> PinPostAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        return await SetPinnedAsync(postId, true, cancellationToken);
    }

    public async Task<Result> UnpinPostAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        return await SetPinnedAsync(postId, false, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<PostResponse>>> ListPinnedPostsAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanViewChannel(userId, channelId, cancellationToken))
        {
            return Result<IReadOnlyList<PostResponse>>.Failure("Channel not found.");
        }

        var posts = await channels.ListPinnedPostsAsync(channelId, cancellationToken);
        return Result<IReadOnlyList<PostResponse>>.Success(posts.Select(ToPost).ToList());
    }

    private async Task<Result> SetPinnedAsync(Guid postId, bool pinned, CancellationToken cancellationToken)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        if (post is null || !TryCurrentUser(out var userId) || !await authorization.CanPinPost(userId, postId, cancellationToken))
        {
            return Result.Failure("You are not allowed to pin this post.");
        }

        post.PinnedAt = pinned ? clock.UtcNow : null;
        post.PinnedByUserId = pinned ? userId : null;
        await AuditAsync(userId, pinned ? "PostPinned" : "PostUnpinned", post.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<ChannelResponse> ToResponseAsync(Channel channel, CancellationToken cancellationToken)
    {
        var pinned = await channels.ListPinnedPostsAsync(channel.Id, cancellationToken);
        return new ChannelResponse(channel.Id, channel.GroupId, channel.Name, channel.Description, channel.Type, channel.Status, channel.CreatedByUserId, channel.CreatedAt, channel.UpdatedAt, pinned.Select(ToPost).ToList());
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    private Task AuditAsync(Guid actorUserId, string action, Guid targetId, CancellationToken cancellationToken)
    {
        var targetType = action.StartsWith("Post", StringComparison.Ordinal) ? "Post" : "Channel";
        return auditLogger.LogAsync(new AuditLogEntry(actorUserId, action, targetType, targetId, SummaryFor(action)), cancellationToken);
    }

    private static string SummaryFor(string action) => action switch
    {
        "ChannelCreated" => "Channel created.",
        "PostDeleted" => "Post deleted.",
        _ => $"{action} completed."
    };

    private static ChannelListItemResponse ToListItem(Channel channel)
    {
        return new ChannelListItemResponse(channel.Id, channel.GroupId, channel.Name, channel.Description, channel.Type, channel.Status, channel.CreatedAt, channel.UpdatedAt);
    }

    private static ChannelMemberResponse ToMember(ChannelMember member)
    {
        return new ChannelMemberResponse(member.UserId, member.User?.DisplayName ?? string.Empty, member.User?.Email ?? string.Empty, member.Role, member.JoinedAt);
    }

    private static PostResponse ToPost(Post post)
    {
        return new PostResponse(post.Id, post.ChannelId, post.AuthorUserId, post.AuthorUser?.DisplayName ?? string.Empty, post.Body, post.PinnedAt.HasValue, post.CreatedAt, post.UpdatedAt, post.EditedAt);
    }

    private static ThreadReplyResponse ToThread(PostThread thread)
    {
        return new ThreadReplyResponse(thread.Id, thread.PostId, thread.AuthorUserId, thread.AuthorUser?.DisplayName ?? string.Empty, thread.Body, thread.CreatedAt, thread.UpdatedAt);
    }
}
