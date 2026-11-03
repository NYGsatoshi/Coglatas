using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Events;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Events;

// Exercises the production service, authorizers, repositories and audit logger with EF InMemory.
// Only attendance counts use a test adapter because InMemory cannot translate the repository's final GroupBy.
// This proves application contracts, not PostgreSQL translation, constraints, locking or RLS.
public sealed class EventLifecycleContractTests
{
    [Theory]
    [InlineData(false, true, "You are not allowed to update this event.")]
    [InlineData(true, false, "You are not allowed to move this event to the selected scope.")]
    public async Task ScopeMoveRequiresBothSourceManagementAndDestinationCreationAuthority(
        bool managesSource, bool managesDestination, string expectedError)
    {
        await using var fixture = await Fixture.CreateAsync();
        var destination = await fixture.AddWorkspaceAsync("Destination");
        await fixture.SetActorRoleAsync(fixture.Workspace.Id, managesSource ? WorkspaceRole.Admin : WorkspaceRole.Member);
        await fixture.SetActorRoleAsync(destination.Id, managesDestination ? WorkspaceRole.Admin : WorkspaceRole.Member);

        var result = await fixture.Service.UpdateAsync(fixture.Event.Id,
            new UpdateEventRequest(WorkspaceId: destination.Id, Title: "Unauthorized move"));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        await fixture.AssertNoEffectsAsync();
        var persisted = await fixture.ReadEventAsync();
        Assert.Equal(fixture.Workspace.Id, persisted.WorkspaceId);
        Assert.Equal("Original event", persisted.Title);
    }

