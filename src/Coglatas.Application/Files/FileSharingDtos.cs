namespace Coglatas.Application.Files;

/// <summary>
/// Server-owned summary used by the Files list and Preview header. A null
/// external count is intentional: the caller may see the External state while
/// lacking authority to inspect its recipient aggregate.
/// </summary>
public sealed record FileSharingPresentation(
    string AccessState,
    int? ExternalRecipientCount,
    bool CanManageSharing,
    long SharingVersion);

public sealed record FileSharingResponse(
    Guid FileObjectId,
    string AccessState,
    string SharingPolicy,
    long SharingVersion,
    bool CanInspectSharing,
    bool CanManageSharing,
    int? ExternalRecipientCount,
    IReadOnlyList<FileShareRecipientResponse> Recipients,
    IReadOnlyList<FileShareRecipientCandidateResponse> AvailableRecipients);

/// <summary>
/// Recipient identity is returned only from a successful sharing-inspection
/// projection. It deliberately omits email and other profile metadata.
/// </summary>
public sealed record FileShareRecipientResponse(
    Guid GrantId,
    string DisplayName,
    string AccessKind);

/// <summary>
/// This candidate is available only to a current sharing manager. The User ID
/// remains an input to a server-side eligibility check, never a grant by itself.
/// </summary>
public sealed record FileShareRecipientCandidateResponse(
    Guid UserId,
    string DisplayName,
    string AccessKind);

public sealed record FileShareGrantCreateRequest(Guid RecipientUserId, long ExpectedSharingVersion);

public sealed record FileSharingPolicyUpdateRequest(bool ShareWithWorkspace, long ExpectedSharingVersion);
