using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class Workspace : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    /// <summary>IANA or platform timezone ID. Null inherits the tenant fallback.</summary>
    public string? TimeZone { get; set; }
    /// <summary>Workspace-local digest default; this value intentionally has no timezone.</summary>
    public TimeOnly DefaultTaskDeadlineDigestLocalTime { get; set; } = new(8, 0);
    public long TaskNotificationSettingsVersion { get; set; } = 1;
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;
    public Guid CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
    public ICollection<WorkspaceMember> Members { get; } = new List<WorkspaceMember>();
    public ICollection<Group> Groups { get; } = new List<Group>();
}

public sealed class WorkspaceMember : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public DateTimeOffset? JoinedAt { get; set; }
    /// <summary>Null inherits <see cref="Workspace.DefaultTaskDeadlineDigestLocalTime"/>.</summary>
    public TimeOnly? TaskDeadlineDigestLocalTime { get; set; }
    public long TaskNotificationPreferenceVersion { get; set; } = 1;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}

public sealed class Group : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ParentGroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GroupType GroupType { get; set; } = GroupType.Other;
    public GroupStatus Status { get; set; } = GroupStatus.Active;
    public Guid CreatedByUserId { get; set; }

    public Workspace? Workspace { get; set; }
    public Group? ParentGroup { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<Group> ChildGroups { get; } = new List<Group>();
    public ICollection<GroupMember> Members { get; } = new List<GroupMember>();
}

public sealed class GroupMember : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public DateTimeOffset JoinedAt { get; set; }

    public Group? Group { get; set; }
    public User? User { get; set; }
}
