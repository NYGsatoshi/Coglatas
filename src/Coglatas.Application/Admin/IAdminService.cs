using Coglatas.Application.Common;

namespace Coglatas.Application.Admin;

public interface IAdminService
{
    Task<Result<PagedResponse<AdminUserListItemResponse>>> ListUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Result<AdminUserDetailResponse>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<AdminUserDetailResponse>> UpdateUserAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> SuspendUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ActivateUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordInviteAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ArchiveUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<AdminUserDetailResponse>> ChangeSystemRoleAsync(Guid userId, ChangeSystemRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<AdminInviteResponse>>> ListInvitesAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Result<AdminInviteResponse>> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AdminInviteResponse>>> BulkCreateInvitesAsync(BulkCreateInviteRequest request, CancellationToken cancellationToken = default);

    Task<Result> RevokeInviteAsync(Guid inviteId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SystemSettingResponse>>> ListSettingsAsync(CancellationToken cancellationToken = default);

    Task<Result<SystemSettingResponse>> GetSettingAsync(string key, CancellationToken cancellationToken = default);

    Task<Result<SystemSettingResponse>> UpdateSettingAsync(string key, UpdateSystemSettingRequest request, CancellationToken cancellationToken = default);

    Task<Result> ArchiveWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<Result> ArchiveGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<Result> ArchiveProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Result> ArchiveChannelAsync(Guid channelId, CancellationToken cancellationToken = default);

    Task<Result> RestartTaskDeadlineDigestAsync(
        Guid jobId,
        RestartTaskDeadlineDigestRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AdminDashboardResponse>> GetDashboardAsync(CancellationToken cancellationToken = default);
}
