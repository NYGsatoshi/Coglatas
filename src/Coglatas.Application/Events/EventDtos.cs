using Coglatas.Domain.Enums;

namespace Coglatas.Application.Events;

public sealed record EventListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    EventStatus? Status = null);

public sealed record CalendarQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null);

public sealed record ScopeSummaryResponse(
    string ScopeType,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    string Label);

public sealed record EventListItemResponse(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    EventStatus Status,
    ScopeSummaryResponse RelatedScope,
    int? Capacity,
    int AttendingCount);

public sealed record EventDetailResponse(
    Guid Id,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? AttendanceDeadline,
    int? Capacity,
    string? BringItemsText,
    EventStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    ScopeSummaryResponse RelatedScope,
    int AttendingCount,
    AttendanceResponse? CurrentUserAttendance);

public sealed record CreateEventRequest(
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? AttendanceDeadline = null,
    int? Capacity = null,
    string? BringItemsText = null,
    EventStatus Status = EventStatus.Draft);

public sealed record UpdateEventRequest(
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null,
    string? Title = null,
    string? Description = null,
    string? Location = null,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null,
    DateTimeOffset? AttendanceDeadline = null,
    int? Capacity = null,
    string? BringItemsText = null,
    EventStatus? Status = null);

public sealed record AttendanceResponse(
    Guid EventId,
    Guid UserId,
    string? UserDisplayName,
    AttendanceStatus Status,
    string? Comment,
    DateTimeOffset? RespondedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateMyAttendanceRequest(
    AttendanceStatus Status,
    string? Comment = null);

public sealed record UpdateAttendanceRequest(
    AttendanceStatus Status,
    string? Comment = null);

public sealed record CalendarItemResponse(
    string ItemType,
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? Route,
    string Status,
    ScopeSummaryResponse RelatedScope);
