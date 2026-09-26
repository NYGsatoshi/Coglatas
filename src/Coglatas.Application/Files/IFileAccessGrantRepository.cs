using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Files;

public sealed record FileAccessGrantSummary(int InternalRecipientCount, int ExternalRecipientCount);

public sealed record FileAccessGrantRecipient(
    Guid GrantId,
    Guid UserId,
    string DisplayName,
    FileAccessGrantRecipientKind RecipientKind);

public sealed record FileAccessGrantCandidate(
    Guid UserId,
    string DisplayName,
    FileAccessGrantRecipientKind RecipientKind);

/// <summary>
/// Persistence boundary for explicit File grants. Every "effective" query
/// applies current Tenant, user, Workspace membership, and Project-member
/// checks; stale persisted rows must never become access by themselves.
/// </summary>
public interface IFileAccessGrantRepository
{
    Task<Attachment?> GetWorkspaceAttachmentAsync(Guid fileObjectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, FileAccessGrantSummary>> GetEffectiveSummariesAsync(
        IReadOnlyCollection<Guid> fileObjectIds,
        CancellationToken cancellationToken = default);

    Task<bool> HasEffectiveGrantAsync(
        Guid fileObjectId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileAccessGrantRecipient>> ListEffectiveRecipientsAsync(
        Guid fileObjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileAccessGrantCandidate>> ListEligibleRecipientsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<FileAccessGrantCandidate?> FindEligibleRecipientAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<FileAccessGrant?> GetActiveGrantAsync(
        Guid fileObjectId,
        Guid grantId,
        CancellationToken cancellationToken = default);

    Task<FileAccessGrant?> GetActiveGrantForRecipientAsync(
        Guid fileObjectId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task AddAsync(FileAccessGrant grant, CancellationToken cancellationToken = default);
}
