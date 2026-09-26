using System.Diagnostics;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Realtime;

public sealed class TransactionalOutbox(
    IOutboxEventRepository repository,
    ICurrentTenant currentTenant,
    IClock clock) : ITransactionalOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ProhibitedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "inviteToken", "accessToken", "grantToken", "secret",
        "storagePath", "storageKey", "filePath", "rawFilePath", "objectStoragePath", "signedUrl",
        "attachmentContent", "connectionString", "stackTrace", "sql", "license", "licenseKey", "licenseMaterial"
    };
    private static readonly HashSet<string> ProhibitedTaskProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "body", "commentBody", "bodyPlainText", "description",
        "reviewReason", "reviewReturnReason", "blockedReason",
        "watch", "watchState", "watchStates", "isWatching", "isManualWatch", "isExplicitOptOut",
        "preference", "preferences", "preferenceValue", "notificationPreference", "taskNotificationPreference",
        "deadlineDigestLocalTime", "effectiveDeadlineDigestLocalTime",
        "title", "taskTitle", "restrictedTitle", "displayName", "taskDisplayName",
        "recipients", "recipientIds", "relationshipIds", "assigneeIds", "reviewerIds", "collaboratorIds"
    };

    public async Task<Result<Guid>> EnqueueAsync(
        DurableEventEnvelope envelope,
        IReadOnlyCollection<RealtimeRoutingTarget> routingTargets,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || envelope.TenantId != currentTenant.TenantId)
        {
            return Result<Guid>.Failure("A matching active tenant context is required.");
        }

        if (envelope.EventId == Guid.Empty ||
            envelope.AggregateId == Guid.Empty ||
            string.IsNullOrWhiteSpace(envelope.AggregateType) ||
            !RealtimeEventCatalog.IsSupported(envelope.EventType, envelope.PayloadSchemaVersion) ||
            routingTargets.Count == 0 ||
            routingTargets.Any(target => target.ResourceId == Guid.Empty))
        {
            return Result<Guid>.Failure("The durable realtime event contract is invalid.");
        }

        if (!IsRoutingAllowed(envelope.EventType, routingTargets))
        {
            return Result<Guid>.Failure("The durable realtime event routing contract is invalid.");
        }

        var payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var routingJson = JsonSerializer.Serialize(routingTargets, JsonOptions);
        if (payloadJson.Length > 65536 ||
            routingJson.Length > 8192 ||
            ContainsProhibitedProperty(envelope.Payload, ProhibitedProperties) ||
            IsUnsafeTaskPayload(envelope.EventType, envelope.Payload))
        {
            return Result<Guid>.Failure("The durable realtime event payload is not safe to store.");
        }

        await repository.AddAsync(new OutboxEvent(envelope.EventId)
        {
            TenantId = envelope.TenantId,
            EventType = envelope.EventType,
            PayloadSchemaVersion = envelope.PayloadSchemaVersion,
            AggregateType = envelope.AggregateType,
            AggregateId = envelope.AggregateId,
            AggregateVersion = envelope.AggregateVersion,
            OccurredAt = envelope.OccurredAt,
            PayloadJson = payloadJson,
            RoutingJson = routingJson,
            CorrelationId = envelope.CorrelationId ?? Activity.Current?.TraceId.ToString(),
            CausationId = envelope.CausationId,
            Status = OutboxEventStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = clock.UtcNow
        }, cancellationToken);

        return Result<Guid>.Success(envelope.EventId);
    }

    private static bool IsRoutingAllowed(string eventType, IReadOnlyCollection<RealtimeRoutingTarget> routingTargets)
    {
        if (eventType.StartsWith("Messaging.Message", StringComparison.Ordinal) ||
            eventType == "Messaging.ThreadChanged.v1")
        {
            return routingTargets.All(target => target.SubscriptionType is RealtimeSubscriptionType.Conversation or RealtimeSubscriptionType.User);
        }

        if (eventType is "Messaging.ConversationUnreadChanged.v1" or "Notifications.NotificationCreated.v1" or "Notifications.NotificationReadStateChanged.v1" or "Security.AuthorizationStateChanged.v1")
        {
            return routingTargets.All(target => target.SubscriptionType == RealtimeSubscriptionType.User);
        }

        return routingTargets.All(target => target.SubscriptionType is RealtimeSubscriptionType.User or RealtimeSubscriptionType.Workspace or RealtimeSubscriptionType.Project);
    }

    private static bool IsUnsafeTaskPayload(string eventType, JsonElement payload)
    {
        return eventType.StartsWith("Projects.Task", StringComparison.Ordinal) &&
               ContainsProhibitedProperty(payload, ProhibitedTaskProperties);
    }

    private static bool ContainsProhibitedProperty(JsonElement element, ISet<string> prohibited)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (prohibited.Contains(property.Name) || ContainsProhibitedProperty(property.Value, prohibited))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsProhibitedProperty(item, prohibited))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
