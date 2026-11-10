using Coglatas.Application.Announcements;
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
        var created = await fixture.Service.CreateAsync(fixture.Request("Original"), "editable-draft-create");
        Assert.True(created.IsSuccess, created.Error);
        var originalId = created.Value!.Id;
        var originalVersion = created.Value.Version;
        var sentinel = await fixture.Service.CreateAsync(fixture.Request("Unrelated retained draft"), "editable-draft-sentinel");
        Assert.True(sentinel.IsSuccess, sentinel.Error);
        Assert.NotEqual(originalId, sentinel.Value!.Id);
        var before = await CaptureDraftRowsAsync(fixture);
        var workspace = targetShape == 0 ? (Guid?)null : Guid.NewGuid();
        var group = targetShape >= 2 ? (Guid?)Guid.NewGuid() : null;
        var channel = targetShape == 3 ? (Guid?)Guid.NewGuid() : null;
        var target = new AnnouncementDraftTargetRequest(workspace, group, channel);
        var pinned = (targetShape & 1) != 0;
        var requiresRead = (targetShape & 2) != 0;
        DateTimeOffset? expires = targetShape == 0 ? null : fixture.Clock.UtcNow.AddDays(3);
        var content = new AnnouncementDraftContentRequest(target, "  Saved title  ", "  Saved body.  ", priority, pinned, requiresRead, expires);
        using var cancellation = new CancellationTokenSource();

        var saved = await fixture.Service.SaveAsync(originalId, new SaveAnnouncementDraftRequest(originalVersion, content), cancellation.Token);

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
        Assert.NotNull(response.UpdatedAtUtc);
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
        var idempotencyIds = await fixture.Db.IdempotencyRecords.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

        var stale = await fixture.Service.SaveAsync(originalId,
            new SaveAnnouncementDraftRequest(originalVersion, fixture.Content("Rejected stale replacement")), cancellation.Token);

        Assert.False(stale.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_STALE", stale.ErrorDetail!.Code);
        Assert.Empty(FindDraftRowDifferences(afterSave, await CaptureDraftRowsAsync(fixture)));
        Assert.Equal(idempotencyIds, await fixture.Db.IdempotencyRecords.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
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
        var queued = await fixture.Service.PublishNowAsync(created.Value!.Id,
            new PublishAnnouncementDraftRequest(created.Value.Version), "retained-publication-queue");
        Assert.True(queued.IsSuccess, queued.Error);
        if (publish)
        {
            fixture.Clock.UtcNow = queued.Value!.ScheduledForUtc!.Value;
            var claim = Assert.Single(await fixture.Service.ClaimDueAsync("retained-publication-worker", fixture.Clock.UtcNow, 1, TimeSpan.FromMinutes(2)));
            await fixture.Service.ProcessAsync(claim, fixture.Clock.UtcNow, TimeSpan.FromMinutes(5));
        }
        var persisted = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        Assert.Equal(publish ? AnnouncementDraftStatus.Published : AnnouncementDraftStatus.Scheduled, persisted.Status);
        var before = await CaptureDraftRowsAsync(fixture);
        var announcementIds = await fixture.Db.Announcements.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        var invalidationIds = fixture.Invalidations.AnnouncementChanges.ToArray();
        var idempotencyIds = await fixture.Db.IdempotencyRecords.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

        var rejected = await fixture.Service.SaveAsync(persisted.Id,
            new SaveAnnouncementDraftRequest(persisted.VersionNo - (staleVersion ? 1 : 0), fixture.Content("Rejected edit")));

        Assert.False(rejected.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_NOT_EDITABLE", rejected.ErrorDetail!.Code);
        Assert.Empty(FindDraftRowDifferences(before, await CaptureDraftRowsAsync(fixture)));
        Assert.Equal(announcementIds, await fixture.Db.Announcements.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
        Assert.Equal(invalidationIds, fixture.Invalidations.AnnouncementChanges);
        Assert.Equal(idempotencyIds, await fixture.Db.IdempotencyRecords.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ListingRechecksEachOwnDraftAudienceAndRetainsStoredRows(int revokedIndex)
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(fixture.Request("First own draft"), "list-first-draft-create");
        var second = await fixture.Service.CreateAsync(fixture.Request("Second own draft"), "list-second-draft-create");
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        var actor = fixture.CurrentUser.UserId;
        fixture.CurrentUser.UserId = Guid.NewGuid();
        var other = await fixture.Service.CreateAsync(fixture.Request("Other author draft"), "list-other-draft-create");
        Assert.True(other.IsSuccess, other.Error);
        fixture.CurrentUser.UserId = actor;
        var ownRows = await fixture.Db.AnnouncementDrafts.AsNoTracking().Where(row => row.AuthorUserId == actor)
            .OrderByDescending(row => row.UpdatedAt ?? row.CreatedAt).ThenByDescending(row => row.Id).ToArrayAsync();
        Assert.Equal(2, ownRows.Length);
        var before = await CaptureDraftRowsAsync(fixture);
        for (var index = 0; index < ownRows.Length; index++) fixture.Audiences.Outcomes.Enqueue(index != revokedIndex);
        var expected = ownRows.Where((_, index) => index != revokedIndex).Select(row =>
            new AnnouncementDraftListItemResponse(row.Id, 1, AnnouncementDraftStatus.Draft, row.Title, null, null, null, null, row.CreatedAt, row.UpdatedAt)).ToArray();

        var listed = await fixture.Service.ListMineAsync();

        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Equal(expected, listed.Value!);
        Assert.DoesNotContain(listed.Value!, row => row.Id == other.Value!.Id);
        Assert.Empty(fixture.Audiences.Outcomes);
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
}
