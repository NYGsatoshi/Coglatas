using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Notifications;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Canonical current-state authorization for protected notification and realtime
/// Project/Task targets. Task and Project paths resolve directly through
/// VisibleProjectsFor; the legacy resolver is used only for non-Project target
/// families whose established contracts are unchanged by WPC-Final01.
/// </summary>
public sealed class CanonicalCurrentAuthorizationTargetResolver(
    AppDbContext dbContext,
    ICurrentTenant currentTenant,
    CurrentAuthorizationTargetResolver inner) : INotificationTargetResolver, IRealtimeEventTargetResolver
{
    private static readonly HashSet<string> TaskEventTypes = new(StringComparer.Ordinal)
    {
        "Projects.TaskChanged.v1",
        "Projects.TaskAssignmentChanged.v1",
        "Projects.TaskWorkflowChanged.v1",
        "Projects.TaskCommentChanged.v1"
    };

    public async Task<NotificationTargetResolution> ResolveAsync(
        Guid tenantId,
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsTenantInScope(tenantId) || userId == Guid.Empty || notificationId == Guid.Empty)
        {
            return NotOwned();
        }

        var notification = await FindNotificationAsync(
            tenantId,
            userId,
            notificationId,
            includeDeleted: false,
            cancellationToken);
        if (notification is null)
        {
            return NotOwned();
        }

        if (notification.RelatedEntityType is not ("TaskItem" or "Task"))
        {
            return await inner.ResolveAsync(tenantId, userId, notificationId, cancellationToken);
        }

        if (!await HasCurrentTenantUserAsync(tenantId, userId, cancellationToken) ||
            !notification.RelatedEntityId.HasValue)
        {
            return Unavailable(notification.StateVersion);
        }

        var target = await ResolveTaskTargetAsync(
            tenantId,
            userId,
            notification.RelatedEntityId.Value,
            cancellationToken);
        return target is null
            ? Unavailable(notification.StateVersion)
            : new NotificationTargetResolution(
                true,
                true,
                $"/projects/{target.ProjectId}/tasks/{target.TaskId}",
                notification.StateVersion,
                target.WorkspaceId);
    }

    public async Task<bool> CanDeliverCreatedAsync(
        Guid tenantId,
        Guid recipientUserId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!IsTenantInScope(tenantId) ||
            envelope.EventType != "Notifications.NotificationCreated.v1" ||
            envelope.TenantId != tenantId ||
            envelope.AggregateId == Guid.Empty)
        {
            return false;
        }

        var notification = await FindNotificationAsync(
            tenantId,
            recipientUserId,
            envelope.AggregateId,
            includeDeleted: false,
            cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.RelatedEntityType is not ("TaskItem" or "Task"))
        {
            return await inner.CanDeliverCreatedAsync(
                tenantId,
                recipientUserId,
                envelope,
                cancellationToken);
        }

        return await HasCurrentTenantUserAsync(tenantId, recipientUserId, cancellationToken) &&
               notification.RelatedEntityId.HasValue &&
               TryGetGuid(envelope.Payload, "notificationId", out var payloadNotificationId) &&
               payloadNotificationId == envelope.AggregateId &&
               IsReferenceOnlyNotificationCreatedPayload(
                   envelope.Payload,
                   payloadNotificationId,
                   envelope.AggregateVersion) &&
               await ResolveTaskTargetAsync(
                   tenantId,
                   recipientUserId,
                   notification.RelatedEntityId.Value,
                   cancellationToken) is not null;
    }

    public async Task<bool> CanDeliverReadStateAsync(
        Guid tenantId,
        Guid recipientUserId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!IsTenantInScope(tenantId) ||
            envelope.EventType != "Notifications.NotificationReadStateChanged.v1" ||
            envelope.TenantId != tenantId ||
            !TryGetNullableGuid(envelope.Payload, "notificationId", out var notificationId))
        {
            return false;
        }

        if (!notificationId.HasValue)
        {
            return await inner.CanDeliverReadStateAsync(
                tenantId,
                recipientUserId,
                envelope,
                cancellationToken);
        }

        if (envelope.AggregateId != notificationId.Value)
        {
            return false;
        }

        var change = GetString(envelope.Payload, "change");
        if (change is not ("read" or "deleted"))
        {
            return false;
        }

        var notification = await FindNotificationAsync(
            tenantId,
            recipientUserId,
            notificationId.Value,
            includeDeleted: change == "deleted",
            cancellationToken,
            requireDeleted: change == "deleted");
        if (notification is null)
        {
            return false;
        }

        if (notification.RelatedEntityType is not ("TaskItem" or "Task"))
        {
            return await inner.CanDeliverReadStateAsync(
                tenantId,
                recipientUserId,
                envelope,
                cancellationToken);
        }

        return await HasCurrentTenantUserAsync(tenantId, recipientUserId, cancellationToken) &&
               notification.RelatedEntityId.HasValue &&
               await ResolveTaskTargetAsync(
                   tenantId,
                   recipientUserId,
                   notification.RelatedEntityId.Value,
                   cancellationToken) is not null;
    }

    public async Task<IReadOnlySet<Guid>> FilterAvailableNotificationIdsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken cancellationToken = default)
    {
        if (!IsTenantInScope(tenantId) || userId == Guid.Empty || notificationIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var requested = notificationIds.Distinct().ToArray();
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(item =>
                requested.Contains(item.Id) &&
                item.TenantId == tenantId &&
                item.UserId == userId &&
                item.DeletedAt == null)
            .Select(item => new NotificationTarget(
                item.Id,
                item.RelatedEntityType,
                item.RelatedEntityId,
                item.StateVersion))
            .ToListAsync(cancellationToken);

        var available = new HashSet<Guid>();
        var nonTaskIds = new List<Guid>();
        foreach (var notification in notifications)
        {
            if (notification.RelatedEntityType is not ("TaskItem" or "Task"))
            {
                nonTaskIds.Add(notification.NotificationId);
                continue;
            }

            if (notification.RelatedEntityId.HasValue &&
                await ResolveTaskTargetAsync(
                    tenantId,
                    userId,
                    notification.RelatedEntityId.Value,
                    cancellationToken) is not null)
            {
                available.Add(notification.NotificationId);
            }
        }

        if (nonTaskIds.Count > 0)
        {
            available.UnionWith(await inner.FilterAvailableNotificationIdsAsync(
                tenantId,
                userId,
                nonTaskIds,
                cancellationToken));
        }

        return available;
    }

    public async Task<bool> CanReceiveTaskEventAsync(
        Guid tenantId,
        Guid userId,
        RealtimeSubscriptionType targetType,
        Guid targetResourceId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!IsTenantInScope(tenantId) ||
            !TaskEventTypes.Contains(envelope.EventType) ||
            envelope.TenantId != tenantId ||
            envelope.AggregateType != "Task" ||
            !TryGetGuid(envelope.Payload, "taskId", out var taskId) ||
            taskId != envelope.AggregateId ||
            !TryGetGuid(envelope.Payload, "projectId", out var projectId))
        {
            return false;
        }

        var target = await ResolveTaskTargetAsync(tenantId, userId, taskId, cancellationToken);
        if (target is null || target.ProjectId != projectId)
        {
            return false;
        }

        return targetType switch
        {
            RealtimeSubscriptionType.User => targetResourceId == userId,
            RealtimeSubscriptionType.Project => targetResourceId == target.ProjectId,
            RealtimeSubscriptionType.Workspace => targetResourceId == target.WorkspaceId,
            _ => false
        };
    }

    public async Task<bool> CanReceiveProjectEventAsync(
        Guid tenantId,
        Guid userId,
        RealtimeSubscriptionType targetType,
        Guid targetResourceId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!IsTenantInScope(tenantId) ||
            envelope.EventType != "Projects.ProjectChanged.v1" ||
            envelope.TenantId != tenantId ||
            envelope.AggregateType != "Project" ||
            !TryGetGuid(envelope.Payload, "projectId", out var projectId) ||
            projectId != envelope.AggregateId ||
            !TryGetGuid(envelope.Payload, "workspaceId", out var workspaceId))
        {
            return false;
        }

        var project = await ResolveProjectTargetAsync(tenantId, userId, projectId, cancellationToken);
        if (project is null || project.WorkspaceId != workspaceId)
        {
            return false;
        }

        return targetType switch
        {
            RealtimeSubscriptionType.Project => targetResourceId == project.ProjectId,
            RealtimeSubscriptionType.Workspace => targetResourceId == project.WorkspaceId,
            RealtimeSubscriptionType.User => targetResourceId == userId,
            _ => false
        };
    }

    public Task<bool> CanReceiveAuthorizationInvalidationAsync(
        Guid tenantId,
        Guid userId,
        RealtimeSubscriptionType targetType,
        Guid targetResourceId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        inner.CanReceiveAuthorizationInvalidationAsync(
            tenantId,
            userId,
            targetType,
            targetResourceId,
            envelope,
            cancellationToken);

    private async Task<ResolvedTaskTarget?> ResolveTaskTargetAsync(
        Guid tenantId,
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!await HasCurrentTenantUserAsync(tenantId, userId, cancellationToken))
        {
            return null;
        }

        var task = await dbContext.TaskItems
            .AsNoTracking()
            .Where(item =>
                item.Id == taskId &&
                item.TenantId == tenantId &&
                item.DeletedAt == null)
            .Select(item => new { item.Id, item.ProjectId, item.WorkspaceId })
            .SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return null;
        }

        var project = await ResolveProjectTargetAsync(
            tenantId,
            userId,
            task.ProjectId,
            cancellationToken);
        return project is null || project.WorkspaceId != task.WorkspaceId
            ? null
            : new ResolvedTaskTarget(task.Id, project.ProjectId, project.WorkspaceId);
    }

    private async Task<ResolvedProjectTarget?> ResolveProjectTargetAsync(
        Guid tenantId,
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!await HasCurrentTenantUserAsync(tenantId, userId, cancellationToken))
        {
            return null;
        }

        var project = await dbContext.VisibleProjectsFor(userId)
            .Where(item => item.Id == projectId && item.TenantId == tenantId)
            .Select(item => new { item.Id, item.WorkspaceId })
            .SingleOrDefaultAsync(cancellationToken);
        return project is null
            ? null
            : new ResolvedProjectTarget(project.Id, project.WorkspaceId);
    }

    private async Task<bool> HasCurrentTenantUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId &&
            user.DeletedAt == null &&
            user.Status == UserStatus.Active,
            cancellationToken) &&
            await dbContext.Tenants.AsNoTracking().AnyAsync(tenant =>
                tenant.Id == tenantId &&
                tenant.DeletedAt == null &&
                tenant.Status == TenantStatus.Active,
                cancellationToken) &&
            await dbContext.TenantUsers.AsNoTracking().AnyAsync(member =>
                member.TenantId == tenantId &&
                member.UserId == userId &&
                member.Status == TenantUserStatus.Active,
                cancellationToken);
    }

    private Task<NotificationTarget?> FindNotificationAsync(
        Guid tenantId,
        Guid userId,
        Guid notificationId,
        bool includeDeleted,
        CancellationToken cancellationToken,
        bool requireDeleted = false) =>
        dbContext.Notifications
            .AsNoTracking()
            .Where(item =>
                item.Id == notificationId &&
                item.TenantId == tenantId &&
                item.UserId == userId &&
                (includeDeleted || item.DeletedAt == null) &&
                (!requireDeleted || item.DeletedAt != null))
            .Select(item => new NotificationTarget(
                item.Id,
                item.RelatedEntityType,
                item.RelatedEntityId,
                item.StateVersion))
            .SingleOrDefaultAsync(cancellationToken);

    private bool IsTenantInScope(Guid tenantId) =>
        tenantId != Guid.Empty &&
        currentTenant.IsAvailable &&
        !currentTenant.IsPlatformScope &&
        currentTenant.TenantId == tenantId;

    private static NotificationTargetResolution NotOwned() => new(false, false, null, 0);
    private static NotificationTargetResolution Unavailable(long stateVersion) => new(true, false, null, stateVersion);

    private static bool IsReferenceOnlyNotificationCreatedPayload(
        JsonElement payload,
        Guid notificationId,
        long? aggregateVersion)
    {
        if (payload.ValueKind != JsonValueKind.Object || aggregateVersion is not > 0)
        {
            return false;
        }

        var expectedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "notificationId",
            "stateVersion",
            "requiresRefetch"
        };
        var propertyCount = 0;
        foreach (var property in payload.EnumerateObject())
        {
            if (!expectedProperties.Contains(property.Name))
            {
                return false;
            }
            propertyCount++;
        }

        return propertyCount == expectedProperties.Count &&
               TryGetGuid(payload, "notificationId", out var payloadNotificationId) &&
               payloadNotificationId == notificationId &&
               TryGetLong(payload, "stateVersion", out var stateVersion) &&
               stateVersion == aggregateVersion &&
               payload.TryGetProperty("requiresRefetch", out var requiresRefetch) &&
               requiresRefetch.ValueKind == JsonValueKind.True;
    }

    private static bool TryGetGuid(JsonElement payload, string name, out Guid value)
    {
        value = Guid.Empty;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryGetLong(JsonElement payload, string name, out long value)
    {
        value = 0;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }

    private static bool TryGetNullableGuid(JsonElement payload, string name, out Guid? value)
    {
        value = null;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var property))
        {
            return false;
        }
        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static string? GetString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record NotificationTarget(
        Guid NotificationId,
        string? RelatedEntityType,
        Guid? RelatedEntityId,
        long StateVersion);

    private sealed record ResolvedTaskTarget(Guid TaskId, Guid ProjectId, Guid WorkspaceId);
    private sealed record ResolvedProjectTarget(Guid ProjectId, Guid WorkspaceId);
}

