using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.ProjectIde.Runtime;

public sealed record ProjectIdeViewerContext(Guid TenantId, Guid UserId, Guid SessionId);

/// <summary>Revalidates the viewer at a disclosure or write boundary without staging writes.</summary>
public sealed class ProjectIdeViewerContextReader(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ISessionRepository sessions,
    ITenantRepository tenants,
    IClock clock)
{
    public ProjectIdeViewerContext? Capture() =>
        currentTenant is { IsAvailable: true, IsPlatformScope: false } &&
        currentTenant.TenantId is var tenantId && tenantId != Guid.Empty &&
        currentUser.IsAuthenticated &&
        currentUser.UserId is { } userId && userId != Guid.Empty &&
        currentUser.SessionId is { } sessionId && sessionId != Guid.Empty
            ? new(tenantId, userId, sessionId)
            : null;

    public async Task<bool> IsCurrentAsync(ProjectIdeViewerContext viewer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        cancellationToken.ThrowIfCancellationRequested();
        if (Capture() != viewer)
            return false;

        var membership = await tenants.GetCurrentTenantUserAsync(viewer.TenantId, viewer.UserId, cancellationToken);
        if (membership is not { Status: TenantUserStatus.Active, Tenant: { Status: TenantStatus.Active, DeletedAt: null } } ||
            membership.TenantId != viewer.TenantId || membership.UserId != viewer.UserId || Capture() != viewer)
            return false;

        var session = await sessions.GetCurrentByIdWithUserAsync(viewer.SessionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return session is { RevokedAt: null, User: { Status: UserStatus.Active, DeletedAt: null } } &&
               session.Id == viewer.SessionId && session.UserId == viewer.UserId && session.ExpiresAt > clock.UtcNow &&
               Capture() == viewer;
    }
}
