using System.Text.Json;
using Coglatas.Application.Messaging;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class WorkspaceChannelMessagingPostgreSqlTests
{
    private static readonly JsonSerializerOptions ThreadMutationJsonOptions = new(JsonSerializerDefaults.Web);

    [PostgreSqlFact]
    public Task CanonicalReplyEditPersistsVersionAuditAndThreadRefreshWithoutChangingOtherRows() =>
        WithReplyMutationAsync(delete: false);

    [PostgreSqlFact]
    public Task CanonicalReplyDeleteRetainsTombstoneAuditAndThreadRefreshWithoutChangingOtherRows() =>
        WithReplyMutationAsync(delete: true);

    private static Task WithReplyMutationAsync(bool delete) => WithScenarioAsync(async scenario =>
    {
        const string originalBody = "Original synthetic reply.";
        const string editedBody = "Edited synthetic reply.";
        var root = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Original synthetic root.", ClientRequestId: Guid.NewGuid()));
        Assert.True(root.IsSuccess, root.Error);
        var rootId = root.Value!.Id;
        var reply = await scenario.Messages.SendThreadMessageAsync(rootId, new(originalBody, Guid.NewGuid()));
        Assert.True(reply.IsSuccess, reply.Error);
        var replyId = reply.Value!.Message.Id;
        var sentinel = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Unrelated synthetic message.", ClientRequestId: Guid.NewGuid()));
        Assert.True(sentinel.IsSuccess, sentinel.Error);
        var sentinelId = sentinel.Value!.Id;
        Assert.Equal(rootId, reply.Value.Message.ThreadRootMessageId);
        Assert.Equal(3, new[] { rootId, replyId, sentinelId }.Distinct().Count());
        var database = scenario.Db.Database.GetConnectionString()!;
        await using var beforeReader = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var beforeMessages = await CaptureThreadScalarRowsAsync(beforeReader, beforeReader.Messages, row => row.Id);
        Assert.Equal(new[] { rootId, replyId, sentinelId }.Order(), beforeMessages.Keys.Order());
        var storedReply = await beforeReader.Messages.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == replyId);
        Assert.Equal((scenario.Tenant.Id, scenario.Workspace.Id, scenario.Conversation.Id, scenario.Actor.Id, rootId),
            (storedReply.TenantId, storedReply.WorkspaceId, storedReply.ConversationId, storedReply.AuthorUserId, storedReply.ThreadRootMessageId!.Value));
        Assert.Equal((originalBody, 1L), (storedReply.Body, storedReply.Version));
        Assert.Null(storedReply.EditedAt);
        Assert.Null(storedReply.DeletedAt);
        Assert.Equal("Original synthetic root.", beforeMessages[rootId]["Body"]);
        Assert.Equal("Unrelated synthetic message.", beforeMessages[sentinelId]["Body"]);
        foreach (var id in new[] { rootId, sentinelId })
        {
            Assert.Equal(scenario.Tenant.Id, beforeMessages[id]["TenantId"]);
            Assert.Equal(scenario.Workspace.Id, beforeMessages[id]["WorkspaceId"]);
            Assert.Equal(scenario.Conversation.Id, beforeMessages[id]["ConversationId"]);
            Assert.Equal(scenario.Actor.Id, beforeMessages[id]["AuthorUserId"]);
            Assert.Null(beforeMessages[id]["ThreadRootMessageId"]);
            Assert.Equal(1L, beforeMessages[id]["Version"]);
            Assert.Null(beforeMessages[id]["DeletedAt"]);
        }
        var beforeConversations = await CaptureThreadScalarRowsAsync(beforeReader, beforeReader.Conversations, row => row.Id);
        Assert.True(beforeConversations.ContainsKey(scenario.Conversation.Id));
        var beforeOutbox = await CaptureThreadScalarRowsAsync(beforeReader, beforeReader.OutboxEvents, row => row.Id);
        var beforeAudit = await CaptureThreadScalarRowsAsync(beforeReader, beforeReader.AuditLogs, row => row.Id);
        Assert.NotEmpty(beforeOutbox);
        Assert.NotEmpty(beforeAudit);
        scenario.Db.ChangeTracker.Clear();
        using var cancellation = new CancellationTokenSource();
        var startedAt = DateTimeOffset.UtcNow;
        if (delete)
        {
            var result = await scenario.Messages.DeleteMessageAsync(replyId, cancellation.Token);
            Assert.True(result.IsSuccess, result.Error);
        }
        else
        {
            var result = await scenario.Messages.UpdateMessageAsync(replyId, new UpdateMessageRequest($"  {editedBody}  "), cancellation.Token);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal((replyId, scenario.Conversation.Id, scenario.Actor.Id, rootId, 2L, editedBody, false),
                (result.Value!.Id, result.Value.ConversationId, result.Value.AuthorUserId, result.Value.ThreadRootMessageId!.Value,
                    result.Value.Version, result.Value.Body, result.Value.IsDeleted));
            Assert.Empty(result.Value.Attachments);
        }
        var completedAt = DateTimeOffset.UtcNow;
        // PostgreSQL timestamp storage has microsecond precision; allow its sub-microsecond truncation.
        var storageLowerBound = startedAt.AddTicks(-9);
        await using var afterReader = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var after = await afterReader.Messages.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == replyId);
        Assert.Equal(2L, after.Version);
        Assert.NotNull(after.UpdatedAt);
        Assert.InRange(after.UpdatedAt!.Value, storageLowerBound, completedAt);
        var expectedReply = new Dictionary<string, object?>(beforeMessages[replyId])
        {
            ["Body"] = delete ? string.Empty : editedBody,
            ["Version"] = 2L,
            ["UpdatedAt"] = after.UpdatedAt
        };
        if (delete)
        {
            Assert.NotNull(after.DeletedAt);
            Assert.InRange(after.DeletedAt!.Value, storageLowerBound, completedAt);
            Assert.Equal(scenario.Actor.Id, after.DeletedByUserId);
            Assert.Equal("author_delete", after.DeleteReason);
            expectedReply["DeletedAt"] = after.DeletedAt;
            expectedReply["DeletedByUserId"] = scenario.Actor.Id;
            expectedReply["DeleteReason"] = "author_delete";
        }
        else
        {
            Assert.NotNull(after.EditedAt);
            Assert.InRange(after.EditedAt!.Value, storageLowerBound, completedAt);
            expectedReply["EditedAt"] = after.EditedAt;
        }
        var expectedMessages = beforeMessages.ToDictionary(row => row.Key, row => new Dictionary<string, object?>(row.Value));
        expectedMessages[replyId] = expectedReply;
        Assert.Empty(ThreadScalarDifferences(expectedMessages, await CaptureThreadScalarRowsAsync(afterReader, afterReader.Messages, row => row.Id)));
        Assert.Empty(ThreadScalarDifferences(beforeConversations, await CaptureThreadScalarRowsAsync(afterReader, afterReader.Conversations, row => row.Id)));

        var outboxRows = await afterReader.OutboxEvents.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        var addedEvents = outboxRows.Where(row => !beforeOutbox.ContainsKey(row.Id)).ToArray();
        Assert.Equal(2, addedEvents.Length);
        var retainedOutbox = (await CaptureThreadScalarRowsAsync(afterReader, afterReader.OutboxEvents, row => row.Id))
            .Where(row => beforeOutbox.ContainsKey(row.Key)).ToDictionary(row => row.Key, row => row.Value);
        Assert.Empty(ThreadScalarDifferences(beforeOutbox, retainedOutbox));
        var messageType = delete ? "Messaging.MessageDeleted.v1" : "Messaging.MessageUpdated.v1";
        var messageEvent = Assert.Single(addedEvents, row => row.EventType == messageType);
        var threadEvent = Assert.Single(addedEvents, row => row.EventType == "Messaging.ThreadChanged.v1");
        Assert.Equal(("Message", replyId, (long?)2), (messageEvent.AggregateType, messageEvent.AggregateId, messageEvent.AggregateVersion));
        Assert.Equal(("MessageThread", rootId), (threadEvent.AggregateType, threadEvent.AggregateId));
        Assert.Null(threadEvent.AggregateVersion);
        foreach (var item in addedEvents)
        {
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.Equal((scenario.Tenant.Id, 1, OutboxEventStatus.Pending, 0),
                (item.TenantId, item.PayloadSchemaVersion, item.Status, item.AttemptCount));
            using var document = JsonDocument.Parse(item.PayloadJson);
            var envelope = document.RootElement;
            var isMessage = item.Id == messageEvent.Id;
            Assert.Equal(item.Id, envelope.GetProperty("eventId").GetGuid());
            Assert.Equal(scenario.Tenant.Id, envelope.GetProperty("tenantId").GetGuid());
            Assert.Equal("User", envelope.GetProperty("actor").GetProperty("actorType").GetString());
            Assert.Equal(scenario.Actor.Id, envelope.GetProperty("actor").GetProperty("actorId").GetGuid());
            Assert.Equal(isMessage ? messageType : "Messaging.ThreadChanged.v1", envelope.GetProperty("eventType").GetString());
            Assert.Equal(1, envelope.GetProperty("payloadSchemaVersion").GetInt32());
            Assert.Equal(isMessage ? "Message" : "MessageThread", envelope.GetProperty("aggregateType").GetString());
            Assert.Equal(isMessage ? replyId : rootId, envelope.GetProperty("aggregateId").GetGuid());
            if (isMessage) Assert.Equal(2L, envelope.GetProperty("aggregateVersion").GetInt64());
            else Assert.Equal(JsonValueKind.Null, envelope.GetProperty("aggregateVersion").ValueKind);
            var target = Assert.Single(JsonSerializer.Deserialize<RealtimeRoutingTarget[]>(item.RoutingJson, ThreadMutationJsonOptions)!);
            Assert.Equal(new RealtimeRoutingTarget(RealtimeSubscriptionType.Conversation, scenario.Conversation.Id), target);
        }
        using var messageDocument = JsonDocument.Parse(messageEvent.PayloadJson);
        var messagePayload = messageDocument.RootElement.GetProperty("payload");
        Assert.Equal(scenario.Conversation.Id, messagePayload.GetProperty("conversationId").GetGuid());
        Assert.Equal(replyId, messagePayload.GetProperty("messageId").GetGuid());
        Assert.Equal(rootId, messagePayload.GetProperty("threadRootMessageId").GetGuid());
        Assert.Equal(2L, messagePayload.GetProperty("messageVersion").GetInt64());
        var persistedMutationTimestamp = delete ? after.DeletedAt!.Value : after.EditedAt!.Value;
        var eventMutationTimestamp = messagePayload.GetProperty(delete ? "deletedAt" : "updatedAt").GetDateTimeOffset();
        Assert.InRange(eventMutationTimestamp, persistedMutationTimestamp, persistedMutationTimestamp.AddTicks(9));
        if (delete)
        {
            Assert.Equal("tombstone", messagePayload.GetProperty("deletionMode").GetString());
            Assert.Equal(JsonValueKind.Null, messagePayload.GetProperty("displayText").ValueKind);
            Assert.False(messagePayload.TryGetProperty("body", out _));
        }
        else
        {
            Assert.Equal(editedBody, messagePayload.GetProperty("body").GetString());
            Assert.False(messagePayload.GetProperty("requiresRefetch").GetBoolean());
            Assert.Empty(messagePayload.GetProperty("attachmentSummaries").EnumerateArray());
        }
        using var threadDocument = JsonDocument.Parse(threadEvent.PayloadJson);
        var threadPayload = threadDocument.RootElement.GetProperty("payload");
        string[] expectedThreadProperties = ["change", "conversationId", "latestReplyAt", "replyCount", "requiresRefetch", "threadRootMessageId"];
        Assert.Equal(expectedThreadProperties, threadPayload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(scenario.Conversation.Id, threadPayload.GetProperty("conversationId").GetGuid());
        Assert.Equal(rootId, threadPayload.GetProperty("threadRootMessageId").GetGuid());
        Assert.Equal(1, threadPayload.GetProperty("replyCount").GetInt32());
        Assert.Equal(storedReply.CreatedAt, threadPayload.GetProperty("latestReplyAt").GetDateTimeOffset());
        Assert.Equal(delete ? "replyDeleted" : "replyUpdated", threadPayload.GetProperty("change").GetString());
        Assert.True(threadPayload.GetProperty("requiresRefetch").GetBoolean());
        Assert.DoesNotContain(originalBody, threadEvent.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(editedBody, threadEvent.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Actor.DisplayName, threadEvent.PayloadJson, StringComparison.Ordinal);

        var auditRows = await afterReader.AuditLogs.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        var addedAudit = Assert.Single(auditRows, row => !beforeAudit.ContainsKey(row.Id));
        var retainedAudit = (await CaptureThreadScalarRowsAsync(afterReader, afterReader.AuditLogs, row => row.Id))
            .Where(row => beforeAudit.ContainsKey(row.Key)).ToDictionary(row => row.Key, row => row.Value);
        Assert.Empty(ThreadScalarDifferences(beforeAudit, retainedAudit));
        Assert.Equal((scenario.Tenant.Id, (Guid?)scenario.Actor.Id, "Message", (Guid?)replyId, scenario.Workspace.Id),
            (addedAudit.TenantId, addedAudit.ActorUserId, addedAudit.EntityType, addedAudit.EntityId, addedAudit.WorkspaceId!.Value));
        Assert.Equal(delete ? "communication.message_deleted" : "communication.message_edited", addedAudit.Action);
        using var auditDocument = JsonDocument.Parse(addedAudit.MetadataJson!);
        var metadata = auditDocument.RootElement;
        Assert.Equal(scenario.Conversation.Id, metadata.GetProperty("conversationId").GetGuid());
        Assert.Equal(replyId, metadata.GetProperty("messageId").GetGuid());
        Assert.Equal("allow", metadata.GetProperty("decision").GetString());
        Assert.Equal(delete ? "author_delete" : "author", metadata.GetProperty("reasonCode").GetString());
    });

    private static async Task<Dictionary<Guid, Dictionary<string, object?>>> CaptureThreadScalarRowsAsync<TEntity>(
        AppDbContext reader, IQueryable<TEntity> query, Func<TEntity, Guid> identity) where TEntity : class
    {
        var rows = await query.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        return rows.ToDictionary(identity, row =>
        {
            var properties = reader.Entry(row).Properties.ToArray();
            Assert.Empty(properties.Where(property => property.Metadata.IsShadowProperty()));
            return properties.ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);
        });
    }

    private static IReadOnlyList<string> ThreadScalarDifferences(Dictionary<Guid, Dictionary<string, object?>> expected, Dictionary<Guid, Dictionary<string, object?>> actual)
    {
        var differences = new List<string>();
        foreach (var id in expected.Keys.Union(actual.Keys))
        {
            if (!expected.TryGetValue(id, out var original) || !actual.TryGetValue(id, out var current))
            {
                differences.Add($"Stored row identity changed: {id}.");
                continue;
            }
            foreach (var field in original.Keys.Union(current.Keys))
                if (!original.TryGetValue(field, out var before) || !current.TryGetValue(field, out var after) || !Equals(before, after))
                    differences.Add($"Stored row {id} changed scalar {field}.");
        }
        return differences;
    }
}
