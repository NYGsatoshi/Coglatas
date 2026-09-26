using System.Net.Mail;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Auth;

public sealed class AuthService(
    IUserRepository users,
    IInviteRepository invites,
    ITenantRepository tenants,
    IWorkspaceRepository workspaces,
    ISessionRepository sessions,
    IUserSessionService userSessions,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    IAuditLogger auditLogger,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthSecurityOptions securityOptions) : IAuthService
{
    private const int MinimumPasswordLength = 8;
    private const string GenericLoginError = "Invalid email or password.";
    private const string GenericInviteError = "Invite is invalid or expired.";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            await auditLogger.LogSecurityAsync("LoginFailure", "Invalid login attempt.", LoginSecurityMetadata(null, request.Email), SecurityEventSeverity.Warning, cancellationToken);
            await LogAndSaveAsync(null, "LoginFailure", "User", null, "Invalid login attempt.", cancellationToken);
            return Result<LoginResponse>.Failure(GenericLoginError);
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await users.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is not null && CanUserLogin(user))
        {
            NormalizeExpiredLockout(user);
            if (IsLockedOut(user))
            {
                await auditLogger.LogSecurityAsync(
                    "LoginLockout",
                    "Login attempt rejected because the account is locked.",
                    LoginSecurityMetadata(user, request.Email),
                    SecurityEventSeverity.Warning,
                    cancellationToken);
                await LogAndSaveAsync(user.Id, "LoginLockout", "User", user.Id, "Login attempt rejected because the account is locked.", cancellationToken);
                return Result<LoginResponse>.Failure(GenericLoginError);
            }
        }

        if (user is null || !CanUserLogin(user))
        {
            await auditLogger.LogSecurityAsync("LoginFailure", "Invalid login attempt.", LoginSecurityMetadata(user, request.Email), SecurityEventSeverity.Warning, cancellationToken);
            await LogAndSaveAsync(user?.Id, "LoginFailure", "User", user?.Id, "Invalid login attempt.", cancellationToken);
            return Result<LoginResponse>.Failure(GenericLoginError);
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
        {
            ApplyFailedLogin(user);
            await auditLogger.LogSecurityAsync("LoginFailure", "Invalid login attempt.", LoginSecurityMetadata(user, request.Email), SecurityEventSeverity.Warning, cancellationToken);
            if (IsLockedOut(user))
            {
                await auditLogger.LogSecurityAsync(
                    "LoginLockout",
                    "Account locked after repeated failed login attempts.",
                    LoginSecurityMetadata(user, request.Email),
                    SecurityEventSeverity.Warning,
                    cancellationToken);
                await auditLogger.LogAsync(new AuditLogEntry(user.Id, "LoginLockout", "User", user.Id, "Account locked after repeated failed login attempts."), cancellationToken);
            }

            await LogAndSaveAsync(user.Id, "LoginFailure", "User", user.Id, "Invalid login attempt.", cancellationToken);
            return Result<LoginResponse>.Failure(GenericLoginError);
        }

        ResetFailedLoginState(user);
        user.LastLoginAt = clock.UtcNow;
        var session = CreateSession(user.Id);
        await sessions.AddAsync(session, cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(user.Id, "LoginSuccess", "User", user.Id, "User logged in."), cancellationToken);
        await auditLogger.LogSecurityAsync("LoginSuccess", "User logged in.", LoginSecurityMetadata(user, request.Email), cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(await ToLoginResponseAsync(user, session, cancellationToken));
    }

    public async Task<Result<LoginResponse>> RegisterByInviteAsync(RegisterByInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result<LoginResponse>.Failure("Display name is required.");
        }

        if (!IsValidEmail(request.Email))
        {
            return Result<LoginResponse>.Failure("A valid email address is required.");
        }

        if (!IsValidPassword(request.Password))
        {
            return Result<LoginResponse>.Failure($"Password must be at least {MinimumPasswordLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.InviteToken))
        {
            return Result<LoginResponse>.Failure("Invite token is required.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        return await unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            var inviteResult = await GetUsableInviteAsync(request.InviteToken, forUpdate: true, transactionToken);
            if (!inviteResult.IsSuccess || inviteResult.Value is null)
            {
                return await RejectInviteAcceptanceAsync(
                    inviteResult.Error ?? GenericInviteError,
                    InviteFailureReason(inviteResult.Error),
                    null,
                    transactionToken);
            }

            var invite = inviteResult.Value;
            if (!string.Equals(invite.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            {
                return await RejectInviteAcceptanceAsync(GenericInviteError, "EmailMismatch", invite, transactionToken);
            }

            return await AcceptResolvedInviteAsync(invite, request.DisplayName, request.Password, transactionToken);
        }, cancellationToken);
    }

    public async Task<Result<InviteValidationResponse>> ValidateInviteAsync(string token, CancellationToken cancellationToken = default)
    {
        var inviteResult = await GetUsableInviteAsync(token, forUpdate: false, cancellationToken);
        if (!inviteResult.IsSuccess || inviteResult.Value is null)
        {
            return Result<InviteValidationResponse>.Failure(inviteResult.Error ?? "Invite is invalid.");
        }

        var scopeResult = await GetInviteScopeAsync(inviteResult.Value, cancellationToken);
        if (!scopeResult.IsSuccess || scopeResult.Value is null)
        {
            return Result<InviteValidationResponse>.Failure(GenericInviteError);
        }

        var tenant = scopeResult.Value.Tenant;
        var workspace = scopeResult.Value.Workspace;
        var tenantName = !string.IsNullOrWhiteSpace(tenant.DisplayName)
            ? tenant.DisplayName
            : !string.IsNullOrWhiteSpace(tenant.Name)
                ? tenant.Name
                : "Coglatas Portal";
        var workspaceName = !string.IsNullOrWhiteSpace(workspace.Name)
            ? workspace.Name
            : "Default Workspace";

        return Result<InviteValidationResponse>.Success(new InviteValidationResponse(
            true,
            inviteResult.Value.Email,
            inviteResult.Value.Role.ToString(),
            tenantName,
            workspaceName,
            inviteResult.Value.ExpiresAt));
    }

    public async Task<Result<LoginResponse>> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result<LoginResponse>.Failure("Display name is required.");
        }

        if (!IsValidPassword(request.Password))
        {
            return Result<LoginResponse>.Failure($"Password must be at least {MinimumPasswordLength} characters.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            var inviteResult = await GetUsableInviteAsync(request.Token, forUpdate: true, transactionToken);
            if (!inviteResult.IsSuccess || inviteResult.Value is null)
            {
                return await RejectInviteAcceptanceAsync(
                    inviteResult.Error ?? GenericInviteError,
                    InviteFailureReason(inviteResult.Error),
                    null,
                    transactionToken);
            }

            return await AcceptResolvedInviteAsync(inviteResult.Value, request.DisplayName, request.Password, transactionToken);
        }, cancellationToken);
    }

    private async Task<Result<LoginResponse>> AcceptResolvedInviteAsync(
        Invite invite,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var scopeResult = await GetInviteScopeAsync(invite, cancellationToken);
        if (!scopeResult.IsSuccess)
        {
            return await RejectInviteAcceptanceAsync(GenericInviteError, "ScopeInvalid", invite, cancellationToken);
        }

        var existingUser = await users.GetByNormalizedEmailAsync(invite.NormalizedEmail, cancellationToken);
        if (existingUser is not null && !CanUserLogin(existingUser))
        {
            return await RejectInviteAcceptanceAsync(
                "Invite cannot be accepted for this account.",
                "AccountUnavailable",
                invite,
                cancellationToken);
        }

        if (existingUser is not null)
        {
            await unitOfWork.LockInviteAcceptanceMembershipsAsync(
                invite.TenantId,
                invite.WorkspaceId,
                existingUser.Id,
                cancellationToken);

            var existingTenantMembership = await tenants.GetTenantUserAsync(invite.TenantId, existingUser.Id, cancellationToken);
            if (existingTenantMembership is not null &&
                existingTenantMembership.Status is TenantUserStatus.Suspended or TenantUserStatus.Left or TenantUserStatus.Archived)
            {
                return await RejectInviteAcceptanceAsync(GenericInviteError, "TenantMembershipUnavailable", invite, cancellationToken);
            }

            var existingWorkspaceMembership = await workspaces.GetMemberAsync(invite.WorkspaceId, existingUser.Id, cancellationToken);
            if (existingWorkspaceMembership is not null && existingWorkspaceMembership.TenantId != invite.TenantId)
            {
                return await RejectInviteAcceptanceAsync(GenericInviteError, "MembershipScopeMismatch", invite, cancellationToken);
            }

            if (existingWorkspaceMembership is { Status: MembershipStatus.Suspended })
            {
                return await RejectInviteAcceptanceAsync(GenericInviteError, "WorkspaceMembershipUnavailable", invite, cancellationToken);
            }
        }

        return await AcceptInviteCoreAsync(invite, existingUser, displayName, password, cancellationToken);
    }

    private async Task<Result<LoginResponse>> AcceptInviteCoreAsync(
        Invite invite,
        User? existingUser,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        User user;
        if (existingUser is null)
        {
            user = new User
            {
                DisplayName = displayName.Trim(),
                Email = invite.Email.Trim(),
                NormalizedEmail = invite.NormalizedEmail,
                PasswordHash = passwordHasher.HashPassword(password),
                SystemRole = SystemRole.User,
                Status = UserStatus.Active
            };
            await users.AddAsync(user, cancellationToken);
        }
        else
        {
            user = existingUser;
        }

        var now = clock.UtcNow;
        await EnsureTenantMembershipAsync(invite, user.Id, now, cancellationToken);
        await EnsureWorkspaceMembershipAsync(invite, user.Id, now, cancellationToken);
        invite.AcceptedAt = now;

        var session = CreateSession(user.Id);
        await sessions.AddAsync(session, cancellationToken);
        var acceptedMetadata = new Dictionary<string, object?>
        {
            ["userId"] = user.Id,
            ["inviteId"] = invite.Id,
            ["tenantId"] = invite.TenantId,
            ["workspaceId"] = invite.WorkspaceId
        };
        await auditLogger.LogAsync(
            new AuditLogEntry(
                user.Id,
                "InviteAccepted",
                "Invite",
                invite.Id,
                "Invite accepted.",
                WorkspaceId: invite.WorkspaceId,
                Metadata: acceptedMetadata,
                TenantId: invite.TenantId),
            cancellationToken);
        await auditLogger.LogSecurityAsync("InviteAccepted", "Invite accepted.", acceptedMetadata, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(await ToLoginResponseAsync(user, session, cancellationToken));
    }

    private async Task<Result<Invite>> GetUsableInviteAsync(
        string token,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result<Invite>.Failure("Invite token is required.");
        }

        var tokenHash = tokenHasher.HashToken(token);
        var invite = forUpdate
            ? await invites.GetByTokenHashForUpdateAsync(tokenHash, cancellationToken)
            : await invites.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (invite is null)
        {
            return Result<Invite>.Failure("Invite is invalid.");
        }

        if (invite.AcceptedAt.HasValue)
        {
            return Result<Invite>.Failure("Invite has already been used.");
        }

        if (invite.RevokedAt.HasValue)
        {
            return Result<Invite>.Failure("Invite was revoked.");
        }

        if (invite.ExpiresAt <= clock.UtcNow)
        {
            return Result<Invite>.Failure("Invite has expired.");
        }

        return Result<Invite>.Success(invite);
    }

    private async Task<Result<InviteScope>> GetInviteScopeAsync(Invite invite, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetTenantAsync(invite.TenantId, cancellationToken);
        if (tenant is null || tenant.DeletedAt.HasValue || tenant.Status != TenantStatus.Active)
        {
            return Result<InviteScope>.Failure(GenericInviteError);
        }

        var workspace = await workspaces.GetByIdAsync(invite.WorkspaceId, cancellationToken);
        if (workspace is null ||
            workspace.DeletedAt.HasValue ||
            workspace.Status != WorkspaceStatus.Active ||
            workspace.TenantId != invite.TenantId)
        {
            return Result<InviteScope>.Failure(GenericInviteError);
        }

        return Result<InviteScope>.Success(new InviteScope(tenant, workspace));
    }

    private async Task<Result<LoginResponse>> RejectInviteAcceptanceAsync(
        string clientError,
        string reasonCode,
        Invite? invite,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["inviteId"] = invite?.Id,
            ["tenantId"] = invite?.TenantId,
            ["workspaceId"] = invite?.WorkspaceId,
            ["reason"] = reasonCode
        };
        await auditLogger.LogAsync(
            new AuditLogEntry(
                null,
                "InviteAcceptanceDenied",
                "Invite",
                invite?.Id,
                "Invite acceptance denied.",
                Metadata: metadata,
                TenantId: invite?.TenantId),
            cancellationToken);
        await auditLogger.LogSecurityAsync(
            "InviteAcceptanceDenied",
            "Invite acceptance denied.",
            metadata,
            SecurityEventSeverity.Warning,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<LoginResponse>.Failure(clientError);
    }

    private static string InviteFailureReason(string? error)
    {
        return error switch
        {
            "Invite token is required." => "TokenMissing",
            "Invite has already been used." => "AlreadyUsed",
            "Invite was revoked." => "Revoked",
            "Invite has expired." => "Expired",
            _ => "Invalid"
        };
    }

    private async Task EnsureTenantMembershipAsync(Invite invite, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var membership = await tenants.GetTenantUserAsync(invite.TenantId, userId, cancellationToken);
        if (membership is null)
        {
            // Invite.Role is workspace-scoped. Creating the Tenant presence row must
            // not turn WorkspaceAdmin/Owner into TenantAdmin.
            await tenants.AddTenantUserAsync(new TenantUser
            {
                TenantId = invite.TenantId,
                UserId = userId,
                Role = TenantUserRole.Member,
                Status = TenantUserStatus.Active,
                JoinedAt = now,
                InvitedByUserId = invite.InvitedByUserId
            }, cancellationToken);
            return;
        }

        // Invite acceptance may promote only the pre-membership Invited state.
        // Suspended/Left/Archived are administrative lifecycle boundaries and are
        // rejected before this mutation path is entered.
        if (membership.Status == TenantUserStatus.Invited)
        {
            membership.Status = TenantUserStatus.Active;
        }

        if (membership.JoinedAt == default)
        {
            membership.JoinedAt = now;
        }

        membership.InvitedByUserId ??= invite.InvitedByUserId;
    }

    private async Task EnsureWorkspaceMembershipAsync(Invite invite, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var membership = await workspaces.GetMemberAsync(invite.WorkspaceId, userId, cancellationToken);
        if (membership is null)
        {
            await workspaces.AddMemberAsync(new WorkspaceMember
            {
                TenantId = invite.TenantId,
                WorkspaceId = invite.WorkspaceId,
                UserId = userId,
                Role = invite.Role,
                Status = MembershipStatus.Active,
                JoinedAt = now
            }, cancellationToken);
            return;
        }

        membership.Role = invite.Role;
        if (membership.Status == MembershipStatus.Pending)
        {
            membership.Status = MembershipStatus.Active;
        }
        membership.JoinedAt ??= now;
    }

    public async Task<Result> LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result.Success();
        }

        if (currentUser.SessionId.HasValue)
        {
            await userSessions.RevokeSessionAsync(currentUser.SessionId.Value, currentUser.UserId, "Logout", cancellationToken);
        }

        await auditLogger.LogAsync(new AuditLogEntry(currentUser.UserId, "Logout", "User", currentUser.UserId, "User logged out."), cancellationToken);
        await auditLogger.LogSecurityAsync("Logout", "User logged out.", cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure("Authentication is required.");
        }

        if (!IsValidPassword(request.NewPassword))
        {
            return Result.Failure($"Password must be at least {MinimumPasswordLength} characters.");
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken);
        if (user is null || user.DeletedAt.HasValue || user.Status != UserStatus.Active)
        {
            return Result.Failure("Authentication is required.");
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, request.CurrentPassword))
        {
            return Result.Failure("Current password is incorrect.");
        }

        user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        await auditLogger.LogAsync(new AuditLogEntry(user.Id, "PasswordChanged", "User", user.Id, "Password changed."), cancellationToken);
        await auditLogger.LogSecurityAsync("PasswordChanged", "Password changed.", new Dictionary<string, object?> { ["userId"] = user.Id }, cancellationToken: cancellationToken);
        await userSessions.RevokeUserSessionsAsync(user.Id, user.Id, "PasswordChanged", currentUser.SessionId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result<CurrentUserResponse>.Failure("Authentication is required.");
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken);
        if (user is null || user.DeletedAt.HasValue || user.Status != UserStatus.Active)
        {
            return Result<CurrentUserResponse>.Failure("Authentication is required.");
        }

        var workspaceContext = await BuildWorkspaceContextAsync(user, cancellationToken);

        return Result<CurrentUserResponse>.Success(new CurrentUserResponse(
            user.Id,
            user.DisplayName,
            user.Email,
            user.SystemRole,
            user.Status,
            BuildCapabilities(user.SystemRole),
            workspaceContext.CurrentWorkspace,
            workspaceContext.Workspaces));
    }

    private Session CreateSession(Guid userId)
    {
        return new Session
        {
            UserId = userId,
            SessionKeyHash = tokenHasher.HashToken(Guid.NewGuid().ToString("N")),
            ExpiresAt = clock.UtcNow.Add(SessionLifetime),
            LastSeenAt = clock.UtcNow
        };
    }

    private async Task<LoginResponse> ToLoginResponseAsync(User user, Session session, CancellationToken cancellationToken)
    {
        var workspaceContext = await BuildWorkspaceContextAsync(user, cancellationToken);

        return new LoginResponse(
            user.Id,
            session.Id,
            user.DisplayName,
            user.Email,
            user.SystemRole,
            session.ExpiresAt,
            BuildCapabilities(user.SystemRole),
            workspaceContext.CurrentWorkspace,
            workspaceContext.Workspaces);
    }

    private async Task<WorkspaceContext> BuildWorkspaceContextAsync(User user, CancellationToken cancellationToken)
    {
        var includeAll = user.SystemRole == SystemRole.SystemAdmin;
        var userWorkspaces = await workspaces.ListForUserAsync(user.Id, includeAll, cancellationToken);
        var activeWorkspaces = userWorkspaces
            .Where(workspace => !workspace.DeletedAt.HasValue && workspace.Status is not WorkspaceStatus.Archived and not WorkspaceStatus.Deleted)
            .OrderBy(workspace => workspace.CreatedAt)
            .Select(workspace => new AuthWorkspaceSummary(workspace.Id, workspace.Name, workspace.Description, workspace.Status))
            .ToList();

        return new WorkspaceContext(
            activeWorkspaces.Count == 1 ? activeWorkspaces[0] : null,
            activeWorkspaces);
    }

    private sealed record InviteScope(Tenant Tenant, Workspace Workspace);

    private sealed record WorkspaceContext(
        AuthWorkspaceSummary? CurrentWorkspace,
        IReadOnlyList<AuthWorkspaceSummary> Workspaces);

    private static IReadOnlyList<string> BuildCapabilities(SystemRole role)
    {
        var capabilities = new List<string>
        {
            "workspace:view",
            "announcements:view",
            "projects:view",
            "files:view",
            "account:view"
        };

        if (role is SystemRole.Admin or SystemRole.PlatformAdmin or SystemRole.SystemAdmin)
        {
            capabilities.Add("audit:view");
        }

        if (role is SystemRole.PlatformAdmin or SystemRole.SystemAdmin)
        {
            capabilities.Add("admin:access");
            capabilities.Add("invite:read");
            capabilities.Add("invite:create");
        }

        return capabilities;
    }

    private bool IsLockedOut(User user)
    {
        return securityOptions.LoginLockoutEnabled &&
               user.LockoutEndAt.HasValue &&
               user.LockoutEndAt.Value > clock.UtcNow;
    }

    private static bool CanUserLogin(User user)
    {
        return !user.DeletedAt.HasValue && user.Status == UserStatus.Active;
    }

    private void NormalizeExpiredLockout(User user)
    {
        if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value <= clock.UtcNow)
        {
            user.LockoutEndAt = null;
            user.FailedLoginAttempts = 0;
        }
    }

    private void ApplyFailedLogin(User user)
    {
        if (!securityOptions.LoginLockoutEnabled)
        {
            return;
        }

        var maxAttempts = Math.Max(1, securityOptions.MaxFailedLoginAttempts);
        var durationMinutes = Math.Max(1, securityOptions.LoginLockoutDurationMinutes);
        user.FailedLoginAttempts++;
        if (user.FailedLoginAttempts >= maxAttempts)
        {
            user.LockoutEndAt = clock.UtcNow.AddMinutes(durationMinutes);
        }
    }

    private static void ResetFailedLoginState(User user)
    {
        user.FailedLoginAttempts = 0;
        user.LockoutEndAt = null;
    }

    private async Task LogAndSaveAsync(
        Guid? actorUserId,
        string action,
        string targetType,
        Guid? targetId,
        string? summary,
        CancellationToken cancellationToken)
    {
        await auditLogger.LogAsync(new AuditLogEntry(actorUserId, action, targetType, targetId, summary), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static bool IsValidPassword(string password)
    {
        return !string.IsNullOrWhiteSpace(password) && password.Length >= MinimumPasswordLength;
    }

    private static IReadOnlyDictionary<string, object?> LoginSecurityMetadata(User? user, string? submittedEmail)
    {
        return new Dictionary<string, object?>
        {
            ["emailProvided"] = !string.IsNullOrWhiteSpace(submittedEmail),
            ["userId"] = user?.Id
        };
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
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

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }
}
