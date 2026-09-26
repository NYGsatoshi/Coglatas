using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Realtime;

public sealed class OutboxReplayService(
    IOutboxEventRepository repository,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IAuditLogger auditLogger,
    IClock clock,
    IUnitOfWork unitOfWork) : IOutboxReplayService
{
    public async Task<Result> ReplayAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            return Result.Failure("A bounded replay reason is required.");
        }

        if (!currentTenant.IsAvailable || currentUser.SystemRole is not SystemRole.PlatformAdmin)
        {
            return Result.Failure("The realtime outbox replay capability is required.");
        }

        var eventItem = await repository.GetByIdAsync(eventId, cancellationToken);
        if (eventItem is null || eventItem.TenantId != currentTenant.TenantId)
        {
            return Result.Failure("Outbox event not found.");
        }

        if (!RealtimeEventCatalog.IsSupported(eventItem.EventType, eventItem.PayloadSchemaVersion))
        {
            return Result.Failure("The durable event schema is not supported.");
        }

        if (!await repository.ReplayAsync(eventId, clock.UtcNow, cancellationToken))
        {
            return Result.Failure("The outbox event cannot be replayed in its current state.");
        }

        await auditLogger.LogUserActionAsync(
            currentUser.UserId ?? Guid.Empty,
            "RealtimeOutboxReplay",
            "OutboxEvent",
            eventId,
            "A durable realtime event was replayed.",
            new Dictionary<string, object?> { ["reason"] = reason.Trim() },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
