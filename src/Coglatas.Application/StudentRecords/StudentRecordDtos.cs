namespace Coglatas.Application.StudentRecords;

public sealed record StudentRecordPublicResponse(
    Guid Id,
    Guid WorkspaceId,
    string? PublicDisplayName,
    string? HomeroomLabel);

public sealed record StudentRecordRestrictedRequest(
    IReadOnlyCollection<string> Fields,
    bool IncludePublic = false);

public sealed record StudentRecordRestrictedResponse(
    Guid Id,
    Guid WorkspaceId,
    StudentRecordPublicResponse? Public,
    IReadOnlyDictionary<string, string?> RestrictedFields);

public sealed record StudentRecordExportRequest(
    IReadOnlyCollection<string> Fields,
    string? Reason,
    bool IncludePublic = false);

public sealed record StudentRecordExportGrantResponse(
    Guid ExportPackageGrantId,
    Guid StudentRecordId,
    Guid WorkspaceId,
    IReadOnlyCollection<string> RequestedFields,
    IReadOnlyCollection<string> AuthorizedFields,
    string Classification,
    DateTimeOffset ReauthorizedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? BuiltAt,
    DateTimeOffset? DownloadedAt);

public sealed record StudentRecordExportPackageResponse(
    StudentRecordExportGrantResponse Grant,
    string FileName,
    string ContentType,
    byte[] Content);
