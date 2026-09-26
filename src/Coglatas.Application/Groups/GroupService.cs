using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Groups;

public sealed class GroupService(
    IGroupRepository groups,
    IWorkspaceRepository workspaces,
    IUserRepository users,
    IGroupAuthorizationService authorization,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork) : IGroupService
{
    public async Task<Result<IReadOnlyList<GroupListItemResponse>>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) ||
            (currentUser.SystemRole != SystemRole.SystemAdmin &&
             await workspaces.GetMemberAsync(workspaceId, userId, cancellationToken) is null))
        {
            return Result<IReadOnlyList<GroupListItemResponse>>.Failure("Workspace not found.");
        }

        var items = await groups.ListByWorkspaceAsync(workspaceId, cancellationToken);
        return Result<IReadOnlyList<GroupListItemResponse>>.Success(items.Where(g => g.Status == GroupStatus.Active).Select(ToListItem).ToList());
    }

    public async Task<Result<GroupDetailResponse>> CreateAsync(Guid workspaceId, CreateGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanCreateGroup(userId, workspaceId, cancellationToken))
        {
            return Result<GroupDetailResponse>.Failure("You are not allowed to create groups.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<GroupDetailResponse>.Failure("Group name is required.");
        }

        if (request.ParentGroupId.HasValue)
        {
            var parent = await groups.GetByIdAsync(request.ParentGroupId.Value, cancellationToken);
            if (parent is null || parent.WorkspaceId != workspaceId)
            {
                return Result<GroupDetailResponse>.Failure("Parent group must belong to the same workspace.");
            }
        }

        var group = new Group
        {
            WorkspaceId = workspaceId,
            ParentGroupId = request.ParentGroupId,
            Name = request.Name.Trim(),
            Slug = SlugGenerator.FromName(request.Name),
            Description = request.Description?.Trim(),
            GroupType = request.GroupType,
            Status = GroupStatus.Active,
            CreatedByUserId = userId
        };

        await groups.AddAsync(group, cancellationToken);
        await groups.AddMemberAsync(new GroupMember
        {
            GroupId = group.Id,
            UserId = userId,
            Role = GroupRole.Owner,
            JoinedAt = clock.UtcNow
        }, cancellationToken);
        await AuditAsync(userId, "GroupCreated", group.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<GroupDetailResponse>.Success(ToDetail(group));
    }

    public async Task<Result<GroupDetailResponse>> GetAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanViewGroup(userId, groupId, cancellationToken))
        {
            return Result<GroupDetailResponse>.Failure("Group not found.");
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        return group is null
            ? Result<GroupDetailResponse>.Failure("Group not found.")
            : Result<GroupDetailResponse>.Success(ToDetail(group));
    }

    public async Task<Result<GroupDetailResponse>> UpdateAsync(Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanManageGroup(userId, groupId, cancellationToken))
        {
            return Result<GroupDetailResponse>.Failure("You are not allowed to manage this group.");
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            return Result<GroupDetailResponse>.Failure("Group not found.");
        }

        if (request.ParentGroupId.HasValue)
        {
            var parent = await groups.GetByIdAsync(request.ParentGroupId.Value, cancellationToken);
            if (parent is null || parent.WorkspaceId != group.WorkspaceId || parent.Id == group.Id)
            {
                return Result<GroupDetailResponse>.Failure("Parent group must belong to the same workspace.");
            }
            group.ParentGroupId = request.ParentGroupId;
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Result<GroupDetailResponse>.Failure("Group name is required.");
            }
            group.Name = request.Name.Trim();
            group.Slug = SlugGenerator.FromName(group.Name);
        }

        group.Description = request.Description?.Trim() ?? group.Description;
        group.GroupType = request.GroupType ?? group.GroupType;
        group.Status = request.Status ?? group.Status;
        await AuditAsync(userId, "GroupUpdated", group.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<GroupDetailResponse>.Success(ToDetail(group));
    }

    public async Task<Result> ArchiveAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanManageGroup(userId, groupId, cancellationToken))
        {
            return Result.Failure("You are not allowed to manage this group.");
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure("Group not found.");
        }

        group.Status = GroupStatus.Archived;
        await AuditAsync(userId, "GroupArchived", group.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RestoreAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanManageGroup(userId, groupId, cancellationToken))
        {
            return Result.Failure("You are not allowed to manage this group.");
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure("Group not found.");
        }

        group.Restore();
        if (group.Status is GroupStatus.Archived or GroupStatus.Deleted)
        {
            group.Status = GroupStatus.Active;
        }

        await AuditAsync(userId, "GroupRestored", group.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<GroupMemberResponse>>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanViewGroup(userId, groupId, cancellationToken))
        {
            return Result<IReadOnlyList<GroupMemberResponse>>.Failure("Group not found.");
        }

        var members = await groups.ListMembersAsync(groupId, cancellationToken);
        return Result<IReadOnlyList<GroupMemberResponse>>.Success(members.Select(ToMember).ToList());
    }

    public async Task<Result<GroupMemberResponse>> AddMemberAsync(Guid groupId, AddGroupMemberRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) || !await authorization.CanManageGroup(actorUserId, groupId, cancellationToken))
        {
            return Result<GroupMemberResponse>.Failure("You are not allowed to manage group members.");
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (group is null || user is null)
        {
            return Result<GroupMemberResponse>.Failure("Group or user not found.");
        }

        if (await workspaces.GetMemberAsync(group.WorkspaceId, request.UserId, cancellationToken) is not { Status: MembershipStatus.Active })
        {
            return Result<GroupMemberResponse>.Failure("User must belong to the workspace before joining the group.");
        }

        if (await groups.GetMemberAsync(groupId, request.UserId, cancellationToken) is not null)
        {
            return Result<GroupMemberResponse>.Failure("User is already a group member.");
        }

        var member = new GroupMember
        {
            GroupId = groupId,
            UserId = request.UserId,
            User = user,
            Role = request.Role,
            JoinedAt = clock.UtcNow
        };

        await groups.AddMemberAsync(member, cancellationToken);
        await AuditAsync(actorUserId, "GroupMemberAdded", groupId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<GroupMemberResponse>.Success(ToMember(member));
    }

    public async Task<Result<GroupMemberResponse>> UpdateMemberAsync(Guid groupId, Guid userId, UpdateGroupMemberRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) || !await authorization.CanManageGroup(actorUserId, groupId, cancellationToken))
        {
            return Result<GroupMemberResponse>.Failure("You are not allowed to manage group members.");
        }

        var member = await groups.GetMemberAsync(groupId, userId, cancellationToken);
        if (member is null)
        {
            return Result<GroupMemberResponse>.Failure("Group member not found.");
        }

        member.Role = request.Role;
        await AuditAsync(actorUserId, "GroupMemberRoleChanged", groupId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<GroupMemberResponse>.Success(ToMember(member));
    }

    public async Task<Result> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) || !await authorization.CanManageGroup(actorUserId, groupId, cancellationToken))
        {
            return Result.Failure("You are not allowed to manage group members.");
        }

        var member = await groups.GetMemberAsync(groupId, userId, cancellationToken);
        if (member is null)
        {
            return Result.Failure("Group member not found.");
        }

        member.Role = GroupRole.ReadOnly;
        await AuditAsync(actorUserId, "GroupMemberRemoved", groupId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    private Task AuditAsync(Guid actorUserId, string action, Guid targetId, CancellationToken cancellationToken)
    {
        return auditLogger.LogAsync(new AuditLogEntry(actorUserId, action, "Group", targetId, SummaryFor(action)), cancellationToken);
    }

    private static string SummaryFor(string action) => action switch
    {
        "GroupCreated" => "Group created.",
        "GroupMemberRoleChanged" => "Group permission changed.",
        _ => $"{action} completed."
    };

    private static GroupListItemResponse ToListItem(Group group)
    {
        return new GroupListItemResponse(group.Id, group.WorkspaceId, group.ParentGroupId, group.Name, group.Description, group.GroupType, group.Status, group.CreatedAt, group.UpdatedAt);
    }

    private static GroupDetailResponse ToDetail(Group group)
    {
        return new GroupDetailResponse(group.Id, group.WorkspaceId, group.ParentGroupId, group.Name, group.Description, group.GroupType, group.Status, group.CreatedByUserId, group.CreatedAt, group.UpdatedAt);
    }

    private static GroupMemberResponse ToMember(GroupMember member)
    {
        return new GroupMemberResponse(member.UserId, member.User?.DisplayName ?? string.Empty, member.User?.Email ?? string.Empty, member.Role, member.JoinedAt);
    }
}
