using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Channels;
using Coglatas.Application.Groups;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Announcements;

public static class AnnouncementEngagementActions
{
    public const string Acknowledged = "AnnouncementAcknowledged";
    public const string CtaClicked = "AnnouncementCtaClicked";
}

public sealed record AnnouncementEngagementAggregate(
    bool HasFrozenDeliveryCohort,
    int RecipientCount,
    int ReadCount,
    int AcknowledgedCount,
    int CtaClickedCount,
    IReadOnlyList<DateTimeOffset> ReadTimesUtc);

public interface IAnnouncementEngagementStore
{
    Task RecordOnceAsync(
        Guid tenantId,
        Guid announcementId,
        Guid userId,
        string action,
        CancellationToken cancellationToken = default);

    Task<AnnouncementEngagementAggregate> GetAggregateAsync(
        Guid tenantId,
        Guid announcementId,
        IReadOnlyCollection<Guid> recipientUserIds,
        CancellationToken cancellationToken = default);
}

public sealed record AnnouncementAnalyticsResponse(
    Guid AnnouncementId,
    int RecipientCount,
    int ReadCount,
    double ReadRate,
    int? AcknowledgedCount,
    double? AcknowledgementRate,
    int? CtaClickCount,
    double? CtaClickThroughRate,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    string DenominatorKind,
    string? CtaMetric,
    double? MedianTimeToRecognitionSeconds);

public interface IAnnouncementAnalyticsService
{
    Task<Result<AnnouncementAnalyticsResponse>> GetAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default);

    Task<Result> AcknowledgeAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default);

    Task<Result> TrackCtaClickAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregate-only Announcement analytics for Issue #390. Recipient identities
