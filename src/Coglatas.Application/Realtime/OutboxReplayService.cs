using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Realtime;

public sealed class OutboxReplayService(
    IOutboxEventRepository repository,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IAuditLogger auditLogger,
    IClock clock,
    IUnitOfWork unitOfWork,
    ISessionRepository sessions,
    ICapabilityGrantEvaluator capabilities) : IOutboxReplayService
{
    public async Task<Result> ReplayAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            return Result.Failure("A bounded replay reason is required.");
        }

        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            !currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty ||
            currentUser.SessionId is not { } sessionId || sessionId == Guid.Empty ||
            currentUser.SystemRole is not SystemRole.PlatformAdmin)
        {
            return Result.Failure("The realtime outbox replay capability is required.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            if (!await IsCurrentlyAuthorizedAsync(userId, sessionId, transactionToken))
            {
                return Result.Failure("The realtime outbox replay capability is required.");
            }

            var eventItem = await repository.GetByIdForReplayAsync(eventId, transactionToken);
            if (eventItem is null || eventItem.TenantId != currentTenant.TenantId)
            {
                return Result.Failure("Outbox event not found.");
            }

            // A dispatcher lock wait can outlive the initial grant/session decision.
            if (!await IsCurrentlyAuthorizedAsync(userId, sessionId, transactionToken))
            {
                return Result.Failure("The realtime outbox replay capability is required.");
            }

            if (!RealtimeEventCatalog.IsSupported(eventItem.EventType, eventItem.PayloadSchemaVersion))
            {
                return Result.Failure("The durable event schema is not supported.");
            }

            if (!await repository.ReplayAsync(eventId, clock.UtcNow, transactionToken))
            {
                return Result.Failure("The outbox event cannot be replayed in its current state.");
            }

            await auditLogger.LogUserActionAsync(
                userId,
                "RealtimeOutboxReplay",
                "OutboxEvent",
                eventId,
                "A durable realtime event was replayed.",
                new Dictionary<string, object?> { ["reason"] = reason.Trim() },
                transactionToken);
            await unitOfWork.SaveChangesAsync(transactionToken);
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<bool> IsCurrentlyAuthorizedAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetCurrentByIdWithUserAsync(sessionId, cancellationToken);
        return session is not null && session.UserId == userId && !session.RevokedAt.HasValue &&
               session.ExpiresAt > clock.UtcNow &&
               session.User is { Status: UserStatus.Active, DeletedAt: null, SystemRole: SystemRole.PlatformAdmin } &&
               await capabilities.HasActiveGrantAsync(userId, currentTenant.TenantId,
                   CapabilityKeys.RealtimeOutboxReplay, CapabilityScopeType.Tenant,
                   currentTenant.TenantId, cancellationToken);
    }
}
