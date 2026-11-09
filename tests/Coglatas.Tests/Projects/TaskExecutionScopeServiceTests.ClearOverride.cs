using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed partial class TaskExecutionScopeServiceTests
{
    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public async Task ClearOverrideRejectsNegativeVersionsWithoutSavingOrEffects(long version)
    {
        await using var fixture = await Fixture.CreateAsync();
        await SeedClearOverrideAsync(fixture);
        var before = await ClearOverrideStoredStateAsync(fixture);
        var unit = new CountingClearUnitOfWork(fixture.Db);

        var result = await fixture.CreateService(unit).ClearTaskOverrideAsync(
            fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(version));

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_VALIDATION_FAILED", result.ErrorDetail!.Code);
        Assert.Equal("expectedVersion", result.ErrorDetail.Target);
        Assert.Equal(before, await ClearOverrideStoredStateAsync(fixture));
        Assert.Equal(0, unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(await fixture.Db.OutboxEvents.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries<OutboxEvent>().Where(entry => entry.State == EntityState.Added));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("viewer")]
    [InlineData("deleted")]
    [InlineData("foreignTenant")]
    public async Task ClearOverrideDeniesUnavailableTasksWithoutMutatingStoredState(string scenario)
    {
        await using var fixture = await Fixture.CreateAsync(canManage: scenario != "viewer");
        await SeedClearOverrideAsync(fixture);
        if (scenario == "deleted")
        {
            fixture.TaskItem.MarkDeleted(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));
            await fixture.Db.SaveChangesAsync();
        }
        if (scenario == "foreignTenant") await fixture.SwitchToOtherTenantAsync();
        var taskId = scenario switch
        {
            "empty" => Guid.Empty,
            "missing" => Guid.NewGuid(),
            _ => fixture.TaskItem.Id
        };
        var before = await ClearOverrideStoredStateAsync(fixture);
        var unit = new CountingClearUnitOfWork(fixture.Db);

        var result = await fixture.CreateService(unit).ClearTaskOverrideAsync(
            taskId, new ClearTaskExecutionScopeOverrideRequest(9_000_000_003L));

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_NOT_FOUND", result.ErrorDetail!.Code);
        Assert.Equal(before, await ClearOverrideStoredStateAsync(fixture));
        Assert.Equal(0, unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(await fixture.Db.OutboxEvents.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries<OutboxEvent>().Where(entry => entry.State == EntityState.Added));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(9_000_000_002L)]
    [InlineData(9_000_000_004L)]
    public async Task ClearOverrideRejectsStaleVersionsBeforeMutation(long expectedVersion)
    {
        await using var fixture = await Fixture.CreateAsync();
        await SeedClearOverrideAsync(fixture);
        var before = await ClearOverrideStoredStateAsync(fixture);
        var unit = new CountingClearUnitOfWork(fixture.Db);

        var result = await fixture.CreateService(unit).ClearTaskOverrideAsync(
            fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(expectedVersion));

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_STALE_VERSION", result.ErrorDetail!.Code);
        Assert.Equal(before, await ClearOverrideStoredStateAsync(fixture));
        Assert.Equal(0, unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(await fixture.Db.OutboxEvents.ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries<OutboxEvent>().Where(entry => entry.State == EntityState.Added));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public async Task ClearMissingOverrideRequiresTheAbsentVersion(long expectedVersion)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await ClearOverrideStoredStateAsync(fixture);
        var unit = new CountingClearUnitOfWork(fixture.Db);

        var result = await fixture.CreateService(unit).ClearTaskOverrideAsync(
            fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(expectedVersion));

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_STALE_VERSION", result.ErrorDetail!.Code);
        Assert.Equal(before, await ClearOverrideStoredStateAsync(fixture));
        Assert.Equal(0, unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(await fixture.Db.OutboxEvents.ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries<OutboxEvent>().Where(entry => entry.State == EntityState.Added));
    }

    [Fact]
    public async Task ClearingAnAbsentOverrideReturnsInheritedPolicyWithoutSaving()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await ClearOverrideStoredStateAsync(fixture);
        var unit = new CountingClearUnitOfWork(fixture.Db);

        var result = await fixture.CreateService(unit).ClearTaskOverrideAsync(
            fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(0));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TaskExecutionScopeOrigin.ProjectDefault, result.Value!.Origin);
        Assert.Equal(1, result.Value.ProjectDefaultVersion);
        Assert.Null(result.Value.TaskOverrideVersion);
        Assert.Null(result.Value.TaskOverridePolicy);
        Assert.True(result.Value.CanManage);
        Assert.False(result.Value.EffectivePolicy.WebEnabled);
        Assert.False(result.Value.EffectivePolicy.ProjectFilesEnabled);
        Assert.Equal("nextRun", result.Value.ChangesApplyTo);
        Assert.Equal(before, await ClearOverrideStoredStateAsync(fixture));
        Assert.Equal(0, unit.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(await fixture.Db.OutboxEvents.ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries<OutboxEvent>().Where(entry => entry.State == EntityState.Added));
    }

    [Fact]
    public async Task ClearOverrideSavesInheritedPolicyAndOneAuditAndEventThenBecomesANoOp()
    {
        await using var fixture = await Fixture.CreateAsync();
        var removed = await SeedClearOverrideAsync(fixture);
        var projectScope = await fixture.Db.ProjectExecutionScopes.SingleAsync();
        projectScope.WebEnabled = true;
        projectScope.ProjectFilesEnabled = false;
        projectScope.VersionNo = 6_000_000_003L;
        await fixture.Db.SaveChangesAsync();
        var beforeTaskVersion = fixture.TaskItem.VersionNo;
        var unit = new CountingClearUnitOfWork(fixture.Db);
        var service = fixture.CreateService(unit);

        var result = await service.ClearTaskOverrideAsync(
            fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(removed.VersionNo));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TaskExecutionScopeOrigin.ProjectDefault, result.Value!.Origin);
        Assert.Equal(projectScope.VersionNo, result.Value.ProjectDefaultVersion);
        Assert.True(result.Value.EffectivePolicy.WebEnabled);
        Assert.False(result.Value.EffectivePolicy.ProjectFilesEnabled);
        Assert.Null(result.Value.TaskOverrideVersion);
        Assert.Null(result.Value.TaskOverridePolicy);
        Assert.True(result.Value.CanManage);
        Assert.Equal("nextRun", result.Value.ChangesApplyTo);
        Assert.Empty(await fixture.Db.TaskExecutionScopeOverrides.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(beforeTaskVersion + 1, (await fixture.Db.TaskItems.AsNoTracking().SingleAsync()).VersionNo);
        Assert.Equal(1, unit.SaveCalls);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("TaskExecutionScopeOverrideCleared", audit.Action);
        Assert.Equal("TaskExecutionScopeOverride", audit.EntityType);
        Assert.Equal(removed.Id, audit.EntityId);
        Assert.Equal(fixture.Actor.Id, audit.ActorUserId);
        Assert.Equal(fixture.Workspace.Id, audit.WorkspaceId);
        Assert.Equal(fixture.Project.Id, audit.ProjectId);
        Assert.Equal(removed.VersionNo, Assert.IsType<long>(audit.Metadata!["overrideVersion"]));
        var change = Assert.Single(await fixture.Db.OutboxEvents.AsNoTracking().ToListAsync());
        Assert.Equal(fixture.Tenant.Id, change.TenantId);
        Assert.Equal("Projects.TaskChanged.v1", change.EventType);
        Assert.Equal("Task", change.AggregateType);
        Assert.Equal(fixture.TaskItem.Id, change.AggregateId);
        Assert.Equal(beforeTaskVersion + 1, change.AggregateVersion);
        using var payload = JsonDocument.Parse(change.PayloadJson);
        Assert.Equal(fixture.Tenant.Id, payload.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Equal(fixture.Actor.Id, payload.RootElement.GetProperty("actor").GetProperty("actorId").GetGuid());
        var body = payload.RootElement.GetProperty("payload");
        Assert.Equal(fixture.TaskItem.Id, body.GetProperty("taskId").GetGuid());
        Assert.Equal(fixture.Project.Id, body.GetProperty("projectId").GetGuid());
        Assert.Equal(beforeTaskVersion + 1, body.GetProperty("taskVersion").GetInt64());
        Assert.Equal("executionScopeChanged", body.GetProperty("change").GetString());
        Assert.True(body.GetProperty("requiresRefetch").GetBoolean());
        var stored = await ClearOverrideStoredStateAsync(fixture);

        var repeated = await service.ClearTaskOverrideAsync(fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(0));

        Assert.True(repeated.IsSuccess, repeated.Error);
        Assert.Equal(TaskExecutionScopeOrigin.ProjectDefault, repeated.Value!.Origin);
        Assert.Equal(stored, await ClearOverrideStoredStateAsync(fixture));
        Assert.Equal(1, unit.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Equal(change.Id, Assert.Single(await fixture.Db.OutboxEvents.AsNoTracking().ToListAsync()).Id);
        Assert.Empty(fixture.Db.ChangeTracker.Entries<OutboxEvent>().Where(entry => entry.State == EntityState.Added));
    }

    [Fact]
    public async Task ClearOverrideReportsSaveConflictWithoutClaimingThatStagedEffectsWereCommitted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var removed = await SeedClearOverrideAsync(fixture);
        var before = await ClearOverrideStoredStateAsync(fixture);
        var unit = new UniqueConflictTaskUnitOfWork(fixture.Db);

        var result = await fixture.CreateService(unit).ClearTaskOverrideAsync(
            fixture.TaskItem.Id, new ClearTaskExecutionScopeOverrideRequest(removed.VersionNo));

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_STALE_VERSION", result.ErrorDetail!.Code);
        Assert.True(unit.SaveAttempted);
        Assert.Equal(before, await ClearOverrideStoredStateAsync(fixture));
        Assert.Empty(await fixture.Db.OutboxEvents.AsNoTracking().ToListAsync());
        Assert.Equal("TaskExecutionScopeOverrideCleared", Assert.Single(fixture.Audit.Entries).Action);
    }

    private static async Task<TaskExecutionScopeOverride> SeedClearOverrideAsync(Fixture fixture)
    {
        var item = new TaskExecutionScopeOverride
        {
            TenantId = fixture.Tenant.Id, WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id, TaskItemId = fixture.TaskItem.Id,
            WebEnabled = false, ProjectFilesEnabled = true, VersionNo = 9_000_000_003L,
            UpdatedByUserId = fixture.Actor.Id
        };
        fixture.TaskItem.VersionNo = 7_000_000_003L;
        fixture.Db.TaskExecutionScopeOverrides.Add(item);
        await fixture.Db.SaveChangesAsync();
        return item;
    }

    private static async Task<string> ClearOverrideStoredStateAsync(Fixture fixture) => JsonSerializer.Serialize(new
    {
        Tasks = await fixture.Db.TaskItems.IgnoreQueryFilters().AsNoTracking().OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.TenantId, item.ProjectId, item.WorkspaceId, item.VersionNo, item.Title, item.DeletedAt }).ToArrayAsync(),
        Overrides = await fixture.Db.TaskExecutionScopeOverrides.IgnoreQueryFilters().AsNoTracking().OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.TenantId, item.WorkspaceId, item.ProjectId, item.TaskItemId, item.VersionNo, item.WebEnabled, item.ProjectFilesEnabled, item.UpdatedByUserId }).ToArrayAsync()
    });

    private sealed class CountingClearUnitOfWork(AppDbContext db) : ITaskCommandUnitOfWork
    {
        private readonly EfUnitOfWork _inner = new(db);
        public int SaveCalls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return _inner.SaveChangesAsync(cancellationToken);
        }
        public Task<TaskCommandSaveOutcome> SaveTaskCommandAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return _inner.SaveTaskCommandAsync(cancellationToken);
        }
        public void ClearTaskCommandTracking() => _inner.ClearTaskCommandTracking();
    }
}
