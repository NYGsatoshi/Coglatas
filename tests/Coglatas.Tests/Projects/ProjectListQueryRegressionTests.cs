using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed class ProjectListQueryRegressionTests
{
    [Theory]
    [InlineData("I")]
    [InlineData("i")]
    [InlineData("İ")]
    [InlineData("ı")]
    [InlineData("Σ")]
    [InlineData("東京")]
    public async Task NonemptySearchRetainsOrdinalCompatibilityPath(string search)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null(await fixture.Repository.ListVisiblePageAsync(fixture.User.Id, new ProjectListQuery(Search: search)));
        Assert.Null(await fixture.Repository.ListTasksPageAsync(Guid.NewGuid(), new TaskListQuery(Search: search)));
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner, null, ProjectVisibility.WorkspaceVisible, false)]
    [InlineData(WorkspaceRole.Admin, ProjectRole.Viewer, ProjectVisibility.WorkspaceVisible, false)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Contributor, ProjectVisibility.WorkspaceVisible, false)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Reviewer, ProjectVisibility.MembersOnly, false)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Owner, ProjectVisibility.Restricted, false)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Manager, ProjectVisibility.MembersOnly, false)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Viewer, ProjectVisibility.MembersOnly, false)]
    [InlineData(WorkspaceRole.Owner, ProjectRole.Viewer, ProjectVisibility.Restricted, false)]
    [InlineData(WorkspaceRole.Member, null, ProjectVisibility.WorkspaceVisible, false)]
    [InlineData(WorkspaceRole.Member, null, ProjectVisibility.WorkspaceVisible, true)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Viewer, null, true)]
    [InlineData(WorkspaceRole.Member, ProjectRole.Contributor, null, false)]
    [InlineData(WorkspaceRole.ReadOnly, ProjectRole.Owner, ProjectVisibility.WorkspaceVisible, false)]
    public async Task BatchedCreateCapabilityMatchesAuthoritativeAuthorization(
        WorkspaceRole workspaceRole, ProjectRole? projectRole, ProjectVisibility? visibility, bool managesGroup)
        => await AssertCapabilityAsync(workspaceRole, projectRole, visibility, managesGroup, false);

    private static async Task AssertCapabilityAsync(
        WorkspaceRole workspaceRole, ProjectRole? projectRole, ProjectVisibility? visibility, bool managesGroup, bool postgres)
    {
        await using var fixture = await Fixture.CreateAsync(workspaceRole, postgres);
        var group = new Group { WorkspaceId = fixture.Workspace.Id, Name = "Group", Slug = "group", CreatedByUserId = fixture.User.Id };
        fixture.Context.Groups.Add(group);
        var project = fixture.Project("Capability", visibility: visibility);
        project.GroupId = group.Id;
        fixture.Context.Projects.Add(project);
        if (projectRole.HasValue)
            fixture.Context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = fixture.User.Id, Role = projectRole.Value });
        if (managesGroup)
            fixture.Context.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = fixture.User.Id, Role = GroupRole.Admin });
        await fixture.Context.SaveChangesAsync();

        var workspaces = new WorkspaceRepository(fixture.Context);
        var groups = new GroupRepository(fixture.Context);
        var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(fixture.Context), workspaces);
        var authoritative = new ProjectAuthorizationService(fixture.Repository, workspaceAuthorization,
            new GroupAuthorizationService(groups, workspaces, workspaceAuthorization), groups);
        var allowed = await fixture.Repository.ListTaskCreationAllowedProjectIdsAsync(fixture.User.Id, [project.Id]);
        Assert.Equal(await authoritative.CanCreateTask(fixture.User.Id, project.Id), allowed!.Contains(project.Id));

        fixture.Workspace.Status = WorkspaceStatus.Archived;
        await fixture.Context.SaveChangesAsync();
        Assert.False(await authoritative.CanCreateTask(fixture.User.Id, project.Id));
        Assert.Empty((await fixture.Repository.ListTaskCreationAllowedProjectIdsAsync(fixture.User.Id, [project.Id]))!);
    }

    [Fact]
    public async Task ProjectPageKeepsFilteredCountScopeAndStableTies()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Project("Same", Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = fixture.Project("Same", Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var deleted = fixture.Project("Same");
        deleted.MarkDeleted(DateTimeOffset.UtcNow);
        var hidden = fixture.Project("Same", visibility: ProjectVisibility.MembersOnly);
        var completed = fixture.Project("Same");
        completed.Status = ProjectStatus.Completed;
        fixture.Context.Projects.AddRange(second, first, deleted, hidden, completed);
        await fixture.Context.SaveChangesAsync();
        var query = new ProjectListQuery(WorkspaceId: fixture.Workspace.Id, Status: ProjectStatus.Active, PageSize: 1);
        var page1 = await fixture.Repository.ListVisiblePageAsync(fixture.User.Id, query);
        var page2 = await fixture.Repository.ListVisiblePageAsync(fixture.User.Id, query with { Page = 2 });
        Assert.Equal(2, page1!.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page1.Items).Id);
        Assert.Equal(second.Id, Assert.Single(page2!.Items).Id);
        Assert.Empty((await fixture.Repository.ListVisiblePageAsync(fixture.User.Id, query with { WorkspaceId = Guid.NewGuid() }))!.Items);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TaskPageDerivesParentsFromChildrenOutsidePageAndFilter(bool weighted)
        => await AssertTaskPageAsync(weighted, false);

    private static async Task AssertTaskPageAsync(bool weighted, bool postgres)
    {
        await using var fixture = await Fixture.CreateAsync(postgres: postgres);
        var project = fixture.Project("Tasks");
        fixture.Context.Projects.Add(project);
        var parent = fixture.Task(project, "Parent", 0);
        parent.Priority = TaskPriority.Critical;
        parent.PlannedStartDate = new DateOnly(2099, 1, 1);
        parent.PlannedEndDate = new DateOnly(2099, 2, 1);
        parent.ProgressPercent = 99;
        var first = fixture.Task(project, "Child first", 1, parent.Id);
        first.PlannedStartDate = new DateOnly(2026, 1, 2);
        first.PlannedEndDate = new DateOnly(2026, 1, 10);
        first.ProgressPercent = 25;
        first.EstimatedEffortMinutes = weighted ? 10 : null;
        var second = fixture.Task(project, "Child second", 2, parent.Id);
        second.StartDate = new DateOnly(2026, 1, 1);
        second.DueDate = new DateOnly(2026, 1, 12);
        second.ProgressPercent = 75;
        second.EstimatedEffortMinutes = 30;
        var cancelled = fixture.Task(project, "Cancelled", 3, parent.Id);
        cancelled.Status = TaskItemStatus.Cancelled;
        cancelled.PlannedEndDate = new DateOnly(2026, 1, 15);
        cancelled.ProgressPercent = 100;
        var deleted = fixture.Task(project, "Deleted", 4, parent.Id);
        deleted.MarkDeleted(DateTimeOffset.UtcNow);
        deleted.PlannedEndDate = new DateOnly(2099, 1, 1);
        fixture.Context.TaskItems.AddRange(parent, first, second, cancelled, deleted);
        fixture.Context.Artifacts.Add(new Artifact { ProjectId = project.Id, TaskItemId = parent.Id, Name = "Proof", CreatedByUserId = fixture.User.Id });
        await fixture.Context.SaveChangesAsync();

        var page = await fixture.Repository.ListTasksPageAsync(project.Id, new TaskListQuery(Priority: TaskPriority.Critical, PageSize: 1));
        var row = Assert.Single(page!.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(parent.Id, row.Task.Id);
        Assert.True(row.HasArtifact);
        Assert.Equal(ParentTaskDerivedValuesCalculator.Calculate(parent, [first, second, cancelled, deleted], CategoryOf), row.DerivedValues);
        Assert.Equal(weighted ? 63 : 50, row.DerivedValues.ProgressPercent);
        Assert.Equal(new DateOnly(2026, 1, 15), row.DerivedValues.PlannedEndDate);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlTranslatesBoundedParentAggregatesAndBatchedCreateScope()
    {
        await AssertTaskPageAsync(true, true);
        await AssertTaskPageAsync(false, true);
        await AssertCapabilityAsync(WorkspaceRole.Member, null, ProjectVisibility.WorkspaceVisible, true, true);
    }

    [Fact]
    public async Task TaskPageUsesAssignmentFilterBeforeCountingAndPaging()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.Project("Tasks");
        fixture.Context.Projects.Add(project);
        var tasks = Enumerable.Range(0, 8).Select(index => fixture.Task(project, "Same", index)).ToArray();
        fixture.Context.TaskItems.AddRange(tasks);
        fixture.Context.TaskAssignments.AddRange(tasks.Where((_, index) => index % 2 == 0).Select(task =>
            new TaskAssignment { TaskItemId = task.Id, UserId = fixture.User.Id, AssignedByUserId = fixture.User.Id, Role = TaskAssignmentRole.Assignee }));
        await fixture.Context.SaveChangesAsync();
        var page = await fixture.Repository.ListTasksPageAsync(project.Id, new TaskListQuery(AssignedUserId: fixture.User.Id, Page: 2, PageSize: 2));
        Assert.Equal(4, page!.TotalCount);
        Assert.Equal(tasks.Where((_, index) => index % 2 == 0).Skip(2).Select(task => task.Id), page.Items.Select(row => row.Task.Id));
    }

    private static TaskStageCategory CategoryOf(TaskItem task) => task.Status == TaskItemStatus.Cancelled ? TaskStageCategory.Cancelled : TaskStageCategory.Todo;

    private sealed class Fixture(AppDbContext context, User user, Workspace workspace) : IAsyncDisposable
    {
        public AppDbContext Context { get; } = context;
        public User User { get; } = user;
        public Workspace Workspace { get; } = workspace;
        public ProjectRepository Repository { get; } = new(context);

        public static async Task<Fixture> CreateAsync(WorkspaceRole role = WorkspaceRole.Member, bool postgres = false)
        {
            var tenantScope = new CurrentTenantService();
            tenantScope.SetPlatformScope();
            var suffix = Guid.NewGuid().ToString("N");
            var options = new DbContextOptionsBuilder<AppDbContext>();
            if (postgres) options.UseNpgsql(PostgreSqlTestEnvironment.RequireConnectionString());
            else options.UseInMemoryDatabase(suffix);
            var context = new AppDbContext(options.Options, tenantScope);
            if (postgres) await context.Database.BeginTransactionAsync();
            var tenant = new Tenant { Name = "Regression", DisplayName = "Regression", Slug = suffix };
            var user = new User { DisplayName = "Reader", Email = $"{suffix}@example.test", NormalizedEmail = $"{suffix.ToUpperInvariant()}@EXAMPLE.TEST", PasswordHash = "hash", Status = UserStatus.Active };
            context.AddRange(tenant, user);
            await context.SaveChangesAsync();
            tenantScope.SetTenant(tenant.Id, tenant.Slug);
            var workspace = new Workspace { Name = "Workspace", Slug = "workspace", CreatedByUserId = user.Id, Status = WorkspaceStatus.Active };
            context.Workspaces.Add(workspace);
            context.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = role, Status = MembershipStatus.Active });
            await context.SaveChangesAsync();
            return new Fixture(context, user, workspace);
        }

        public Project Project(string name, Guid? id = null, ProjectVisibility? visibility = ProjectVisibility.WorkspaceVisible) => new()
        {
            Id = id ?? Guid.NewGuid(), WorkspaceId = Workspace.Id, Name = name, Slug = Guid.NewGuid().ToString("N"),
            OwnerUserId = User.Id, CreatedByUserId = User.Id, Status = ProjectStatus.Active,
            Visibility = visibility, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            ActivationVersion = 1, VersionNo = 1
        };

        public TaskItem Task(Project project, string title, long sortKey, Guid? parentId = null) => new()
        {
            WorkspaceId = Workspace.Id, ProjectId = project.Id, CreatedByUserId = User.Id,
            Title = title, SortKey = sortKey, ParentTaskItemId = parentId
        };

        public async ValueTask DisposeAsync()
        {
            if (Context.Database.CurrentTransaction is { } transaction)
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
            }
            await Context.DisposeAsync();
        }
    }
}
