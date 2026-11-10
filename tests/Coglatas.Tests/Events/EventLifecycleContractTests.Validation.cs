using Coglatas.Application.Events;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Events;

public sealed partial class EventLifecycleContractTests
{
    [Theory]
    [InlineData("no-scope", "Exactly one of WorkspaceId, GroupId, or ProjectId must be set.")]
    [InlineData("multiple-scopes", "Exactly one of WorkspaceId, GroupId, or ProjectId must be set.")]
    [InlineData("null-title", "Event title is required.")]
    [InlineData("blank-title", "Event title is required.")]
    [InlineData("equal-times", "Event start time must be before the end time.")]
    [InlineData("reversed-times", "Event start time must be before the end time.")]
    [InlineData("late-deadline", "Attendance deadline cannot be after the event start time.")]
    [InlineData("negative-capacity", "Capacity must be greater than or equal to 0.")]
    [InlineData("missing-workspace", "Workspace not found.")]
    [InlineData("archived-workspace", "Workspace not found.")]
    [InlineData("deleted-workspace", "Workspace not found.")]
    public async Task InvalidCreateRetainsExistingEventAndProducesNoRowsOrEffects(string invalidCase, string expectedError)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (invalidCase == "archived-workspace") fixture.Workspace.Status = WorkspaceStatus.Archived;
        if (invalidCase == "deleted-workspace")
            fixture.Workspace.MarkDeleted(fixture.Clock.UtcNow, fixture.Actor.Id, "Synthetic scope deletion");
        await fixture.Db.SaveChangesAsync();
        var eventId = fixture.Event.Id;
        var before = EventValidationSnapshot(fixture.Event);
        var storedBefore = EventValidationSnapshot(await fixture.ReadEventAsync());
        CreateEventRequest request = new(fixture.Workspace.Id, null, null, "Candidate", null, null,
            fixture.Clock.UtcNow.AddDays(2), fixture.Clock.UtcNow.AddDays(2).AddHours(1));
        request = invalidCase switch
        {
            "no-scope" => request with { WorkspaceId = null },
            "multiple-scopes" => request with { GroupId = Guid.NewGuid() },
            "null-title" => request with { Title = null! },
            "blank-title" => request with { Title = " \t " },
            "equal-times" => request with { EndsAt = request.StartsAt },
            "reversed-times" => request with { EndsAt = request.StartsAt.AddTicks(-1) },
            "late-deadline" => request with { AttendanceDeadline = request.StartsAt.AddTicks(1) },
            "negative-capacity" => request with { Capacity = -1 },
            "missing-workspace" => request with { WorkspaceId = Guid.NewGuid() },
            _ => request
        };

