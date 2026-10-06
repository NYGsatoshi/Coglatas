using System.Reflection;
using System.Text.Json;
using Coglatas.Application;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Coglatas.Tests.Projects;

public sealed class TaskDetailReadRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReadCapabilitiesMatchEachLivePrimitiveAndReflectRevocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        foreach (var workspaceRole in Enum.GetValues<WorkspaceRole>())
        foreach (var projectRole in Enum.GetValues<ProjectRole>().Cast<ProjectRole?>().Prepend(null))
        foreach (var visibility in Enum.GetValues<ProjectVisibility>().Cast<ProjectVisibility?>().Prepend(null))
        {
            fixture.WorkspaceMember.Role = workspaceRole;
            fixture.Project.Visibility = visibility;
            await fixture.SetProjectRoleAsync(projectRole);
            await AssertCapabilitiesAsync(fixture);
        }

        fixture.WorkspaceMember.Role = WorkspaceRole.Member;
        await fixture.SetProjectRoleAsync(ProjectRole.Contributor);
        foreach (var status in Enum.GetValues<ProjectStatus>())
        foreach (var workspaceStatus in Enum.GetValues<WorkspaceStatus>())
        {
            fixture.Project.Status = status;
            fixture.Workspace.Status = workspaceStatus;
            await fixture.Context.SaveChangesAsync();
            await AssertCapabilitiesAsync(fixture);
        }
        fixture.Project.Status = ProjectStatus.Active;
        fixture.Workspace.Status = WorkspaceStatus.Active;
        foreach (var membership in Enum.GetValues<MembershipStatus>())
        {
            fixture.WorkspaceMember.Status = membership;
            await fixture.Context.SaveChangesAsync();
            await AssertCapabilitiesAsync(fixture);
        }
        fixture.Task.MarkDeleted(DateTimeOffset.UtcNow);
        await fixture.Context.SaveChangesAsync();
        await AssertCapabilitiesAsync(fixture);
        Assert.Equal(new TaskReadCapabilities(false, false, false, false, false),
            await fixture.Authorization.GetReadCapabilitiesAsync(fixture.User.Id, Guid.NewGuid()));
    }

    private static async Task AssertCapabilitiesAsync(Fixture fixture)
    {
        var authorization = fixture.Authorization;
        var actor = fixture.User.Id;
        var id = fixture.Task.Id;
        Assert.Equal(new TaskReadCapabilities(
            await authorization.CanUpdateTask(actor, id), await authorization.CanAssignTask(actor, id),
            await authorization.CanDeleteTask(actor, id), await authorization.CanReviewTask(actor, id),
            await authorization.CanOverrideTaskReview(actor, id)),
            await authorization.GetReadCapabilitiesAsync(actor, id));
    }

    [Fact]
    public async Task ReviewerAssigneeAndSystemAdminCapabilitiesKeepTheirSeparateBoundaries()
    {
        await using var fixture = await Fixture.CreateAsync();
        foreach (var creator in new[] { fixture.User.Id, Guid.NewGuid() })
        foreach (var assignee in new Guid?[] { fixture.User.Id, null })
        foreach (var reviewer in new Guid?[] { fixture.User.Id, null })
        {
            fixture.Task.CreatedByUserId = creator;
            fixture.Task.PrimaryAssigneeUserId = assignee;
            fixture.Task.ReviewerUserId = reviewer;
            await fixture.Context.SaveChangesAsync();
            await AssertCapabilitiesAsync(fixture);
        }
        fixture.User.SystemRole = SystemRole.SystemAdmin;
        fixture.Context.WorkspaceMembers.Remove(fixture.WorkspaceMember);
        await fixture.SetProjectRoleAsync(null);
        foreach (var visibility in Enum.GetValues<ProjectVisibility>().Cast<ProjectVisibility?>().Prepend(null))
        {
            fixture.Project.Visibility = visibility;
            await fixture.Context.SaveChangesAsync();
            await AssertCapabilitiesAsync(fixture);
        }
        fixture.Workspace.Status = WorkspaceStatus.Archived;
        await fixture.Context.SaveChangesAsync();
        await AssertCapabilitiesAsync(fixture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SummaryMatchesCanonicalCalculatorAndCompositeResponse(bool weighted)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddChildrenAsync(weighted);
        await AssertResponseAsync(fixture);
        await AssertSummaryAsync(fixture);
    }

    [Fact]
    public async Task LeafAndCancelledChildrenPreserveStoredValuesAndZeroProgress()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AssertSummaryAsync(fixture);
        var child = fixture.Child("Cancelled", 1, fixture.Task.Id);
        child.Status = TaskItemStatus.Cancelled;
        child.PlannedEndDate = new DateOnly(2026, 10, 9);
        fixture.Context.TaskItems.Add(child);
        await fixture.Context.SaveChangesAsync();
        await AssertSummaryAsync(fixture);
        Assert.Equal(0, (await fixture.Repository.GetTaskDetailSummaryAsync(fixture.Project.Id, fixture.Task.Id))!.DerivedValues.ProgressPercent);
    }

    [Fact]
    public async Task CrossTenantAndUnauthorizedReadsRemainIndistinguishableFromMissing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.TenantScope.SetTenant(Guid.NewGuid(), "other");
        Assert.Null(await fixture.Repository.GetTaskDetailSummaryAsync(fixture.Project.Id, fixture.Task.Id));
        Assert.Equal(new TaskReadCapabilities(false, false, false, false, false),
            await fixture.Authorization.GetReadCapabilitiesAsync(fixture.User.Id, fixture.Task.Id));
        Assert.Equal("TASK_NOT_FOUND|Task not found.", (await fixture.Detail.GetDetailAsync(fixture.Task.Id)).Error);
        fixture.TenantScope.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        fixture.WorkspaceMember.Status = MembershipStatus.Suspended;
        await fixture.Context.SaveChangesAsync();
        Assert.Equal("TASK_NOT_FOUND|Task not found.", (await fixture.Detail.GetDetailAsync(fixture.Task.Id)).Error);
        Assert.Equal("TASK_NOT_FOUND|Task not found.", (await fixture.Detail.GetDetailAsync(Guid.NewGuid())).Error);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlCompositeKeepsResponseAndBoundsCommandsAndMaterialization()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await Fixture.CreateAsync(database);
                await fixture.AddChildrenAsync(weighted: true);
                await AssertResponseAsync(fixture);
                await AssertSummaryAsync(fixture);

                fixture.Context.ChangeTracker.Clear();
                int firstCommands;
                int firstReaderOperations;
                using (var measurement = fixture.Capture.Begin())
                {
                    Assert.True((await fixture.Detail.GetDetailAsync(fixture.Task.Id)).IsSuccess);
                    firstCommands = measurement.Snapshot().Count;
                    firstReaderOperations = measurement.Snapshot().Sum(command => command.ReadOperations ?? 0);
                }
                fixture.Context.TaskItems.AddRange(Enumerable.Range(0, 300).Select(index =>
                {
                    var unrelated = fixture.Child("Unrelated " + index, index + 100);
                    unrelated.ParentTaskItemId = null;
                    return unrelated;
                }));
                await fixture.Context.SaveChangesAsync();
                fixture.Context.ChangeTracker.Clear();
                using (var measurement = fixture.Capture.Begin())
                {
                    Assert.True((await fixture.Detail.GetDetailAsync(fixture.Task.Id)).IsSuccess);
                    Assert.Equal(firstCommands, measurement.Snapshot().Count);
                    Assert.Equal(firstReaderOperations, measurement.Snapshot().Sum(command => command.ReadOperations ?? 0));
                    Assert.DoesNotContain(fixture.Context.ChangeTracker.Entries<TaskItem>(),
                        entry => entry.Entity.Title.StartsWith("Unrelated", StringComparison.Ordinal));
                }
                fixture.Context.ChangeTracker.Clear();
                using (var measurement = fixture.Capture.Begin())
                {
                    Assert.NotNull(await fixture.Repository.GetTaskDetailSummaryAsync(fixture.Project.Id, fixture.Task.Id));
                    var command = Assert.Single(measurement.Snapshot());
                    Assert.InRange(command.ReadOperations!.Value, 1, 2);
                    Assert.Empty(fixture.Context.ChangeTracker.Entries());
                }
                int oldCommands;
                using (var measurement = fixture.Capture.Begin())
                {
                    var legacy = fixture.LegacyDetail();
                    Assert.True((await legacy.GetDetailAsync(fixture.Task.Id)).IsSuccess);
                    oldCommands = measurement.Snapshot().Count;
                }
                Assert.True(firstCommands < oldCommands - 30, $"Before {oldCommands}; after {firstCommands}");
                output.WriteLine($"Task detail fixture scoped commands: legacy={oldCommands}, fixed={firstCommands}; unrelated Tasks=0/300, summary rows<=2.");
            });
    }

    private static async Task AssertSummaryAsync(Fixture fixture)
    {
        var tasks = await fixture.Repository.ListTasksAsync(fixture.Project.Id);
        var checklist = await fixture.Repository.ListChecklistAsync(fixture.Task.Id);
        var expected = new TaskDetailSummaryReadRow(
            ParentTaskDerivedValuesCalculator.Calculate(fixture.Task, tasks, CategoryOf),
            new(checklist.Count(item => item.IsCompleted), checklist.Count,
                await fixture.Repository.CountTaskCommentsAsync(fixture.Task.Id),
                (await fixture.Repository.ListWorkItemLabelsAsync(fixture.Task.Id)).Count,
                tasks.Count(child => child.ParentTaskItemId == fixture.Task.Id && !child.DeletedAt.HasValue)));
        Assert.Equal(expected, await fixture.Repository.GetTaskDetailSummaryAsync(fixture.Project.Id, fixture.Task.Id));
        Assert.Null(await fixture.Repository.GetTaskDetailSummaryAsync(Guid.NewGuid(), fixture.Task.Id));
    }

    private static async Task AssertResponseAsync(Fixture fixture)
    {
        var before = await fixture.LegacyDetail().GetDetailAsync(fixture.Task.Id);
        var after = await fixture.Detail.GetDetailAsync(fixture.Task.Id);
        Assert.True(before.IsSuccess, before.Error);
        Assert.True(after.IsSuccess, after.Error);
        Assert.Equal(JsonSerializer.Serialize(before.Value), JsonSerializer.Serialize(after.Value));
    }

    private static TaskStageCategory CategoryOf(TaskItem task) => task.WorkflowStage?.InternalCategory ?? task.Status switch
    {
        TaskItemStatus.InProgress => TaskStageCategory.InProgress, TaskItemStatus.WaitingReview => TaskStageCategory.Review,
        TaskItemStatus.Completed => TaskStageCategory.Done, TaskItemStatus.Cancelled => TaskStageCategory.Cancelled, _ => TaskStageCategory.Todo
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public CurrentTenantService TenantScope { get; } = new();
        public PerformanceDbCapture Capture { get; } = new();
        public AppDbContext Context { get; private set; } = null!;
        public Tenant Tenant { get; } = new() { Name = "Regression", DisplayName = "Regression", Slug = Guid.NewGuid().ToString("N") };
        public User User { get; } = new() { DisplayName = "Reader", Email = "reader@example.test", NormalizedEmail = "READER@EXAMPLE.TEST", PasswordHash = "hash", Status = UserStatus.Active };
        public Workspace Workspace { get; private set; } = null!;
        public WorkspaceMember WorkspaceMember { get; private set; } = null!;
        public Project Project { get; private set; } = null!;
        public TaskItem Task { get; private set; } = null!;
        public ProjectRepository Repository => new(Context);
        public ProjectAuthorizationService Authorization => Provider.GetRequiredService<ProjectAuthorizationService>();
        public ITaskSubresourceService Detail => Provider.GetRequiredService<ITaskSubresourceService>();
        private ServiceProvider Provider { get; set; } = null!;

        public static async Task<Fixture> CreateAsync(string? database = null)
        {
            var fixture = new Fixture();
            fixture.TenantScope.SetPlatformScope();
            var options = new DbContextOptionsBuilder<AppDbContext>().AddInterceptors(fixture.Capture);
            if (database is null) options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            else options.UseNpgsql(database);
            fixture.Context = new(options.Options, fixture.TenantScope);
            fixture.Context.AddRange(fixture.Tenant, fixture.User);
            await fixture.Context.SaveChangesAsync();
            fixture.TenantScope.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
            fixture.Context.TenantUsers.Add(new TenantUser { TenantId = fixture.Tenant.Id, UserId = fixture.User.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
            await fixture.Context.SaveChangesAsync();
            fixture.Workspace = new() { Name = "Workspace", Slug = "workspace", CreatedByUserId = fixture.User.Id, Status = WorkspaceStatus.Active, TimeZone = "UTC" };
            fixture.WorkspaceMember = new() { WorkspaceId = fixture.Workspace.Id, UserId = fixture.User.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active };
            fixture.Context.AddRange(fixture.Workspace, fixture.WorkspaceMember);
            fixture.Project = new() { WorkspaceId = fixture.Workspace.Id, Name = "Project", Slug = "project", OwnerUserId = fixture.User.Id, CreatedByUserId = fixture.User.Id, Status = ProjectStatus.Active, Visibility = ProjectVisibility.WorkspaceVisible, ActivationState = ProjectActivationState.Activated, ActivationVersion = 1, ActivatedAtUtc = DateTimeOffset.UtcNow };
            fixture.Context.Projects.Add(fixture.Project);
            fixture.Task = fixture.Child("Parent", 0);
            fixture.Task.ParentTaskItemId = null;
            fixture.Task.ProgressPercent = 99;
            fixture.Task.StartDate = new DateOnly(2026, 9, 1);
            fixture.Task.DueDate = new DateOnly(2026, 12, 1);
            fixture.Context.TaskItems.Add(fixture.Task);
            await fixture.Context.SaveChangesAsync();
            await fixture.SetProjectRoleAsync(ProjectRole.Contributor);
            var services = new ServiceCollection().AddLogging();
            services.AddApplication();
            services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database ?? "Host=unused" }).Build());
            services.AddSingleton(fixture.Context);
            services.AddSingleton<ICurrentTenant>(fixture.TenantScope);
            services.AddSingleton<ICurrentUser>(new CurrentUser(fixture.User.Id));
            services.AddSingleton<IClock, FixedClock>();
            fixture.Provider = services.BuildServiceProvider();
            return fixture;
        }

        public TaskItem Child(string title, long sortKey, Guid? parentId = null) => new()
        {
            ProjectId = Project.Id, WorkspaceId = Workspace.Id, CreatedByUserId = User.Id,
            PrimaryAssigneeUserId = User.Id, Title = title,
            ParentTaskItemId = parentId, SortKey = sortKey, VersionNo = 1
        };

        public async Task SetProjectRoleAsync(ProjectRole? role)
        {
            var member = await Context.ProjectMembers.FirstOrDefaultAsync(item => item.ProjectId == Project.Id && item.UserId == User.Id);
            if (role is null && member is not null) Context.Remove(member);
            if (role.HasValue && member is null) Context.Add(new ProjectMember { ProjectId = Project.Id, UserId = User.Id, Role = role.Value });
            if (role.HasValue && member is not null) member.Role = role.Value;
            await Context.SaveChangesAsync();
        }

        public async Task AddChildrenAsync(bool weighted)
        {
            var first = Child("First", 1, Task.Id); first.ProgressPercent = 25; first.EstimatedEffortMinutes = weighted ? 10 : null; first.StartDate = new DateOnly(2026, 10, 1);
            var second = Child("Second", 2, Task.Id); second.ProgressPercent = 75; second.EstimatedEffortMinutes = 30; second.DueDate = new DateOnly(2026, 10, 6);
            var cancelled = Child("Cancelled", 3, Task.Id); cancelled.Status = TaskItemStatus.Cancelled; cancelled.DueDate = new DateOnly(2026, 10, 8);
            var deleted = Child("Deleted", 4, Task.Id); deleted.MarkDeleted(DateTimeOffset.UtcNow); deleted.DueDate = new DateOnly(2099, 1, 1);
            Context.TaskItems.AddRange(first, second, cancelled, deleted);
            Context.TaskChecklistItems.AddRange(new TaskChecklistItem { TaskItemId = Task.Id, Text = "Complete", IsCompleted = true }, new TaskChecklistItem { TaskItemId = Task.Id, Text = "Pending" });
            var comment = new TaskComment { TaskItemId = Task.Id, ProjectId = Project.Id, WorkspaceId = Workspace.Id, AuthorUserId = User.Id, BodyPlainText = "Tombstone" };
            comment.MarkDeleted(DateTimeOffset.UtcNow);
            Context.TaskComments.Add(comment);
            await Context.SaveChangesAsync();
        }

        public ITaskSubresourceService LegacyDetail()
        {
            var proxy = DispatchProxy.Create(typeof(IProjectRepository), typeof(LegacyRepository));
            ((LegacyRepository)proxy).Target = Repository;
            var repository = (IProjectRepository)proxy;
            // Activate only read services; their dependencies come from the same
            // production fixture. DispatchProxy disables the new scalar path.
            var commands = ActivatorUtilities.CreateInstance<TaskCommandService>(Provider, repository, new LegacyAuthorization(Authorization));
            return ActivatorUtilities.CreateInstance<TaskSubresourceService>(Provider, repository, new LegacyAuthorization(Authorization), commands);
        }

        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); await Context.DisposeAsync(); Capture.Dispose(); }
    }

    public class LegacyRepository : DispatchProxy
    {
        public IProjectRepository Target { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == nameof(IProjectRepository.GetTaskDetailSummaryAsync)
            ? Task.FromResult<TaskDetailSummaryReadRow?>(null) : method.Invoke(Target, args);
    }

    private sealed class LegacyAuthorization(ProjectAuthorizationService target) : ITaskAuthorizationService
    {
        public Task<bool> CanCreateTask(Guid user, Guid id, CancellationToken ct = default) => target.CanCreateTask(user, id, ct);
        public Task<bool> CanUpdateTask(Guid user, Guid id, CancellationToken ct = default) => target.CanUpdateTask(user, id, ct);
        public Task<bool> CanAssignTask(Guid user, Guid id, CancellationToken ct = default) => target.CanAssignTask(user, id, ct);
        public Task<bool> CanDeleteTask(Guid user, Guid id, CancellationToken ct = default) => target.CanDeleteTask(user, id, ct);
        public Task<bool> CanReviewTask(Guid user, Guid id, CancellationToken ct = default) => target.CanReviewTask(user, id, ct);
        public Task<bool> CanOverrideTaskReview(Guid user, Guid id, CancellationToken ct = default) => target.CanOverrideTaskReview(user, id, ct);
    }

    private sealed class CurrentUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero); }
}
