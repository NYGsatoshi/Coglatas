using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

/// <summary>Resolves current operation authority over the exact Source and its protected premises.</summary>
public interface IProjectIdeOperationAuthorization
{
    ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken);
}

/// <summary>Checks persisted viewer authority before and after resolving operation permissions.</summary>
public sealed class ProjectIdeOperationAccess(ProjectIdeViewerContextReader viewers, IProjectIdeOperationAuthorization authorization)
{
    public ProjectIdeViewerContext? CaptureViewer() => viewers.Capture();

    public async Task<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken) =>
        viewer.TenantId == source.TenantId && await viewers.IsCurrentAsync(viewer, cancellationToken) &&
        await authorization.IsAllowedAsync(viewer, operation, source, sourceDigest, cancellationToken) &&
        await viewers.IsCurrentAsync(viewer, cancellationToken);
}
