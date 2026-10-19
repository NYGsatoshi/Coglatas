using System.Text.Json.Serialization;

namespace Coglatas.Application.Artifacts;

public sealed record WorkspaceArtifactListRequest(
    string? Query = null,
    int PageSize = 20,
    string? Cursor = null,
    string Sort = "updatedAt",
    string Direction = "desc");

public sealed record WorkspaceArtifactPageResponse(
    IReadOnlyList<ArtifactListItemResponse> Items,
    string? NextCursor,
    bool HasMore)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? TotalCount => null;
}

public sealed record WorkspaceArtifactPosition(DateTimeOffset UpdatedAtUtc, Guid Id);

public sealed record WorkspaceArtifactReadQuery(
    Guid TenantId,
    Guid ActorUserId,
    Guid WorkspaceId,
    string Query,
    int PageSize,
    WorkspaceArtifactPosition? After);

public sealed record WorkspaceArtifactCursor(
    int FormatVersion,
    Guid TenantId,
    Guid ActorUserId,
    Guid WorkspaceId,
    string Query,
    string Sort,
    string Direction,
    int PageSize,
    WorkspaceArtifactPosition? After,
    DateTimeOffset ExpiresAtUtc);
