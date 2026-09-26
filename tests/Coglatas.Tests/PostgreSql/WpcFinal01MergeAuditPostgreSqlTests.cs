using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class WpcFinal01MergeAuditPostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 7, 45, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02A")]
    public async Task VisibilityCapabilityGrantAllowsExplicitVisibilityMutation()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedAsync(database, "visibility-capability", ProjectVisibility.MembersOnly);

            await using var db = CreateTenantContext(database, graph);
            db.Set<CapabilityGrant>().Add(new CapabilityGrant
            {
                TenantId = graph.TenantId,
                SubjectUserId = graph.ReaderUserId,
                CapabilityKey = CapabilityKeys.ProjectVisibilityManage,
                ScopeType = CapabilityScopeType.Workspace,
                ScopeId = graph.WorkspaceId,
                GrantedByUserId = graph.OwnerUserId,
                GrantedAt = Now.AddMinutes(-1),
                ExpiresAt = Now.AddHours(1),
                VersionNo = 1
            });
            await db.SaveChangesAsync();

            var currentTenant = TenantScope(graph);
            await using var commandDb = new AppDbContext(Options(database), currentTenant);
            var clock = new FixedClock(Now);
            var currentUser = new TestCurrentUser(graph.ReaderUserId);
            var projects = new ProjectRepository(commandDb);
            var workspaces = new WorkspaceRepository(commandDb);
            var outbox = new TransactionalOutbox(new OutboxEventRepository(commandDb), currentTenant, clock);
            var evaluator = new CapabilityGrantEvaluator(
                new CapabilityGrantRepository(commandDb),
                new TenantRepository(commandDb),
                workspaces,
                currentTenant,
                clock);
            var service = new ProjectVisibilityService(
                projects,
                workspaces,
                evaluator,
                currentUser,
                currentTenant,
                new DbAuditLogger(commandDb, clock, currentUser, currentTenant),
                new BusinessInvalidationPublisher(outbox, currentTenant, clock),
                new AuthorizationStateChangePublisher(outbox, currentTenant, clock),
                new EfUnitOfWork(commandDb));

            var result = await service.UpdateAsync(
                graph.ProjectId,
                new ProjectVisibilityMutationRequest(ProjectVisibility.Restricted, 1));

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(ProjectVisibility.Restricted, result.Value!.Visibility);
            Assert.Equal(2, result.Value.VersionNo);

            commandDb.ChangeTracker.Clear();
            var persisted = await commandDb.Projects.SingleAsync(item => item.Id == graph.ProjectId);
            Assert.Equal(ProjectVisibility.Restricted, persisted.Visibility);
            Assert.Equal(2, persisted.VersionNo);
            Assert.Equal(1, await commandDb.AuditLogs.CountAsync(item =>
                item.Action == "ProjectVisibilityChanged" &&
                item.EntityId == graph.ProjectId &&
                item.ActorUserId == graph.ReaderUserId));
            Assert.True(await commandDb.OutboxEvents.CountAsync() >= 2);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02F")]
    public async Task RestrictedProjectBlocksTaskNotificationAndRealtimeForWorkspaceOnlyMember()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedAsync(database, "restricted-current-auth", ProjectVisibility.Restricted);

            var currentTenant = TenantScope(graph);
            await using var db = new AppDbContext(Options(database), currentTenant);
            var inner = new CurrentAuthorizationTargetResolver(db, currentTenant, new MessagingRepository(db));
            var resolver = new CanonicalCurrentAuthorizationTargetResolver(db, currentTenant, inner);

            Assert.False(await db.VisibleProjectsFor(graph.ReaderUserId)
                .AnyAsync(item => item.Id == graph.ProjectId));

            var navigation = await resolver.ResolveAsync(
                graph.TenantId,
                graph.ReaderUserId,
                graph.NotificationId);
            Assert.True(navigation.IsOwned);
            Assert.False(navigation.IsAvailable);
            Assert.Null(navigation.Route);

            Assert.False(await resolver.CanDeliverCreatedAsync(
                graph.TenantId,
                graph.ReaderUserId,
                NewNotificationCreatedEvent(graph)));

            Assert.False(await resolver.CanReceiveTaskEventAsync(
                graph.TenantId,
                graph.ReaderUserId,
                RealtimeSubscriptionType.Workspace,
                graph.WorkspaceId,
                NewTaskEvent(graph)));

            Assert.False(await resolver.CanReceiveProjectEventAsync(
                graph.TenantId,
                graph.ReaderUserId,
                RealtimeSubscriptionType.Workspace,
                graph.WorkspaceId,
                NewProjectEvent(graph)));
        });
    }

    private static async Task<Graph> SeedAsync(
        string database,
        string suffix,
        ProjectVisibility visibility)
    {
        var run = Guid.NewGuid().ToString("N");
        var tenant = new Tenant
        {
            Name = $"Final01 Merge Audit Tenant {suffix} {run}",
            DisplayName = $"Final01 Merge Audit Tenant {suffix}",
            Slug = $"final01-merge-{suffix}-{run}",
            Status = TenantStatus.Active
        };
        var owner = NewUser($"owner-{suffix}-{run}");
        var reader = NewUser($"reader-{suffix}-{run}");

        await using (var platform = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            platform.AddRange(tenant, owner, reader);
            await platform.SaveChangesAsync();
        }

        var workspaceId = Guid.NewGuid();
        var currentTenant = new CurrentTenantService();
        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        await using var db = new AppDbContext(Options(database), currentTenant);

        db.TenantUsers.AddRange(
            NewTenantUser(tenant.Id, owner.Id, TenantUserRole.Owner),
            NewTenantUser(tenant.Id, reader.Id, TenantUserRole.Member));

        var workspace = new Workspace
        {
            Id = workspaceId,
            TenantId = tenant.Id,
            Name = $"Final01 Merge Audit Workspace {suffix}",
            Slug = $"final01-merge-workspace-{suffix}-{run}",
            Status = WorkspaceStatus.Active,
            CreatedByUserId = owner.Id
        };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.AddRange(
            NewWorkspaceMember(tenant.Id, workspace.Id, owner.Id, WorkspaceRole.Owner),
            NewWorkspaceMember(tenant.Id, workspace.Id, reader.Id, WorkspaceRole.Member));

        var project = new Project
        {
            TenantId = tenant.Id,
            WorkspaceId = workspace.Id,
            OwnerUserId = owner.Id,
            Name = $"Final01 Merge Audit Project {suffix}",
            Slug = $"final01-merge-project-{suffix}-{run}",
            Status = ProjectStatus.Active,
            Visibility = visibility,
            ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = Now,
            ActivationVersion = 1,
            VersionNo = 1,
            CreatedByUserId = owner.Id
        };
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            TenantId = tenant.Id,
            ProjectId = project.Id,
            UserId = owner.Id,
            Role = ProjectRole.Owner,
            JoinedAt = Now
        });

        var task = new TaskItem
        {
            TenantId = tenant.Id,
            WorkspaceId = workspace.Id,
            ProjectId = project.Id,
            Title = $"Final01 Merge Audit Task {suffix}",
            Status = TaskItemStatus.NotStarted,
            Priority = TaskPriority.Medium,
            VersionNo = 1,
            CreatedByUserId = owner.Id
        };
        var notification = new Notification
        {
            TenantId = tenant.Id,
            UserId = reader.Id,
            NotificationType = NotificationType.TaskAssigned,
            Title = "Task assigned",
            RelatedEntityType = "Task",
            RelatedEntityId = task.Id,
            CreatedAt = Now,
            StateVersion = 1
        };
        db.AddRange(task, notification);
        await db.SaveChangesAsync();

        return new Graph(
            tenant.Id,
            tenant.Slug,
            workspace.Id,
            project.Id,
            task.Id,
            notification.Id,
            owner.Id,
            reader.Id);
    }

    private static DurableEventEnvelope NewNotificationCreatedEvent(Graph graph) => new(
        Guid.NewGuid(),
        "Notifications.NotificationCreated.v1",
        RealtimeEventCatalog.PayloadSchemaVersion1,
        Now,
        graph.TenantId,
        "Notification",
        graph.NotificationId,
        1,
        RealtimeActor.System(),
        null,
        null,
        JsonSerializer.SerializeToElement(new
        {
            notificationId = graph.NotificationId,
            stateVersion = 1,
            requiresRefetch = true
        }));

    private static DurableEventEnvelope NewTaskEvent(Graph graph) => new(
        Guid.NewGuid(),
        "Projects.TaskChanged.v1",
        RealtimeEventCatalog.PayloadSchemaVersion1,
        Now,
        graph.TenantId,
        "Task",
        graph.TaskId,
        1,
        RealtimeActor.System(),
        null,
        null,
        JsonSerializer.SerializeToElement(new
        {
            taskId = graph.TaskId,
            projectId = graph.ProjectId,
            requiresRefetch = true
        }));

    private static DurableEventEnvelope NewProjectEvent(Graph graph) => new(
        Guid.NewGuid(),
        "Projects.ProjectChanged.v1",
        RealtimeEventCatalog.PayloadSchemaVersion1,
        Now,
        graph.TenantId,
        "Project",
        graph.ProjectId,
        1,
        RealtimeActor.System(),
        null,
        null,
        JsonSerializer.SerializeToElement(new
        {
            workspaceId = graph.WorkspaceId,
            projectId = graph.ProjectId,
            requiresRefetch = true
        }));

    private static DbContextOptions<AppDbContext> Options(string database) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database)
            .AddInterceptors(new ProjectGovernanceSaveChangesInterceptor())
            .Options;

    private static CurrentTenantService TenantScope(Graph graph)
    {
        var currentTenant = new CurrentTenantService();
        currentTenant.SetTenant(graph.TenantId, graph.TenantSlug);
        return currentTenant;
    }

    private static AppDbContext CreateTenantContext(string database, Graph graph) =>
        new(Options(database), TenantScope(graph));

    private static User NewUser(string suffix) => new()
    {
        DisplayName = $"Final01 Merge Audit User {suffix}",
        Email = $"{suffix}@example.test".ToLowerInvariant(),
        NormalizedEmail = $"{suffix}@example.test".ToUpperInvariant(),
        PasswordHash = "hash",
        Status = UserStatus.Active,
        SystemRole = SystemRole.NormalUser
    };

    private static TenantUser NewTenantUser(Guid tenantId, Guid userId, TenantUserRole role) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        Role = role,
        Status = TenantUserStatus.Active,
        JoinedAt = Now
    };

    private static WorkspaceMember NewWorkspaceMember(
        Guid tenantId,
        Guid workspaceId,
        Guid userId,
        WorkspaceRole role) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        UserId = userId,
        Role = role,
        Status = MembershipStatus.Active,
        JoinedAt = Now
    };

    private sealed record Graph(
        Guid TenantId,
        string TenantSlug,
        Guid WorkspaceId,
        Guid ProjectId,
        Guid TaskId,
        Guid NotificationId,
        Guid OwnerUserId,
        Guid ReaderUserId);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.NormalUser;
        public bool IsAuthenticated => true;
    }
}