/// remain inside the authorization/persistence boundary; the API exposes only
/// bounded counts, rates, the denominator definition, and aggregate timing.
/// </summary>
public sealed class AnnouncementAnalyticsService(
    IAnnouncementRepository announcements,
    IWorkspaceAuthorizationService workspaceAuthorization,
    IGroupAuthorizationService groupAuthorization,
    IChannelAuthorizationService channelAuthorization,
    IUserRepository users,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    IAnnouncementEngagementStore? engagementStore = null,
    IAnnouncementDistributionStore? distributionStore = null) : IAnnouncementAnalyticsService
{
    public async Task<Result<AnnouncementAnalyticsResponse>> GetAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default)
    {
        if (engagementStore is null || distributionStore is null)
        {
            return Result<AnnouncementAnalyticsResponse>.Failure("Announcement analytics persistence is unavailable.");
        }

        var announcement = await announcements.GetAsync(announcementId, cancellationToken);
        if (announcement is null || announcement.DeletedAt.HasValue)
        {
            return Result<AnnouncementAnalyticsResponse>.Failure("Announcement not found.");
        }

        if (!TryCurrentUser(out var userId) ||
            !await CanViewAnalyticsAsync(userId, announcement, cancellationToken))
        {
            return Result<AnnouncementAnalyticsResponse>.Failure("You are not allowed to view announcement analytics.");
        }

        var targets = await announcements.ListTargetUsersAsync(announcement, cancellationToken);
        var recipientIds = targets.Select(target => target.UserId).Distinct().ToArray();
        var aggregate = await engagementStore.GetAggregateAsync(
            announcement.TenantId,
            announcement.Id,
            recipientIds,
            cancellationToken);

        var recipientCount = aggregate.RecipientCount;
        var hasCta = AnnouncementContentContract.Decode(announcement.Body).Cta is not null;
        var now = clock.UtcNow;
        var periodStart = announcement.PublishedAt;
        var periodEnd = announcement.ExpiresAt is { } expiresAt && expiresAt < now ? expiresAt : now;
        if (periodEnd < periodStart)
        {
            periodEnd = periodStart;
        }

        var recognitionSeconds = aggregate.ReadTimesUtc
            .Where(readAt => readAt >= periodStart && readAt <= periodEnd)
            .Select(readAt => Math.Max(0d, (readAt - periodStart).TotalSeconds))
            .OrderBy(seconds => seconds)
            .ToArray();

        var medianRecognition = announcement.Priority is AnnouncementPriority.Important or AnnouncementPriority.Urgent
            ? Median(recognitionSeconds)
            : null;

        return Result<AnnouncementAnalyticsResponse>.Success(new AnnouncementAnalyticsResponse(
            announcement.Id,
            recipientCount,
            aggregate.ReadCount,
            Rate(aggregate.ReadCount, recipientCount),
            announcement.RequiresReadConfirmation ? aggregate.AcknowledgedCount : null,
            announcement.RequiresReadConfirmation ? Rate(aggregate.AcknowledgedCount, recipientCount) : null,
            hasCta ? aggregate.CtaClickedCount : null,
            hasCta ? Rate(aggregate.CtaClickedCount, recipientCount) : null,
            periodStart,
            periodEnd,
            aggregate.HasFrozenDeliveryCohort ? "frozenDeliveryCohort" : "currentAuthorizedAudience",
            hasCta ? "clickThrough" : null,
            medianRecognition));
    }

    public async Task<Result> AcknowledgeAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default)
    {
        if (engagementStore is null)
        {
            return Result.Failure("Announcement engagement persistence is unavailable.");
        }

        var context = await GetRecipientContextAsync(announcementId, cancellationToken);
        if (!context.IsSuccess)
        {
            return Result.Failure(context.Error!);
        }

        var value = context.Value!;
        if (!value.Announcement.RequiresReadConfirmation)
        {
            return Result.Failure("This announcement does not require acknowledgement.");
        }

        if (!value.Target.HasRead)
        {
            await announcements.AddReadAsync(new AnnouncementRead
            {
                AnnouncementId = value.Announcement.Id,
                UserId = value.UserId,
                ReadAt = clock.UtcNow
            }, cancellationToken);
        }

        // Persist the explicit read before the private sidecar event. If the
        // sidecar write fails, a retry can safely record acknowledgement later;
        // an acknowledgement can therefore never exist without its read.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await engagementStore.RecordOnceAsync(
            value.Announcement.TenantId,
            value.Announcement.Id,
            value.UserId,
            AnnouncementEngagementActions.Acknowledged,
            cancellationToken);
        return Result.Success();
    }

    public async Task<Result> TrackCtaClickAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default)
    {
        if (engagementStore is null)
        {
            return Result.Failure("Announcement engagement persistence is unavailable.");
        }

        var context = await GetRecipientContextAsync(announcementId, cancellationToken);
        if (!context.IsSuccess)
        {
            return Result.Failure(context.Error!);
        }

        var value = context.Value!;
        if (AnnouncementContentContract.Decode(value.Announcement.Body).Cta is null)
        {
            return Result.Failure("This announcement does not have a CTA.");
        }

        await engagementStore.RecordOnceAsync(
            value.Announcement.TenantId,
            value.Announcement.Id,
            value.UserId,
            AnnouncementEngagementActions.CtaClicked,
            cancellationToken);
        return Result.Success();
    }

    private async Task<Result<RecipientContext>> GetRecipientContextAsync(
        Guid announcementId,
        CancellationToken cancellationToken)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result<RecipientContext>.Failure("Authentication is required.");
        }

        if (!await announcements.IsVisibleToUserAsync(
                announcementId,
                userId,
                await IsSystemAdminAsync(userId, cancellationToken),
                cancellationToken))
        {
            return Result<RecipientContext>.Failure("Announcement not found.");
        }

        var announcement = await announcements.GetAsync(announcementId, cancellationToken);
        if (announcement is null || announcement.DeletedAt.HasValue)
        {
            return Result<RecipientContext>.Failure("Announcement not found.");
        }

        var targets = await announcements.ListTargetUsersAsync(announcement, cancellationToken);
        var target = targets.FirstOrDefault(candidate => candidate.UserId == userId);
        if (target is null)
        {
            // A manager may be allowed to inspect an Announcement without being
            // part of the frozen delivery cohort. Engagement commands are
            // recipient actions, so they fail closed instead of contaminating
            // the campaign denominator.
            return Result<RecipientContext>.Failure("Announcement not found.");
        }

        return Result<RecipientContext>.Success(new RecipientContext(announcement, target, userId));
    }

    private async Task<bool> CanViewAnalyticsAsync(
        Guid userId,
        Announcement announcement,
        CancellationToken cancellationToken)
    {
        if (await IsSystemAdminAsync(userId, cancellationToken))
        {
            return true;
        }

        if (distributionStore is null)
        {
            return false;
        }

        var publishedTargets = await distributionStore.GetAnnouncementTargetsAsync(
            announcement.TenantId,
            announcement.Id,
            cancellationToken);
        if (publishedTargets.Count > 0)
        {
            // Multi-audience analytics describe the union cohort. Requiring
            // management authority over every persisted target prevents a
            // manager of one scope from inferring aggregate activity belonging
            // to another selected scope.
            foreach (var target in publishedTargets)
            {
                if (!await CanManageTargetAsync(userId, target, cancellationToken))
                {
                    return false;
                }
            }
            return true;
        }

        // Legacy Announcements predate the #388 sidecar. Preserve the stricter
        // manager-only policy while deriving their one effective scope from the
        // legacy columns.
        return await CanManageTargetAsync(
            userId,
            new AnnouncementDraftTargetRequest(
                announcement.WorkspaceId,
                announcement.GroupId,
                announcement.ChannelId),
            cancellationToken);
    }

    private async Task<bool> CanManageTargetAsync(
        Guid userId,
        AnnouncementDraftTargetRequest target,
        CancellationToken cancellationToken)
    {
        if (target.ChannelId.HasValue)
        {
            if (!target.GroupId.HasValue || !target.WorkspaceId.HasValue)
            {
                return false;
            }
            return await channelAuthorization.CanManageChannel(userId, target.ChannelId.Value, cancellationToken);
        }

        if (target.GroupId.HasValue)
        {
            if (!target.WorkspaceId.HasValue)
            {
                return false;
            }
            return await groupAuthorization.CanManageGroup(userId, target.GroupId.Value, cancellationToken);
        }

        if (target.WorkspaceId.HasValue)
        {
            return await workspaceAuthorization.CanManageWorkspace(userId, target.WorkspaceId.Value, cancellationToken);
        }

        return false;
    }

    private async Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is { Status: UserStatus.Active, DeletedAt: null, SystemRole: SystemRole.SystemAdmin };
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    private static double Rate(int count, int denominator) =>
        denominator <= 0 ? 0d : Math.Round((double)count / denominator, 4, MidpointRounding.AwayFromZero);

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2d;
    }

    private sealed record RecipientContext(
        Announcement Announcement,
        AnnouncementTargetUser Target,
        Guid UserId);
}
