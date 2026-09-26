using System.Text.Json.Serialization;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Workspaces;

public sealed record WorkspaceListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    WorkspaceStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceDashboardAccessSource
{
    WorkspaceMembership = 0,
    SystemAdmin = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceNeedsAttentionKind
{
    ReviewRequired = 0,
    ResearchFailed = 1
}

public sealed record WorkspaceNeedsAttentionItemResponse(
    Guid Id,
    WorkspaceNeedsAttentionKind Kind,
    string TargetRoute,
    DateTimeOffset OccurredAt);

public sealed record WorkspaceDashboardListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    WorkspaceStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    WorkspaceRole? CurrentUserRole,
    WorkspaceDashboardAccessSource AccessSource,
    bool CanOpenWorkspace,
    bool CanOpenMembers,
    bool CanOpenProjects,
    bool CanCreateProject,
    bool CanAddFiles,
    int UnreadAnnouncementCount,
    int UnreadConversationCount,
    int InProgressProjectCount,
    int RunningProjectCount,
    int NeedsReviewProjectCount,
    bool CanOpenProjectCreate = false,
    bool HasExternalShares = false,
    int? ExternalShareCount = null,
    bool CanInspectSharing = false,
    bool CanManageSharing = false,
    IReadOnlyList<WorkspaceMemberPreviewResponse>? MemberPreview = null,
    int NeedsAttentionCount = 0,
    IReadOnlyList<WorkspaceNeedsAttentionItemResponse>? NeedsAttentionItems = null);

public sealed record WorkspaceMemberPreviewResponse(Guid UserId, string DisplayName);

public sealed record WorkspaceDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    WorkspaceStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateWorkspaceRequest(string Name, string? Description, string? Icon);

public sealed record WorkspaceCapabilitiesResponse(bool CanCreate);

public sealed record UpdateWorkspaceRequest(string? Name, string? Description, string? Icon, WorkspaceStatus? Status);

/// <summary>
/// Ordinary Workspace member projection. This contract is intentionally data-minimized:
/// it contains only identity needed for display and the current Workspace relationship.
/// Only current active memberships are projected by the application service, so Status
/// preserves the existing relationship-state contract without exposing revoked rows.
/// </summary>
public sealed record WorkspaceMemberResponse(
    Guid UserId,
    string DisplayName,
    WorkspaceRole Role,
    MembershipStatus Status)
{
    /// <summary>
    /// Compatibility constructor for existing mutation code. Email and JoinedAt are
    /// deliberately discarded so an ordinary response can never serialize them.
    /// </summary>
    public WorkspaceMemberResponse(
        Guid userId,
        string displayName,
        string email,
        WorkspaceRole role,
        MembershipStatus status,
        DateTimeOffset? joinedAt)
        : this(userId, displayName, role, status)
    {
        _ = email;
        _ = joinedAt;
    }
}

/// <summary>
/// Membership-management projection. Fields that are unnecessary for ordinary member
/// surfaces live here and may be returned only after explicit Workspace management
/// authorization.
/// </summary>
public sealed record WorkspaceMemberManagementResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    WorkspaceRole Role,
    MembershipStatus Status,
    UserStatus AccountStatus,
    DateTimeOffset? JoinedAt);

public sealed record AddWorkspaceMemberRequest(Guid UserId, WorkspaceRole Role);

public sealed record UpdateWorkspaceMemberRequest(WorkspaceRole Role, MembershipStatus? Status);
