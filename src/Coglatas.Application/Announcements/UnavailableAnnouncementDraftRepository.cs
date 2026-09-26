using Coglatas.Domain.Entities;

namespace Coglatas.Application.Announcements;

/// <summary>
/// Fail-closed fallback for hosts that compose AddApplication() without the
/// Infrastructure persistence layer. Production composition replaces this
/// registration with AnnouncementDraftRepository from AddInfrastructure().
/// </summary>
internal sealed class UnavailableAnnouncementDraftRepository : IAnnouncementDraftRepository
{
    public Task AddAsync(AnnouncementDraft draft, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Announcement draft persistence is unavailable.");

    public Task<AnnouncementDraft?> GetAsync(Guid draftId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AnnouncementDraft?>(null);

    public Task<IReadOnlyList<AnnouncementDraft>> ListForAuthorAsync(
        Guid authorUserId,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AnnouncementDraft>>([]);

    public Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<IReadOnlyList<AnnouncementPublicationClaim>> ClaimDueAsync(
        string claimOwner,
        DateTimeOffset now,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AnnouncementPublicationClaim>>([]);

    public Task<AnnouncementDraft?> GetClaimedAsync(
        Guid draftId,
        Guid claimToken,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AnnouncementDraft?>(null);
}
