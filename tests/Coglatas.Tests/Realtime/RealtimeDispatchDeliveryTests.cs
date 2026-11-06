using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Messaging;
using Coglatas.Application.Notifications;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Web.Realtime;

namespace Coglatas.Tests.Realtime;

// Actual production dispatch boundary with strict synthetic dependencies. These
// controls do not establish persisted revocation, PostgreSQL RLS or live delivery.
public sealed class RealtimeDispatchDeliveryTests
{
    [Theory]
    [InlineData("type", false)]
    [InlineData("resource", false)]
    [InlineData("tenant", false)]
    [InlineData("type", true)]
    [InlineData("resource", true)]
    [InlineData("tenant", true)]
    public async Task MismatchedSubscriptionCannotReachSessionsOrAnyDeliveryAuthority(string mismatch, bool invalidation)
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(RealtimeSubscriptionType.User);
        var type = mismatch == "type" ? RealtimeSubscriptionType.Workspace : subscription.SubscriptionType;
        var resource = mismatch == "resource" ? Guid.NewGuid() : subscription.ResourceId;
        var envelope = fixture.Envelope(invalidation ? "Security.AuthorizationStateChanged.v1" : "Files.FileChanged.v1");
        if (mismatch == "tenant") envelope = envelope with { TenantId = Guid.NewGuid() };

        Assert.False(await fixture.Authorizer.CanReceiveAsync(subscription, type, resource, envelope, CancellationToken.None));

