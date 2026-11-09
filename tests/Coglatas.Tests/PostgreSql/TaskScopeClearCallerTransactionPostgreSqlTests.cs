using System.Data;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class TaskScopeClearCallerTransactionPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ClearingOverrideAndItsPolicyDocumentRespectsCallerRollbackAndCommit() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), ExerciseCallerOutcomesAsync);

    private static async Task ExerciseCallerOutcomesAsync(string connectionString)
    {
        var now = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var tenant = new Tenant(Guid.NewGuid()) { Name = "Clear policy tenant", Slug = "clear-policy", Status = TenantStatus.Active };
        var actor = new User
        {
            DisplayName = "Clear policy manager", Email = "clear-policy@example.invalid",
            NormalizedEmail = "CLEAR-POLICY@EXAMPLE.INVALID", Status = UserStatus.Active
        };
        var workspace = new Workspace
        {
            TenantId = tenant.Id, Name = "Clear policy workspace", Slug = "clear-policy-workspace",
            Status = WorkspaceStatus.Active, CreatedByUserId = actor.Id
        };
        var project = new Project
        {
            TenantId = tenant.Id, WorkspaceId = workspace.Id, Name = "Clear policy project", Slug = "clear-policy-project",
            OwnerUserId = actor.Id, CreatedByUserId = actor.Id, Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.MembersOnly, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = now, ActivationVersion = 1, VersionNo = 1
        };
        var task = new TaskItem
        {
            TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id,
            Title = "Clear policy task", CreatedByUserId = actor.Id, SortKey = 1024, VersionNo = 7_000_000_003L
        };
        var taskOverride = new TaskExecutionScopeOverride
        {
            TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id, TaskItemId = task.Id,
            WebEnabled = false, ProjectFilesEnabled = true, VersionNo = 9_000_000_003L, UpdatedByUserId = actor.Id
        };
        await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString))
        {
            seed.Tenants.Add(tenant);
            seed.Users.Add(actor);
            seed.Workspaces.Add(workspace);
            seed.Projects.Add(project);
            seed.TaskItems.Add(task);
            await seed.SaveChangesAsync();
            var projectScope = await seed.ProjectExecutionScopes.SingleAsync(item => item.ProjectId == project.Id);
            projectScope.WebEnabled = true;
            projectScope.ProjectFilesEnabled = false;
            projectScope.VersionNo = 6_000_000_003L;
            seed.TaskExecutionScopeOverrides.Add(taskOverride);
            await seed.SaveChangesAsync();
            var repository = new TaskExecutionScopeRepository(seed);
            repository.StageSourcePolicyDocument(new TaskExecutionSourcePolicyDocument(
                TaskExecutionSourcePolicyOwnerType.Project, project.Id, tenant.Id, workspace.Id, project.Id,
                null, projectScope.VersionNo, null, TaskExecutionSourcePolicyV2.FromLegacy(true, false)));
            var taskDocument = new TaskExecutionSourcePolicyDocument(
                TaskExecutionSourcePolicyOwnerType.Task, task.Id, tenant.Id, workspace.Id, project.Id,
                task.Id, projectScope.VersionNo, taskOverride.VersionNo, TaskExecutionSourcePolicyV2.FromLegacy(false, true));
            repository.StageSourcePolicyDocument(taskDocument);
            await repository.FlushPendingSourcePolicyDocumentsAsync();
            Assert.False(repository.HasPendingSourcePolicyDocuments);
            var persistedDocument = await repository.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Task, task.Id);
            Assert.NotNull(persistedDocument);
            Assert.Equal(JsonSerializer.Serialize(taskDocument), JsonSerializer.Serialize(persistedDocument));
        }

        var before = await ReadStoredStateAsync(connectionString, task.Id, project.Id);
        using var initial = JsonDocument.Parse(before);
        var projectDocumentJson = initial.RootElement.GetProperty("ProjectDocument").GetRawText();
        bool[] outcomes = [false, true];
        foreach (var commit in outcomes)
        {
            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(tenant.Id, tenant.Slug);
            await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString).Options, currentTenant);
            var repository = new TaskExecutionScopeRepository(writer);
            var clock = new ClearClock();
            var audit = new ClearAudit();
            var service = new TaskExecutionScopeService(
                new ProjectRepository(writer), new ClearAuthority(actor.Id, project.Id), repository,
                new ResearchPlanRepository(writer), new ClearUser(actor.Id), clock, audit,
                new BusinessInvalidationPublisher(new TransactionalOutbox(new OutboxEventRepository(writer), currentTenant, clock), currentTenant, clock),
                new EfUnitOfWork(writer, repository), new EfCreateIdempotencyCoordinator(writer));
            await using var transaction = await writer.Database.BeginTransactionAsync();

            var result = await service.ClearTaskOverrideAsync(task.Id, new ClearTaskExecutionScopeOverrideRequest(taskOverride.VersionNo));

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(TaskExecutionScopeOrigin.ProjectDefault, result.Value!.Origin);
            Assert.Equal(6_000_000_003L, result.Value.ProjectDefaultVersion);
            Assert.Null(result.Value.TaskOverrideVersion);
            Assert.Null(result.Value.TaskOverridePolicy);
            Assert.True(result.Value.CanManage);
            Assert.True(result.Value.EffectivePolicy.WebEnabled);
            Assert.False(result.Value.EffectivePolicy.ProjectFilesEnabled);
            Assert.Same(transaction, writer.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, writer.Database.GetDbConnection().State);
            Assert.False(repository.HasPendingSourcePolicyDocuments);
            Assert.Null(await repository.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Task, task.Id));
            Assert.Empty(await writer.TaskExecutionScopeOverrides.AsNoTracking().Where(item => item.TaskItemId == task.Id).ToListAsync());
            Assert.Equal(task.VersionNo + 1, await writer.TaskItems.AsNoTracking().Where(item => item.Id == task.Id).Select(item => item.VersionNo).SingleAsync());
            var staged = Assert.Single(await writer.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == task.Id).ToListAsync());
            AssertInvalidation(staged, tenant.Id, project.Id, task.Id, actor.Id, task.VersionNo + 1);
            var entry = Assert.Single(audit.Entries);
            Assert.Equal("TaskExecutionScopeOverrideCleared", entry.Action);
            Assert.Equal("TaskExecutionScopeOverride", entry.EntityType);
            Assert.Equal(taskOverride.Id, entry.EntityId);
            Assert.Equal(actor.Id, entry.ActorUserId);
            Assert.Equal(workspace.Id, entry.WorkspaceId);
            Assert.Equal(project.Id, entry.ProjectId);
            Assert.Equal(taskOverride.VersionNo, Assert.IsType<long>(entry.Metadata!["overrideVersion"]));
            Assert.Equal(before, await ReadStoredStateAsync(connectionString, task.Id, project.Id));

            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();

            if (!commit)
            {
                Assert.Equal(before, await ReadStoredStateAsync(connectionString, task.Id, project.Id));
                continue;
            }
            await using var independent = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
            var committedRepository = new TaskExecutionScopeRepository(independent);
            Assert.Null(await committedRepository.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Task, task.Id));
            var inherited = await committedRepository.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Project, project.Id);
            Assert.NotNull(inherited);
            Assert.Equal(projectDocumentJson, JsonSerializer.Serialize(inherited));
            Assert.Equal(6_000_000_003L, inherited.ProjectScopeVersion);
            Assert.True(inherited.Policy.WebEnabled);
            Assert.False(inherited.Policy.ProjectFilesEnabled);
            Assert.Empty(await independent.TaskExecutionScopeOverrides.Where(item => item.TaskItemId == task.Id).ToListAsync());
            Assert.Equal(task.VersionNo + 1, await independent.TaskItems.Where(item => item.Id == task.Id).Select(item => item.VersionNo).SingleAsync());
            var committed = Assert.Single(await independent.OutboxEvents.Where(item => item.AggregateId == task.Id).ToListAsync());
            Assert.Equal(staged.Id, committed.Id);
            AssertInvalidation(committed, tenant.Id, project.Id, task.Id, actor.Id, task.VersionNo + 1);
        }
    }

    private static void AssertInvalidation(OutboxEvent entry, Guid tenant, Guid project, Guid task, Guid actor, long version)
    {
        Assert.Equal(tenant, entry.TenantId);
        Assert.Equal("Projects.TaskChanged.v1", entry.EventType);
        Assert.Equal("Task", entry.AggregateType);
        Assert.Equal(task, entry.AggregateId);
        Assert.Equal(version, entry.AggregateVersion);
        using var envelope = JsonDocument.Parse(entry.PayloadJson);
        Assert.Equal(tenant, envelope.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Equal(actor, envelope.RootElement.GetProperty("actor").GetProperty("actorId").GetGuid());
        var payload = envelope.RootElement.GetProperty("payload");
        Assert.Equal(task, payload.GetProperty("taskId").GetGuid());
        Assert.Equal(project, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(version, payload.GetProperty("taskVersion").GetInt64());
        Assert.Equal("executionScopeChanged", payload.GetProperty("change").GetString());
        Assert.True(payload.GetProperty("requiresRefetch").GetBoolean());
    }

    private static async Task<string> ReadStoredStateAsync(string connectionString, Guid task, Guid project)
    {
        await using var reader = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
        var repository = new TaskExecutionScopeRepository(reader);
        return JsonSerializer.Serialize(new
        {
            Task = await reader.TaskItems.AsNoTracking().Where(item => item.Id == task)
                .Select(item => new { item.Id, item.TenantId, item.WorkspaceId, item.ProjectId, item.Title, item.VersionNo, item.DeletedAt }).SingleAsync(),
            Override = await reader.TaskExecutionScopeOverrides.AsNoTracking().Where(item => item.TaskItemId == task)
                .Select(item => new { item.Id, item.TenantId, item.WorkspaceId, item.ProjectId, item.TaskItemId, item.VersionNo, item.WebEnabled, item.ProjectFilesEnabled, item.UpdatedByUserId }).SingleOrDefaultAsync(),
            TaskDocument = await repository.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Task, task),
            ProjectDocument = await repository.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Project, project),
            Outbox = await reader.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == task).OrderBy(item => item.Id)
                .Select(item => new { item.Id, item.EventType, item.TenantId, item.AggregateType, item.AggregateId, item.AggregateVersion, item.PayloadJson, item.RoutingJson, item.Status }).ToArrayAsync()
        });
    }

    private sealed class ClearAuthority(Guid actor, Guid project) : IProjectAuthorizationService
    {
        public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(userId == actor && projectId == project);
        public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(userId == actor && projectId == project);
        public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class ClearUser(Guid actor) : ICurrentUser
    {
        public Guid? UserId => actor;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.NormalUser;
        public bool IsAuthenticated => true;
    }

    private sealed class ClearClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class ClearAudit : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
