using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class ActivityEvent : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? AttendanceDeadline { get; set; }
    public int? Capacity { get; set; }
    public string? BringItemsText { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;

    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public Project? Project { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<EventAttendance> Attendances { get; } = new List<EventAttendance>();
}

public sealed class EventAttendance : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Unanswered;
    public string? Comment { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    public ActivityEvent? Event { get; set; }
    public User? User { get; set; }
}
