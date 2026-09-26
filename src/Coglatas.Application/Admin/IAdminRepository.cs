using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Admin;

public interface IAdminRepository
{
    Task<PagedResponse<AdminUserListItemResponse>> ListUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<User?> GetUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default);

    Task<int> CountSystemAdminsExcludingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListActiveSystemAdminIdsAsync(CancellationToken cancellationToken = default);

    Task<PagedResponse<AdminInviteResponse>> ListInvitesAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task AddInviteAsync(Invite invite, CancellationToken cancellationToken = default);

    Task<Invite?> GetInviteAsync(Guid inviteId, CancellationToken cancellationToken = default);

    Task<Workspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<Group?> GetGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<Project?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Channel?> GetChannelAsync(Guid channelId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(CancellationToken cancellationToken = default);

    Task<SystemSetting?> GetSettingAsync(string key, CancellationToken cancellationToken = default);

    Task AddSettingAsync(SystemSetting setting, CancellationToken cancellationToken = default);

    Task<AdminDashboardSnapshot> GetDashboardSnapshotAsync(int recentCount, DateOnly today, CancellationToken cancellationToken = default);
}
