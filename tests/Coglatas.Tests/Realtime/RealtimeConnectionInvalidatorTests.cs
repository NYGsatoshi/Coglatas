using Coglatas.Application.Realtime;
using Coglatas.Web.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Coglatas.Tests.Realtime;

public sealed class RealtimeConnectionInvalidatorTests
{
    [Theory]
    [InlineData(RealtimeSubscriptionType.Tenant)]
    [InlineData(RealtimeSubscriptionType.User)]
    [InlineData(RealtimeSubscriptionType.Workspace)]
    [InlineData(RealtimeSubscriptionType.Project)]
    [InlineData(RealtimeSubscriptionType.Conversation)]
    public async Task RemovesEveryMatchingSessionBeforeGroupRemovalAndNotifiesOnlyItsConnections(RealtimeSubscriptionType type)
    {
        var fixture = new Fixture(type);
        using var cancellation = new CancellationTokenSource();
        fixture.Hub.GroupsRecorder.BeforeRemove = (_, _, token) =>
        {
            fixture.AssertScopeRemoved();
            Assert.Equal(cancellation.Token, token);
            return Task.CompletedTask;
        };
        fixture.Hub.ClientsRecorder.BeforeSend = (_, method, arguments, token) =>
        {
            fixture.AssertScopeRemoved();
            Assert.Equal("AuthorizationInvalidated", method);
            Assert.Empty(arguments);
            Assert.Equal(cancellation.Token, token);
            return Task.CompletedTask;
        };

        await fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User, cancellation.Token);

