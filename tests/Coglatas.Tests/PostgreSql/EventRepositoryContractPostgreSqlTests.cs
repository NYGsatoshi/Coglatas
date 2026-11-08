using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Events;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

// Native production repository queries against migrated, synthetic databases.
// Uses the supplied test role; does not establish deployed-role/RLS or HTTP authorization acceptance.
[Collection("PostgreSqlTaskV1")]
public sealed class EventRepositoryContractPostgreSqlTests
{
    [PostgreSqlFact]
    public Task EventQueriesRespectTenantScopeInclusiveUtcDateBoundariesArchiveStateAndOrdering() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), AssertEventQueriesAsync);

    /// <summary>Executes native Event queries against a migrated synthetic database and checks scope and ordering.</summary>
    /// <param name="connectionString">The isolated database supplied by the test harness.</param>
    private static async Task AssertEventQueriesAsync(string connectionString)
    {
        await using var fixture = await Fixture.CreateAsync(connectionString);
        var at = Fixture.At;
        var first = fixture.AddEvent("Alpha", at, at.AddHours(1));
        var second = fixture.AddEvent("Zulu", at, at.AddHours(1));
        var endsAtBoundary = fixture.AddEvent("Ends at boundary", at.AddHours(-1), at);
        fixture.AddEvent("Before range", at.AddHours(-2), at.AddTicks(-10));
        fixture.AddEvent("After range", at.AddTicks(10), at.AddHours(1));
        var cancelled = fixture.AddEvent("Cancelled", at, at.AddHours(1), EventStatus.Cancelled);
        var archived = fixture.AddEvent("Archived", at, at.AddHours(1), EventStatus.Archived);
        archived.MarkDeleted(at.AddDays(-1));
        var deleted = fixture.AddEvent("Deleted published", at, at.AddHours(1));
        deleted.MarkDeleted(at.AddDays(-1));
        var otherScope = fixture.AddEvent("Other scope", at, at.AddHours(1));
        otherScope.WorkspaceId = fixture.OtherWorkspace.Id;
        var foreign = fixture.AddEvent("Foreign tenant", at, at.AddHours(1));
        foreign.TenantId = fixture.ForeignTenantId;
        foreign.WorkspaceId = fixture.ForeignWorkspace.Id;
        await fixture.Seed.SaveChangesAsync();

        // Non-zero input offsets must become UTC before Npgsql receives timestamptz parameters.
        var boundary = at.ToOffset(TimeSpan.FromHours(9));
        var rows = await fixture.Repository.ListAsync(new EventListQuery(
            WorkspaceId: fixture.Workspace.Id, FromDate: boundary, ToDate: boundary));

        Assert.Equal([endsAtBoundary.Id, first.Id, cancelled.Id, second.Id], rows.Select(item => item.Id));
        Assert.Equal(Enumerable.Repeat(fixture.TenantId, rows.Count), rows.Select(item => item.TenantId));
        Assert.Empty(fixture.Scoped.ChangeTracker.Entries());

        // Without a scope predicate, only tenant isolation can exclude the foreign event.
        var allScopes = await fixture.Repository.ListAsync(new EventListQuery(FromDate: boundary, ToDate: boundary));
        Assert.Equal([endsAtBoundary.Id, first.Id, cancelled.Id, otherScope.Id, second.Id], allScopes.Select(item => item.Id));
        var allCalendarScopes = await fixture.Repository.ListCalendarEventsAsync(new CalendarQuery(boundary, boundary));
        Assert.Equal([endsAtBoundary.Id, first.Id, cancelled.Id, otherScope.Id, second.Id], allCalendarScopes.Select(item => item.Id));

        var published = await fixture.Repository.ListAsync(new EventListQuery(WorkspaceId: fixture.Workspace.Id,
            FromDate: boundary, ToDate: boundary, Status: EventStatus.Published));
        Assert.Equal([endsAtBoundary.Id, first.Id, second.Id], published.Select(item => item.Id));

        var archiveRows = await fixture.Repository.ListAsync(new EventListQuery(
            WorkspaceId: fixture.Workspace.Id, Status: EventStatus.Archived));
        Assert.Equal(archived.Id, Assert.Single(archiveRows).Id);
        Assert.Equal(archived.DeletedAt, archiveRows[0].DeletedAt);

        var calendarRows = await fixture.Repository.ListCalendarEventsAsync(new CalendarQuery(
            boundary, boundary, WorkspaceId: fixture.Workspace.Id));
        Assert.Equal([endsAtBoundary.Id, first.Id, cancelled.Id, second.Id], calendarRows.Select(item => item.Id));
        Assert.Empty(fixture.Scoped.ChangeTracker.Entries());

        var local = await fixture.Repository.GetAsync(first.Id);
        Assert.NotNull(local);
        Assert.Equal(fixture.Workspace.Id, local.Workspace!.Id);
        Assert.Equal(fixture.Creator.Id, local.CreatedByUser!.Id);
        Assert.Null(await fixture.Repository.GetAsync(foreign.Id));
        Assert.Null(await fixture.Repository.GetAsync(Guid.NewGuid()));
    }

    [PostgreSqlFact]
    public Task AttendanceReadsCountsAndScopeRecipientsStayWithinTheCurrentTenantAndRequestedEvent() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), AssertAttendanceQueriesAsync);

    /// <summary>Checks native attendance accounting and recipient queries across Tenant and Event boundaries.</summary>
    /// <param name="connectionString">The isolated database supplied by the test harness.</param>
    private static async Task AssertAttendanceQueriesAsync(string connectionString)
    {
        await using var fixture = await Fixture.CreateAsync(connectionString);
        var activityEvent = fixture.AddEvent("Local event", Fixture.At, Fixture.At.AddHours(1));
        var anotherEvent = fixture.AddEvent("Another local event", Fixture.At, Fixture.At.AddHours(1));
        var foreign = fixture.AddEvent("Foreign event", Fixture.At, Fixture.At.AddHours(1));
        foreign.TenantId = fixture.ForeignTenantId;
        foreign.WorkspaceId = fixture.ForeignWorkspace.Id;
        var alpha = fixture.AddUser("Alpha");
        var beta = fixture.AddUser("Beta");
        var zulu = fixture.AddUser("Zulu");
        var outsider = fixture.AddUser("Foreign user");
        fixture.AddAttendance(activityEvent, zulu, AttendanceStatus.Attending);
        fixture.AddAttendance(activityEvent, alpha, AttendanceStatus.Maybe);
        fixture.AddAttendance(activityEvent, beta, AttendanceStatus.Attending);
        fixture.AddAttendance(anotherEvent, alpha, AttendanceStatus.Attending);
        fixture.AddAttendance(foreign, outsider, AttendanceStatus.Attending);
        fixture.Seed.WorkspaceMembers.AddRange(
            new WorkspaceMember
            {
                TenantId = fixture.TenantId, WorkspaceId = fixture.Workspace.Id, UserId = alpha.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            },
            new WorkspaceMember
            {
                TenantId = fixture.TenantId, WorkspaceId = fixture.Workspace.Id, UserId = beta.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Suspended
            },
            new WorkspaceMember
            {
                TenantId = fixture.ForeignTenantId, WorkspaceId = fixture.ForeignWorkspace.Id, UserId = outsider.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
        await fixture.Seed.SaveChangesAsync();

        var attendance = await fixture.Repository.ListAttendanceAsync(activityEvent.Id);
        Assert.Equal([alpha.Id, beta.Id, zulu.Id], attendance.Select(item => item.UserId));
        Assert.Equal(["Alpha", "Beta", "Zulu"], attendance.Select(item => item.User!.DisplayName));
        Assert.Equal(Enumerable.Repeat(activityEvent.Id, attendance.Count), attendance.Select(item => item.EventId));
        Assert.Empty(await fixture.Repository.ListAttendanceAsync(foreign.Id));
        Assert.Equal(beta.Id, (await fixture.Repository.GetAttendanceAsync(activityEvent.Id, beta.Id))!.UserId);
        Assert.Null(await fixture.Repository.GetAttendanceAsync(activityEvent.Id, outsider.Id));
        Assert.Null(await fixture.Repository.GetAttendanceAsync(foreign.Id, outsider.Id));

        var counts = await fixture.Repository.GetAttendingCountsAsync([activityEvent.Id, anotherEvent.Id, foreign.Id]);
        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts[activityEvent.Id]);
        Assert.Equal(1, counts[anotherEvent.Id]);
        Assert.False(counts.ContainsKey(foreign.Id));
        Assert.Empty(await fixture.Repository.GetAttendingCountsAsync([]));
        Assert.Empty(await fixture.Repository.GetAttendingCountsAsync([Guid.NewGuid()]));

        Assert.Equal(alpha.Id, Assert.Single(await fixture.Repository.ListScopeRecipientUserIdsAsync(activityEvent)));
        Assert.Empty(await fixture.Repository.ListScopeRecipientUserIdsAsync(foreign));
    }

    [PostgreSqlFact]
    public Task ProjectCalendarQueriesTranslateAndPreserveTenantScopeUtcDatesAndCompleteProjections() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), AssertProjectCalendarQueriesAsync);

    private static async Task AssertProjectCalendarQueriesAsync(string connectionString)
    {
        await using var fixture = await Fixture.CreateAsync(connectionString);
        var date = new DateOnly(2026, 11, 4);
        var group = new Group
        {
            TenantId = fixture.TenantId, WorkspaceId = fixture.Workspace.Id,
            Name = "Calendar group", Slug = "calendar-group", CreatedByUserId = fixture.Creator.Id
        };
        fixture.Seed.Groups.Add(group);

        Project AddProject(string name, Workspace workspace, Guid tenantId)
        {
            var project = new Project
            {
                TenantId = tenantId, WorkspaceId = workspace.Id, Name = name,
                Slug = Guid.NewGuid().ToString("N"), OwnerUserId = fixture.Creator.Id,
                CreatedByUserId = fixture.Creator.Id, Status = ProjectStatus.Active, DueDate = date
            };
            fixture.Seed.Projects.Add(project);
            return project;
        }

        Milestone AddMilestone(Project project, string title, DateOnly? dueDate)
        {
            var milestone = new Milestone
            {
                TenantId = project.TenantId, ProjectId = project.Id, Name = title, DueDate = dueDate
            };
            fixture.Seed.Milestones.Add(milestone);
            return milestone;
        }

        TaskItem AddTask(Project project, string title, DateOnly? dueDate)
        {
            var task = new TaskItem
            {
                TenantId = project.TenantId, WorkspaceId = project.WorkspaceId, ProjectId = project.Id,
                Title = title, CreatedByUserId = fixture.Creator.Id, DueDate = dueDate
            };
            fixture.Seed.TaskItems.Add(task);
            return task;
        }

        var local = AddProject("Local project", fixture.Workspace, fixture.TenantId);
        local.GroupId = group.Id;
        var sameWorkspace = AddProject("Middle project", fixture.Workspace, fixture.TenantId);
        var otherWorkspace = AddProject("Remote workspace project", fixture.OtherWorkspace, fixture.TenantId);
        var foreign = AddProject("Foreign project", fixture.ForeignWorkspace, fixture.ForeignTenantId);
        var archived = AddProject("Archived project", fixture.Workspace, fixture.TenantId);
        archived.Status = ProjectStatus.Archived;
        var deleted = AddProject("Deleted project", fixture.Workspace, fixture.TenantId);
        deleted.MarkDeleted(Fixture.At);
        var dateless = AddProject("Dateless project", fixture.Workspace, fixture.TenantId);
        dateless.DueDate = null;
        var milestone = AddMilestone(local, "Alpha milestone", date);
        var task = AddTask(local, "Zulu task", date);
        var beforeMilestone = AddMilestone(local, "Before milestone", date.AddDays(-1));
        var afterMilestone = AddMilestone(local, "After milestone", date.AddDays(1));
        var beforeTask = AddTask(local, "Before task", date.AddDays(-1));
        var afterTask = AddTask(local, "After task", date.AddDays(1));
        AddMilestone(local, "Dateless milestone", null);
        AddTask(local, "Dateless task", null);
        AddMilestone(local, "Deleted milestone", date).MarkDeleted(Fixture.At);
        AddTask(local, "Deleted task", date).MarkDeleted(Fixture.At);
        foreach (var excluded in new[] { foreign, archived, deleted })
        {
            AddMilestone(excluded, "Excluded milestone", date);
            AddTask(excluded, "Excluded task", date);
        }
        await fixture.Seed.SaveChangesAsync();

        // The input is November 5 locally, but its UTC calendar date is November 4.
        var boundary = new DateTimeOffset(2026, 11, 5, 0, 30, 0, TimeSpan.FromHours(9));
        var rows = await fixture.Repository.ListProjectCalendarItemsAsync(new CalendarQuery(boundary, boundary));
        Assert.Equal([milestone.Id, local.Id, sameWorkspace.Id, otherWorkspace.Id, task.Id], rows.Select(item => item.Id));
        Assert.All(rows, item =>
        {
            Assert.Equal(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), item.StartsAt);
            Assert.Null(item.EndsAt);
        });
        var projectedProject = Assert.Single(rows, item => item.Id == local.Id);
        Assert.Equal("ProjectDeadline", projectedProject.ItemType);
        Assert.Equal(local.Id, projectedProject.ProjectId);
        Assert.Equal(local.WorkspaceId, projectedProject.WorkspaceId);
        Assert.Equal(group.Id, projectedProject.GroupId);
        Assert.Equal(local.Name, projectedProject.ScopeLabel);
        Assert.Equal(ProjectStatus.Active.ToString(), projectedProject.Status);
        Assert.Equal($"/projects/{local.Id}", projectedProject.Route);
        var projectedMilestone = Assert.Single(rows, item => item.Id == milestone.Id);
        Assert.Equal("Milestone", projectedMilestone.ItemType);
        Assert.Equal(local.Id, projectedMilestone.ProjectId);
        Assert.Equal(milestone.Name, projectedMilestone.Title);
        Assert.Equal(milestone.Status.ToString(), projectedMilestone.Status);
        Assert.Equal($"/projects/{local.Id}/milestones/{milestone.Id}", projectedMilestone.Route);
        var projectedTask = Assert.Single(rows, item => item.Id == task.Id);
        Assert.Equal("TaskDueDate", projectedTask.ItemType);
        Assert.Equal(local.Id, projectedTask.ProjectId);
        Assert.Equal(task.Title, projectedTask.Title);
        Assert.Equal(task.Status.ToString(), projectedTask.Status);
        Assert.Equal($"/tasks/{task.Id}", projectedTask.Route);

        var workspaceRows = await fixture.Repository.ListProjectCalendarItemsAsync(
            new CalendarQuery(boundary, boundary, WorkspaceId: fixture.Workspace.Id));
        Assert.Equal([milestone.Id, local.Id, sameWorkspace.Id, task.Id], workspaceRows.Select(item => item.Id));
        var groupRows = await fixture.Repository.ListProjectCalendarItemsAsync(
            new CalendarQuery(boundary, boundary, GroupId: group.Id));
        Assert.Equal([milestone.Id, local.Id, task.Id], groupRows.Select(item => item.Id));
        var projectRows = await fixture.Repository.ListProjectCalendarItemsAsync(
            new CalendarQuery(boundary, boundary, ProjectId: local.Id));
        Assert.Equal([milestone.Id, local.Id, task.Id], projectRows.Select(item => item.Id));
        Assert.Empty(await fixture.Repository.ListProjectCalendarItemsAsync(
            new CalendarQuery(boundary, boundary, WorkspaceId: fixture.OtherWorkspace.Id, ProjectId: local.Id)));
        Assert.Empty(await fixture.Repository.ListProjectCalendarItemsAsync(
            new CalendarQuery(boundary, boundary, ProjectId: foreign.Id)));

        var unbounded = await fixture.Repository.ListProjectCalendarItemsAsync(new CalendarQuery(ProjectId: local.Id));
        Assert.Equal([beforeMilestone.Id, beforeTask.Id, milestone.Id, local.Id, task.Id, afterMilestone.Id, afterTask.Id],
            unbounded.Select(item => item.Id));
        Assert.Empty(fixture.Scoped.ChangeTracker.Entries());
        Assert.All(fixture.Seed.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Equal(date, local.DueDate);
        Assert.Equal(date, milestone.DueDate);
        Assert.Equal(date, task.DueDate);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset At = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

        private Fixture(string connectionString)
        {
            Seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
            var tenant = new CurrentTenantService();
            tenant.SetTenant(TenantId, "synthetic-events");
            Scoped = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenant);
            Repository = new EventRepository(Scoped);
        }

        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid ForeignTenantId { get; } = Guid.NewGuid();
        public AppDbContext Seed { get; }
        public AppDbContext Scoped { get; }
        public EventRepository Repository { get; }
        public User Creator { get; private set; } = null!;
        public Workspace Workspace { get; private set; } = null!;
        public Workspace OtherWorkspace { get; private set; } = null!;
        public Workspace ForeignWorkspace { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(string connectionString)
        {
            var fixture = new Fixture(connectionString);
            fixture.Seed.Tenants.AddRange(
                new Tenant(fixture.TenantId) { Name = "Synthetic local", Slug = "synthetic-local", Status = TenantStatus.Active },
                new Tenant(fixture.ForeignTenantId) { Name = "Synthetic foreign", Slug = "synthetic-foreign", Status = TenantStatus.Active });
            fixture.Creator = fixture.AddUser("Creator");
            fixture.Workspace = fixture.AddWorkspace("Local", fixture.TenantId);
            fixture.OtherWorkspace = fixture.AddWorkspace("Other", fixture.TenantId);
            fixture.ForeignWorkspace = fixture.AddWorkspace("Foreign", fixture.ForeignTenantId);
            await fixture.Seed.SaveChangesAsync();
            return fixture;
        }

        public User AddUser(string displayName)
        {
            var user = new User
            {
                DisplayName = displayName, Email = $"{Guid.NewGuid():N}@example.invalid",
                NormalizedEmail = Guid.NewGuid().ToString("N"), Status = UserStatus.Active
            };
            Seed.Users.Add(user);
            return user;
        }

        private Workspace AddWorkspace(string name, Guid tenantId)
        {
            var workspace = new Workspace
            {
                TenantId = tenantId, Name = name, Slug = name.ToLowerInvariant(),
                CreatedByUserId = Creator.Id, Status = WorkspaceStatus.Active
            };
            Seed.Workspaces.Add(workspace);
            return workspace;
        }

        public ActivityEvent AddEvent(string title, DateTimeOffset startsAt, DateTimeOffset endsAt,
            EventStatus status = EventStatus.Published)
        {
            var activityEvent = new ActivityEvent
            {
                TenantId = TenantId, WorkspaceId = Workspace.Id, CreatedByUserId = Creator.Id,
                Title = title, StartsAt = startsAt, EndsAt = endsAt, Status = status
            };
            Seed.ActivityEvents.Add(activityEvent);
            return activityEvent;
        }

        public void AddAttendance(ActivityEvent activityEvent, User user, AttendanceStatus status)
        {
            Seed.EventAttendances.Add(new EventAttendance
            {
                TenantId = activityEvent.TenantId, EventId = activityEvent.Id, UserId = user.Id,
                Status = status, RespondedAt = At
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Scoped.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }
}