        Assert.Empty(fixture.Sessions.Calls);
        Assert.Empty(fixture.Trace);
    }

    [Theory]
    [InlineData("Messaging.MessageCreated.v1")]
    [InlineData("Messaging.ConversationUnreadChanged.v1")]
    [InlineData("Notifications.NotificationCreated.v1")]
    [InlineData("Notifications.NotificationReadStateChanged.v1")]
    [InlineData("Projects.TaskChanged.v1")]
    [InlineData("Projects.ProjectChanged.v1")]
    [InlineData("Files.FileChanged.v1")]
    public async Task EachDeliveryRechecksTheExactSessionAndCannotReuseAnEarlierPermission(string eventType)
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(RealtimeSubscriptionType.User);
        var envelope = fixture.Envelope(eventType);
        using var cancellation = new CancellationTokenSource();

        Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
        var traceBeforeRevocation = fixture.Trace.ToArray();
        fixture.Sessions.Valid = false;
        Assert.False(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
        Assert.Equal(traceBeforeRevocation, fixture.Trace);
        fixture.Sessions.Valid = true;
        Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));

        Assert.Equal(3, fixture.Sessions.Calls.Count);
        Assert.All(fixture.Sessions.Calls, call => Assert.Equal(new SessionCall(
            fixture.User, fixture.Session, fixture.Tenant, true, cancellation.Token), call));
    }

    [Theory]
    [InlineData("Notifications.NotificationCreated.v1", RealtimeSubscriptionType.Workspace)]
    [InlineData("Notifications.NotificationCreated.v1", RealtimeSubscriptionType.User)]
    [InlineData("Notifications.NotificationReadStateChanged.v1", RealtimeSubscriptionType.Workspace)]
    [InlineData("Notifications.NotificationReadStateChanged.v1", RealtimeSubscriptionType.User)]
    public async Task RecipientEventsRejectAValidSubscriptionForAnotherRouteBeforeResolvingProtectedTargets(string eventType, RealtimeSubscriptionType type)
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(type) with { ResourceId = Guid.NewGuid() };

        Assert.False(await fixture.ReceiveAsync(subscription, fixture.Envelope(eventType), CancellationToken.None));

        Assert.Single(fixture.Sessions.Calls);
        Assert.Empty(fixture.Trace);
    }

    [Theory]
    [InlineData("Notifications.NotificationCreated.v1", "created", true)]
    [InlineData("Notifications.NotificationCreated.v1", "created", false)]
    [InlineData("Notifications.NotificationReadStateChanged.v1", "read-state", true)]
    [InlineData("Notifications.NotificationReadStateChanged.v1", "read-state", false)]
    public async Task RecipientEventsUseOnlyTheirCurrentTargetDecisionAndPreserveTheOriginalEnvelope(string eventType, string operation, bool allowed)
    {
        var fixture = new Fixture { Notifications = { Allowed = allowed } };
        var envelope = fixture.Envelope(eventType);
        using var cancellation = new CancellationTokenSource();

        Assert.Equal(allowed, await fixture.ReceiveAsync(fixture.Subscription(RealtimeSubscriptionType.User), envelope, cancellation.Token));

        var call = Assert.Single(fixture.Notifications.Calls);
        Assert.Equal((operation, fixture.Tenant, fixture.User, cancellation.Token), (call.Operation, call.Tenant, call.User, call.Token));
        Assert.Same(envelope, call.Envelope);
        Assert.Equal([operation], fixture.Trace);
        Assert.Single(fixture.Sessions.Calls);
    }

    [Theory]
    [InlineData("Notifications.NotificationCreated.v1", "created")]
    [InlineData("Notifications.NotificationReadStateChanged.v1", "read-state")]
    public async Task RecipientAuthorityIsRecheckedAfterRevocationOnTheSameAuthorizerAndEnvelope(string eventType, string operation)
    {
        var fixture = new Fixture();
        var envelope = fixture.Envelope(eventType);
        var subscription = fixture.Subscription(RealtimeSubscriptionType.User);
        using var cancellation = new CancellationTokenSource();

        Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
        fixture.Notifications.Allowed = false;
        Assert.False(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
        fixture.Notifications.Allowed = true;
        Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));

        Assert.Equal(3, fixture.Sessions.Calls.Count);
        Assert.Equal(3, fixture.Notifications.Calls.Count);
        foreach (var call in fixture.Notifications.Calls)
        {
            Assert.Equal((operation, fixture.Tenant, fixture.User, cancellation.Token),
                (call.Operation, call.Tenant, call.User, call.Token));
            Assert.Same(envelope, call.Envelope);
        }
        Assert.Equal([operation, operation, operation], fixture.Trace);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("17")]
    [InlineData("\"not-an-object\"")]
    [InlineData("{\"conversationId\":17}")]
    public async Task UnreadPayloadShapesCannotFallThroughToTheHistoricalUserRoute(string json)
    {
        var fixture = new Fixture();
        using var document = JsonDocument.Parse(json);
        var envelope = fixture.Envelope("Messaging.ConversationUnreadChanged.v1") with { Payload = document.RootElement.Clone() };

        Assert.False(await fixture.ReceiveAsync(fixture.Subscription(RealtimeSubscriptionType.User), envelope, CancellationToken.None));

        Assert.Single(fixture.Sessions.Calls);
        Assert.Empty(fixture.Trace);
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.Workspace)]
    [InlineData(RealtimeSubscriptionType.User)]
    public async Task UnreadPayloadDoesNotAuthorizeDeliveryToAnotherRoute(RealtimeSubscriptionType type)
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(type) with { ResourceId = Guid.NewGuid() };

        Assert.False(await fixture.ReceiveAsync(subscription, fixture.Envelope("Messaging.ConversationUnreadChanged.v1"), CancellationToken.None));

        Assert.Single(fixture.Sessions.Calls);
        Assert.Empty(fixture.Trace);
    }

    [Fact]
    public async Task DelayedUnreadEventUsesThePayloadConversationAndItsCurrentReadAuthorityOnEveryAttempt()
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(RealtimeSubscriptionType.User);
        var envelope = fixture.Envelope("Messaging.ConversationUnreadChanged.v1");
        Assert.NotEqual(envelope.AggregateId, fixture.Conversation);
        Assert.NotEqual(subscription.ResourceId, fixture.Conversation);
        using var cancellation = new CancellationTokenSource();

        Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
        fixture.Resources.Allowed = false;
        Assert.False(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));

        Assert.Equal(2, fixture.Resources.Calls.Count);
        Assert.All(fixture.Resources.Calls, call => Assert.Equal(
            new ResourceCall(RealtimeSubscriptionType.Conversation, fixture.User, fixture.Conversation, cancellation.Token), call));
        Assert.Equal(["Conversation", "Conversation"], fixture.Trace);
    }

    [Theory]
    [InlineData("Projects.TaskChanged.v1", "task")]
    [InlineData("Projects.TaskAssignmentChanged.v1", "task")]
    [InlineData("Projects.TaskWorkflowChanged.v1", "task")]
    [InlineData("Projects.TaskCommentChanged.v1", "task")]
    [InlineData("Projects.ProjectChanged.v1", "project")]
    [InlineData("Security.AuthorizationStateChanged.v1", "authorization")]
    public async Task InvalidationsPreserveCurrentTargetResolutionForEverySubscriptionScope(string eventType, string operation)
    {
        foreach (var type in Enum.GetValues<RealtimeSubscriptionType>())
        {
            var fixture = new Fixture();
            var subscription = fixture.Subscription(type);
            var envelope = fixture.Envelope(eventType);
            using var cancellation = new CancellationTokenSource();
            if (operation == "authorization") fixture.Sessions.Valid = false;

            Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
            fixture.Targets.Allowed = false;
            Assert.False(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));

            Assert.Equal(operation == "authorization" ? 0 : 2, fixture.Sessions.Calls.Count);
            Assert.Equal(2, fixture.Targets.Calls.Count);
            foreach (var call in fixture.Targets.Calls)
            {
                Assert.Equal((operation, fixture.Tenant, fixture.User, type, subscription.ResourceId, cancellation.Token),
                    (call.Operation, call.Tenant, call.User, call.Type, call.Resource, call.Token));
                Assert.Same(envelope, call.Envelope);
            }
            Assert.Equal([operation, operation], fixture.Trace);
        }
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.User, true)]
    [InlineData(RealtimeSubscriptionType.User, false)]
    [InlineData(RealtimeSubscriptionType.Tenant, true)]
    [InlineData(RealtimeSubscriptionType.Tenant, false)]
    public async Task IdentityRoutesRequireTheAuthenticatedIdentityEvenWhenHistoricalSubscriptionMatches(RealtimeSubscriptionType type, bool own)
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(type);
        if (!own) subscription = subscription with { ResourceId = Guid.NewGuid() };

        Assert.Equal(own, await fixture.ReceiveAsync(subscription, fixture.Envelope("Messaging.MessageCreated.v1"), CancellationToken.None));

        Assert.Single(fixture.Sessions.Calls);
        Assert.Empty(fixture.Trace);
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.Workspace)]
    [InlineData(RealtimeSubscriptionType.Conversation)]
    [InlineData(RealtimeSubscriptionType.Project)]
    public async Task ResourceRoutesUseOnlyTheCurrentReadDecisionAndRecheckItAfterRevocation(RealtimeSubscriptionType type)
    {
        var fixture = new Fixture();
        var subscription = fixture.Subscription(type);
        using var cancellation = new CancellationTokenSource();
        var envelope = fixture.Envelope("Messaging.MessageCreated.v1");

        Assert.True(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));
        fixture.Resources.Allowed = false;
        Assert.False(await fixture.ReceiveAsync(subscription, envelope, cancellation.Token));

        Assert.Equal(2, fixture.Resources.Calls.Count);
        Assert.All(fixture.Resources.Calls, call => Assert.Equal(new ResourceCall(type, fixture.User, subscription.ResourceId, cancellation.Token), call));
        Assert.Equal([type.ToString(), type.ToString()], fixture.Trace);
    }

    [Fact]
    public async Task UnsupportedSubscriptionKindsCannotAuthorizeFileLookupOrDelivery()
    {
        var fixture = new Fixture();

        Assert.False(await fixture.ReceiveAsync(fixture.Subscription((RealtimeSubscriptionType)999), fixture.Envelope("Files.FileChanged.v1"), CancellationToken.None));

        Assert.Single(fixture.Sessions.Calls);
        Assert.Empty(fixture.Trace);
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.Workspace)]
    [InlineData(RealtimeSubscriptionType.Conversation)]
    [InlineData(RealtimeSubscriptionType.Project)]
    public async Task DeniedResourceCannotReadTheEventFileAttachment(RealtimeSubscriptionType type)
    {
        var fixture = new Fixture { Resources = { Allowed = false } };

        Assert.False(await fixture.ReceiveAsync(fixture.Subscription(type), fixture.Envelope("Files.FileChanged.v1"), CancellationToken.None));

        Assert.Equal([type.ToString()], fixture.Trace);
        Assert.Empty(fixture.Files.Reads);
        Assert.Empty(fixture.FileAuthority.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("allowed")]
    public async Task FileInvalidationRequiresTheCurrentAttachmentAndItsIndependentReadDecision(string state)
    {
        var fixture = new Fixture { FileAuthority = { Allowed = state == "allowed" } };
        if (state == "missing") fixture.Files.Attachment = null;
        var envelope = fixture.Envelope("Files.FileChanged.v1");
        using var cancellation = new CancellationTokenSource();

        Assert.Equal(state == "allowed", await fixture.ReceiveAsync(fixture.Subscription(RealtimeSubscriptionType.Workspace), envelope, cancellation.Token));

        Assert.Equal((envelope.AggregateId, cancellation.Token), Assert.Single(fixture.Files.Reads));
        if (state == "missing")
        {
            Assert.Empty(fixture.FileAuthority.Calls);
            Assert.Equal(["Workspace", "lookup"], fixture.Trace);
        }
        else
        {
            var call = Assert.Single(fixture.FileAuthority.Calls);
            Assert.Equal(fixture.User, call.User);
            Assert.Same(fixture.Files.Attachment, call.Attachment);
            Assert.Equal(cancellation.Token, call.Token);
            Assert.Equal(["Workspace", "lookup", "file"], fixture.Trace);
        }
    }

    [Fact]
    public async Task APreviouslyPermittedFileEventCannotReuseAnOldAttachmentOrPermission()
    {
        var fixture = new Fixture();
        var envelope = fixture.Envelope("Files.FileChanged.v1");
        var subscription = fixture.Subscription(RealtimeSubscriptionType.User);
        var first = fixture.Files.Attachment;

        Assert.True(await fixture.ReceiveAsync(subscription, envelope, CancellationToken.None));
        fixture.Files.Attachment = new Attachment { Id = Guid.NewGuid(), TenantId = fixture.Tenant, OwnerId = Guid.NewGuid() };
        fixture.FileAuthority.Allowed = false;
        Assert.False(await fixture.ReceiveAsync(subscription, envelope, CancellationToken.None));

        Assert.Equal(2, fixture.Files.Reads.Count);
        Assert.All(fixture.Files.Reads, call => Assert.Equal(envelope.AggregateId, call.File));
        Assert.Collection(fixture.FileAuthority.Calls,
            call => Assert.Same(first, call.Attachment),
            call => Assert.Same(fixture.Files.Attachment, call.Attachment));
        Assert.Equal(["lookup", "file", "lookup", "file"], fixture.Trace);
    }

    [Theory]
    [InlineData("session", false)]
    [InlineData("workspace", false)]
    [InlineData("conversation", false)]
    [InlineData("project-read", false)]
    [InlineData("created", false)]
    [InlineData("read-state", false)]
    [InlineData("task", false)]
    [InlineData("project", false)]
    [InlineData("authorization", false)]
    [InlineData("lookup", false)]
    [InlineData("file", false)]
    [InlineData("session", true)]
    [InlineData("workspace", true)]
    [InlineData("conversation", true)]
    [InlineData("project-read", true)]
    [InlineData("created", true)]
    [InlineData("read-state", true)]
    [InlineData("task", true)]
    [InlineData("project", true)]
    [InlineData("authorization", true)]
    [InlineData("lookup", true)]
    [InlineData("file", true)]
    public async Task DependencyFailureOrCancellationCannotBecomeAnAuthorizedDelivery(string stage, bool cancelled)
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        Exception expected = cancelled
            ? new OperationCanceledException("Synthetic dispatch cancellation.", cancellation.Token)
            : new IOException("Synthetic dispatch dependency failure.");
        var type = RealtimeSubscriptionType.User;
        var eventType = stage switch
        {
            "created" => "Notifications.NotificationCreated.v1",
            "read-state" => "Notifications.NotificationReadStateChanged.v1",
            "task" => "Projects.TaskChanged.v1",
            "project" => "Projects.ProjectChanged.v1",
            "authorization" => "Security.AuthorizationStateChanged.v1",
            "lookup" or "file" => "Files.FileChanged.v1",
            _ => "Messaging.MessageCreated.v1"
        };
        switch (stage)
        {
            case "session": fixture.Sessions.Failure = expected; break;
            case "workspace": type = RealtimeSubscriptionType.Workspace; fixture.Resources.Failure = expected; break;
            case "conversation": type = RealtimeSubscriptionType.Conversation; fixture.Resources.Failure = expected; break;
            case "project-read": type = RealtimeSubscriptionType.Project; fixture.Resources.Failure = expected; break;
            case "created" or "read-state": fixture.Notifications.Failure = expected; break;
            case "task" or "project" or "authorization": fixture.Targets.Failure = expected; break;
            case "lookup": fixture.Files.Failure = expected; break;
            case "file": fixture.FileAuthority.Failure = expected; break;
        }

        var error = await Record.ExceptionAsync(() => fixture.ReceiveAsync(fixture.Subscription(type), fixture.Envelope(eventType), cancellation.Token));

        Assert.Same(expected, error);
        Assert.Equal(stage == "authorization" ? 0 : 1, fixture.Sessions.Calls.Count);
        Assert.All(fixture.Sessions.Calls, call => Assert.Equal(cancellation.Token, call.Token));
        string[] expectedTrace = stage switch
        {
            "session" => [],
            "workspace" or "conversation" or "project-read" => [type.ToString()],
            "file" => ["lookup", "file"],
            _ => [stage]
        };
        Assert.Equal(expectedTrace, fixture.Trace);
        Assert.All(fixture.Resources.Calls, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.All(fixture.Notifications.Calls, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.All(fixture.Targets.Calls, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.All(fixture.Files.Reads, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.All(fixture.FileAuthority.Calls, call => Assert.Equal(cancellation.Token, call.Token));
    }

    private sealed class Fixture
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public Guid Session { get; } = Guid.NewGuid();
        public Guid Conversation { get; } = Guid.NewGuid();
        public List<string> Trace { get; } = [];
        public RecordingSubscriptionSessions Sessions { get; } = new();
        public ResourceAuthority Resources { get; }
        public NotificationAuthority Notifications { get; }
        public TargetAuthority Targets { get; }
        public CurrentFiles Files { get; }
        public FileAuthority FileAuthority { get; }
        public RealtimeDispatchAuthorizer Authorizer { get; }

        public Fixture()
        {
            Resources = new ResourceAuthority(Trace);
            Notifications = new NotificationAuthority(Trace);
            Targets = new TargetAuthority(Trace);
            Files = new CurrentFiles(Trace) { Attachment = new Attachment { Id = Guid.NewGuid(), TenantId = Tenant } };
            FileAuthority = new FileAuthority(Trace);
            Authorizer = new RealtimeDispatchAuthorizer(Sessions, Resources, Resources, Resources, Files, FileAuthority, Notifications, Targets);
        }

        public HubSubscription Subscription(RealtimeSubscriptionType type) => new(
            "synthetic-dispatch-connection", User, Session, Tenant, type,
            type switch { RealtimeSubscriptionType.User => User, RealtimeSubscriptionType.Tenant => Tenant, _ => Guid.NewGuid() },
            "untrusted-historical-group", DateTimeOffset.UtcNow);

        public DurableEventEnvelope Envelope(string eventType) => new(Guid.NewGuid(), eventType, 1, DateTimeOffset.UtcNow,
            Tenant, "Synthetic", Guid.NewGuid(), 1, RealtimeActor.System(), "synthetic-correlation", "synthetic-causation",
            JsonSerializer.SerializeToElement(new { conversationId = Conversation, unreadCount = 1 }));

        public Task<bool> ReceiveAsync(HubSubscription subscription, DurableEventEnvelope envelope, CancellationToken token) =>
            Authorizer.CanReceiveAsync(subscription, subscription.SubscriptionType, subscription.ResourceId, envelope, token);
    }

    private sealed class ResourceAuthority(List<string> trace) : IWorkspaceAuthorizationService, IConversationAuthorizationService, IProjectAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Exception? Failure { get; set; }
        public List<ResourceCall> Calls { get; } = [];
        private Task<bool> ReadAsync(RealtimeSubscriptionType type, Guid user, Guid resource, CancellationToken token)
        {
            trace.Add(type.ToString());
            Calls.Add(new ResourceCall(type, user, resource, token));
            return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Allowed);
        }
        public Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => ReadAsync(RealtimeSubscriptionType.Workspace, userId, workspaceId, cancellationToken);
        public Task<bool> CanViewConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => ReadAsync(RealtimeSubscriptionType.Conversation, userId, conversationId, cancellationToken);
        public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => ReadAsync(RealtimeSubscriptionType.Project, userId, projectId, cancellationToken);
        public Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanSendMessage(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanManageConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanModerateConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanCreateThread(Guid userId, Guid parentConversationId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanEditMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanDeleteMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private sealed record NotificationCall(string Operation, Guid Tenant, Guid User, DurableEventEnvelope Envelope, CancellationToken Token);
    private sealed class NotificationAuthority(List<string> trace) : INotificationTargetResolver
    {
        public bool Allowed { get; set; } = true;
        public Exception? Failure { get; set; }
        public List<NotificationCall> Calls { get; } = [];
        private Task<bool> ReadAsync(string operation, Guid tenant, Guid user, DurableEventEnvelope envelope, CancellationToken token)
        {
            trace.Add(operation);
            Calls.Add(new NotificationCall(operation, tenant, user, envelope, token));
            return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Allowed);
        }
        public Task<bool> CanDeliverCreatedAsync(Guid tenantId, Guid recipientUserId, DurableEventEnvelope envelope, CancellationToken cancellationToken = default) => ReadAsync("created", tenantId, recipientUserId, envelope, cancellationToken);
        public Task<bool> CanDeliverReadStateAsync(Guid tenantId, Guid recipientUserId, DurableEventEnvelope envelope, CancellationToken cancellationToken = default) => ReadAsync("read-state", tenantId, recipientUserId, envelope, cancellationToken);
        public Task<NotificationTargetResolution> ResolveAsync(Guid tenantId, Guid userId, Guid notificationId, CancellationToken cancellationToken = default) => throw Unexpected();
        public IQueryable<Guid> QueryAvailableNotificationIds(Guid tenantId, Guid userId) => throw Unexpected();
        public Task<IReadOnlySet<Guid>> FilterAvailableNotificationIdsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private sealed record TargetCall(string Operation, Guid Tenant, Guid User, RealtimeSubscriptionType Type, Guid Resource, DurableEventEnvelope Envelope, CancellationToken Token);
    private sealed class TargetAuthority(List<string> trace) : IRealtimeEventTargetResolver
    {
        public bool Allowed { get; set; } = true;
        public Exception? Failure { get; set; }
        public List<TargetCall> Calls { get; } = [];
        private Task<bool> ReadAsync(string operation, Guid tenant, Guid user, RealtimeSubscriptionType type, Guid resource, DurableEventEnvelope envelope, CancellationToken token)
        {
            trace.Add(operation);
            Calls.Add(new TargetCall(operation, tenant, user, type, resource, envelope, token));
            return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Allowed);
        }
        public Task<bool> CanReceiveTaskEventAsync(Guid tenantId, Guid userId, RealtimeSubscriptionType targetType, Guid targetResourceId, DurableEventEnvelope envelope, CancellationToken cancellationToken = default) => ReadAsync("task", tenantId, userId, targetType, targetResourceId, envelope, cancellationToken);
        public Task<bool> CanReceiveProjectEventAsync(Guid tenantId, Guid userId, RealtimeSubscriptionType targetType, Guid targetResourceId, DurableEventEnvelope envelope, CancellationToken cancellationToken = default) => ReadAsync("project", tenantId, userId, targetType, targetResourceId, envelope, cancellationToken);
        public Task<bool> CanReceiveAuthorizationInvalidationAsync(Guid tenantId, Guid userId, RealtimeSubscriptionType targetType, Guid targetResourceId, DurableEventEnvelope envelope, CancellationToken cancellationToken = default) => ReadAsync("authorization", tenantId, userId, targetType, targetResourceId, envelope, cancellationToken);
    }

    private sealed class CurrentFiles(List<string> trace) : IFileRepository
    {
        public Attachment? Attachment { get; set; }
        public Exception? Failure { get; set; }
        public List<(Guid File, CancellationToken Token)> Reads { get; } = [];
        public Task<Attachment?> GetAttachmentByFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default)
        {
            trace.Add("lookup");
            Reads.Add((fileObjectId, cancellationToken));
            return Failure is { } failure ? Task.FromException<Attachment?>(failure) : Task.FromResult(Attachment);
        }
        public Task<FileObject?> GetFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<PagedResponse<Attachment>> ListWorkspaceFileObjectsAsync(Guid workspaceId, int page, int pageSize, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<PagedResponse<Attachment>> ListAccessibleWorkspaceFileObjectsAsync(Guid workspaceId, Guid userId, bool canManageSharing, int page, int pageSize, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task AddFileObjectAsync(FileObject fileObject, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<Attachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<FileOwnerContext?> ResolveOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<IReadOnlyList<FileVersionRecord>> ListFileVersionsAsync(Guid tenantId, Guid fileObjectId, int limit, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<FileVersionRecord?> GetFileVersionAsync(Guid tenantId, Guid fileObjectId, Guid versionId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<IReadOnlyList<FileSharingActivityRecord>> ListFileSharingActivityAsync(Guid tenantId, Guid fileObjectId, int limit, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<IReadOnlyList<Attachment>> ListTaskAttachmentsAsync(Guid taskItemId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<PagedResponse<Attachment>> ListTaskAttachmentsPageAsync(Guid taskItemId, int page, int pageSize, CancellationToken cancellationToken = default) => throw Unexpected();
        public void RemoveAttachment(Attachment attachment) => throw Unexpected();
        public Task<bool> ReadCurrentUploadAdmissionAsync(Func<CancellationToken, Task<bool>> admission, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private sealed class FileAuthority(List<string> trace) : IFileAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Exception? Failure { get; set; }
        public List<(Guid User, Attachment Attachment, CancellationToken Token)> Calls { get; } = [];
        public Task<bool> CanViewAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default)
        {
            trace.Add("file");
            Calls.Add((userId, attachment, cancellationToken));
            return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Allowed);
        }
        public Task<bool> CanUploadAttachment(Guid userId, AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanViewWorkspaceFiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanDownloadAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<IReadOnlySet<Guid>> GetDeletableWorkspaceAttachmentIdsAsync(Guid userId, Guid workspaceId, IReadOnlyCollection<Attachment> attachments, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> CanDeleteAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) => throw Unexpected();
    }

    private static InvalidOperationException Unexpected() => new("Dispatch must not invoke an unrelated read, write, download or notification-open operation.");
}
