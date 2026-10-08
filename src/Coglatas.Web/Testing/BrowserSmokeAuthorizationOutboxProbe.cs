using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Web.Testing;

public sealed record BrowserSmokeAuthorizationOutboxSnapshot(bool IsSettled);

/// <summary>Test-only observation of the current synthetic actor's control queue.</summary>
public static class BrowserSmokeAuthorizationOutboxProbe
{
    public const string Path = "/internal/browser-smoke/authorization-outbox";

    public static void MapEndpoint(IEndpointRouteBuilder endpoints, string environmentName, bool requested)
    {
        if (BrowserSmokeTestBoundary.IsEnabled(environmentName, requested))
            endpoints.MapGet(Path, GetAsync).RequireAuthorization();
    }

    public static async Task<BrowserSmokeAuthorizationOutboxSnapshot?> GetSnapshotAsync(
        AppDbContext dbContext, ICurrentTenant currentTenant, ICurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actorUserId || actorUserId == Guid.Empty ||
            currentUser.Email?.EndsWith("@example.test", StringComparison.OrdinalIgnoreCase) != true ||
            !currentTenant.IsAvailable || currentTenant.IsPlatformScope || currentTenant.TenantId == Guid.Empty)
            return null;

        // Delivery may finish with NoAuthorizedRecipient during reconnect. That
        // is settled queue work, not proof that a browser received the frame.
        // Failed, cancelled and dead-lettered work must not qualify a handoff.
        var unfinished = await dbContext.OutboxEvents.AsNoTracking().AnyAsync(item =>
            item.TenantId == currentTenant.TenantId && item.AggregateId == actorUserId &&
            item.AggregateType == "AuthorizationState" &&
            item.EventType == "Security.AuthorizationStateChanged.v1" &&
            item.Status != OutboxEventStatus.Delivered, cancellationToken);
        return new(!unfinished);
    }

    private static async Task<IResult> GetAsync(AppDbContext dbContext, ICurrentTenant currentTenant,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(dbContext, currentTenant, currentUser, cancellationToken);
        return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
    }
}
