using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed partial class TaskExecutionScopeServiceTests
{
    [Theory]
    [InlineData("null", "TASK_EXECUTION_MISSING_IDEMPOTENCY_KEY")]
    [InlineData("empty", "TASK_EXECUTION_MISSING_IDEMPOTENCY_KEY")]
    [InlineData("spaces", "TASK_EXECUTION_MISSING_IDEMPOTENCY_KEY")]
    [InlineData("short", "TASK_EXECUTION_INVALID_IDEMPOTENCY_KEY")]
    [InlineData("long", "TASK_EXECUTION_INVALID_IDEMPOTENCY_KEY")]
    [InlineData("tab", "TASK_EXECUTION_INVALID_IDEMPOTENCY_KEY")]
    [InlineData("newline", "TASK_EXECUTION_INVALID_IDEMPOTENCY_KEY")]
    [InlineData("delete", "TASK_EXECUTION_INVALID_IDEMPOTENCY_KEY")]
    [InlineData("nonAscii", "TASK_EXECUTION_INVALID_IDEMPOTENCY_KEY")]
    public async Task InvalidRunKeysNeverReachCreationOrChangeExistingExecutionRows(string scenario, string errorCode)
    {
        await using var fixture = await Fixture.CreateAsync();
        await SeedRunAdmissionControlAsync(fixture);
        var before = await CaptureRunAdmissionStateAsync(fixture);
        var coordinator = new RunAdmissionCoordinator(IdempotentCreateDisposition.RequestMismatch);
        var unit = new CountingClearUnitOfWork(fixture.Db);
        var key = scenario switch
        {
            "null" => null,
            "empty" => "",
            "spaces" => new string(' ', 8),
            "short" => "1234567",
            "long" => new string('k', 129),
            "tab" => "1234\t678",
            "newline" => "1234\n678",
            "delete" => "1234567\u007f",
            "nonAscii" => "1234567\u0080",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await fixture.CreateService(unit, coordinator).RequestRunAsync(fixture.TaskItem.Id, key);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(errorCode, result.ErrorDetail!.Code);
        Assert.Equal("header.Idempotency-Key", result.ErrorDetail.Target);
        Assert.Equal(0, coordinator.Calls);
        Assert.Null(coordinator.Context);
        Assert.Equal(0, unit.SaveCalls);
        Assert.Equal(before, await CaptureRunAdmissionStateAsync(fixture));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public async Task PrintableBoundaryKeysReachTheCoordinatorWithoutTrimmingOrChangingRows(int length)
    {
        await using var fixture = await Fixture.CreateAsync();
        var retainedRunId = await SeedRunAdmissionControlAsync(fixture);
        var before = await CaptureRunAdmissionStateAsync(fixture);
        var coordinator = new RunAdmissionCoordinator(IdempotentCreateDisposition.RequestMismatch);
        var unit = new CountingClearUnitOfWork(fixture.Db);
        var key = " " + new string('~', length - 2) + " ";
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.CreateService(unit, coordinator).RequestRunAsync(fixture.TaskItem.Id, key, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("TASK_EXECUTION_IDEMPOTENCY_CONFLICT", result.ErrorDetail!.Code);
        Assert.Equal("header.Idempotency-Key", result.ErrorDetail.Target);
        AssertRunAdmissionContext(fixture, coordinator, key, cancellation.Token, retainedRunId);
        Assert.Equal(0, unit.SaveCalls);
        Assert.Equal(before, await CaptureRunAdmissionStateAsync(fixture));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Theory]
    [InlineData(IdempotentCreateDisposition.Created, "TASK_EXECUTION_REPLAY_UNAVAILABLE")]
    [InlineData(IdempotentCreateDisposition.Replayed, "TASK_EXECUTION_REPLAY_UNAVAILABLE")]
    [InlineData(IdempotentCreateDisposition.RequestMismatch, "TASK_EXECUTION_IDEMPOTENCY_CONFLICT")]
    [InlineData(IdempotentCreateDisposition.ReplayUnavailable, "TASK_EXECUTION_REPLAY_UNAVAILABLE")]
    public async Task MissingCoordinatorResultsNeverClaimAcceptanceOrChangeExistingExecutionRows(IdempotentCreateDisposition disposition, string errorCode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var retainedRunId = await SeedRunAdmissionControlAsync(fixture);
        var before = await CaptureRunAdmissionStateAsync(fixture);
        var coordinator = new RunAdmissionCoordinator(disposition);
        var unit = new CountingClearUnitOfWork(fixture.Db);
        using var cancellation = new CancellationTokenSource();
        const string key = "missing-result-request";

        var result = await fixture.CreateService(unit, coordinator).RequestRunAsync(fixture.TaskItem.Id, key, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(errorCode, result.ErrorDetail!.Code);
        Assert.Equal(disposition == IdempotentCreateDisposition.RequestMismatch
            ? "The Idempotency-Key was already used for a different execution request."
            : "The execution request could not be reconciled. Retry with a new Idempotency-Key.", result.ErrorDetail.Message);
        Assert.Equal(disposition == IdempotentCreateDisposition.RequestMismatch ? "header.Idempotency-Key" : null, result.ErrorDetail.Target);
        AssertRunAdmissionContext(fixture, coordinator, key, cancellation.Token, retainedRunId);
        Assert.Equal(0, unit.SaveCalls);
        Assert.Equal(before, await CaptureRunAdmissionStateAsync(fixture));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task CoordinatorFailureBeforeStagingReturnsTheGenericPersistenceErrorAndRetainsExistingRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var retainedRunId = await SeedRunAdmissionControlAsync(fixture);
        var before = await CaptureRunAdmissionStateAsync(fixture);
        var coordinator = new RunAdmissionCoordinator(IdempotentCreateDisposition.Created, failBeforeStaging: true);
        var unit = new CountingClearUnitOfWork(fixture.Db);
        using var cancellation = new CancellationTokenSource();
        const string key = "failure-before-staging";

        var result = await fixture.CreateService(unit, coordinator).RequestRunAsync(fixture.TaskItem.Id, key, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("TASK_EXECUTION_PERSISTENCE_UNAVAILABLE", result.ErrorDetail!.Code);
        Assert.Equal("The execution request could not be recorded.", result.ErrorDetail.Message);
        Assert.Null(result.ErrorDetail.Target);
        AssertRunAdmissionContext(fixture, coordinator, key, cancellation.Token, retainedRunId);
        Assert.Equal(0, unit.SaveCalls);
        Assert.Equal(before, await CaptureRunAdmissionStateAsync(fixture));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    private static async Task<Guid> SeedRunAdmissionControlAsync(Fixture fixture)
    {
        var taskOverride = await SeedClearOverrideAsync(fixture);
        var storedOverride = Assert.Single(await fixture.Db.TaskExecutionScopeOverrides.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(taskOverride.Id, storedOverride.Id);
        Assert.Equal((fixture.Tenant.Id, fixture.Workspace.Id, fixture.Project.Id, fixture.TaskItem.Id),
            (storedOverride.TenantId, storedOverride.WorkspaceId, storedOverride.ProjectId, storedOverride.TaskItemId));
        Assert.Equal(9_000_000_003L, storedOverride.VersionNo);
        Assert.Equal(fixture.Actor.Id, storedOverride.UpdatedByUserId);
        Assert.False(storedOverride.WebEnabled);
        Assert.True(storedOverride.ProjectFilesEnabled);
        var projectScope = Assert.Single(await fixture.Db.ProjectExecutionScopes.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.NotEqual(Guid.Empty, projectScope.Id);
        Assert.Equal((fixture.Tenant.Id, fixture.Workspace.Id, fixture.Project.Id),
            (projectScope.TenantId, projectScope.WorkspaceId, projectScope.ProjectId));
        Assert.Equal(1L, projectScope.VersionNo);
        Assert.False(projectScope.WebEnabled);
        Assert.False(projectScope.ProjectFilesEnabled);
        var accepted = await fixture.Service.RequestRunAsync(fixture.TaskItem.Id, "retained-admission-run");
        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.NotEqual(Guid.Empty, accepted.Value!.Id);
        Assert.Equal(accepted.Value.Id, Assert.Single(await fixture.Db.TaskExecutionRuns.AsNoTracking().ToListAsync()).Id);
        Assert.Single(await fixture.Db.IdempotencyRecords.AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Db.OutboxEvents.AsNoTracking().ToListAsync());
        Assert.Single(fixture.Audit.Entries);
        var retainedRunId = accepted.Value.Id;
        var run = Assert.Single(await fixture.Db.TaskExecutionRuns.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(retainedRunId, run.Id);
        Assert.Equal(TaskExecutionRunStatus.Accepted, run.Status);
        Assert.Equal(TaskExecutionRunStatus.Accepted, accepted.Value.Status);
        Assert.Equal((fixture.Tenant.Id, fixture.Workspace.Id, fixture.Project.Id, fixture.TaskItem.Id, fixture.Actor.Id),
            (run.TenantId, run.WorkspaceId, run.ProjectId, run.TaskItemId, run.RequestedByUserId));
        Assert.Equal(TaskExecutionScopeOrigin.TaskOverride, run.SnapshotScopeOrigin);
        Assert.Equal(1L, run.SnapshotProjectScopeVersion);
        Assert.Equal(9_000_000_003L, run.SnapshotTaskOverrideVersion);
        Assert.False(run.SnapshotWebEnabled);
        Assert.True(run.SnapshotProjectFilesEnabled);
        Assert.Equal(TaskExecutionProvider.FirstPartyProjectFilesRuntimeV1, run.RuntimeProvider);
        Assert.Equal(TaskExecutionRun.RuntimeContractVersion1, run.RuntimeContractVersion);
        var record = Assert.Single(await fixture.Db.IdempotencyRecords.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal((fixture.Tenant.Id, fixture.Actor.Id, "TaskExecution.Run.v1", "TaskExecutionRun", retainedRunId),
            (record.TenantId, record.ActorUserId, record.Operation, record.ResourceType, record.ResourceId));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("retained-admission-run"))), record.KeyHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.TaskItem.Id.ToString("N")))), record.RequestHash);
        var change = Assert.Single(await fixture.Db.OutboxEvents.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.NotEqual(Guid.Empty, change.Id);
        Assert.Equal(fixture.Tenant.Id, change.TenantId);
        Assert.Equal("Projects.TaskChanged.v1", change.EventType);
        Assert.Equal("Task", change.AggregateType);
        Assert.Equal(fixture.TaskItem.Id, change.AggregateId);
        Assert.Equal(7_000_000_004L, change.AggregateVersion);
        using var payload = JsonDocument.Parse(change.PayloadJson);
        Assert.Equal(fixture.Actor.Id, payload.RootElement.GetProperty("actor").GetProperty("actorId").GetGuid());
        Assert.Equal(fixture.TaskItem.Id, payload.RootElement.GetProperty("payload").GetProperty("taskId").GetGuid());
        Assert.Equal("executionRunChanged", payload.RootElement.GetProperty("payload").GetProperty("change").GetString());
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(fixture.Actor.Id, audit.ActorUserId);
        Assert.Equal("TaskExecutionRunRequested", audit.Action);
        Assert.Equal("TaskExecutionRun", audit.EntityType);
        Assert.Equal(retainedRunId, audit.EntityId);
        Assert.Equal(fixture.Workspace.Id, audit.WorkspaceId);
        Assert.Equal(fixture.Project.Id, audit.ProjectId);
        return retainedRunId;
    }

    private static void AssertRunAdmissionContext(Fixture fixture, RunAdmissionCoordinator coordinator, string key, CancellationToken token, Guid retainedRunId)
    {
        var expectedRequestIdentity = (key, token);
        Guid[] nonReusableRunIds = [Guid.Empty, retainedRunId];
        Assert.Equal(1, coordinator.Calls);
        Assert.Equal(token, coordinator.Token);
        var context = Assert.IsType<CreateIdempotencyContext>(coordinator.Context);
        Assert.Equal(expectedRequestIdentity, (context.ClientRequestIdentity, coordinator.Token));
        Assert.DoesNotContain(context.ResourceId, nonReusableRunIds);
        Assert.Equal(fixture.Tenant.Id, context.TenantId);
        Assert.Equal(fixture.Actor.Id, context.ActorUserId);
        Assert.Equal("TaskExecution.Run.v1", context.Operation);
        Assert.Equal(key, context.ClientRequestIdentity);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.TaskItem.Id.ToString("N")))), context.RequestHash);
        Assert.Equal("TaskExecutionRun", context.ResourceType);
        Assert.NotEqual(Guid.Empty, context.ResourceId);
        Assert.NotEqual(retainedRunId, context.ResourceId);
        Assert.NotEqual(Assert.Single(fixture.Db.TaskExecutionRuns.AsNoTracking()).Id, context.ResourceId);
    }

    private static async Task<string> CaptureRunAdmissionStateAsync(Fixture fixture) => JsonSerializer.Serialize(new
    {
        Tasks = await CaptureRunAdmissionRowsAsync(fixture, fixture.Db.TaskItems.IgnoreQueryFilters()),
        ProjectScopes = await CaptureRunAdmissionRowsAsync(fixture, fixture.Db.ProjectExecutionScopes.IgnoreQueryFilters()),
        Overrides = await CaptureRunAdmissionRowsAsync(fixture, fixture.Db.TaskExecutionScopeOverrides.IgnoreQueryFilters()),
        Runs = await CaptureRunAdmissionRowsAsync(fixture, fixture.Db.TaskExecutionRuns.IgnoreQueryFilters()),
        Idempotency = await CaptureRunAdmissionRowsAsync(fixture, fixture.Db.IdempotencyRecords.IgnoreQueryFilters()),
        Outbox = await CaptureRunAdmissionRowsAsync(fixture, fixture.Db.OutboxEvents.IgnoreQueryFilters()),
        Audit = fixture.Audit.Entries
    });

    private static async Task<Dictionary<string, object?>[]> CaptureRunAdmissionRowsAsync<TEntity>(Fixture fixture, IQueryable<TEntity> source)
        where TEntity : class
    {
        var rows = await source.AsNoTracking().ToArrayAsync();
        return rows.Select(row =>
            {
                var properties = fixture.Db.Entry(row).Properties.ToArray();
                Assert.Empty(properties.Where(property => property.Metadata.IsShadowProperty()));
                return properties.OrderBy(property => property.Metadata.Name)
                    .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);
            })
            .OrderBy(row => (Guid)row["Id"]!).ToArray();
    }

    private sealed class RunAdmissionCoordinator(IdempotentCreateDisposition disposition, bool failBeforeStaging = false) : ICreateIdempotencyCoordinator
    {
        public int Calls { get; private set; }
        public CreateIdempotencyContext? Context { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<IdempotentCreateResult<T>> ExecuteAsync<T>(CreateIdempotencyContext context,
            Func<CancellationToken, Task<T>> stageCreation, Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
            CancellationToken cancellationToken = default) where T : class
        {
            Calls++;
            Context = context;
            Token = cancellationToken;
            if (failBeforeStaging) throw new InvalidOperationException("Synthetic coordinator failure before either callback.");
            return Task.FromResult(new IdempotentCreateResult<T>(disposition, null));
        }
    }
}
