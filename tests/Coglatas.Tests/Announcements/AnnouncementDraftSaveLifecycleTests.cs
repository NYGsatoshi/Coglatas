using Coglatas.Application.Announcements;
using Coglatas.Application.Common;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Announcements;

public sealed partial class AnnouncementDraftServiceTests
{
    [Theory]
    [InlineData(AnnouncementPriority.Normal, 0)]
    [InlineData(AnnouncementPriority.Normal, 1)]
    [InlineData(AnnouncementPriority.Normal, 2)]
    [InlineData(AnnouncementPriority.Normal, 3)]
    [InlineData(AnnouncementPriority.Important, 0)]
    [InlineData(AnnouncementPriority.Important, 1)]
    [InlineData(AnnouncementPriority.Important, 2)]
    [InlineData(AnnouncementPriority.Important, 3)]
    [InlineData(AnnouncementPriority.Urgent, 0)]
    [InlineData(AnnouncementPriority.Urgent, 1)]
    [InlineData(AnnouncementPriority.Urgent, 2)]
    [InlineData(AnnouncementPriority.Urgent, 3)]
    public async Task SavingEditableContentRetainsIdentityAndRejectsTheReusedVersion(AnnouncementPriority priority, int targetShape)
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = fixture.CurrentUser.UserId!.Value;
        var tenant = fixture.TenantId;
        var initialShape = 3 - targetShape;
        var initialTarget = new AnnouncementDraftTargetRequest(initialShape == 0 ? null : Guid.NewGuid(),
            initialShape >= 2 ? Guid.NewGuid() : null, initialShape == 3 ? Guid.NewGuid() : null);
        var pinned = (targetShape & 1) != 0;
        var requiresRead = (targetShape & 2) != 0;
        var initialPriority = (AnnouncementPriority)(((int)priority + 1) % 3);
        DateTimeOffset? initialExpiry = targetShape == 0 ? fixture.Clock.UtcNow.AddDays(1) : null;
        var initialContent = new AnnouncementDraftContentRequest(initialTarget, "Original", "Original body.",
            initialPriority, !pinned, !requiresRead, initialExpiry);
        var created = await fixture.Service.CreateAsync(new CreateAnnouncementDraftRequest(initialContent), "editable-draft-create");
        Assert.True(created.IsSuccess, created.Error);
        var originalId = created.Value!.Id;
        var originalVersion = created.Value.Version;
        Assert.NotEqual(Guid.Empty, originalId);
        Assert.Equal(1L, originalVersion);
        var initial = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync(row => row.Id == originalId);
        Assert.Equal((originalId, tenant, actor), (initial.Id, initial.TenantId, initial.AuthorUserId));
        Assert.Equal((initialTarget.WorkspaceId, initialTarget.GroupId, initialTarget.ChannelId), (initial.WorkspaceId, initial.GroupId, initial.ChannelId));
        Assert.Equal(("Original", "Original body.", initialPriority, !pinned, !requiresRead, initialExpiry),
            (initial.Title, initial.Body, initial.Priority, initial.IsPinned, initial.RequiresReadConfirmation, initial.ExpiresAt));
        Assert.Equal((1L, AnnouncementDraftStatus.Draft, 0), (initial.VersionNo, initial.Status, initial.PublicationAttemptCount));
        Assert.Null(initial.UpdatedAt);
        Assert.Null(initial.ScheduledForUtc);
        Assert.Null(initial.ScheduleTimeZoneId);
        Assert.Null(initial.ScheduleLocalDateTime);
        Assert.Null(initial.ScheduleUtcOffsetMinutes);
        Assert.Null(initial.PublishedAnnouncementId);
        Assert.Null(initial.PublishedAtUtc);
        Assert.Null(initial.PublicationClaimOwner);
        Assert.Null(initial.PublicationClaimToken);
        Assert.Null(initial.PublicationClaimExpiresAtUtc);
        Assert.Null(initial.NextPublicationAttemptAtUtc);
        Assert.Null(initial.LastPublicationFailureCode);
        var sentinel = await fixture.Service.CreateAsync(fixture.Request("Unrelated retained draft"), "editable-draft-sentinel");
        Assert.True(sentinel.IsSuccess, sentinel.Error);
        Assert.NotEqual(originalId, sentinel.Value!.Id);
        var before = await CaptureDraftRowsAsync(fixture);
        Assert.Equal(2, before.Count);
        Assert.True(before.ContainsKey(sentinel.Value.Id));
        Assert.Equal(actor, before[sentinel.Value.Id]["AuthorUserId"]);
        Assert.Equal(tenant, before[sentinel.Value.Id]["TenantId"]);
        Assert.Equal("Unrelated retained draft", before[sentinel.Value.Id]["Title"]);
        var workspace = targetShape == 0 ? (Guid?)null : Guid.NewGuid();
        var group = targetShape >= 2 ? (Guid?)Guid.NewGuid() : null;
        var channel = targetShape == 3 ? (Guid?)Guid.NewGuid() : null;
        var target = new AnnouncementDraftTargetRequest(workspace, group, channel);
        DateTimeOffset? expires = targetShape == 0 ? null : fixture.Clock.UtcNow.AddDays(3);
        var content = new AnnouncementDraftContentRequest(target, "  Saved title  ", "  Saved body.  ", priority, pinned, requiresRead, expires);
        using var cancellation = new CancellationTokenSource();

