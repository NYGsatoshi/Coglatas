using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public interface ICanonicalProjectCreateService
{
    Task<Result<ProjectCreateOptionsResponse>> GetCreateOptionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<Result<CanonicalProjectCreateResponse>> CreateAsync(
        Guid workspaceId,
        CanonicalCreateProjectRequest request,
        string? clientRequestIdentity,
        CancellationToken cancellationToken = default);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CanonicalCreateProjectRequest(
    [property: JsonRequired] string Title,
    string? Description = null,
    Guid? GroupId = null,
    ProjectVisibility? Visibility = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public sealed record CanonicalProjectCreateResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid? GroupId,
    Guid OwnerUserId,
    string Title,
    string? Description,
    ProjectStatus Status,
    ProjectVisibility Visibility,
    ProjectActivationState ActivationState,
    DateOnly? StartDate,
    DateOnly? EndDate,
    long VersionNo,
    DateTimeOffset CreatedAt);

public sealed record ProjectCreateOptionsResponse(
    Guid WorkspaceId,
    bool CanCreateUngrouped,
    IReadOnlyList<ProjectVisibility> AllowedVisibilities,
    IReadOnlyList<ProjectCreateGroupOptionResponse> Groups);

public sealed record ProjectCreateGroupOptionResponse(Guid Id, string Name);
