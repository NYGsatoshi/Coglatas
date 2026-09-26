using Coglatas.Application.Notifications;
using Coglatas.Application.Realtime;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Adds canonical navigation metadata for protected Artifact and Message
/// notifications without weakening the existing current-authorization
/// resolver. The final authorization check is deliberately performed after
/// navigation metadata is resolved so a stale notification never becomes
/// navigation authority.
/// </summary>
public sealed class NotificationNavigationTargetResolver(
    AppDbContext dbContext,
    CurrentAuthorizationTargetResolver currentAuthorization) : INotificationTargetResolver
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

        if (TryParseGuidRoute(initial.Route, "/artifacts/", out var artifactId))
        {
            return await ResolveArtifactNavigationAsync(
                initial,
                tenantId,
                userId,
                notificationId,
                artifactId,
                cancellationToken);
        }

        if (TryParseGuidRoute(initial.Route, "/messages/", out var messageId))
        {
            return await ResolveMessageNavigationAsync(
                initial,
                tenantId,
                userId,
                notificationId,
                messageId,
                cancellationToken);
        }

        return initial;
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

    private async Task<NotificationTargetResolution> ResolveArtifactNavigationAsync(
        NotificationTargetResolution initial,
        Guid tenantId,
        Guid userId,
        Guid notificationId,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.Artifacts
            .AsNoTracking()
            .Where(item =>
                item.Id == artifactId &&
                item.TenantId == tenantId &&
                item.DeletedAt == null)
            .Join(
                dbContext.VisibleProjectsFor(userId),
                artifact => artifact.ProjectId,
                project => project.Id,
                (artifact, project) => new
                {
                    ArtifactId = artifact.Id,
                    project.WorkspaceId
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return Unavailable(initial.StateVersion);
        }

        var finalAuthorization = await currentAuthorization.ResolveAsync(
            tenantId,
            userId,
            notificationId,
            cancellationToken);
        if (!IsSameAuthorizedTarget(initial, finalAuthorization))
        {
            return finalAuthorization.IsOwned
                ? Unavailable(finalAuthorization.StateVersion)
                : finalAuthorization;
        }

        return finalAuthorization with
        {
            Route = $"/artifacts/{target.ArtifactId}",
            WorkspaceId = target.WorkspaceId
        };
    }

    private async Task<NotificationTargetResolution> ResolveMessageNavigationAsync(
        NotificationTargetResolution initial,
        Guid tenantId,
        Guid userId,
        Guid notificationId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.Messages
            .AsNoTracking()
            .Where(item =>
                item.Id == messageId &&
                item.TenantId == tenantId &&
                item.DeletedAt == null &&
                item.WorkspaceId == item.Conversation!.WorkspaceId)
            .Select(item => new
            {
                MessageId = item.Id,
                item.ConversationId,
                item.WorkspaceId
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return Unavailable(initial.StateVersion);
        }

        // Recursive Conversation authorization remains owned by the canonical
        // current-state resolver. Re-run it after metadata resolution so a
        // concurrent revocation fails closed before any route is returned.
        var finalAuthorization = await currentAuthorization.ResolveAsync(
            tenantId,
            userId,
            notificationId,
            cancellationToken);
        if (!IsSameAuthorizedTarget(initial, finalAuthorization))
        {
            return finalAuthorization.IsOwned
                ? Unavailable(finalAuthorization.StateVersion)
                : finalAuthorization;
        }

        return finalAuthorization with
        {
            Route = $"/conversations/{target.ConversationId}?messageId={target.MessageId}",
            WorkspaceId = target.WorkspaceId
        };
    }

    private static bool IsSameAuthorizedTarget(
        NotificationTargetResolution initial,
        NotificationTargetResolution current) =>
        current.IsOwned &&
        current.IsAvailable &&
        string.Equals(initial.Route, current.Route, StringComparison.Ordinal);

    private static bool TryParseGuidRoute(string route, string prefix, out Guid id)
    {
        id = Guid.Empty;
        return route.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParse(route[prefix.Length..], out id);
    }

    private static NotificationTargetResolution Unavailable(long stateVersion) =>
        new(true, false, null, stateVersion);
}
