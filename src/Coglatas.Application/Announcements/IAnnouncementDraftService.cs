using Coglatas.Application.Common;

namespace Coglatas.Application.Announcements;

public interface IAnnouncementDraftService
{
    Task<Result<AnnouncementDraftResponse>> CreateAsync(
        CreateAnnouncementDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AnnouncementDraftListItemResponse>>> ListMineAsync(
        CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDraftResponse>> GetAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDraftResponse>> SaveAsync(
        Guid draftId,
        SaveAnnouncementDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDraftResponse>> PublishNowAsync(
        Guid draftId,
        PublishAnnouncementDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<AnnouncementDraftResponse>> ScheduleAsync(
        Guid draftId,
        ScheduleAnnouncementDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Internal server-side port used by the due-time hosted worker. Callers must
/// establish the claimed Tenant context before claiming or processing work.
/// </summary>
public interface IAnnouncementPublicationProcessor
{
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

    Task ProcessAsync(
        AnnouncementPublicationClaim claim,
        DateTimeOffset now,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        AnnouncementPublicationClaim claim,
        DateTimeOffset now,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);
}