        var result = await fixture.Service.CreateAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Equal(before, EventValidationSnapshot(fixture.Event));
        Assert.Equal(storedBefore, EventValidationSnapshot(await fixture.Db.ActivityEvents.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(item => item.Id == eventId)));
        Assert.Equal(eventId, Assert.Single(await fixture.Db.ActivityEvents.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Id);
        Assert.Empty(await fixture.Db.AuditLogs.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.EventAttendances.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        await fixture.AssertNoEffectsAsync();
    }

    [Theory]
    [InlineData("multiple-scopes", "Exactly one of WorkspaceId, GroupId, or ProjectId must be set.")]
    [InlineData("blank-title", "Event title is required.")]
    [InlineData("equal-times", "Event start time must be before the end time.")]
    [InlineData("reversed-times", "Event start time must be before the end time.")]
    [InlineData("late-deadline", "Attendance deadline cannot be after the event start time.")]
    [InlineData("negative-capacity", "Capacity must be greater than or equal to 0.")]
    public async Task InvalidUpdateRetainsEveryEventScalarAndProducesNoEffects(string invalidCase, string expectedError)
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = fixture.Event.Id;
        var before = EventValidationSnapshot(fixture.Event);
        var storedBefore = EventValidationSnapshot(await fixture.ReadEventAsync());
        var request = invalidCase switch
        {
            "multiple-scopes" => new UpdateEventRequest(WorkspaceId: fixture.Workspace.Id, GroupId: Guid.NewGuid()),
            "blank-title" => new UpdateEventRequest(Title: " \t "),
            "equal-times" => new UpdateEventRequest(EndsAt: fixture.Event.StartsAt),
            "reversed-times" => new UpdateEventRequest(EndsAt: fixture.Event.StartsAt.AddTicks(-1)),
            "late-deadline" => new UpdateEventRequest(AttendanceDeadline: fixture.Event.StartsAt.AddTicks(1)),
            "negative-capacity" => new UpdateEventRequest(Capacity: -1),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
        };

        var result = await fixture.Service.UpdateAsync(eventId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Equal(before, EventValidationSnapshot(fixture.Event));
        Assert.Equal(storedBefore, EventValidationSnapshot(await fixture.Db.ActivityEvents.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(item => item.Id == eventId)));
        Assert.Equal(eventId, Assert.Single(await fixture.Db.ActivityEvents.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Id);
        Assert.Empty(await fixture.Db.AuditLogs.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.EventAttendances.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        await fixture.AssertNoEffectsAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAcceptsEqualDeadlineAndZeroCapacityWhileNormalizingOptionalText(bool emptyText)
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalId = fixture.Event.Id;
        var original = EventValidationSnapshot(await fixture.ReadEventAsync());
        var startsAt = fixture.Clock.UtcNow.AddDays(2);
        var result = await fixture.Service.CreateAsync(new CreateEventRequest(fixture.Workspace.Id, null, null,
            "  Synthetic new event  ", emptyText ? " \t " : "  Description  ", emptyText ? " \t " : "  Room  ",
            startsAt, startsAt.AddHours(1), startsAt, 0, emptyText ? " \t " : "  Notebook  "));

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.NotEqual(originalId, value.Id);
        Assert.Equal(fixture.Actor.Id, value.CreatedByUserId);
        Assert.Equal(fixture.Workspace.Id, value.WorkspaceId);
        Assert.Null(value.GroupId);
        Assert.Null(value.ProjectId);
        Assert.Equal("Synthetic new event", value.Title);
        Assert.Equal(emptyText ? null : "Description", value.Description);
        Assert.Equal(emptyText ? null : "Room", value.Location);
        Assert.Equal(emptyText ? null : "Notebook", value.BringItemsText);
        Assert.Equal(startsAt, value.StartsAt);
        Assert.Equal(startsAt.AddHours(1), value.EndsAt);
        Assert.Equal(startsAt, value.AttendanceDeadline);
        Assert.Equal(0, value.Capacity);
        Assert.Equal(EventStatus.Draft, value.Status);
        Assert.Equal(0, value.AttendingCount);
        Assert.Null(value.CurrentUserAttendance);
        var stored = await fixture.Db.ActivityEvents.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == value.Id);
        Assert.Equal((value.WorkspaceId, value.GroupId, value.ProjectId, value.CreatedByUserId, value.Title,
                value.Description, value.Location, value.StartsAt, value.EndsAt, value.AttendanceDeadline,
                value.Capacity, value.BringItemsText, value.Status),
            (stored.WorkspaceId, stored.GroupId, stored.ProjectId, stored.CreatedByUserId, stored.Title,
                stored.Description, stored.Location, stored.StartsAt, stored.EndsAt, stored.AttendanceDeadline,
                stored.Capacity, stored.BringItemsText, stored.Status));
        Assert.Equal(original, EventValidationSnapshot(await fixture.Db.ActivityEvents.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(item => item.Id == originalId)));
        Assert.Equal(2, await fixture.Db.ActivityEvents.IgnoreQueryFilters().CountAsync());
        var audit = Assert.Single(await fixture.Db.AuditLogs.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        (Guid? Actor, string Action, string? EntityType, Guid? EntityId, Guid TenantId) expectedAudit =
            (fixture.Actor.Id, "EventCreated", "ActivityEvent", value.Id, fixture.Event.TenantId);
        Assert.Equal(expectedAudit, (audit.ActorUserId, audit.Action, audit.EntityType, audit.EntityId, audit.TenantId));
        Assert.Empty(fixture.Notifications.Calls);
        Assert.Empty(await fixture.Db.EventAttendances.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(1, fixture.UnitOfWork.Saves);
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateDistinguishesOmittedOptionalTextFromExplicitWhitespace(bool clearText)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Event.Location = "Original location";
        await fixture.Db.SaveChangesAsync();
        var eventId = fixture.Event.Id;
        var original = EventValidationSnapshot(fixture.Event);
        var startsAt = fixture.Clock.UtcNow.AddDays(2);

        var result = await fixture.Service.UpdateAsync(eventId, new UpdateEventRequest(Title: "  Updated event  ",
            Description: clearText ? " \t " : null, Location: clearText ? " \t " : null,
            StartsAt: startsAt, EndsAt: startsAt.AddHours(1), AttendanceDeadline: startsAt, Capacity: 0,
            BringItemsText: clearText ? " \t " : null));

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(eventId, value.Id);
        Assert.Equal(original.WorkspaceId, value.WorkspaceId);
        Assert.Equal(original.CreatedByUserId, value.CreatedByUserId);
        Assert.Equal("Updated event", value.Title);
        Assert.Equal(clearText ? null : "Original description", value.Description);
        Assert.Equal(clearText ? null : "Original location", value.Location);
        Assert.Equal(clearText ? null : "Notebook", value.BringItemsText);
        Assert.Equal(startsAt, value.StartsAt);
        Assert.Equal(startsAt.AddHours(1), value.EndsAt);
        Assert.Equal(startsAt, value.AttendanceDeadline);
        Assert.Equal(0, value.Capacity);
        Assert.Equal(EventStatus.Published, value.Status);
        var stored = await fixture.Db.ActivityEvents.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == eventId);
        Assert.Equal(EventValidationSnapshot(fixture.Event), EventValidationSnapshot(stored));
        Assert.Equal((value.Title, value.Description, value.Location, value.BringItemsText, value.StartsAt,
                value.EndsAt, value.AttendanceDeadline, value.Capacity, value.Status),
            (stored.Title, stored.Description, stored.Location, stored.BringItemsText, stored.StartsAt,
                stored.EndsAt, stored.AttendanceDeadline, stored.Capacity, stored.Status));
        Assert.Equal(original.CreatedAt, stored.CreatedAt);
        Assert.Equal(original.GroupId, stored.GroupId);
        Assert.Equal(original.ProjectId, stored.ProjectId);
        Assert.Equal(original.TenantId, stored.TenantId);
        Assert.Equal(original.DeletedAt, stored.DeletedAt);
        Assert.Equal(original.DeletedByUserId, stored.DeletedByUserId);
        Assert.Equal(original.DeleteReason, stored.DeleteReason);
        Assert.Equal(1, await fixture.Db.ActivityEvents.IgnoreQueryFilters().CountAsync());
        var notification = Assert.Single(fixture.Notifications.Calls);
        fixture.AssertNotificationIdentity(notification);
        Assert.Equal("Event updated: Updated event", notification.Title);
        Assert.Equal(clearText ? null : "Original description", notification.Body);
        await fixture.AssertAuditAsync("EventUpdated");
        Assert.Empty(await fixture.Db.EventAttendances.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(1, fixture.UnitOfWork.Saves);
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    private static (Guid Id, Guid TenantId, Guid? WorkspaceId, Guid? GroupId, Guid? ProjectId,
        Guid CreatedByUserId, string Title, string? Description, string? Location, DateTimeOffset StartsAt,
        DateTimeOffset EndsAt, DateTimeOffset? AttendanceDeadline, int? Capacity, string? BringItemsText,
        EventStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? DeletedAt,
        Guid? DeletedByUserId, string? DeleteReason) EventValidationSnapshot(ActivityEvent item) => (
        item.Id, item.TenantId, item.WorkspaceId, item.GroupId, item.ProjectId, item.CreatedByUserId,
        item.Title, item.Description, item.Location, item.StartsAt, item.EndsAt, item.AttendanceDeadline,
        item.Capacity, item.BringItemsText, item.Status, item.CreatedAt, item.UpdatedAt, item.DeletedAt,
        item.DeletedByUserId, item.DeleteReason);

}