/// <summary>
/// Production notification resolver. Task navigation is already canonical in
/// CanonicalCurrentAuthorizationTargetResolver. Artifact/Message navigation is
/// layered through the established WPC-02F navigation resolver and then
/// reauthorized before being returned.
/// </summary>
public sealed class CanonicalNotificationTargetResolver(
    CanonicalCurrentAuthorizationTargetResolver currentAuthorization,
    NotificationNavigationTargetResolver navigation) : INotificationTargetResolver
{
    public async Task<NotificationTargetResolution> ResolveAsync(
        Guid tenantId,
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var initial = await currentAuthorization.ResolveAsync(
            tenantId,
            userId,
            notificationId,
            cancellationToken);
        if (!initial.IsOwned || !initial.IsAvailable || string.IsNullOrWhiteSpace(initial.Route))
        {
            return initial;
        }

        // Task routes are produced directly by the canonical resolver. Calling
        // the legacy navigation wrapper here would reintroduce its historical
        // Project predicate for group-bound WorkspaceVisible Projects.
        if (initial.Route.StartsWith("/projects/", StringComparison.Ordinal))
        {
            return initial;
        }

        var navigated = await navigation.ResolveAsync(
            tenantId,
            userId,
            notificationId,
            cancellationToken);
        if (!navigated.IsOwned || !navigated.IsAvailable)
        {
            return navigated;
        }

        var final = await currentAuthorization.ResolveAsync(
            tenantId,
            userId,
            notificationId,
            cancellationToken);
        if (!final.IsOwned ||
            !final.IsAvailable ||
            final.StateVersion != initial.StateVersion ||
            !string.Equals(final.Route, initial.Route, StringComparison.Ordinal))
        {
            return final.IsOwned
                ? new NotificationTargetResolution(true, false, null, final.StateVersion)
                : final;
        }

        return navigated with { StateVersion = final.StateVersion };
    }

    public Task<bool> CanDeliverCreatedAsync(
        Guid tenantId,
        Guid recipientUserId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        currentAuthorization.CanDeliverCreatedAsync(
            tenantId,
            recipientUserId,
            envelope,
            cancellationToken);

    public Task<bool> CanDeliverReadStateAsync(
        Guid tenantId,
        Guid recipientUserId,
        DurableEventEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        currentAuthorization.CanDeliverReadStateAsync(
            tenantId,
            recipientUserId,
            envelope,
            cancellationToken);

    public Task<IReadOnlySet<Guid>> FilterAvailableNotificationIdsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken cancellationToken = default) =>
        currentAuthorization.FilterAvailableNotificationIdsAsync(
            tenantId,
            userId,
            notificationIds,
            cancellationToken);
}