    [Fact]
    public async Task AuthorizedScopeMovePersistsDestinationAndNotifiesItsRecipients()
    {
        await using var fixture = await Fixture.CreateAsync();
        var destination = await fixture.AddWorkspaceAsync("Destination");
        await fixture.SetActorRoleAsync(destination.Id, WorkspaceRole.Admin);
        var destinationMember = await fixture.AddMemberAsync(destination.Id);

        var result = await fixture.Service.UpdateAsync(fixture.Event.Id,
            new UpdateEventRequest(WorkspaceId: destination.Id, Title: "  Moved event  ",
                Description: "  New description  ", Location: "  Hall  ", BringItemsText: "   "));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(destination.Id, result.Value!.WorkspaceId);
        Assert.Null(result.Value.GroupId);
        Assert.Null(result.Value.ProjectId);
        var persisted = await fixture.ReadEventAsync();
        Assert.Equal(destination.Id, persisted.WorkspaceId);
        Assert.Equal("Moved event", persisted.Title);
        Assert.Equal("New description", persisted.Description);
        Assert.Equal("Hall", persisted.Location);
        Assert.Null(persisted.BringItemsText);
        await fixture.AssertAuditAsync("EventUpdated");
        var notification = Assert.Single(fixture.Notifications.Calls);
        fixture.AssertNotificationIdentity(notification);
        Assert.Equal("Event updated: Moved event", notification.Title);
        Assert.Equal("New description", notification.Body);
        Assert.Equal(new[] { fixture.Actor.Id, destinationMember.Id }.Order(), notification.Recipients.Order());
        Assert.DoesNotContain(fixture.Member.Id, notification.Recipients);
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("archived")]
    [InlineData("deleted")]
    public async Task InvalidDestinationDoesNotMutateTheSourceEvent(string destinationState)
    {
        await using var fixture = await Fixture.CreateAsync();
        var destination = await fixture.AddWorkspaceAsync("Destination");
        await fixture.SetActorRoleAsync(destination.Id, WorkspaceRole.Admin);
        if (destinationState == "archived") destination.Status = WorkspaceStatus.Archived;
        if (destinationState == "deleted") destination.MarkDeleted(fixture.Clock.UtcNow);
        await fixture.Db.SaveChangesAsync();
        var destinationId = destinationState == "missing" ? Guid.NewGuid() : destination.Id;

        var result = await fixture.Service.UpdateAsync(fixture.Event.Id,
            new UpdateEventRequest(WorkspaceId: destinationId, Title: "Invalid move"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Workspace not found.", result.Error);
        await fixture.AssertNoEffectsAsync();
        var persisted = await fixture.ReadEventAsync();
        Assert.Equal(fixture.Workspace.Id, persisted.WorkspaceId);
        Assert.Equal("Original event", persisted.Title);
    }

    [Theory]
    [InlineData(EventStatus.Draft, EventStatus.Published, "Event published: Original event", "EventUpdated")]
    [InlineData(EventStatus.Published, EventStatus.Published, "Event updated: Original event", "EventUpdated")]
    [InlineData(EventStatus.Published, EventStatus.Cancelled, "Event cancelled: Original event", "EventCancelled")]
    [InlineData(EventStatus.Cancelled, EventStatus.Cancelled, null, "EventUpdated")]
    [InlineData(EventStatus.Published, EventStatus.Draft, null, "EventUpdated")]
    [InlineData(EventStatus.Published, EventStatus.Archived, null, "EventArchived")]
    public async Task StatusTransitionPersistsAuditAndExpectedNotification(
        EventStatus previousStatus, EventStatus nextStatus, string? notificationTitle, string expectedAction)
    {
        await using var fixture = await Fixture.CreateAsync(previousStatus);

        var result = await fixture.Service.UpdateAsync(fixture.Event.Id, new UpdateEventRequest(Status: nextStatus));

        Assert.True(result.IsSuccess, result.Error);
        var persisted = await fixture.ReadEventAsync();
        Assert.Equal(nextStatus, persisted.Status);
        DateTimeOffset? expectedDeletedAt = nextStatus == EventStatus.Archived ? fixture.Clock.UtcNow : null;
        Assert.Equal(expectedDeletedAt, persisted.DeletedAt);
        var expectedActions = expectedAction == "EventUpdated"
            ? new[] { "EventUpdated" }
            : new[] { "EventUpdated", expectedAction };
        await fixture.AssertAuditAsync(expectedActions);
        if (notificationTitle is null)
        {
            Assert.Empty(fixture.Notifications.Calls);
        }
        else
        {
            var notification = Assert.Single(fixture.Notifications.Calls);
            fixture.AssertNotificationIdentity(notification);
            Assert.Equal(notificationTitle, notification.Title);
            Assert.Equal("Original description", notification.Body);
            Assert.Equal(new[] { fixture.Actor.Id, fixture.Member.Id, fixture.Creator.Id }.Order(), notification.Recipients.Order());
        }
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteArchivesAndPreservesAnExistingDeletionTimestamp(bool alreadyDeleted)
    {
        await using var fixture = await Fixture.CreateAsync();
        var expectedDeletedAt = alreadyDeleted ? fixture.Clock.UtcNow.AddDays(-2) : fixture.Clock.UtcNow;
        if (alreadyDeleted)
        {
            fixture.Event.Status = EventStatus.Archived;
            fixture.Event.MarkDeleted(expectedDeletedAt);
            await fixture.Db.SaveChangesAsync();
        }

        var result = await fixture.Service.DeleteAsync(fixture.Event.Id);

        Assert.True(result.IsSuccess, result.Error);
        var persisted = await fixture.ReadEventAsync();
        Assert.Equal(EventStatus.Archived, persisted.Status);
        Assert.Equal(expectedDeletedAt, persisted.DeletedAt);
        await fixture.AssertAuditAsync("EventArchived");
        Assert.Empty(fixture.Notifications.Calls);
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("attendance")]
    public async Task OrdinaryMemberCannotPerformManagerMutations(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Current.UserIdValue = fixture.Member.Id;

        var isSuccess = operation switch
        {
            "update" => (await fixture.Service.UpdateAsync(fixture.Event.Id, new UpdateEventRequest(Title: "Denied"))).IsSuccess,
            "delete" => (await fixture.Service.DeleteAsync(fixture.Event.Id)).IsSuccess,
            _ => (await fixture.Service.UpdateAttendanceAsync(fixture.Event.Id, fixture.Actor.Id,
                new UpdateAttendanceRequest(AttendanceStatus.Attending))).IsSuccess
        };

        Assert.False(isSuccess);
        await fixture.AssertNoEffectsAsync();
        var persisted = await fixture.ReadEventAsync();
        Assert.Equal("Original event", persisted.Title);
        Assert.Equal(EventStatus.Published, persisted.Status);
        Assert.Null(persisted.DeletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagerAttendanceOverrideBypassesDeadlineAndCapacityForAnAccessibleActiveUser(bool existingAttendance)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Event.AttendanceDeadline = fixture.Clock.UtcNow.AddHours(-1);
        fixture.Event.Capacity = 0;
        if (existingAttendance)
        {
            fixture.Db.EventAttendances.Add(new EventAttendance
            {
                EventId = fixture.Event.Id, UserId = fixture.Member.Id, Status = AttendanceStatus.NotAttending,
                Comment = "Old comment", RespondedAt = fixture.Clock.UtcNow.AddDays(-1)
            });
        }
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.UpdateAttendanceAsync(fixture.Event.Id, fixture.Member.Id,
            new UpdateAttendanceRequest(AttendanceStatus.Attending, "  Confirmed  "));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(fixture.Member.Id, result.Value!.UserId);
        Assert.Equal("Synthetic member", result.Value.UserDisplayName);
        Assert.Equal(AttendanceStatus.Attending, result.Value.Status);
        Assert.Equal("Confirmed", result.Value.Comment);
        Assert.Equal(fixture.Clock.UtcNow, result.Value.RespondedAt);
        var attendance = Assert.Single(await fixture.Db.EventAttendances.AsNoTracking().ToListAsync());
        Assert.Equal(fixture.Member.Id, attendance.UserId);
        Assert.Equal(AttendanceStatus.Attending, attendance.Status);
        Assert.Equal("Confirmed", attendance.Comment);
        Assert.Equal(fixture.Clock.UtcNow, attendance.RespondedAt);
        await fixture.AssertAuditAsync("AttendanceChangedByAdmin");
        Assert.Equal(1, fixture.UnitOfWork.Saves);
        Assert.Empty(fixture.Notifications.Calls);
    }

    [Theory]
    [InlineData("deadline", "Attendance is closed for this event.")]
    [InlineData("capacity", "Event capacity has been reached.")]
    public async Task OrdinaryAttendanceCannotUseManagerDeadlineOrCapacityOverride(string limit, string expectedError)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Current.UserIdValue = fixture.Member.Id;
        fixture.Event.AttendanceDeadline = limit == "deadline" ? fixture.Clock.UtcNow : null;
        fixture.Event.Capacity = limit == "capacity" ? 0 : null;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.UpsertMyAttendanceAsync(fixture.Event.Id,
            new UpdateMyAttendanceRequest(AttendanceStatus.Attending));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        await fixture.AssertNoEffectsAsync();
    }

    [Theory]
    [InlineData("missing", "User not found.")]
    [InlineData("inactive", "User not found.")]
    [InlineData("outside", "The selected user cannot access this event scope.")]
    public async Task ManagerCannotOverrideAttendanceForAnInvalidTarget(string targetState, string expectedError)
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = fixture.Member;
        if (targetState == "inactive") target.Status = UserStatus.Suspended;
        if (targetState == "outside")
        {
            var membership = await fixture.Db.WorkspaceMembers.SingleAsync(item => item.UserId == target.Id);
            membership.Status = MembershipStatus.Suspended;
        }
        await fixture.Db.SaveChangesAsync();
        var targetId = targetState == "missing" ? Guid.NewGuid() : target.Id;

        var result = await fixture.Service.UpdateAttendanceAsync(fixture.Event.Id, targetId,
            new UpdateAttendanceRequest(AttendanceStatus.Attending));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        await fixture.AssertNoEffectsAsync();
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Cancelled)]
    [InlineData(EventStatus.Completed)]
    [InlineData(EventStatus.Archived)]
    public async Task ManagerOverrideCannotReopenAttendanceForAnUnpublishedEvent(EventStatus status)
    {
        await using var fixture = await Fixture.CreateAsync(status);

        var result = await fixture.Service.UpdateAttendanceAsync(fixture.Event.Id, fixture.Member.Id,
            new UpdateAttendanceRequest(AttendanceStatus.Attending));

        Assert.False(result.IsSuccess);
        Assert.Equal("Attendance is only available for published events.", result.Error);
        await fixture.AssertNoEffectsAsync();
    }

    [Theory]
    [InlineData(AttendanceStatus.Attending, "Confirmed", false)]
    [InlineData(AttendanceStatus.Attending, "Changed", true)]
    [InlineData(AttendanceStatus.NotAttending, "Confirmed", true)]
    [InlineData(AttendanceStatus.Unanswered, "Changed", false)]
    public async Task ManagerAttendanceRefreshesResponseTimeOnlyForAnEffectiveAnsweredChange(
        AttendanceStatus nextStatus, string comment, bool refreshTimestamp)
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalTimestamp = fixture.Clock.UtcNow.AddDays(-1);
        fixture.Db.EventAttendances.Add(new EventAttendance
        {
            EventId = fixture.Event.Id, UserId = fixture.Member.Id, Status = AttendanceStatus.Attending,
            Comment = "Confirmed", RespondedAt = originalTimestamp
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.UpdateAttendanceAsync(fixture.Event.Id, fixture.Member.Id,
            new UpdateAttendanceRequest(nextStatus, $"  {comment}  "));

        Assert.True(result.IsSuccess, result.Error);
        var attendance = Assert.Single(await fixture.Db.EventAttendances.AsNoTracking().ToListAsync());
        Assert.Equal(nextStatus, attendance.Status);
        Assert.Equal(comment, attendance.Comment);
        Assert.Equal(refreshTimestamp ? fixture.Clock.UtcNow : originalTimestamp, attendance.RespondedAt);
        await fixture.AssertAuditAsync("AttendanceChangedByAdmin");
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture()
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Tenant);
            var users = new UserRepository(Db);
            var workspaces = new WorkspaceRepository(Db);
            var groups = new GroupRepository(Db);
            var projects = new ProjectRepository(Db);
            var workspaceAuthorization = new WorkspaceAuthorizationService(users, workspaces);
            var groupAuthorization = new GroupAuthorizationService(groups, workspaces, workspaceAuthorization);
            var projectAuthorization = new ProjectAuthorizationService(projects, workspaceAuthorization, groupAuthorization, groups);
            UnitOfWork = new CountingUnitOfWork(Db);
            Service = new EventService(new InMemoryCountsEventRepository(Db), users, workspaces, groups, projects,
                new EventAuthorizationService(users, workspaceAuthorization, groupAuthorization, projectAuthorization),
                projectAuthorization, Current, Clock, new DbAuditLogger(Db, Clock, Current, Tenant), Notifications, UnitOfWork);
        }

        public AppDbContext Db { get; }
        private TenantContext Tenant { get; } = new();
        public CurrentUser Current { get; } = new();
        public FixedClock Clock { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public CountingUnitOfWork UnitOfWork { get; }
        public EventService Service { get; }
        public User Actor { get; } = NewUser("Synthetic manager");
        public User Member { get; } = NewUser("Synthetic member");
        public User Creator { get; } = NewUser("Synthetic creator");
        public Workspace Workspace { get; private set; } = null!;
        public ActivityEvent Event { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(EventStatus status = EventStatus.Published)
        {
            var fixture = new Fixture();
            fixture.Db.Tenants.Add(new Tenant(fixture.Tenant.TenantId)
            {
                Name = "Synthetic event tenant", Slug = fixture.Tenant.TenantSlug, Status = TenantStatus.Active
            });
            await fixture.Db.SaveChangesAsync();
            fixture.Db.Users.AddRange(fixture.Actor, fixture.Member, fixture.Creator);
            fixture.Current.UserIdValue = fixture.Actor.Id;
            fixture.Workspace = await fixture.AddWorkspaceAsync("Source");
            await fixture.SetActorRoleAsync(fixture.Workspace.Id, WorkspaceRole.Admin);
            fixture.Db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = fixture.Workspace.Id, UserId = fixture.Member.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
            fixture.Db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = fixture.Workspace.Id, UserId = fixture.Creator.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
            fixture.Event = new ActivityEvent
            {
                WorkspaceId = fixture.Workspace.Id, CreatedByUserId = fixture.Creator.Id,
                Title = "Original event", Description = "Original description", BringItemsText = "Notebook",
                StartsAt = fixture.Clock.UtcNow.AddDays(1), EndsAt = fixture.Clock.UtcNow.AddDays(1).AddHours(1), Status = status
            };
            fixture.Db.ActivityEvents.Add(fixture.Event);
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public async Task<Workspace> AddWorkspaceAsync(string name)
        {
            var workspace = new Workspace
            {
                Name = name, Slug = name.ToLowerInvariant(), CreatedByUserId = Actor.Id, Status = WorkspaceStatus.Active
            };
            Db.Workspaces.Add(workspace);
            await Db.SaveChangesAsync();
            return workspace;
        }

        public async Task SetActorRoleAsync(Guid workspaceId, WorkspaceRole role)
        {
            var membership = await Db.WorkspaceMembers.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.UserId == Actor.Id);
            if (membership is null)
            {
                Db.WorkspaceMembers.Add(new WorkspaceMember
                {
                    WorkspaceId = workspaceId, UserId = Actor.Id, Role = role, Status = MembershipStatus.Active
                });
            }
            else membership.Role = role;
            await Db.SaveChangesAsync();
        }

        public async Task<User> AddMemberAsync(Guid workspaceId)
        {
            var member = NewUser("Destination member");
            Db.Users.Add(member);
            Db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId, UserId = member.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
            await Db.SaveChangesAsync();
            return member;
        }

        public Task<ActivityEvent> ReadEventAsync() => Db.ActivityEvents.AsNoTracking().SingleAsync(item => item.Id == Event.Id);

        public void AssertNotificationIdentity(NotificationCall notification)
        {
            Assert.Equal(NotificationType.Event, notification.Type);
            Assert.Equal("ActivityEvent", notification.EntityType);
            Assert.Equal(Event.Id, notification.EntityId);
            Assert.Equal(Actor.Id, notification.ActorUserId);
        }

        public async Task AssertNoEffectsAsync()
        {
            Assert.Equal(0, UnitOfWork.Saves);
            Assert.Empty(Notifications.Calls);
            Assert.Empty(await Db.AuditLogs.AsNoTracking().ToListAsync());
            Assert.Empty(await Db.EventAttendances.AsNoTracking().ToListAsync());
            Assert.False(Db.ChangeTracker.HasChanges());
        }

        public async Task AssertAuditAsync(params string[] actions)
        {
            var entries = await Db.AuditLogs.AsNoTracking().ToListAsync();
            Assert.Equal(actions.Order(), entries.Select(entry => entry.Action).Order());
            (Guid? ActorUserId, string EntityType, Guid? EntityId, Guid TenantId) expectedIdentity =
                (Actor.Id, "ActivityEvent", Event.Id, Tenant.TenantId);
            var identities = entries.Select(entry => (entry.ActorUserId, entry.EntityType, entry.EntityId, entry.TenantId));
            Assert.Equal(Enumerable.Repeat(expectedIdentity, entries.Count), identities);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static User NewUser(string name) => new()
        {
            DisplayName = name, Email = $"{Guid.NewGuid():N}@example.invalid",
            NormalizedEmail = Guid.NewGuid().ToString("N"), Status = UserStatus.Active, SystemRole = SystemRole.User
        };
    }

    private sealed class TenantContext : ICurrentTenant
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsAvailable => true;
        public string TenantSlug => "synthetic-events";
        public bool IsPlatformScope => false;
    }

    private sealed class CurrentUser : ICurrentUser
    {
        public Guid UserIdValue { get; set; }
        public Guid? UserId => UserIdValue;
        public Guid? SessionId => null;
        public string Email => "synthetic@example.invalid";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class CountingUnitOfWork(AppDbContext db) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return await db.SaveChangesAsync(cancellationToken);
        }
    }

    // Keeps actual tracked reads, writes and recipient queries. Native repository tests cover the unmodified count query.
    private sealed class InMemoryCountsEventRepository(AppDbContext db) : IEventRepository
    {
        private readonly EventRepository _repository = new(db);

        public Task<IReadOnlyList<ActivityEvent>> ListAsync(EventListQuery query, CancellationToken cancellationToken = default) =>
            _repository.ListAsync(query, cancellationToken);
        public Task<IReadOnlyList<ActivityEvent>> ListCalendarEventsAsync(CalendarQuery query, CancellationToken cancellationToken = default) =>
            _repository.ListCalendarEventsAsync(query, cancellationToken);
        public Task<ActivityEvent?> GetAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            _repository.GetAsync(eventId, cancellationToken);
        public Task AddAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default) =>
            _repository.AddAsync(activityEvent, cancellationToken);
        public Task<IReadOnlyList<EventAttendance>> ListAttendanceAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            _repository.ListAttendanceAsync(eventId, cancellationToken);
        public Task<EventAttendance?> GetAttendanceAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default) =>
            _repository.GetAttendanceAsync(eventId, userId, cancellationToken);
        public Task AddAttendanceAsync(EventAttendance attendance, CancellationToken cancellationToken = default) =>
            _repository.AddAttendanceAsync(attendance, cancellationToken);
        public Task<IReadOnlyList<Guid>> ListScopeRecipientUserIdsAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default) =>
            _repository.ListScopeRecipientUserIdsAsync(activityEvent, cancellationToken);
        public Task<IReadOnlyList<ProjectCalendarSourceItem>> ListProjectCalendarItemsAsync(CalendarQuery query, CancellationToken cancellationToken = default) =>
            _repository.ListProjectCalendarItemsAsync(query, cancellationToken);

        public async Task<Dictionary<Guid, int>> GetAttendingCountsAsync(IReadOnlyCollection<Guid> eventIds,
            CancellationToken cancellationToken = default)
        {
            var ids = await db.EventAttendances.Where(item => eventIds.Contains(item.EventId) && item.Status == AttendanceStatus.Attending)
                .Select(item => item.EventId).ToListAsync(cancellationToken);
            return ids.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        }
    }

    private sealed record NotificationCall(IReadOnlyCollection<Guid> Recipients, NotificationType Type, string Title,
        string? Body, string? EntityType, Guid? EntityId, Guid? ActorUserId);

    private sealed class RecordingNotifications : INotificationService
    {
        public List<NotificationCall> Calls { get; } = [];

        public Task<IReadOnlyList<Guid>> CreateManyAsync(IReadOnlyCollection<Guid> userIds, NotificationType type,
            string title, string? body, string? relatedEntityType, Guid? relatedEntityId, Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new NotificationCall(userIds.ToArray(), type, title, body, relatedEntityType, relatedEntityId, actorUserId));
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        public Task NotifyAsync(Guid recipientUserId, string title, string? body, string sourceType, Guid sourceId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected legacy notification path.");
    }
}
