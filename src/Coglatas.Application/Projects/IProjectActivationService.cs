using Coglatas.Application.Common;

namespace Coglatas.Application.Projects;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivateProjectRequest(
    [property: System.Text.Json.Serialization.JsonRequired] long ExpectedVersion);

/// <summary>
/// Owns the explicit first-activation command for a canonical Project.
/// Implementations must stage ProjectGeneral, the resolved Task workflow,
/// activation provenance, lifecycle transition, audit, and realtime/
/// authorization invalidation in one business transaction.
/// </summary>
public interface IProjectActivationService
{
    Task<Result> ActivateAsync(
        Guid projectId,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
