using Coglatas.Domain.Enums;

namespace Coglatas.Application.Groups;

public sealed record GroupListItemResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid? ParentGroupId,
    string Name,
    string? Description,
    GroupType GroupType,
    GroupStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record GroupDetailResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid? ParentGroupId,
    string Name,
    string? Description,
    GroupType GroupType,
    GroupStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateGroupRequest(
    Guid? ParentGroupId,
    string Name,
    string? Description,
    GroupType GroupType);

public sealed record UpdateGroupRequest(
    Guid? ParentGroupId,
    string? Name,
    string? Description,
    GroupType? GroupType,
    GroupStatus? Status);

public sealed record GroupMemberResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    GroupRole Role,
    DateTimeOffset JoinedAt);

public sealed record AddGroupMemberRequest(Guid UserId, GroupRole Role);

public sealed record UpdateGroupMemberRequest(GroupRole Role);
