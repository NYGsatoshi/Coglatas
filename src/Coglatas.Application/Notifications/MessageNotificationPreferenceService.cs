using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.Notifications;

public sealed record MessageNotificationPreferenceResponse(bool MessageNotificationsEnabled);

public sealed record UpdateMessageNotificationPreferenceRequest(
    [property: System.Text.Json.Serialization.JsonRequired] bool MessageNotificationsEnabled);

public interface IMessageNotificationPreferenceStore
{
    Task<bool?> GetEnabledAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task<bool> SetEnabledAsync(
        Guid tenantId,
        Guid userId,
        bool enabled,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}

public interface IMessageNotificationPreferenceService
{
    Task<Result<MessageNotificationPreferenceResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<MessageNotificationPreferenceResponse>> UpdateAsync(
        UpdateMessageNotificationPreferenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MessageNotificationPreferenceService(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    IMessageNotificationPreferenceStore store) : IMessageNotificationPreferenceService
{
    public async Task<Result<MessageNotificationPreferenceResponse>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!TryCurrentScope(out var tenantId, out var userId))
        {
            return Result<MessageNotificationPreferenceResponse>.Failure("Message notification preferences are unavailable.");
        }

        var enabled = await store.GetEnabledAsync(tenantId, userId, cancellationToken);
        return enabled.HasValue
            ? Result<MessageNotificationPreferenceResponse>.Success(new MessageNotificationPreferenceResponse(enabled.Value))
            : Result<MessageNotificationPreferenceResponse>.Failure("Message notification preferences are unavailable.");
    }

    public async Task<Result<MessageNotificationPreferenceResponse>> UpdateAsync(
        UpdateMessageNotificationPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentScope(out var tenantId, out var userId))
        {
            return Result<MessageNotificationPreferenceResponse>.Failure("Message notification preferences are unavailable.");
        }

        var updated = await store.SetEnabledAsync(
            tenantId,
            userId,
            request.MessageNotificationsEnabled,
            clock.UtcNow,
            cancellationToken);
        return updated
            ? Result<MessageNotificationPreferenceResponse>.Success(
                new MessageNotificationPreferenceResponse(request.MessageNotificationsEnabled))
            : Result<MessageNotificationPreferenceResponse>.Failure("Message notification preferences are unavailable.");
    }

    private bool TryCurrentScope(out Guid tenantId, out Guid userId)
    {
        tenantId = Guid.Empty;
        userId = Guid.Empty;
        if (!currentUser.IsAuthenticated ||
            !currentUser.UserId.HasValue ||
            !currentTenant.IsAvailable ||
            currentTenant.IsPlatformScope)
        {
            return false;
        }

        tenantId = currentTenant.TenantId;
        userId = currentUser.UserId.Value;
        return tenantId != Guid.Empty && userId != Guid.Empty;
    }
}
