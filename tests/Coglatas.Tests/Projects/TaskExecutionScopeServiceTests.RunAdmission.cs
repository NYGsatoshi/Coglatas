using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
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
        await SeedRunAdmissionControlAsync(fixture);
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
        AssertRunAdmissionContext(fixture, coordinator, key, cancellation.Token);
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
        await SeedRunAdmissionControlAsync(fixture);
        var before = await CaptureRunAdmissionStateAsync(fixture);
        var coordinator = new RunAdmissionCoordinator(disposition);
        var unit = new CountingClearUnitOfWork(fixture.Db);
        using var cancellation = new CancellationTokenSource();
        const string key = "missing-result-request";

        var result = await fixture.CreateService(unit, coordinator).RequestRunAsync(fixture.TaskItem.Id, key, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(errorCode, result.ErrorDetail!.Code);
        Assert.Equal(disposition == IdempotentCreateDisposition.RequestMismatch ? "header.Idempotency-Key" : null, result.ErrorDetail.Target);
        AssertRunAdmissionContext(fixture, coordinator, key, cancellation.Token);
        Assert.Equal(0, unit.SaveCalls);
        Assert.Equal(before, await CaptureRunAdmissionStateAsync(fixture));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task CoordinatorFailureBeforeStagingReturnsTheGenericPersistenceErrorAndRetainsExistingRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        await SeedRunAdmissionControlAsync(fixture);
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
        AssertRunAdmissionContext(fixture, coordinator, key, cancellation.Token);
        Assert.Equal(0, unit.SaveCalls);
        Assert.Equal(before, await CaptureRunAdmissionStateAsync(fixture));
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    private static async Task SeedRunAdmissionControlAsync(Fixture fixture)
    {
        await SeedClearOverrideAsync(fixture);
        var accepted = await fixture.Service.RequestRunAsync(fixture.TaskItem.Id, "retained-admission-run");
        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.NotEqual(Guid.Empty, accepted.Value!.Id);
        Assert.Equal(accepted.Value.Id, Assert.Single(await fixture.Db.TaskExecutionRuns.AsNoTracking().ToListAsync()).Id);
        Assert.Single(await fixture.Db.IdempotencyRecords.AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Db.OutboxEvents.AsNoTracking().ToListAsync());
        Assert.Single(fixture.Audit.Entries);
    }

    private static void AssertRunAdmissionContext(Fixture fixture, RunAdmissionCoordinator coordinator, string key, CancellationToken token)
    {
        Assert.Equal(1, coordinator.Calls);
        Assert.Equal(token, coordinator.Token);
        var context = Assert.IsType<CreateIdempotencyContext>(coordinator.Context);
        Assert.Equal(fixture.Tenant.Id, context.TenantId);
        Assert.Equal(fixture.Actor.Id, context.ActorUserId);
        Assert.Equal("TaskExecution.Run.v1", context.Operation);
        Assert.Equal(key, context.ClientRequestIdentity);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.TaskItem.Id.ToString("N")))), context.RequestHash);
        Assert.Equal("TaskExecutionRun", context.ResourceType);
        Assert.NotEqual(Guid.Empty, context.ResourceId);
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
        return rows.Select(row => fixture.Db.Entry(row).Properties.OrderBy(property => property.Metadata.Name)
                .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue))
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
