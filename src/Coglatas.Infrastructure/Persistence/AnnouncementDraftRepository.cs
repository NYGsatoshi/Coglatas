using Coglatas.Application.Announcements;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the durable #378 draft store and its short publication
/// lease. The lease is fenced by AnnouncementDraft.VersionNo, so concurrent
/// hosts cannot both hold an active claim for the same scheduled draft.
/// </summary>
public sealed class AnnouncementDraftRepository(
    AppDbContext dbContext,
    ICurrentTenant currentTenant) : IAnnouncementDraftRepository
{
    public Task AddAsync(AnnouncementDraft draft, CancellationToken cancellationToken = default) =>
        dbContext.AnnouncementDrafts.AddAsync(draft, cancellationToken).AsTask();

    public Task<AnnouncementDraft?> GetAsync(Guid draftId, CancellationToken cancellationToken = default) =>
        dbContext.AnnouncementDrafts.SingleOrDefaultAsync(draft => draft.Id == draftId, cancellationToken);

    public async Task<IReadOnlyList<AnnouncementDraft>> ListForAuthorAsync(
        Guid authorUserId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var boundedTake = Math.Clamp(take, 1, 100);
        return await dbContext.AnnouncementDrafts
            .AsNoTracking()
            .Where(draft => draft.AuthorUserId == authorUserId)
            .OrderByDescending(draft => draft.UpdatedAt ?? draft.CreatedAt)
            .ThenByDescending(draft => draft.Id)
            .Take(boundedTake)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsPlatformScope)
        {
            throw new InvalidOperationException("Platform scope is required to enumerate announcement publication tenants.");
        }

        var boundedPage = Math.Max(0, page);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        return await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Active && tenant.DeletedAt == null)
            .OrderBy(tenant => tenant.Id)
            .Skip(boundedPage * boundedPageSize)
            .Take(boundedPageSize)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnnouncementPublicationClaim>> ClaimDueAsync(
        string claimOwner,
        DateTimeOffset now,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsPlatformScope)
        {
            throw new InvalidOperationException("A tenant scope is required to claim scheduled announcements.");
        }

        if (string.IsNullOrWhiteSpace(claimOwner) || claimOwner.Trim().Length > 160)
        {
            throw new ArgumentException("Announcement publication claim owner is invalid.", nameof(claimOwner));
        }

        var boundedBatchSize = Math.Clamp(batchSize, 1, 50);
        var boundedTimeout = claimTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : claimTimeout;
        var candidates = await dbContext.AnnouncementDrafts
            .Where(draft =>
                draft.Status == AnnouncementDraftStatus.Scheduled &&
                draft.ScheduledForUtc.HasValue &&
                draft.ScheduledForUtc.Value <= now &&
                (!draft.NextPublicationAttemptAtUtc.HasValue || draft.NextPublicationAttemptAtUtc.Value <= now) &&
                (!draft.PublicationClaimExpiresAtUtc.HasValue || draft.PublicationClaimExpiresAtUtc.Value <= now))
            .OrderBy(draft => draft.ScheduledForUtc)
            .ThenBy(draft => draft.Id)
            .Take(boundedBatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var claims = new List<AnnouncementPublicationClaim>(candidates.Count);
        foreach (var draft in candidates)
        {
            var token = Guid.NewGuid();
            draft.PublicationClaimOwner = claimOwner.Trim();
            draft.PublicationClaimToken = token;
            draft.PublicationClaimExpiresAtUtc = now + boundedTimeout;
            draft.NextPublicationAttemptAtUtc = null;
            draft.PublicationAttemptCount = checked(draft.PublicationAttemptCount + 1);
            draft.VersionNo = checked(draft.VersionNo + 1);
            claims.Add(new AnnouncementPublicationClaim(draft.Id, draft.TenantId, token));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return claims;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another host acquired a candidate first. Clearing prevents this
            // scope from accidentally persisting a stale claim later; the next
            // short polling cycle observes remaining work authoritatively.
            dbContext.ChangeTracker.Clear();
            return [];
        }
    }

    public Task<AnnouncementDraft?> GetClaimedAsync(
        Guid draftId,
        Guid claimToken,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsPlatformScope)
        {
            throw new InvalidOperationException("A tenant scope is required to load a scheduled announcement claim.");
        }

        return dbContext.AnnouncementDrafts.SingleOrDefaultAsync(draft =>
            draft.Id == draftId &&
            draft.TenantId == currentTenant.TenantId &&
            draft.Status == AnnouncementDraftStatus.Scheduled &&
            draft.PublicationClaimToken == claimToken,
            cancellationToken);
    }
}