        fixture.AssertScopeRemoved();
        Assert.Equal(fixture.Targets.Select(item => (item.ConnectionId, item.CanonicalGroupName)).Order(),
            fixture.Hub.GroupsRecorder.Removals.Order());
        Assert.Equal(fixture.Targets.Select(item => item.ConnectionId).Order(),
            fixture.Hub.ClientsRecorder.Sends.Select(item => item.Connection).Order());
        Assert.Equal(fixture.Targets.Count * 2, fixture.Hub.Trace.Count);
        for (var index = 0; index < fixture.Hub.Trace.Count; index += 2)
        {
            Assert.Equal("remove", fixture.Hub.Trace[index].Kind);
            Assert.Equal("invalidate", fixture.Hub.Trace[index + 1].Kind);
            Assert.Equal(fixture.Hub.Trace[index].Connection, fixture.Hub.Trace[index + 1].Connection);
        }
    }

    [Fact]
    public async Task UnknownIdentityDoesNotRemoveOrNotifyAnySubscription()
    {
        var fixture = new Fixture(RealtimeSubscriptionType.Project);
        await fixture.Invalidator.InvalidateUserAsync(Guid.NewGuid(), fixture.User);
        await fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, Guid.NewGuid());
        foreach (var item in fixture.Targets.Concat(fixture.Controls))
        {
            Assert.Contains(item, fixture.Registry.GetForTarget(item.TenantId, item.SubscriptionType, item.ResourceId));
        }
        Assert.Empty(fixture.Hub.Trace);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("send")]
    [InlineData("cancel")]
    public async Task TransportFailureOrCancellationCannotRestoreRemovedRegistryAuthority(string stage)
    {
        var fixture = new Fixture(RealtimeSubscriptionType.Workspace);
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("Synthetic invalidation transport failure.");
        if (stage == "send")
            fixture.Hub.ClientsRecorder.BeforeSend = (_, _, _, _) => Task.FromException(failure);
        else
            fixture.Hub.GroupsRecorder.BeforeRemove = (_, _, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                if (stage == "cancel") token.ThrowIfCancellationRequested();
                return Task.FromException(failure);
            };
        if (stage == "cancel") cancellation.Cancel();

        if (stage == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User, cancellation.Token));
        else
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User, cancellation.Token)));

        fixture.AssertScopeRemoved();
        Assert.Single(fixture.Hub.GroupsRecorder.Removals);
        Assert.Equal(stage == "send" ? 1 : 0, fixture.Hub.ClientsRecorder.Sends.Count);
        // Failure propagation is the present contract; no successful delivery,
        // transport cleanup retry or atomic revocation is inferred from this.
    }

    [Fact]
    public async Task RepeatedInvalidationIsQuietUntilANewSubscriptionIsRegistered()
    {
        var fixture = new Fixture(RealtimeSubscriptionType.Conversation);
        await fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User);
        var previousCalls = fixture.Hub.Trace.Count;
        await fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User);
        Assert.Equal(previousCalls, fixture.Hub.Trace.Count);
        var later = fixture.Targets[0] with { SessionId = Guid.NewGuid() };
        Assert.True(fixture.Registry.TryAdd(later, 20));
        await fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User);
        fixture.AssertScopeRemoved();
        Assert.Equal(previousCalls + 2, fixture.Hub.Trace.Count);
        Assert.Equal(("remove", later.ConnectionId), fixture.Hub.Trace[^2]);
        Assert.Equal(("invalidate", later.ConnectionId), fixture.Hub.Trace[^1]);
    }

    [Fact]
    public async Task RegistryAuthorityIsRemovedWhileExternalGroupCleanupIsStillAwaited()
    {
        var fixture = new Fixture(RealtimeSubscriptionType.Project);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Hub.GroupsRecorder.BeforeRemove = async (_, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        var invalidation = fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User);
        await entered.Task;
        try
        {
            fixture.AssertScopeRemoved();
            Assert.False(invalidation.IsCompleted);
            Assert.Empty(fixture.Hub.ClientsRecorder.Sends);
            await fixture.Invalidator.InvalidateUserAsync(fixture.Tenant, fixture.User);
            Assert.Single(fixture.Hub.GroupsRecorder.Removals);
        }
        finally { release.TrySetResult(); }
        await invalidation;
        fixture.AssertScopeRemoved();
        Assert.Equal(fixture.Targets.Count, fixture.Hub.ClientsRecorder.Sends.Count);
    }

    private sealed class Fixture
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public HubSubscriptionRegistry Registry { get; } = new();
        public RecordingHub Hub { get; } = new();
        public IReadOnlyList<HubSubscription> Targets { get; }
        public IReadOnlyList<HubSubscription> Controls { get; }
        public RealtimeConnectionInvalidator Invalidator { get; }

        public Fixture(RealtimeSubscriptionType type)
        {
            Targets = [Create(Tenant, User, "connection-a", type), Create(Tenant, User, "connection-a", type),
                Create(Tenant, User, "connection-b", type)];
            Controls = [Create(Tenant, Guid.NewGuid(), "connection-a", type),
                Create(Guid.NewGuid(), User, "connection-b", type), Create(Tenant, Guid.NewGuid(), "control", type)];
            foreach (var item in Targets.Concat(Controls)) Assert.True(Registry.TryAdd(item, 20));
            Invalidator = new RealtimeConnectionInvalidator(Registry, Hub);
        }

        public void AssertScopeRemoved()
        {
            foreach (var item in Targets)
            {
                Assert.DoesNotContain(item, Registry.GetForTarget(item.TenantId, item.SubscriptionType, item.ResourceId));
            }
            foreach (var item in Controls)
            {
                Assert.Contains(item, Registry.GetForTarget(item.TenantId, item.SubscriptionType, item.ResourceId));
            }
        }

        private static HubSubscription Create(Guid tenant, Guid user, string connection, RealtimeSubscriptionType type) =>
            new(connection, user, Guid.NewGuid(), tenant, type, Guid.NewGuid(), $"synthetic-group-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
    }

    private sealed class RecordingHub : IHubContext<AppHub>
    {
        public List<(string Kind, string Connection)> Trace { get; } = [];
        public RecordingGroups GroupsRecorder { get; }
        public RecordingClients ClientsRecorder { get; }
        public IHubClients Clients => ClientsRecorder;
        public IGroupManager Groups => GroupsRecorder;
        public RecordingHub() { GroupsRecorder = new(Trace); ClientsRecorder = new(Trace); }
    }

    private sealed class RecordingGroups(List<(string Kind, string Connection)> trace) : IGroupManager
    {
        public List<(string Connection, string Group)> Removals { get; } = [];
        public Func<string, string, CancellationToken, Task>? BeforeRemove { get; set; }
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removals.Add((connectionId, groupName)); trace.Add(("remove", connectionId));
            return BeforeRemove?.Invoke(connectionId, groupName, cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class RecordingClients(List<(string Kind, string Connection)> trace) : IHubClients
    {
        public List<(string Connection, string Method, object?[] Arguments, CancellationToken Token)> Sends { get; } = [];
        public Func<string, string, object?[], CancellationToken, Task>? BeforeSend { get; set; }
        private void RecordInvalidation(string connection) => trace.Add(("invalidate", connection));
        public IClientProxy Client(string connectionId) => new ClientProxy(this, connectionId);
        private sealed class ClientProxy(RecordingClients owner, string connection) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.Sends.Add((connection, method, args, cancellationToken)); owner.RecordInvalidation(connection);
                return owner.BeforeSend?.Invoke(connection, method, args, cancellationToken) ?? Task.CompletedTask;
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
    }

    private static InvalidOperationException Unexpected() => new("Unexpected broad hub dispatch or group enrollment.");
}
