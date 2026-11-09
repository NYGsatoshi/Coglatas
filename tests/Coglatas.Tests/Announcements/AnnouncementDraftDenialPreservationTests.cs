using Coglatas.Application.Announcements;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Announcements;

public sealed partial class AnnouncementDraftServiceTests
{
    [Theory]
    [InlineData("empty-title")]
    [InlineData("long-title")]
    [InlineData("empty-body")]
    [InlineData("long-body")]
    [InlineData("unknown-priority")]
    [InlineData("expired")]
    [InlineData("expires-now")]
    [InlineData("group-without-workspace")]
    [InlineData("channel-without-group")]
    [InlineData("duplicate-group")]
    [InlineData("multiple-workspaces")]
    [InlineData("too-many-targets")]
    public async Task InvalidContentCannotCreateOrOverwriteAnExistingDraft(string invalidContent)
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(fixture.Request("Preserved title"), "preserved-draft-create");
        Assert.True(created.IsSuccess, created.Error);
        var original = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        var content = fixture.Content("Rejected replacement");
        var group = new AnnouncementDraftTargetRequest(content.Target.WorkspaceId, Guid.NewGuid(), null);
        content = invalidContent switch
        {
            "empty-title" => content with { Title = " " },
            "long-title" => content with { Title = new string('x', 201) },
            "empty-body" => content with { Body = " " },
            "long-body" => content with { Body = new string('x', 20001) },
            "unknown-priority" => content with { Priority = (AnnouncementPriority)999 },
            "expired" => content with { ExpiresAt = fixture.Clock.UtcNow.AddSeconds(-1) },
            "expires-now" => content with { ExpiresAt = fixture.Clock.UtcNow },
            "group-without-workspace" => content with { Targets = [group with { WorkspaceId = null }] },
            "channel-without-group" => content with { Targets = [content.Target with { ChannelId = Guid.NewGuid() }] },
            "duplicate-group" => content with { Targets = [group, group] },
            "multiple-workspaces" => content with { Targets = [content.Target, new AnnouncementDraftTargetRequest(Guid.NewGuid(), null, null)] },
            _ => content with { Targets = Enumerable.Range(0, 21).Select(_ => group with { GroupId = Guid.NewGuid() }).ToArray() }
        };

        var rejectedCreate = await fixture.Service.CreateAsync(new CreateAnnouncementDraftRequest(content), "rejected-draft-create");
        var rejectedSave = await fixture.Service.SaveAsync(created.Value!.Id, new SaveAnnouncementDraftRequest(created.Value.Version, content));

