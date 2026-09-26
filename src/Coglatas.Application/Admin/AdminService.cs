using System.Net.Mail;
using System.Security.Cryptography;
using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Notifications;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Admin;

public sealed class AdminService(
    IAdminRepository adminRepository,
    ITokenHasher tokenHasher,
    IAuditLogger auditLogger,
    ICurrentUser currentUser,
    IClock clock,
    IUserSessionService userSessions,
    IUnitOfWork unitOfWork,
    ICurrentTenant? currentTenant = null,
    IAuthorizationStateChangePublisher? authorizationChanges = null,
    ITaskDeadlineDigestRepository? taskDeadlineDigests = null,
    TaskDeadlineDigestDiagnostics? taskDeadlineDigestDiagnostics = null,
    IWorkspaceRepository? workspaces = null,
    IProjectRepository? projectReaders = null) : IAdminService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const string MaskedSensitiveValue = "********";

    public async Task<Result> RestartTaskDeadlineDigestAsync(
        Guid jobId,
        RestartTaskDeadlineDigestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty ||
            request is null ||
            string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 500)
        {
            return Result.Failure("A bounded digest restart reason is required.");
        }

        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        if (currentTenant is null || !currentTenant.IsAvailable || taskDeadlineDigests is null)
        {
            return Result.Failure("Task deadline digest restart is unavailable.");
        }

        var outcome = await taskDeadlineDigests.RestartFailedAsync(
            jobId,
            currentUser.UserId!.Value,
            request.Reason.Trim(),
            clock.UtcNow,
            cancellationToken);
        if (outcome == TaskDeadlineDigestRestartOutcome.Restarted)
        {
            taskDeadlineDigestDiagnostics?.RecordOperatorRestart();
            return Result.Success();
        }

        return outcome switch
        {
            TaskDeadlineDigestRestartOutcome.NotFound =>
                Result.Failure("Task deadline digest job not found."),
            TaskDeadlineDigestRestartOutcome.NotFailed =>
                Result.Failure("Only a failed Task deadline digest can be restarted."),
            _ => Result.Failure("The Task deadline digest already has an active attempt.")
        };
    }

    public async Task<Result<PagedResponse<AdminUserListItemResponse>>> ListUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<PagedResponse<AdminUserListItemResponse>>.Failure("SystemAdmin access is required.");
        }

        return Result<PagedResponse<AdminUserListItemResponse>>.Success(await adminRepository.ListUsersAsync(NormalizePage(page), NormalizePageSize(pageSize), cancellationToken));
    }

    public async Task<Result<AdminUserDetailResponse>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<AdminUserDetailResponse>.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        return user is null
            ? Result<AdminUserDetailResponse>.Failure("User not found.")
            : Result<AdminUserDetailResponse>.Success(ToUserDetail(user));
    }

    public async Task<Result<AdminUserDetailResponse>> UpdateUserAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<AdminUserDetailResponse>.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<AdminUserDetailResponse>.Failure("User not found.");
        }

        if (request.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 120)
            {
                return Result<AdminUserDetailResponse>.Failure("Display name is required and must be 120 characters or fewer.");
            }

            user.DisplayName = request.DisplayName.Trim();
        }

        if (request.Email is not null)
        {
            if (!IsValidEmail(request.Email))
            {
                return Result<AdminUserDetailResponse>.Failure("A valid email address is required.");
            }

            var normalizedEmail = NormalizeEmail(request.Email);
            var existing = await adminRepository.GetUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);
            if (existing is not null && existing.Id != user.Id)
            {
                return Result<AdminUserDetailResponse>.Failure("A user with this email already exists.");
            }

            user.Email = request.Email.Trim();
            user.NormalizedEmail = normalizedEmail;
        }

        var shouldRevokeSessions = false;
        if (request.Status.HasValue)
        {
            user.Status = request.Status.Value;
            shouldRevokeSessions = user.Status != UserStatus.Active;
        }

        await AuditAsync("AdminUserUpdated", "User", user.Id, "Admin updated user profile.", cancellationToken);
        if (shouldRevokeSessions)
        {
            await userSessions.RevokeUserSessionsAsync(user.Id, currentUser.UserId, "AdminUserStatusChanged", cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AdminUserDetailResponse>.Success(ToUserDetail(user));
    }

    public Task<Result> SuspendUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return SetUserStatusAsync(userId, UserStatus.Suspended, "UserSuspended", "User suspended.", cancellationToken);
    }

    public async Task<Result> ActivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        if (user is null || user.DeletedAt.HasValue)
        {
            return Result.Failure("User not found.");
        }

        user.Status = UserStatus.Active;
        await AuditAsync("UserActivated", "User", user.Id, "User activated.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordInviteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        if (user is null || user.DeletedAt.HasValue)
        {
            return Result.Failure("User not found.");
        }

        // TODO: Add a dedicated password reset token/email flow when outbound mail support exists.
        await AuditAsync("PasswordResetInviteRequested", "User", user.Id, "Password reset invite requested; reset-token delivery is not implemented yet.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ArchiveUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        if (user.SystemRole == SystemRole.SystemAdmin && await adminRepository.CountSystemAdminsExcludingAsync(user.Id, cancellationToken) == 0)
        {
            return Result.Failure("Cannot archive the last SystemAdmin.");
        }

        user.Status = UserStatus.Archived;
        if (!user.DeletedAt.HasValue)
        {
            user.MarkDeleted(clock.UtcNow);
        }

        await AuditAsync("UserArchived", "User", user.Id, "User archived.", cancellationToken);
        await userSessions.RevokeUserSessionsAsync(user.Id, currentUser.UserId, "UserArchived", cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AdminUserDetailResponse>> ChangeSystemRoleAsync(Guid userId, ChangeSystemRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<AdminUserDetailResponse>.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        if (user is null || user.DeletedAt.HasValue)
        {
            return Result<AdminUserDetailResponse>.Failure("User not found.");
        }

        var oldRole = user.SystemRole;
        if (oldRole == SystemRole.SystemAdmin &&
            request.SystemRole != SystemRole.SystemAdmin &&
            await adminRepository.CountSystemAdminsExcludingAsync(user.Id, cancellationToken) == 0)
        {
            return Result<AdminUserDetailResponse>.Failure("Cannot demote the last SystemAdmin.");
        }

        user.SystemRole = request.SystemRole;
        await auditLogger.LogAsync(new AuditLogEntry(
            currentUser.UserId,
            "SystemRoleChanged",
            "User",
            user.Id,
            "System role changed.",
            Metadata: new Dictionary<string, object?>
            {
                ["oldRole"] = oldRole.ToString(),
                ["newRole"] = user.SystemRole.ToString()
            }), cancellationToken);
        await userSessions.RevokeUserSessionsAsync(user.Id, currentUser.UserId, "SystemRoleChanged", cancellationToken: cancellationToken);
        await PublishAccountAuthorizationChangeAsync(user.Id, "membershipChanged", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AdminUserDetailResponse>.Success(ToUserDetail(user));
    }

    public async Task<Result<PagedResponse<AdminInviteResponse>>> ListInvitesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<PagedResponse<AdminInviteResponse>>.Failure("SystemAdmin access is required.");
        }

        return Result<PagedResponse<AdminInviteResponse>>.Success(await adminRepository.ListInvitesAsync(NormalizePage(page), NormalizePageSize(pageSize), cancellationToken));
    }

    public async Task<Result<AdminInviteResponse>> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<AdminInviteResponse>.Failure("SystemAdmin access is required.");
        }

        var result = await BuildInviteAsync(request.WorkspaceId, request.Email, request.Role, request.ExpiresAt, cancellationToken);
        if (!result.IsSuccess)
        {
            return Result<AdminInviteResponse>.Failure(result.Error ?? "Invite could not be created.");
        }

        var (invite, rawToken) = result.Value;
        await adminRepository.AddInviteAsync(invite, cancellationToken);
        await AuditAsync("InviteCreated", "Invite", invite.Id, "Invite created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AdminInviteResponse>.Success(ToInviteResponse(invite, rawToken));
    }

    public async Task<Result<IReadOnlyList<AdminInviteResponse>>> BulkCreateInvitesAsync(BulkCreateInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<IReadOnlyList<AdminInviteResponse>>.Failure("SystemAdmin access is required.");
        }

        if (request.Emails.Count == 0 || request.Emails.Count > 500)
        {
            return Result<IReadOnlyList<AdminInviteResponse>>.Failure("Bulk invite requires 1 to 500 email addresses.");
        }

        var responses = new List<AdminInviteResponse>();
        foreach (var email in request.Emails.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var result = await BuildInviteAsync(request.WorkspaceId, email, request.Role, request.ExpiresAt, cancellationToken);
            if (!result.IsSuccess)
            {
                return Result<IReadOnlyList<AdminInviteResponse>>.Failure(result.Error ?? "Invite could not be created.");
            }

            var (invite, rawToken) = result.Value;
            await adminRepository.AddInviteAsync(invite, cancellationToken);
            responses.Add(ToInviteResponse(invite, rawToken));
        }

        await AuditAsync("BulkInviteCreated", "Invite", null, $"{responses.Count} invites created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<AdminInviteResponse>>.Success(responses);
    }

    public async Task<Result> RevokeInviteAsync(Guid inviteId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var invite = await adminRepository.GetInviteAsync(inviteId, cancellationToken);
        if (invite is null)
        {
            return Result.Failure("Invite not found.");
        }

        if (!invite.AcceptedAt.HasValue)
        {
            invite.RevokedAt ??= clock.UtcNow;
        }

        await AuditAsync("InviteRevoked", "Invite", invite.Id, "Invite revoked.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SystemSettingResponse>>> ListSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<IReadOnlyList<SystemSettingResponse>>.Failure("SystemAdmin access is required.");
        }

        var settings = await adminRepository.ListSettingsAsync(cancellationToken);
        return Result<IReadOnlyList<SystemSettingResponse>>.Success(settings.Select(ToSettingResponse).ToList());
    }

    public async Task<Result<SystemSettingResponse>> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<SystemSettingResponse>.Failure("SystemAdmin access is required.");
        }

        var setting = await adminRepository.GetSettingAsync(NormalizeSettingKey(key), cancellationToken);
        return setting is null
            ? Result<SystemSettingResponse>.Failure("Setting not found.")
            : Result<SystemSettingResponse>.Success(ToSettingResponse(setting));
    }

    public async Task<Result<SystemSettingResponse>> UpdateSettingAsync(string key, UpdateSystemSettingRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<SystemSettingResponse>.Failure("SystemAdmin access is required.");
        }

        var normalizedKey = NormalizeSettingKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length > 160)
        {
            return Result<SystemSettingResponse>.Failure("Setting key is required and must be 160 characters or fewer.");
        }

        var setting = await adminRepository.GetSettingAsync(normalizedKey, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting
            {
                Key = normalizedKey,
                Value = request.Value?.Trim() ?? string.Empty,
                ValueType = string.IsNullOrWhiteSpace(request.ValueType) ? "String" : request.ValueType.Trim(),
                Description = request.Description?.Trim(),
                IsSensitive = request.IsSensitive ?? false,
                UpdatedAt = clock.UtcNow,
                UpdatedByUserId = currentUser.UserId
            };
            await adminRepository.AddSettingAsync(setting, cancellationToken);
        }
        else
        {
            setting.Value = request.Value?.Trim() ?? setting.Value;
            setting.ValueType = string.IsNullOrWhiteSpace(request.ValueType) ? setting.ValueType : request.ValueType.Trim();
            setting.Description = request.Description?.Trim() ?? setting.Description;
            setting.IsSensitive = request.IsSensitive ?? setting.IsSensitive;
            setting.UpdatedAt = clock.UtcNow;
            setting.UpdatedByUserId = currentUser.UserId;
        }

        await AuditAsync("SystemSettingChanged", "SystemSetting", setting.Id, $"System setting '{setting.Key}' changed.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<SystemSettingResponse>.Success(ToSettingResponse(setting));
    }

    public async Task<Result> ArchiveWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var workspace = await adminRepository.GetWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
        {
            return Result.Failure("Workspace not found.");
        }

        var affectedMembers = workspaces is null
            ? []
            : (await workspaces.ListMembersAsync(workspaceId, cancellationToken))
                .Where(member => member.Status == MembershipStatus.Active)
                .Select(member => member.UserId)
                .Distinct()
                .ToArray();
        workspace.Status = WorkspaceStatus.Archived;
        workspace.MarkDeleted(clock.UtcNow);
        await AuditAsync("DataArchived", "Workspace", workspace.Id, "Workspace archived.", cancellationToken);
        foreach (var affectedUserId in affectedMembers)
        {
            await PublishWorkspaceAuthorizationChangeAsync(workspace.TenantId, affectedUserId, workspace.Id, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ArchiveGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var group = await adminRepository.GetGroupAsync(groupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure("Group not found.");
        }

        group.Status = GroupStatus.Archived;
        group.MarkDeleted(clock.UtcNow);
        await AuditAsync("DataArchived", "Group", group.Id, "Group archived.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ArchiveProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var project = await adminRepository.GetProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure("Project not found.");
        }

        if (project.DeletedAt.HasValue || project.Status == ProjectStatus.Deleted)
        {
            return Result.Failure(new ApplicationErrorDetail(
                "InvalidStateTransition",
                "The requested Project lifecycle transition is not available.",
                Target: "project"));
        }

        IReadOnlyList<Guid> affectedUsers;
        if (projectReaders is not null)
        {
            affectedUsers = await projectReaders.ListCurrentReaderUserIdsAsync(project.Id, cancellationToken);
        }
        else
        {
            var activeWorkspaceMembers = workspaces is null
                ? []
                : (await workspaces.ListMembersAsync(project.WorkspaceId, cancellationToken))
                    .Where(member => member.Status == MembershipStatus.Active)
                    .Select(member => member.UserId)
                    .ToArray();
            affectedUsers = activeWorkspaceMembers
                .Concat(await adminRepository.ListActiveSystemAdminIdsAsync(cancellationToken))
                .Distinct()
                .ToArray();
        }

        project.Status = ProjectStatus.Archived;
        project.MarkDeleted(clock.UtcNow);
        await AuditAsync("DataArchived", "Project", project.Id, "Project archived.", cancellationToken);
        foreach (var affectedUserId in affectedUsers)
        {
            await PublishProjectAuthorizationChangeAsync(
                project.TenantId,
                affectedUserId,
                project.Id,
                cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ArchiveChannelAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var channel = await adminRepository.GetChannelAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return Result.Failure("Channel not found.");
        }

        channel.Status = ChannelStatus.Archived;
        channel.MarkDeleted(clock.UtcNow);
        await AuditAsync("DataArchived", "Channel", channel.Id, "Channel archived.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AdminDashboardResponse>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result<AdminDashboardResponse>.Failure("SystemAdmin access is required.");
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var snapshot = await adminRepository.GetDashboardSnapshotAsync(10, today, cancellationToken);
        return Result<AdminDashboardResponse>.Success(new AdminDashboardResponse(
            snapshot.UserCount,
            snapshot.ActiveUserCount,
            snapshot.WorkspaceCount,
            snapshot.GroupCount,
            snapshot.ProjectCount,
            snapshot.OpenTaskCount,
            snapshot.OverdueTaskCount,
            snapshot.StorageUsageEstimateBytes,
            snapshot.RecentAuditLogs,
            snapshot.RecentSecurityEvents));
    }

    private async Task<Result<(Invite Invite, string RawToken)>> BuildInviteAsync(Guid workspaceId, string email, WorkspaceRole role, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        if (!IsValidEmail(email))
        {
            return Result<(Invite Invite, string RawToken)>.Failure("A valid email address is required.");
        }

        var workspace = await adminRepository.GetWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
        {
            return Result<(Invite Invite, string RawToken)>.Failure("Workspace not found.");
        }

        if (workspace.TenantId == Guid.Empty)
        {
            return Result<(Invite Invite, string RawToken)>.Failure("Workspace tenant context is missing.");
        }

        var expiration = expiresAt ?? clock.UtcNow.AddDays(7);
        if (expiration <= clock.UtcNow)
        {
            return Result<(Invite Invite, string RawToken)>.Failure("Invite expiration must be in the future.");
        }

        var rawToken = GenerateToken();
        var invite = new Invite
        {
            TenantId = workspace.TenantId,
            WorkspaceId = workspaceId,
            Email = email.Trim(),
            NormalizedEmail = NormalizeEmail(email),
            Role = role,
            TokenHash = tokenHasher.HashToken(rawToken),
            ExpiresAt = expiration,
            InvitedByUserId = currentUser.UserId!.Value
        };

        return Result<(Invite Invite, string RawToken)>.Success((invite, rawToken));
    }

    private async Task<Result> SetUserStatusAsync(Guid userId, UserStatus status, string action, string summary, CancellationToken cancellationToken)
    {
        if (!await IsSystemAdminAsync(cancellationToken))
        {
            return Result.Failure("SystemAdmin access is required.");
        }

        var user = await adminRepository.GetUserAsync(userId, cancellationToken);
        if (user is null || user.DeletedAt.HasValue)
        {
            return Result.Failure("User not found.");
        }

        user.Status = status;
        await AuditAsync(action, "User", user.Id, summary, cancellationToken);
        if (status != UserStatus.Active)
        {
            await userSessions.RevokeUserSessionsAsync(user.Id, currentUser.UserId, action, cancellationToken: cancellationToken);
        }

        await PublishAccountAuthorizationChangeAsync(user.Id, status == UserStatus.Active ? "activated" : "suspended", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<bool> IsSystemAdminAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
        {
            return false;
        }

        var actor = await adminRepository.GetUserAsync(currentUser.UserId.Value, cancellationToken);
        return actor is { Status: UserStatus.Active, SystemRole: SystemRole.SystemAdmin } && !actor.DeletedAt.HasValue;
    }

    private Task AuditAsync(string action, string entityType, Guid? entityId, string? summary, CancellationToken cancellationToken)
    {
        return auditLogger.LogAsync(new AuditLogEntry(currentUser.UserId, action, entityType, entityId, summary), cancellationToken);
    }

    private Task PublishAccountAuthorizationChangeAsync(Guid userId, string change, CancellationToken cancellationToken)
    {
        if (currentTenant is null || !currentTenant.IsAvailable || authorizationChanges is null)
        {
            return Task.CompletedTask;
        }

        return authorizationChanges.PublishAsync(currentTenant.TenantId, userId, "account", null, change, cancellationToken);
    }

    private Task PublishWorkspaceAuthorizationChangeAsync(Guid tenantId, Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || authorizationChanges is null)
        {
            return Task.CompletedTask;
        }

        return authorizationChanges.PublishAsync(tenantId, userId, "workspace", workspaceId, "archived", cancellationToken);
    }

    private Task PublishProjectAuthorizationChangeAsync(Guid tenantId, Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || authorizationChanges is null)
        {
            return Task.CompletedTask;
        }

        return authorizationChanges.PublishAsync(tenantId, userId, "project", projectId, "archived", cancellationToken);
    }

    private static AdminUserDetailResponse ToUserDetail(User user)
    {
        return new AdminUserDetailResponse(user.Id, user.DisplayName, user.Email, user.SystemRole, user.Status, user.LastLoginAt, user.CreatedAt, user.UpdatedAt, user.DeletedAt);
    }

    private static AdminInviteResponse ToInviteResponse(Invite invite, string? rawToken = null)
    {
        return new AdminInviteResponse(invite.Id, invite.WorkspaceId, invite.Email, invite.Role, invite.ExpiresAt, invite.AcceptedAt, invite.RevokedAt, invite.InvitedByUserId, invite.CreatedAt, rawToken);
    }

    private static SystemSettingResponse ToSettingResponse(SystemSetting setting)
    {
        return new SystemSettingResponse(
            setting.Id,
            setting.Key,
            setting.IsSensitive ? MaskedSensitiveValue : setting.Value,
            setting.ValueType,
            setting.Description,
            setting.IsSensitive,
            setting.UpdatedAt,
            setting.UpdatedByUserId);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize < 1)
        {
            return DefaultPageSize;
        }

        return Math.Min(pageSize, MaxPageSize);
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320)
        {
            return false;
        }

        try
        {
            var parsed = new MailAddress(email.Trim());
            return string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static string NormalizeSettingKey(string key) => key.Trim();

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
