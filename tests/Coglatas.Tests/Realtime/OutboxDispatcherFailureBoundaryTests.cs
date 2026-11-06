using System.Reflection;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Web.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coglatas.Tests.Realtime;

public sealed class OutboxDispatcherFailureBoundaryTests
{
    [Fact]
    public async Task DisabledFeatureReleasesTheExactClaimWithoutAuthorizingOrSending()
    {
        await using var fixture = new Fixture();
        fixture.Flags.Enabled = false;
        fixture.Options.DispatcherPollSeconds = 0;
        var before = DateTimeOffset.UtcNow;
        await fixture.DispatchAsync();
        var release = Assert.Single(fixture.Repository.Releases);
        Assert.InRange(release, before.AddSeconds(1), DateTimeOffset.UtcNow.AddSeconds(1));
        fixture.AssertNoDelivery();
        Assert.Empty(fixture.Repository.Failures);
        Assert.Equal(2, fixture.Repository.Contexts.Count);
        Assert.NotSame(fixture.Repository.Contexts[0], fixture.Repository.Contexts[1]);
    }

    [Fact]
    public async Task UnlockedClaimCannotReleaseSendOrChangeDeliveryState()
    {
        await using var fixture = new Fixture();
        fixture.Event.LockToken = null;
        await fixture.DispatchAsync();
        fixture.AssertNoDelivery();
        Assert.Empty(fixture.Repository.Releases);
        Assert.Empty(fixture.Repository.Failures);
        Assert.Equal(0, fixture.Flags.Calls);
    }

