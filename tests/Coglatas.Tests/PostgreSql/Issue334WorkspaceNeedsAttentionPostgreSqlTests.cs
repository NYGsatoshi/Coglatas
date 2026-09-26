using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

[Trait("Scope", "Issue334")]
public sealed class Issue334WorkspaceNeedsAttentionPostgreSqlTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ProjectionFollowsActionableDomainLifecycleAndAuthorization()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            connectionString,
            async testConnectionString =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);
                var tenantScope = new CurrentTenantService();
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(testConnectionString)
                    .Options;
                await using var dbContext = new AppDbContext(options, tenantScope);

                var tenant = new Tenant
                {
                    Name = "Issue 334 Tenant",
                    DisplayName = "Issue 334 Tenant",
                    Slug = "issue-334",
                    Status = TenantStatus.Active
                };
                var actor = NewUser("actor");
                var other = NewUser("other");

                tenantScope.SetPlatformScope();
                dbContext.Tenants.Add(tenant);
                dbContext.Users.AddRange(actor, other);
                await dbContext.SaveChangesAsync();

                tenantScope.SetTenant(tenant.Id, tenant.Slug);
                dbContext.TenantUsers.AddRange(
                    new TenantUser
                    {
                        TenantId = tenant.Id,
                        UserId = actor.Id,
                        Role = TenantUserRole.Member,
                        Status = TenantUserStatus.Active,
                        JoinedAt = Now
                    },
                    new TenantUser
                    {
                        TenantId = tenant.Id,
                        UserId = other.Id,
                        Role = TenantUserRole.Member,
                        Status = TenantUserStatus.Active,
                        JoinedAt = Now
                    });

                var workspace = NewWorkspace(tenant.Id, actor.Id, "visible-workspace");
                var hiddenWorkspace = NewWorkspace(tenant.Id, other.Id, "hidden-workspace");
                dbContext.Workspaces.AddRange(workspace, hiddenWorkspace);

                var actorMembership = new WorkspaceMember
                {
                    TenantId = tenant.Id,
                    WorkspaceId = workspace.Id,
                    UserId = actor.Id,
                    Role = WorkspaceRole.Member,
                    Status = MembershipStatus.Active,
                    JoinedAt = Now
                };
                dbContext.WorkspaceMembers.AddRange(
                    actorMembership,
                    new WorkspaceMember
                    {
                        TenantId = tenant.Id,
                        WorkspaceId = hiddenWorkspace.Id,
                        UserId = other.Id,
                        Role = WorkspaceRole.Owner,
                        Status = MembershipStatus.Active,
                        JoinedAt = Now
                    });

                var project = NewProject(tenant.Id, workspace.Id, actor.Id, "visible-project");
                var hiddenProject = NewProject(
                    tenant.Id,
                    hiddenWorkspace.Id,
                    other.Id,
                    "hidden-project");
                dbContext.Projects.AddRange(project, hiddenProject);
                dbContext.ProjectMembers.AddRange(
                    new ProjectMember
                    {
                        TenantId = tenant.Id,
                        ProjectId = project.Id,
                        UserId = actor.Id,
                        Role = ProjectRole.Contributor,
                        JoinedAt = Now
                    },
                    new ProjectMember
                    {
                        TenantId = tenant.Id,
                        ProjectId = hiddenProject.Id,
                        UserId = other.Id,
                        Role = ProjectRole.Owner,
                        JoinedAt = Now
                    });

                var reviewTask = NewTask(
                    tenant.Id,
                    workspace.Id,
                    project.Id,
                    actor.Id,
                    "Sensitive review title");
                reviewTask.Status = TaskItemStatus.WaitingReview;
                reviewTask.ReviewerUserId = actor.Id;
                reviewTask.ReviewStatus = TaskReviewStatus.Submitted;
                reviewTask.ReviewSubmittedAt = Now.AddMinutes(-20);

                var failedTask = NewTask(
                    tenant.Id,
                    workspace.Id,
                    project.Id,
                    actor.Id,
                    "Sensitive failed Research title");
                failedTask.Status = TaskItemStatus.InProgress;
                failedTask.PrimaryAssigneeUserId = actor.Id;

                var hiddenTask = NewTask(
                    tenant.Id,
                    hiddenWorkspace.Id,
                    hiddenProject.Id,
                    other.Id,
                    "Hidden target title");
                hiddenTask.Status = TaskItemStatus.WaitingReview;
                hiddenTask.ReviewerUserId = actor.Id;
                hiddenTask.ReviewStatus = TaskReviewStatus.Submitted;
                hiddenTask.ReviewSubmittedAt = Now.AddMinutes(-10);
                dbContext.TaskItems.AddRange(reviewTask, failedTask, hiddenTask);
                await dbContext.SaveChangesAsync();

                var failedRun = NewRun(
                    tenant.Id,
                    workspace.Id,
                    project.Id,
                    failedTask.Id,
                    actor.Id,
                    Now.AddMinutes(-15),
                    TaskExecutionRunStatus.Accepted);
                dbContext.TaskExecutionRuns.Add(failedRun);
                await dbContext.SaveChangesAsync();

                failedRun.Status = TaskExecutionRunStatus.Queued;
                failedRun.QueuedAtUtc = Now.AddMinutes(-14).AddSeconds(-50);
                failedRun.VersionNo++;
                await dbContext.SaveChangesAsync();

                failedRun.Status = TaskExecutionRunStatus.Running;
                failedRun.StartedAtUtc = Now.AddMinutes(-14).AddSeconds(-40);
                failedRun.VersionNo++;
                await dbContext.SaveChangesAsync();

                failedRun.Status = TaskExecutionRunStatus.Failed;
                failedRun.FinishedAtUtc = Now.AddMinutes(-14);
                failedRun.FailureCode = "SENSITIVE_INTERNAL_FAILURE_CODE";
                failedRun.VersionNo++;
                await dbContext.SaveChangesAsync();
                dbContext.ChangeTracker.Clear();

                var dashboard = new WorkspaceDashboardQuery(
                    dbContext,
                    new MessagingRepository(dbContext),
                    new FixedClock(Now));

                var initial = await dashboard.ListAsync(actor.Id);
                var card = Assert.Single(initial);
                Assert.Equal(workspace.Id, card.Id);
                Assert.Equal(2, card.NeedsAttentionCount);
                Assert.NotNull(card.NeedsAttentionItems);
                Assert.Collection(
                    card.NeedsAttentionItems!,
                    item => Assert.Equal(WorkspaceNeedsAttentionKind.ResearchFailed, item.Kind),
                    item => Assert.Equal(WorkspaceNeedsAttentionKind.ReviewRequired, item.Kind));
                Assert.All(card.NeedsAttentionItems!, item =>
                {
                    Assert.StartsWith($"/projects/{project.Id:D}/tasks/", item.TargetRoute);
                    Assert.DoesNotContain("Sensitive", item.TargetRoute, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("FAILURE", item.TargetRoute, StringComparison.OrdinalIgnoreCase);
                });
                Assert.DoesNotContain(initial, item => item.Id == hiddenWorkspace.Id);

                var persistedReviewTask = await dbContext.TaskItems.SingleAsync(task => task.Id == reviewTask.Id);
                persistedReviewTask.ReviewStatus = TaskReviewStatus.Accepted;
                persistedReviewTask.ReviewResolvedAt = Now;
                persistedReviewTask.ReviewResolvedByUserId = actor.Id;

                var retryRun = NewRun(
                    tenant.Id,
                    workspace.Id,
                    project.Id,
                    failedTask.Id,
                    actor.Id,
                    Now.AddMinutes(-5),
                    TaskExecutionRunStatus.Accepted);
                dbContext.TaskExecutionRuns.Add(retryRun);
                await dbContext.SaveChangesAsync();
                dbContext.ChangeTracker.Clear();

                var resolved = Assert.Single(await dashboard.ListAsync(actor.Id));
                Assert.Equal(0, resolved.NeedsAttentionCount);
                Assert.Empty(resolved.NeedsAttentionItems!);

                var membership = await dbContext.WorkspaceMembers.SingleAsync(member =>
                    member.Id == actorMembership.Id);
                membership.Status = MembershipStatus.Suspended;
                await dbContext.SaveChangesAsync();
                dbContext.ChangeTracker.Clear();

                Assert.Empty(await dashboard.ListAsync(actor.Id));
            });
    }

    private static User NewUser(string localPart) => new()
    {
        DisplayName = localPart,
        Email = $"{localPart}@issue334.example",
        NormalizedEmail = $"{localPart.ToUpperInvariant()}@ISSUE334.EXAMPLE",
        PasswordHash = "test-password-hash",
        Status = UserStatus.Active,
        SystemRole = SystemRole.NormalUser
    };

    private static Workspace NewWorkspace(Guid tenantId, Guid creatorId, string slug) => new()
    {
        TenantId = tenantId,
        CreatedByUserId = creatorId,
        Name = slug,
        Slug = slug,
        Status = WorkspaceStatus.Active
    };

    private static Project NewProject(
        Guid tenantId,
        Guid workspaceId,
        Guid ownerId,
        string slug) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        OwnerUserId = ownerId,
        CreatedByUserId = ownerId,
        Name = slug,
        Slug = slug,
        Status = ProjectStatus.Active,
        VersionNo = 1
    };

    private static TaskItem NewTask(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid creatorId,
        string title) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        ProjectId = projectId,
        CreatedByUserId = creatorId,
        Title = title,
        Status = TaskItemStatus.InProgress,
        Priority = TaskPriority.Medium,
        VersionNo = 1
    };

    private static TaskExecutionRun NewRun(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        Guid requesterId,
        DateTimeOffset requestedAt,
        TaskExecutionRunStatus status) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        ProjectId = projectId,
        TaskItemId = taskId,
        RequestedByUserId = requesterId,
        RequestedAtUtc = requestedAt,
        RuntimeProvider = TaskExecutionProvider.FirstPartyProjectFilesRuntimeV1,
        RuntimeContractVersion = TaskExecutionRun.RuntimeContractVersion1,
        Status = status,
        VersionNo = 1,
        SnapshotSchemaVersion = TaskExecutionRun.CurrentSnapshotSchemaVersion,
        SnapshotScopeOrigin = TaskExecutionScopeOrigin.ProjectDefault,
        SnapshotProjectScopeVersion = 1,
        SnapshotWebEnabled = false,
        SnapshotProjectFilesEnabled = true
    };

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
