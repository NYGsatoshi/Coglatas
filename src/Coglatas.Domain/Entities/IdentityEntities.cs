using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class User : SoftDeletableEntity
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public SystemRole SystemRole { get; set; } = SystemRole.User;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockoutEndAt { get; set; }
}

public sealed class Session : AuditableEntity
{
    public Guid UserId { get; set; }
    public string SessionKeyHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    public User? User { get; set; }
}

public sealed class Invite : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Workspace-scoped role granted by this invite. It must never be translated into TenantUserRole authority.
    /// </summary>
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid InvitedByUserId { get; set; }

    public Workspace? Workspace { get; set; }
    public User? InvitedByUser { get; set; }
}