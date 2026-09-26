using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class WorkspaceRepository(AppDbContext dbContext) : IWorkspaceRepository
{
    public async Task<IReadOnlyList<Workspace>> ListForUserAsync(Guid userId, bool includeAll, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Workspaces.AsNoTracking();
        if (!includeAll)
        {
            query = query.Where(workspace => workspace.Members.Any(member =>
                member.UserId == userId &&
                member.Status == MembershipStatus.Active));
        }

        return await query.OrderBy(workspace => workspace.Name).ToListAsync(cancellationToken);
    }

    public Task<Workspace?> GetByIdAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return dbContext.Workspaces.FirstOrDefaultAsync(workspace => workspace.Id == workspaceId, cancellationToken);
    }

    public Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.WorkspaceMembers
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.WorkspaceId == workspaceId && member.UserId == userId, cancellationToken);
    }

    public Task<WorkspaceMember?> GetMemberWithWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.WorkspaceMembers
            .Include(member => member.Workspace)
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.WorkspaceId == workspaceId && member.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceMember>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkspaceMembers
            .AsNoTracking()
            .Include(member => member.User)
            .Where(member => member.WorkspaceId == workspaceId)
            .OrderBy(member => member.User!.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        await dbContext.Workspaces.AddAsync(workspace, cancellationToken);
    }

    public async Task AddMemberAsync(WorkspaceMember member, CancellationToken cancellationToken = default)
    {
        await dbContext.WorkspaceMembers.AddAsync(member, cancellationToken);
    }
}

public sealed class GroupRepository(AppDbContext dbContext) : IGroupRepository
{
    public async Task<IReadOnlyList<Group>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Groups
            .AsNoTracking()
            .Where(group => group.WorkspaceId == workspaceId)
            .OrderBy(group => group.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Group>> ListManagedByUserAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Groups
            .AsNoTracking()
            .Where(group =>
                group.WorkspaceId == workspaceId &&
                group.Status == GroupStatus.Active &&
                group.DeletedAt == null &&
                dbContext.GroupMembers.Any(member =>
                    member.TenantId == group.TenantId &&
                    member.GroupId == group.Id &&
                    member.UserId == userId &&
                    (member.Role == GroupRole.Owner || member.Role == GroupRole.Admin)))
            .OrderBy(group => group.Name)
            .ThenBy(group => group.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Group?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return dbContext.Groups.FirstOrDefaultAsync(group => group.Id == groupId, cancellationToken);
    }

    public Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.GroupMembers
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.GroupId == groupId && member.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<GroupMember>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GroupMembers
            .AsNoTracking()
            .Include(member => member.User)
            .Where(member => member.GroupId == groupId)
            .OrderBy(member => member.User!.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Group group, CancellationToken cancellationToken = default)
    {
        await dbContext.Groups.AddAsync(group, cancellationToken);
    }

    public async Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken = default)
    {
        await dbContext.GroupMembers.AddAsync(member, cancellationToken);
    }
}

public sealed class ChannelRepository(AppDbContext dbContext) : IChannelRepository
{
    public async Task<IReadOnlyList<Channel>> ListByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Channels
            .AsNoTracking()
            .Where(channel => channel.GroupId == groupId)
            .OrderBy(channel => channel.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Channel?> GetByIdAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        return dbContext.Channels.FirstOrDefaultAsync(channel => channel.Id == channelId, cancellationToken);
    }

    public Task<ChannelMember?> GetMemberAsync(Guid channelId, Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.ChannelMembers
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.ChannelId == channelId && member.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelMember>> ListMembersAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ChannelMembers
            .AsNoTracking()
            .Include(member => member.User)
            .Where(member => member.ChannelId == channelId)
            .OrderBy(member => member.User!.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> ListPinnedPostsAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Posts
            .AsNoTracking()
            .Include(post => post.AuthorUser)
            .Where(post => post.ChannelId == channelId && post.PinnedAt != null && post.DeletedAt == null)
            .OrderByDescending(post => post.PinnedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<Post>> ListPostsAsync(Guid channelId, int page, int pageSize, DateTimeOffset? before, DateTimeOffset? after, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Posts
            .AsNoTracking()
            .Include(post => post.AuthorUser)
            .Where(post => post.ChannelId == channelId && post.DeletedAt == null);

        if (before.HasValue)
        {
            query = query.Where(post => post.CreatedAt < before.Value);
        }

        if (after.HasValue)
        {
            query = query.Where(post => post.CreatedAt > after.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(post => post.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Post>(items, page, pageSize, total);
    }

    public Task<Post?> GetPostByIdAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        return dbContext.Posts
            .AsNoTracking()
            .Include(post => post.AuthorUser)
            .FirstOrDefaultAsync(post => post.Id == postId, cancellationToken);
    }

    public async Task<PagedResponse<PostThread>> ListThreadsAsync(Guid postId, int page, int pageSize, DateTimeOffset? before, DateTimeOffset? after, CancellationToken cancellationToken = default)
    {
        var query = dbContext.PostThreads
            .AsNoTracking()
            .Include(thread => thread.AuthorUser)
            .Where(thread => thread.PostId == postId && thread.DeletedAt == null);

        if (before.HasValue)
        {
            query = query.Where(thread => thread.CreatedAt < before.Value);
        }

        if (after.HasValue)
        {
            query = query.Where(thread => thread.CreatedAt > after.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(thread => thread.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<PostThread>(items, page, pageSize, total);
    }

    public async Task AddAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        await dbContext.Channels.AddAsync(channel, cancellationToken);
    }

    public async Task AddMemberAsync(ChannelMember member, CancellationToken cancellationToken = default)
    {
        await dbContext.ChannelMembers.AddAsync(member, cancellationToken);
    }

    public Task RemoveMemberAsync(ChannelMember member, CancellationToken cancellationToken = default)
    {
        dbContext.ChannelMembers.Remove(member);
        return Task.CompletedTask;
    }

    public async Task AddPostAsync(Post post, CancellationToken cancellationToken = default)
    {
        await dbContext.Posts.AddAsync(post, cancellationToken);
    }

    public async Task AddThreadAsync(PostThread thread, CancellationToken cancellationToken = default)
    {
        await dbContext.PostThreads.AddAsync(thread, cancellationToken);
    }
}