    [Theory]
    [InlineData("payload-json")]
    [InlineData("routing-json")]
    [InlineData("payload-null")]
    [InlineData("routing-null")]
    [InlineData("routing-empty")]
    [InlineData("event-id")]
    [InlineData("tenant-id")]
    [InlineData("event-type")]
    [InlineData("schema")]
    [InlineData("aggregate-id")]
    [InlineData("aggregate-type")]
    [InlineData("unsupported")]
    public async Task InvalidStoredContractsFailWithoutAuthorizationOrTransport(string mutation)
    {
        await using var fixture = new Fixture();
        var envelope = fixture.Envelope;
        switch (mutation)
        {
            case "payload-json": fixture.Event.PayloadJson = "{"; break;
            case "routing-json": fixture.Event.RoutingJson = "{"; break;
            case "payload-null": fixture.Event.PayloadJson = "null"; break;
            case "routing-null": fixture.Event.RoutingJson = "null"; break;
            case "routing-empty": fixture.Event.RoutingJson = "[]"; break;
            case "event-id": envelope = envelope with { EventId = Guid.NewGuid() }; break;
            case "tenant-id": envelope = envelope with { TenantId = Guid.NewGuid() }; break;
            case "event-type": envelope = envelope with { EventType = "Projects.ProjectChanged.v1" }; break;
            case "schema": envelope = envelope with { PayloadSchemaVersion = 2 }; break;
            case "aggregate-id": envelope = envelope with { AggregateId = Guid.NewGuid() }; break;
            case "aggregate-type": envelope = envelope with { AggregateType = "Project" }; break;
            case "unsupported":
                envelope = envelope with { EventType = "Synthetic.Unsupported.v1" };
                fixture.Event.EventType = envelope.EventType;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        if (mutation is not ("payload-json" or "payload-null"))
        {
            fixture.Event.PayloadJson = JsonSerializer.Serialize(envelope, Fixture.JsonOptions);
        }
        await fixture.DispatchAsync();
        fixture.AssertNoDelivery();
        var failure = Assert.Single(fixture.Repository.Failures);
        Assert.False(failure.Retryable);
        Assert.Null(failure.NextAttempt);
        Assert.Equal("InvalidOutboxContract", failure.Code);
        Assert.Equal("The durable event envelope or routing contract was invalid.", failure.Summary);
        Assert.Equal(1, fixture.Diagnostics.Snapshot().DispatchFailureCount);
    }

    [Theory]
    [InlineData("authorizer", 0, 5, 15, 4.0, 6.0)]
    [InlineData("send", 15, 5, 1, 48.0, 60.0)]
    [InlineData("acknowledgement", 0, -1, -1, 0.8, 1.2)]
    public async Task TransientFailuresKeepExactClaimAndBoundedRetryWithoutPersistingExceptionText(
        string stage, int attempts, int initialSeconds, int maximumMinutes, double minimumDelay, double maximumDelay)
    {
        await using var fixture = new Fixture();
        fixture.Event.AttemptCount = attempts;
        fixture.Options.InitialRetrySeconds = initialSeconds;
        fixture.Options.MaximumRetryMinutes = maximumMinutes;
        var exception = new IOException("Synthetic transport diagnostic.");
        fixture.FailAt(stage, exception);
        var before = DateTimeOffset.UtcNow;
        await fixture.DispatchAsync();
        var after = DateTimeOffset.UtcNow;
        var failure = Assert.Single(fixture.Repository.Failures);
        Assert.True(failure.Retryable);
        Assert.NotNull(failure.NextAttempt);
        Assert.InRange(failure.NextAttempt.Value, before.AddSeconds(minimumDelay), after.AddSeconds(maximumDelay));
        Assert.Equal("DispatchTransientFailure", failure.Code);
        Assert.Equal("Realtime delivery could not complete.", failure.Summary);
        Assert.DoesNotContain(exception.Message, failure.Summary);
        Assert.Equal(1, fixture.Diagnostics.Snapshot().DispatchFailureCount);
        Assert.Equal(0, fixture.Diagnostics.Snapshot().DispatchSuccessCount);
        Assert.Equal(stage == "authorizer" ? 0 : 1, fixture.Hub.Sends.Count);
        Assert.Equal(stage == "acknowledgement" ? 1 : 0, fixture.Repository.Delivered.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContractFailuresDoNotScheduleAutomaticRetry(bool invalidOperation)
    {
        await using var fixture = new Fixture();
        fixture.FailAt("send", invalidOperation
            ? new InvalidOperationException("Synthetic contract diagnostic.")
            : new JsonException("Synthetic JSON diagnostic."));
        await fixture.DispatchAsync();
        var failure = Assert.Single(fixture.Repository.Failures);
        Assert.False(failure.Retryable);
        Assert.Null(failure.NextAttempt);
        Assert.Equal("DispatchContractFailure", failure.Code);
        Assert.Equal("Realtime delivery contract validation failed.", failure.Summary);
        Assert.Empty(fixture.Repository.Delivered);
    }

    [Fact]
    public async Task HostCancellationPropagatesWithoutTurningShutdownIntoFailureOrDelivery()
    {
        await using var fixture = new Fixture();
        var token = new CancellationToken(canceled: true);
        fixture.FailAt("authorizer", new OperationCanceledException(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.DispatchAsync(token));
        Assert.Empty(fixture.Repository.Failures);
        Assert.Empty(fixture.Repository.Delivered);
        Assert.Empty(fixture.Hub.Sends);
        Assert.Equal(0, fixture.Diagnostics.Snapshot().DispatchFailureCount);
    }

    [Fact]
    public async Task UnrelatedCancellationIsRetryableWhileTheHostIsStillRunning()
    {
        await using var fixture = new Fixture();
        fixture.FailAt("authorizer", new OperationCanceledException(new CancellationToken(canceled: true)));
        await fixture.DispatchAsync();
        Assert.True(Assert.Single(fixture.Repository.Failures).Retryable);
        Assert.Empty(fixture.Repository.Delivered);
    }

    [Fact]
    public async Task AuthorizedDuplicateTargetsSendOnlyOnceAndRecordSuccessfulDelivery()
    {
        await using var fixture = new Fixture();
        var target = new RealtimeRoutingTarget(RealtimeSubscriptionType.User, fixture.Recipient);
        fixture.Event.RoutingJson = JsonSerializer.Serialize(new[] { target, target }, Fixture.JsonOptions);
        await fixture.DispatchAsync();
        Assert.Equal(1, fixture.Authorizer.Calls);
        var send = Assert.Single(fixture.Hub.Sends);
        Assert.Equal("target-connection", send.Connection);
        var envelope = Assert.IsType<DurableEventEnvelope>(Assert.Single(send.Arguments));
        Assert.Equal(JsonSerializer.Serialize(fixture.Envelope, Fixture.JsonOptions), JsonSerializer.Serialize(envelope, Fixture.JsonOptions));
        Assert.Equal("DurableEvent", send.Method);
        Assert.Null(Assert.Single(fixture.Repository.Delivered));
        Assert.Empty(fixture.Repository.Failures);
        Assert.Equal(1, fixture.Diagnostics.Snapshot().DispatchSuccessCount);
    }

    private sealed record Failure(bool Retryable, DateTimeOffset? NextAttempt, string Code, string Summary);

    private sealed class Fixture : IAsyncDisposable
    {
        public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
        private readonly ServiceProvider _services;
        private readonly OutboxDispatcher _dispatcher;
        public Guid Recipient { get; } = Guid.NewGuid();
        public DurableEventEnvelope Envelope { get; }
        public OutboxEvent Event { get; }
        public RecordingRepository Repository { get; }
        public RecordingHub Hub { get; } = new();
        public RecordingAuthorizer Authorizer { get; } = new();
        public Flags Flags { get; } = new();
        public RealtimeOptions Options { get; } = new();
        public RealtimeDiagnostics Diagnostics { get; } = new();

        public Fixture()
        {
            var aggregate = Guid.NewGuid();
            Envelope = new DurableEventEnvelope(Guid.NewGuid(), "Notifications.NotificationCreated.v1", 1,
                DateTimeOffset.UtcNow, Guid.NewGuid(), "Notification", aggregate, 1, RealtimeActor.System(), null, null,
                JsonSerializer.SerializeToElement(new { notificationId = aggregate, stateVersion = 1, requiresRefetch = true }));
            Event = new OutboxEvent(Envelope.EventId)
            {
                TenantId = Envelope.TenantId, EventType = Envelope.EventType, PayloadSchemaVersion = 1,
                AggregateId = aggregate, AggregateType = Envelope.AggregateType, AggregateVersion = 1,
                PayloadJson = JsonSerializer.Serialize(Envelope, JsonOptions),
                RoutingJson = JsonSerializer.Serialize(new[] { new RealtimeRoutingTarget(RealtimeSubscriptionType.User, Recipient) }, JsonOptions),
                LockToken = Guid.NewGuid()
            };
            Repository = new RecordingRepository(Event);
            _services = new ServiceCollection()
                .AddScoped<ICurrentTenantAccessor, CurrentTenantService>()
                .AddScoped<IOutboxEventRepository>(provider =>
                {
                    Repository.Context = provider.GetRequiredService<ICurrentTenantAccessor>();
                    Repository.Contexts.Add(Repository.Context);
                    return Repository;
                })
                .AddScoped<IFeatureFlagService>(_ => Flags)
                .AddScoped<IRealtimeDispatchAuthorizer>(_ => Authorizer)
                .BuildServiceProvider(validateScopes: true);
            var registry = new HubSubscriptionRegistry();
            Assert.True(registry.TryAdd(new HubSubscription("target-connection", Recipient, Guid.NewGuid(),
                Event.TenantId, RealtimeSubscriptionType.User, Recipient, "synthetic-target-group", DateTimeOffset.UtcNow), 20));
            Assert.True(registry.TryAdd(new HubSubscription("foreign-connection", Recipient, Guid.NewGuid(),
                Guid.NewGuid(), RealtimeSubscriptionType.User, Recipient, "synthetic-foreign-group", DateTimeOffset.UtcNow), 20));
            _dispatcher = new OutboxDispatcher(_services.GetRequiredService<IServiceScopeFactory>(), Hub, registry,
                Diagnostics, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<OutboxDispatcher>.Instance);
        }

        public void FailAt(string stage, Exception exception)
        {
            switch (stage)
            {
                case "authorizer": Authorizer.Exception = exception; break;
                case "send": Hub.Exception = exception; break;
                case "acknowledgement": Repository.AcknowledgementException = exception; break;
                default: throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        public async Task DispatchAsync(CancellationToken token = default)
        {
            Repository.Token = token;
            var method = typeof(OutboxDispatcher).GetMethod("DispatchBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw Unexpected();
            await Assert.IsAssignableFrom<Task>(method.Invoke(_dispatcher, [token]));
        }

        public void AssertNoDelivery()
        {
            Assert.Equal(0, Authorizer.Calls);
            Assert.Empty(Hub.Sends);
            Assert.Empty(Repository.Delivered);
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }

    private sealed class RecordingAuthorizer : IRealtimeDispatchAuthorizer
    {
        public int Calls { get; private set; }
        public Exception? Exception { get; set; }
        public Task<bool> CanReceiveAsync(HubSubscription subscription, RealtimeSubscriptionType targetType,
            Guid targetResourceId, DurableEventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(envelope.TenantId, subscription.TenantId);
            Assert.Equal(targetType, subscription.SubscriptionType);
            Assert.Equal(targetResourceId, subscription.ResourceId);
            return Exception is null ? Task.FromResult(true) : Task.FromException<bool>(Exception);
        }
    }

    private sealed class Flags : IFeatureFlagService
    {
        public bool Enabled { get; set; } = true;
        public int Calls { get; private set; }
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
        { Assert.Equal(FeatureKeys.TransactionalOutbox, featureKey); Calls++; return Task.FromResult(Enabled); }
        public Task<Result> RequireEnabledAsync(string featureKey, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private sealed class RecordingRepository(OutboxEvent item) : IOutboxEventRepository
    {
        public ICurrentTenantAccessor Context { get; set; } = null!;
        public List<ICurrentTenantAccessor> Contexts { get; } = [];
        public CancellationToken Token { get; set; }
        public List<Failure> Failures { get; } = [];
        public List<string?> Delivered { get; } = [];
        public List<DateTimeOffset> Releases { get; } = [];
        public Exception? AcknowledgementException { get; set; }
        public Task<IReadOnlyList<OutboxEvent>> ClaimDueAsync(string lockOwner, DateTimeOffset now, int batchSize,
            TimeSpan lockTimeout, CancellationToken cancellationToken = default)
        {
            Assert.True(Context.IsPlatformScope); Assert.False(Context.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(lockOwner)); Assert.True(batchSize > 0); Assert.True(lockTimeout > TimeSpan.Zero);
            Assert.Equal(Token, cancellationToken);
            return Task.FromResult<IReadOnlyList<OutboxEvent>>([item]);
        }
        public Task<bool> MarkDeliveredAsync(Guid eventId, Guid lockToken, DateTimeOffset deliveredAt, string? outcomeCode, CancellationToken cancellationToken = default)
        {
            AssertClaim(eventId, lockToken, cancellationToken); Delivered.Add(outcomeCode);
            return AcknowledgementException is null ? Task.FromResult(true) : Task.FromException<bool>(AcknowledgementException);
        }
        public Task<bool> MarkFailureAsync(Guid eventId, Guid lockToken, DateTimeOffset now, bool retryable,
            DateTimeOffset? nextAttemptAt, string errorCode, string errorSummary, int maximumAttempts, CancellationToken cancellationToken = default)
        {
            AssertClaim(eventId, lockToken, cancellationToken); Assert.Equal(10, maximumAttempts);
            Failures.Add(new Failure(retryable, nextAttemptAt, errorCode, errorSummary)); return Task.FromResult(true);
        }
        public Task<bool> ReleaseAsync(Guid eventId, Guid lockToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        { AssertClaim(eventId, lockToken, cancellationToken); Releases.Add(nextAttemptAt); return Task.FromResult(true); }
        private void AssertClaim(Guid id, Guid token, CancellationToken cancellation)
        {
            Assert.Equal(item.TenantId, Context.TenantId); Assert.True(Context.IsAvailable); Assert.False(Context.IsPlatformScope);
            Assert.Equal(item.Id, id); Assert.Equal(item.LockToken, token); Assert.Equal(Token, cancellation);
        }
        public Task AddAsync(OutboxEvent eventItem, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<int> RecoverStaleLocksAsync(DateTimeOffset staleBefore, DateTimeOffset now, int maximumAttempts, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<int> CleanupAsync(DateTimeOffset deliveredBefore, DateTimeOffset deadLetterBefore, DateTimeOffset cancelledBefore, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<RealtimeOutboxDiagnostics> GetDiagnosticsAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<OutboxEvent?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> ReplayAsync(Guid eventId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private sealed class RecordingHub : IHubContext<AppHub>, IHubClients, IGroupManager
    {
        public List<(string Connection, string Method, object?[] Arguments)> Sends { get; } = [];
        public Exception? Exception { get; set; }
        IHubClients IHubContext<AppHub>.Clients => this;
        IGroupManager IHubContext<AppHub>.Groups => this;
        public IClientProxy Client(string connectionId) => new Proxy(this, connectionId);
        private sealed class Proxy(RecordingHub hub, string connection) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                hub.Sends.Add((connection, method, args));
                return hub.Exception is null ? Task.CompletedTask : Task.FromException(hub.Exception);
            }
        }
        public IClientProxy All => throw Unexpected();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw Unexpected();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw Unexpected();
        public IClientProxy Group(string groupName) => throw Unexpected();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw Unexpected();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw Unexpected();
        public IClientProxy User(string userId) => throw Unexpected();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw Unexpected();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private static InvalidOperationException Unexpected() => new("Unexpected broad transport or repository operation.");
}
