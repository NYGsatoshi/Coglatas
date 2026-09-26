using Coglatas.Domain.Entities;

namespace Coglatas.Application.Announcements;

/// <summary>
/// Persistence shape for the small #378 workflow. Application code owns
/// validation, authorization and transitions; the repository owns tenant
/// filtering and the short-lived due-publication lease.
/// </summary>
public interface IAnnouncementDraftRepository
{
    Task AddAsync(AnnouncementDraft draft, CancellationToken cancellationToken = default);

    Task<AnnouncementDraft?> GetAsync(Guid draftId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnouncementDraft>> ListForAuthorAsync(
        Guid authorUserId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnouncementPublicationClaim>> ClaimDueAsync(
        string claimOwner,
        DateTimeOffset now,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default);

    Task<AnnouncementDraft?> GetClaimedAsync(
        Guid draftId,
        Guid claimToken,
        CancellationToken cancellationToken = default);
}
