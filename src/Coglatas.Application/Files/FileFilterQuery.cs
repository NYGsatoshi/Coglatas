using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Files;

/// <summary>Opt-in review candidate; does not alter the legacy Workspace list.</summary>
public sealed record FileFilterQuery(
    Guid TenantId, Guid WorkspaceId, Guid? ProjectId, Guid? FolderId,
    bool AtRoot, int Page, int PageSize, string Sort, bool Descending)
{
    public long Offset => ((long)Page - 1) * PageSize;

    public static Result<FileFilterQuery> Parse(
        Guid tenantId, Guid workspaceId, string? scope, Guid? projectId,
        Guid? folderId, bool? atRoot, int page, int pageSize, string? sort, string? direction)
    {
        var mode = scope ?? "workspace";
        var order = sort ?? "createdAt";
        var descending = direction ?? "desc";
        if (workspaceId == Guid.Empty ||
            (mode != "workspace" && mode != "project") ||
            (mode == "project" ? projectId is null || projectId == Guid.Empty : projectId is not null) ||
            folderId == Guid.Empty || (folderId.HasValue && atRoot == true) ||
            (order != "createdAt" && order != "updatedAt" && order != "name") ||
            (descending != "asc" && descending != "desc"))
            return Result<FileFilterQuery>.Failure(new ApplicationErrorDetail("ValidationFailed", "File filter is invalid."));
        return Result<FileFilterQuery>.Success(new FileFilterQuery(tenantId, workspaceId, projectId,
            folderId, atRoot == true, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), order, descending == "desc"));
    }
}

public sealed record FilteredFileRecord(Attachment Attachment, Guid? FolderId);

/// <summary>All legacy item properties plus explicit opt-in identities.</summary>
public sealed record FilteredFileListItemResponse : FileListItemResponse
{
    public FilteredFileListItemResponse(FileListItemResponse source, Guid? projectId, Guid? folderId)
        : base(source) { ProjectId = projectId; FolderId = folderId; }
    public Guid AttachmentId => Id;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? ProjectId { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? FolderId { get; }
}
