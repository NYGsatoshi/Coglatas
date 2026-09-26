using Coglatas.Domain.Entities;

namespace Coglatas.Application.Events;

public interface IEventAuthorizationService
{
    Task<bool> CanCreateEvent(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken = default);
    Task<bool> CanViewEvent(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task<bool> CanManageEvent(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task<bool> CanManageAttendance(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default);
    Task<bool> CanAccessScope(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default);
}