        Assert.False(rejectedCreate.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_VALIDATION_FAILED", rejectedCreate.ErrorDetail!.Code);
        Assert.False(rejectedSave.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_VALIDATION_FAILED", rejectedSave.ErrorDetail!.Code);
        var persisted = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        Assert.Equal((original.Id, original.Title, original.Body, original.Priority, original.VersionNo, original.Status, original.ExpiresAt),
            (persisted.Id, persisted.Title, persisted.Body, persisted.Priority, persisted.VersionNo, persisted.Status, persisted.ExpiresAt));
        Assert.Empty(await fixture.Db.Announcements.ToListAsync());
        Assert.Empty(fixture.Invalidations.AnnouncementChanges);
    }

    [Theory]
    [InlineData("utc-kind", "ANNOUNCEMENT_DRAFT_SCHEDULE_INVALID")]
    [InlineData("unsupported-zone", "ANNOUNCEMENT_DRAFT_SCHEDULE_INVALID")]
    [InlineData("spring-gap", "ANNOUNCEMENT_DRAFT_SCHEDULE_INVALID")]
    [InlineData("overlap-no-offset", "ANNOUNCEMENT_DRAFT_SCHEDULE_AMBIGUOUS")]
    [InlineData("overlap-wrong-offset", "ANNOUNCEMENT_DRAFT_SCHEDULE_INVALID")]
    [InlineData("unambiguous-offset", "ANNOUNCEMENT_DRAFT_SCHEDULE_INVALID")]
    public async Task InvalidWallClockSchedulePreservesTheEditableDraft(string invalidSchedule, string expectedCode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(fixture.Request(), "wall-clock-draft-create");
        Assert.True(created.IsSuccess, created.Error);
        var original = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        var request = invalidSchedule switch
        {
            "utc-kind" => new ScheduleAnnouncementDraftRequest(created.Value!.Version, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), "Asia/Tokyo"),
            "unsupported-zone" => new ScheduleAnnouncementDraftRequest(created.Value!.Version, new DateTime(2026, 9, 1, 9, 0, 0), "Missing/FixtureZone"),
            "spring-gap" => new ScheduleAnnouncementDraftRequest(created.Value!.Version, new DateTime(2026, 3, 8, 2, 30, 0), "America/New_York"),
            "overlap-no-offset" => new ScheduleAnnouncementDraftRequest(created.Value!.Version, new DateTime(2026, 11, 1, 1, 30, 0), "America/New_York"),
            "overlap-wrong-offset" => new ScheduleAnnouncementDraftRequest(created.Value!.Version, new DateTime(2026, 11, 1, 1, 30, 0), "America/New_York", 0),
            _ => new ScheduleAnnouncementDraftRequest(created.Value!.Version, new DateTime(2026, 9, 1, 9, 0, 0), "Asia/Tokyo", 540)
        };

        var result = await fixture.Service.ScheduleAsync(created.Value!.Id, request, "wall-clock-draft-schedule");

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.ErrorDetail!.Code);
        var persisted = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        Assert.Equal((original.Title, original.Body, original.VersionNo, original.Status, original.ScheduledForUtc, original.ScheduleTimeZoneId, original.NextPublicationAttemptAtUtc),
            (persisted.Title, persisted.Body, persisted.VersionNo, persisted.Status, persisted.ScheduledForUtc, persisted.ScheduleTimeZoneId, persisted.NextPublicationAttemptAtUtc));
        Assert.Empty(await fixture.Db.Announcements.ToListAsync());
        Assert.Empty(fixture.Invalidations.AnnouncementChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingActorOrTenantScopeCannotReadOrModifyAnExistingDraft(bool missingActor)
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(fixture.Request("Private draft"), "private-draft-create");
        Assert.True(created.IsSuccess, created.Error);
        if (missingActor) fixture.CurrentUser.UserId = null;
        else fixture.Tenant.SetPlatformScope();

        var read = await fixture.Service.GetAsync(created.Value!.Id);
        var save = await fixture.Service.SaveAsync(created.Value.Id, new SaveAnnouncementDraftRequest(created.Value.Version, fixture.Content("Denied replacement")));
        var publish = await fixture.Service.PublishNowAsync(created.Value.Id, new PublishAnnouncementDraftRequest(created.Value.Version), "denied-draft-publish");
        var list = await fixture.Service.ListMineAsync();

        Assert.False(read.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_NOT_FOUND", read.ErrorDetail!.Code);
        Assert.False(save.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_NOT_FOUND", save.ErrorDetail!.Code);
        Assert.False(publish.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_NOT_FOUND", publish.ErrorDetail!.Code);
        Assert.False(list.IsSuccess);
        Assert.Equal("ANNOUNCEMENT_DRAFT_AUTHENTICATION_REQUIRED", list.ErrorDetail!.Code);
        fixture.Tenant.SetTenant(fixture.TenantId, "fixture-tenant");
        var persisted = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        Assert.Equal(("Private draft", created.Value.Version, AnnouncementDraftStatus.Draft), (persisted.Title, persisted.VersionNo, persisted.Status));
        Assert.Null(persisted.ScheduledForUtc);
        Assert.Empty(await fixture.Db.Announcements.ToListAsync());
        Assert.Empty(fixture.Invalidations.AnnouncementChanges);
    }
}
