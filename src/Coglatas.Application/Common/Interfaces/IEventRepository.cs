using Coglatas.Application.Events;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IEventRepository
{
    Task<IReadOnlyList<ActivityEvent>> ListAsync(EventListQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityEvent>> ListCalendarEventsAsync(CalendarQuery query, CancellationToken cancellationToken = default);
    Task<ActivityEvent?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task AddAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventAttendance>> ListAttendanceAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<EventAttendance?> GetAttendanceAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);
    Task AddAttendanceAsync(EventAttendance attendance, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, int>> GetAttendingCountsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListScopeRecipientUserIdsAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectCalendarSourceItem>> ListProjectCalendarItemsAsync(CalendarQuery query, CancellationToken cancellationToken = default);
}

public sealed record ProjectCalendarSourceItem(
    string ItemType,
    Guid Id,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid? GroupId,
    string ScopeLabel,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string Status,
    string Route);
