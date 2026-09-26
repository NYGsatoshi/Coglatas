using Coglatas.Application.Common;

namespace Coglatas.Application.Events;

public interface IEventService
{
    Task<Result<PagedResponse<EventListItemResponse>>> ListAsync(EventListQuery query, CancellationToken cancellationToken = default);
    Task<Result<EventDetailResponse>> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken = default);
    Task<Result<EventDetailResponse>> GetAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<Result<EventDetailResponse>> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<AttendanceResponse>>> GetAttendanceAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<Result<AttendanceResponse>> UpsertMyAttendanceAsync(Guid eventId, UpdateMyAttendanceRequest request, CancellationToken cancellationToken = default);
    Task<Result<AttendanceResponse>> UpdateAttendanceAsync(Guid eventId, Guid userId, UpdateAttendanceRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<CalendarItemResponse>>> GetCalendarAsync(CalendarQuery query, CancellationToken cancellationToken = default);
}