        var saveStartedAt = DateTimeOffset.UtcNow;
        var saved = await fixture.Service.SaveAsync(originalId, new SaveAnnouncementDraftRequest(originalVersion, content), cancellation.Token);
        var saveCompletedAt = DateTimeOffset.UtcNow;

        Assert.True(saved.IsSuccess, saved.Error);
        var response = saved.Value!;
        Assert.Equal(originalId, response.Id);
        Assert.Equal(originalVersion + 1, response.Version);
        Assert.Equal(AnnouncementDraftStatus.Draft, response.Status);
        Assert.Equal((workspace, group, channel), (response.WorkspaceId, response.GroupId, response.ChannelId));
        Assert.Equal(target, Assert.Single(response.Targets));
        Assert.Equal(("Saved title", "Saved body.", priority, pinned, requiresRead, expires),
            (response.Title, response.Body, response.Priority, response.IsPinned, response.RequiresReadConfirmation, response.ExpiresAt));
        Assert.Equal(created.Value.CreatedAtUtc, response.CreatedAtUtc);
        Assert.Equal(initial.CreatedAt, response.CreatedAtUtc);
        Assert.Null(response.Cta);
        Assert.Null(response.Attachment);
        Assert.NotNull(response.UpdatedAtUtc);
        Assert.InRange(response.UpdatedAtUtc!.Value, saveStartedAt, saveCompletedAt);
        Assert.True(response.UpdatedAtUtc.Value >= initial.CreatedAt);
        Assert.Null(response.ScheduledForUtc);
        Assert.Null(response.ScheduleTimeZoneId);
        Assert.Null(response.ScheduleLocalDateTime);
        Assert.Null(response.ScheduleUtcOffsetMinutes);
        Assert.Null(response.PublishedAnnouncementId);
        Assert.Null(response.PublishedAtUtc);
        Assert.Null(response.LastPublicationFailureCode);
        var expected = new Dictionary<string, object?>(before[originalId])
        {
            ["WorkspaceId"] = workspace, ["GroupId"] = group, ["ChannelId"] = channel,
            ["Title"] = "Saved title", ["Body"] = "Saved body.", ["Priority"] = priority,
            ["IsPinned"] = pinned, ["RequiresReadConfirmation"] = requiresRead, ["ExpiresAt"] = expires,
            ["VersionNo"] = originalVersion + 1, ["UpdatedAt"] = response.UpdatedAtUtc
        };
        var afterSave = await CaptureDraftRowsAsync(fixture);
        var expectedRows = before.ToDictionary(row => row.Key, row => new Dictionary<string, object?>(row.Value));
        expectedRows[originalId] = expected;
        Assert.Empty(FindDraftRowDifferences(expectedRows, afterSave));
        Assert.Empty(await fixture.Db.Announcements.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(fixture.Invalidations.AnnouncementChanges);
        var idempotencyIds = await fixture.Db.IdempotencyRecords.IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

        var stale = await fixture.Service.SaveAsync(originalId,
            new SaveAnnouncementDraftRequest(originalVersion, fixture.Content("Rejected stale replacement")), cancellation.Token);

        Assert.False(stale.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_STALE", stale.ErrorDetail!.Code);
        Assert.Empty(FindDraftRowDifferences(afterSave, await CaptureDraftRowsAsync(fixture)));
        Assert.Equal(idempotencyIds, await fixture.Db.IdempotencyRecords.IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
        Assert.Empty(await fixture.Db.Announcements.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(fixture.Invalidations.AnnouncementChanges);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task QueuedOrPublishedDraftsCannotBeEditedEvenWithTheirCurrentVersion(bool publish, bool staleVersion)
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(fixture.Request("Retained publication"), "retained-publication-create");
        Assert.True(created.IsSuccess, created.Error);
        var originalId = created.Value!.Id;
        var actor = fixture.CurrentUser.UserId!.Value;
        var queued = await fixture.Service.PublishNowAsync(originalId,
            new PublishAnnouncementDraftRequest(created.Value.Version), "retained-publication-queue");
        Assert.True(queued.IsSuccess, queued.Error);
        Assert.Equal(originalId, queued.Value!.Id);
        if (publish)
        {
            fixture.Clock.UtcNow = queued.Value!.ScheduledForUtc!.Value;
            var claim = Assert.Single(await fixture.Service.ClaimDueAsync("retained-publication-worker", fixture.Clock.UtcNow, 1, TimeSpan.FromMinutes(2)));
            Assert.Equal(originalId, claim.DraftId);
            Assert.Equal(fixture.TenantId, claim.TenantId);
            Assert.NotEqual(Guid.Empty, claim.ClaimToken);
            await fixture.Service.ProcessAsync(claim, fixture.Clock.UtcNow, TimeSpan.FromMinutes(5));
        }
        var persisted = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        Assert.Equal(originalId, persisted.Id);
        Assert.Equal(publish ? AnnouncementDraftStatus.Published : AnnouncementDraftStatus.Scheduled, persisted.Status);
        if (publish)
        {
            var announcement = await fixture.Db.Announcements.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Equal(announcement.Id, persisted.PublishedAnnouncementId);
            Assert.Equal(announcement.Id, Assert.Single(fixture.Invalidations.AnnouncementChanges));
            Assert.Equal((fixture.TenantId, actor), (announcement.TenantId, announcement.AuthorUserId));
            Assert.NotNull(persisted.PublishedAtUtc);
        }
        else
        {
            Assert.Null(persisted.PublishedAnnouncementId);
            Assert.Null(persisted.PublishedAtUtc);
            Assert.Empty(await fixture.Db.Announcements.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(fixture.Invalidations.AnnouncementChanges);
        }
        var before = await CaptureDraftRowsAsync(fixture);
        Assert.Single(before);
        Assert.True(before.ContainsKey(originalId));
        var announcementIds = await fixture.Db.Announcements.IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        var invalidationIds = fixture.Invalidations.AnnouncementChanges.ToArray();
        var idempotencyIds = await fixture.Db.IdempotencyRecords.IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

        var rejected = await fixture.Service.SaveAsync(originalId,
            new SaveAnnouncementDraftRequest(persisted.VersionNo - (staleVersion ? 1 : 0), fixture.Content("Rejected edit")));

        Assert.False(rejected.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_NOT_EDITABLE", rejected.ErrorDetail!.Code);
        Assert.Empty(FindDraftRowDifferences(before, await CaptureDraftRowsAsync(fixture)));
        Assert.Equal(announcementIds, await fixture.Db.Announcements.IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
        Assert.Equal(invalidationIds, fixture.Invalidations.AnnouncementChanges);
        Assert.Equal(idempotencyIds, await fixture.Db.IdempotencyRecords.IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ListingRechecksEachOwnDraftAudienceAndRetainsStoredRows(int revokedIndex)
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstTarget = new AnnouncementDraftTargetRequest(Guid.NewGuid(), null, null);
        var secondTarget = new AnnouncementDraftTargetRequest(Guid.NewGuid(), null, null);
        Assert.NotEqual(firstTarget.WorkspaceId, secondTarget.WorkspaceId);
        var firstContent = fixture.Content("First own draft") with { Target = firstTarget, Targets = [firstTarget] };
        var secondContent = fixture.Content("Second own draft") with { Target = secondTarget, Targets = [secondTarget] };
        var first = await fixture.Service.CreateAsync(new CreateAnnouncementDraftRequest(firstContent), "list-first-draft-create");
        var second = await fixture.Service.CreateAsync(new CreateAnnouncementDraftRequest(secondContent), "list-second-draft-create");
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        var actor = fixture.CurrentUser.UserId;
        var otherActor = Guid.NewGuid();
        fixture.CurrentUser.UserId = otherActor;
        var other = await fixture.Service.CreateAsync(fixture.Request("Other author draft"), "list-other-draft-create");
        Assert.True(other.IsSuccess, other.Error);
        fixture.CurrentUser.UserId = actor;
        var ownRows = await fixture.Db.AnnouncementDrafts.AsNoTracking().Where(row => row.AuthorUserId == actor)
            .OrderByDescending(row => row.UpdatedAt ?? row.CreatedAt).ThenByDescending(row => row.Id).ToArrayAsync();
        Assert.Equal(2, ownRows.Length);
        Assert.Equal(new[] { first.Value!.Id, second.Value!.Id }.Order(), ownRows.Select(row => row.Id).Order());
        var targets = new Dictionary<Guid, AnnouncementDraftTargetRequest> { [first.Value.Id] = firstTarget, [second.Value.Id] = secondTarget };
        foreach (var row in ownRows) Assert.Equal(targets[row.Id], new AnnouncementDraftTargetRequest(row.WorkspaceId, row.GroupId, row.ChannelId));
        var before = await CaptureDraftRowsAsync(fixture);
        Assert.Equal(3, before.Count);
        Assert.True(before.ContainsKey(other.Value!.Id));
        Assert.Equal(otherActor, before[other.Value.Id]["AuthorUserId"]);
        Assert.Equal(fixture.TenantId, before[other.Value.Id]["TenantId"]);
        Assert.Equal("Other author draft", before[other.Value.Id]["Title"]);
        using var cancellation = new CancellationTokenSource();
        var expectedCalls = ownRows.Select(row => (actor!.Value, targets[row.Id].WorkspaceId, targets[row.Id].GroupId, targets[row.Id].ChannelId, cancellation.Token)).ToArray();
        var observedAudience = new RecordingListAudience();
        if (revokedIndex >= 0)
        {
            var target = targets[ownRows[revokedIndex].Id];
            observedAudience.DeniedTargets.Add((target.WorkspaceId, target.GroupId, target.ChannelId));
        }
        var service = new AnnouncementDraftService(new AnnouncementDraftRepository(fixture.Db, fixture.Tenant),
            new TestAnnouncementRepository(fixture.Db), observedAudience, new TestScheduleTimeZoneResolver(),
            new EfCreateIdempotencyCoordinator(fixture.Db), fixture.CurrentUser, fixture.Tenant, fixture.Clock,
            new RecordingAuditLogger(), fixture.Invalidations, new DbUnitOfWork(fixture.Db));
        var expected = ownRows.Where((_, index) => index != revokedIndex).Select(row =>
            new AnnouncementDraftListItemResponse(row.Id, 1, AnnouncementDraftStatus.Draft, row.Title, null, null, null, null, row.CreatedAt, row.UpdatedAt)).ToArray();

        var listed = await service.ListMineAsync(cancellation.Token);

        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Equal(expected, listed.Value!);
        Assert.DoesNotContain(listed.Value!, row => row.Id == other.Value!.Id);
        Assert.Empty(fixture.Audiences.Outcomes);
        Assert.Equal(expectedCalls, observedAudience.Calls);
        Assert.Empty(FindDraftRowDifferences(before, await CaptureDraftRowsAsync(fixture)));
        Assert.Empty(await fixture.Db.Announcements.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(fixture.Invalidations.AnnouncementChanges);
    }

    private static async Task<Dictionary<Guid, Dictionary<string, object?>>> CaptureDraftRowsAsync(Fixture fixture)
    {
        var rows = await fixture.Db.AnnouncementDrafts.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        return rows.ToDictionary(row => row.Id, row =>
        {
            var properties = fixture.Db.Entry(row).Properties.ToArray();
            Assert.Empty(properties.Where(property => property.Metadata.IsShadowProperty()));
            string[] expectedFields = ["Id", "CreatedAt", "UpdatedAt", "TenantId", "AuthorUserId", "WorkspaceId", "GroupId", "ChannelId",
                "Title", "Body", "Priority", "IsPinned", "RequiresReadConfirmation", "ExpiresAt", "Status", "VersionNo", "ScheduledForUtc",
                "ScheduleTimeZoneId", "ScheduleLocalDateTime", "ScheduleUtcOffsetMinutes", "PublishedAnnouncementId", "PublishedAtUtc",
                "PublicationClaimOwner", "PublicationClaimToken", "PublicationClaimExpiresAtUtc", "NextPublicationAttemptAtUtc",
                "PublicationAttemptCount", "LastPublicationFailureCode"];
            Assert.Equal(expectedFields.Order(), properties.Select(property => property.Metadata.Name).Order());
            return properties.ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);
        });
    }

    private static IReadOnlyList<string> FindDraftRowDifferences(Dictionary<Guid, Dictionary<string, object?>> expected, Dictionary<Guid, Dictionary<string, object?>> actual)
    {
        var differences = new List<string>();
        foreach (var id in expected.Keys.Union(actual.Keys))
        {
            if (!expected.TryGetValue(id, out var original))
            {
                differences.Add($"Unexpected draft {id}.");
                continue;
            }
            if (!actual.TryGetValue(id, out var current))
            {
                differences.Add($"Missing draft {id}.");
                continue;
            }
            foreach (var field in original.Keys.Union(current.Keys))
            {
                if (!original.TryGetValue(field, out var before) || !current.TryGetValue(field, out var after) || !Equals(before, after))
                    differences.Add($"Draft {id} changed field {field}.");
            }
        }
        return differences;
    }

    private sealed class RecordingListAudience : IAnnouncementAudienceService
    {
        public List<(Guid Actor, Guid? Workspace, Guid? Group, Guid? Channel, CancellationToken Token)> Calls { get; } = [];
        public HashSet<(Guid? Workspace, Guid? Group, Guid? Channel)> DeniedTargets { get; } = [];
        public Task<Result<IReadOnlyList<AnnouncementAudienceOptionResponse>>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<bool>> IsAuthorizedAsync(Guid? workspaceId, Guid? groupId, Guid? channelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<bool>> IsAuthorizedForActorAsync(Guid actorUserId, Guid? workspaceId, Guid? groupId, Guid? channelId, CancellationToken cancellationToken = default)
        {
            Calls.Add((actorUserId, workspaceId, groupId, channelId, cancellationToken));
            return Task.FromResult(Result<bool>.Success(!DeniedTargets.Contains((workspaceId, groupId, channelId))));
        }
    }
}
