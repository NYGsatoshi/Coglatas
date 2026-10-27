using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Files;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureFilePolicyTransportTests
{
    private const string TenantSlug = SecurityCiFixtureSeed.TenantASlug;
    private const string FileEvent = "Files.FileChanged.v1";
    private const string TargetName = "sec17-file-policy-target.txt";
    private static readonly byte[] Bytes = "SEC-17 synthetic File policy bytes\n"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC17FilePolicy")]
    public Task ActualWorkspaceFileSharingPolicyReauthorizesEstablishedAndFreshTransportAndReplay() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await AssertMetadataAloneCannotProveDeliveryAsync();
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", TenantSlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", TenantSlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            Guid workspace;
            Guid[] sessions;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                workspace = await db.Workspaces.AsNoTracking().Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug)
                    .Select(row => row.Id).SingleAsync();
                sessions = await db.Sessions.AsNoTracking().Where(row => row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
                Assert.True(sessions.Length > 0, "The synthetic member must have an actual authenticated session.");
            }
            await using var owner = await app.ConnectAsync(ownerClient, "owner", TenantSlug);
            await using var established = await app.ConnectAsync(memberClient, "member", TenantSlug);
            await AssertWorkspaceAuthorityAsync(workspace, owner, established);

            var target = await UploadAsync(ownerClient, database, workspace, TargetName);
            await DeliverAsync(app, target.UploadEvent, target.Tenant, target.File, owner);
            await AssertOrdinaryTargetMemberAsync(database, target, sessions);
            var initial = await ChangePolicyAsync(ownerClient, database, target, share: true, version: target.Version);
            await DeliverAsync(app, initial.Event, target.Tenant, target.File, owner, established);
            await AssertReadableAsync(memberClient, target);
            await AssertOwnerBytesAsync(ownerClient, target);

            // The actor, session, Workspace and participant grants stay unchanged.
            // Only the current File sharing policy narrows its readable audience.
            var denied = await ChangePolicyAsync(ownerClient, database, target, share: false, version: initial.Version);
            await DeliverAsync(app, denied.Event, target.Tenant, target.File, owner);
            await AssertOrdinaryTargetMemberAsync(database, target, sessions);
            await AssertDeniedHttpAsync(memberClient, target);
            await AssertOwnerBytesAsync(ownerClient, target);
            await AssertCurrentPolicyAsync(database, target, FileSharingPolicy.Private, denied.Version);
            await using var fresh = await app.ConnectAsync(memberClient, "member", TenantSlug);
            await AssertWorkspaceAuthorityAsync(workspace, owner, established, fresh);
            await DeliverUnrelatedPositiveAsync(app, memberClient, database, workspace, owner, established, fresh);
            await ObserveExclusionAsync(denied.Event, established, fresh);

            // Requeue the exact HTTP-produced event only inside this disposable
            // fixture. This is repository/dispatcher replay, not operator authority.
            var replayedAfterUtc = DateTimeOffset.UtcNow;
            var ownerCount = owner.DeliveryCount(denied.Event);
            await ReplayImmutableAsync(database, denied.Event, replayedAfterUtc);
            await owner.WaitEventAsync(denied.Event, minimumCount: ownerCount + 1);
            AssertFileReceipt(owner, denied.Event, target.Tenant, target.File);
            await app.WaitDeliveredAsync(denied.Event, replayedAfterUtc);
            await AssertOrdinaryTargetMemberAsync(database, target, sessions);
            await AssertDeniedHttpAsync(memberClient, target);
            await AssertCurrentPolicyAsync(database, target, FileSharingPolicy.Private, denied.Version);
            await AssertWorkspaceAuthorityAsync(workspace, owner, established, fresh);
            await DeliverUnrelatedPositiveAsync(app, memberClient, database, workspace, owner, established, fresh);
            await ObserveExclusionAsync(denied.Event, established, fresh);

            var restored = await ChangePolicyAsync(ownerClient, database, target, share: true, version: denied.Version);
            await DeliverAsync(app, restored.Event, target.Tenant, target.File, owner, established, fresh);
            await AssertReadableAsync(memberClient, target);
            await AssertOwnerBytesAsync(ownerClient, target);
            await AssertOrdinaryTargetMemberAsync(database, target, sessions);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.True(await db.AuditLogs.CountAsync(row => row.EntityId == target.File && row.Action == "FileSharingChanged") == 3,
                    "Only the three actual target sharing changes may have sharing-success audits.");
                Assert.True(await db.FileObjects.AsNoTracking().AnyAsync(row => row.Id == target.File &&
                    row.SharingPolicy == FileSharingPolicy.Workspace && row.SharingVersion == restored.Version),
                    "Restoration must be the persisted current File policy and version.");
            }
        });

    private static async Task<Target> UploadAsync(HttpClient actor, string database, Guid workspace, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "ownerType");
        form.Add(new StringContent(workspace.ToString("D")), "ownerId");
        using var bytes = new ByteArrayContent(Bytes);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(bytes, "file", name);
        using var response = await actor.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attachment = await response.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(attachment);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var file = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == attachment.FileObjectId);
        const string uploadInvariant = "The real upload must create the requested direct Workspace File.";
        Assert.True(attachment.OwnerType == AttachmentOwnerType.Workspace, uploadInvariant);
        Assert.True(attachment.OwnerId == workspace, uploadInvariant);
        Assert.True(file.WorkspaceId == workspace, uploadInvariant);
        Assert.True(file.ProjectId is null, uploadInvariant);
        Assert.True(file.Status == FileObjectStatus.Active, uploadInvariant);
        Assert.True(file.Classification == DataClassification.Private, uploadInvariant);
        var eventId = await NativeEventAsync(database, file.Id, workspace, file.TenantId, "uploaded", file.UploadedByUserId, []);
        return new(file.Id, attachment.Id, workspace, file.TenantId, file.SharingVersion, file.StorageKey, name, eventId);
    }

    private static async Task<(Guid Event, long Version)> ChangePolicyAsync(
        HttpClient owner, string database, Target target, bool share, long version)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var before = await db.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == target.File && row.EventType == FileEvent)
            .Select(row => row.Id).ToArrayAsync();
        using var response = await owner.PutAsJsonAsync($"/api/files/{target.File}/sharing",
            new FileSharingPolicyUpdateRequest(share, version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sharing = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
        Assert.NotNull(sharing);
        Assert.True(sharing.FileObjectId == target.File && sharing.SharingVersion == version + 1 &&
            sharing.SharingPolicy == (share ? "Workspace" : "Private") && sharing.CanManageSharing,
            "The actual sharing response must reflect the exact target and next policy version.");
        var current = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == target.File);
        Assert.True(current.SharingVersion == sharing.SharingVersion &&
            current.SharingPolicy == (share ? FileSharingPolicy.Workspace : FileSharingPolicy.Private),
            "HTTP success must correspond to the persisted current File policy.");
        var id = await NativeEventAsync(database, target.File, target.Workspace, target.Tenant, "sharingChanged", SecurityCiFixtureSeed.TenantAOwnerUserId, before);
        return (id, sharing.SharingVersion);
    }

    private static async Task<Guid> NativeEventAsync(
        string database, Guid file, Guid workspace, Guid tenant, string change, Guid actor, Guid[] before)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var rows = await db.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == file &&
            row.EventType == FileEvent && !before.Contains(row.Id)).ToListAsync();
        Assert.True(rows.Count == 1, "The actual HTTP command must produce one new logical File invalidation event.");
        var row = rows.Single();
        var envelope = JsonSerializer.Deserialize<DurableEventEnvelope>(row.PayloadJson, JsonOptions);
        var routes = JsonSerializer.Deserialize<RealtimeRoutingTarget[]>(row.RoutingJson, JsonOptions);
        Assert.NotNull(envelope);
        const string eventInvariant = "The durable event and route must match the actual HTTP File and current Workspace.";
        Assert.True(envelope.EventId == row.Id, eventInvariant);
        Assert.True(envelope.EventType == FileEvent, eventInvariant);
        Assert.True(envelope.PayloadSchemaVersion == 1, eventInvariant);
        Assert.True(row.PayloadSchemaVersion == 1, eventInvariant);
        Assert.True(envelope.TenantId == tenant, eventInvariant);
        Assert.True(row.TenantId == tenant, eventInvariant);
        Assert.True(envelope.AggregateType == "File", eventInvariant);
        Assert.True(row.AggregateType == "File", eventInvariant);
        Assert.True(envelope.AggregateId == file, eventInvariant);
        Assert.True(envelope.AggregateVersion is > 0, eventInvariant);
        Assert.True(envelope.AggregateVersion == row.AggregateVersion, eventInvariant);
        Assert.True(envelope.Actor.ActorType == "User", eventInvariant);
        Assert.True(envelope.Actor.ActorId == actor, eventInvariant);
        Assert.True(envelope.Payload.GetProperty("fileVersion").GetInt64() == envelope.AggregateVersion, eventInvariant);
        Assert.True(envelope.Payload.GetProperty("fileId").GetGuid() == file, eventInvariant);
        Assert.True(envelope.Payload.GetProperty("workspaceId").GetGuid() == workspace, eventInvariant);
        Assert.True(envelope.Payload.GetProperty("change").GetString() == change, eventInvariant);
        Assert.True(envelope.Payload.GetProperty("requiresRefetch").GetBoolean(), eventInvariant);
        Assert.True(routes is { Length: 1 }, eventInvariant);
        var onlyRoute = routes![0];
        Assert.True(onlyRoute.SubscriptionType == RealtimeSubscriptionType.Workspace, eventInvariant);
        Assert.True(onlyRoute.ResourceId == workspace, eventInvariant);
        return row.Id;
    }

    private static async Task DeliverAsync(SecurityArchitectureSignalRFixture app, Guid id, Guid tenant, Guid file, params RealtimeSocket[] sockets)
    {
        Assert.NotEmpty(sockets);
        foreach (var socket in sockets)
        {
            await socket.WaitEventAsync(id);
            AssertFileReceipt(socket, id, tenant, file);
        }
        await app.WaitDeliveredAsync(id);
    }

    private static void AssertFileReceipt(RealtimeSocket socket, Guid id, Guid tenant, Guid file)
    {
        Assert.True(socket.ReceivedAggregateIdentity(id, FileEvent, tenant, "File", file),
            "The actual live File event must have the expected type, schema, Tenant and aggregate identity.");
        Assert.False(socket.ReceivedAggregateIdentity(id, FileEvent, Guid.Empty, "File", file),
            "A received event cannot qualify for another Tenant.");
        Assert.False(socket.ReceivedAggregateIdentity(id, FileEvent, tenant, "Other", file),
            "A received event cannot qualify for another aggregate type.");
        Assert.False(socket.ReceivedAggregateIdentity(id, FileEvent, tenant, "File", Guid.Empty),
            "A received event cannot qualify for another File.");
        Assert.False(socket.ReceivedAggregateIdentity(id, "Messaging.MessageCreated.v1", tenant, "File", file),
            "A received event cannot qualify for another event type.");
    }

    private static async Task AssertMetadataAloneCannotProveDeliveryAsync()
    {
        await using var recorder = new RealtimeSocket();
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var file = Guid.NewGuid();
        recorder.RecordIdentity(id, tenant, "File", file, null);
        Assert.False(recorder.ReceivedAggregateIdentity(id, FileEvent, tenant, "File", file),
            "A typed metadata entry without actual received type/schema/count cannot qualify as live delivery.");
    }

    private static async Task DeliverUnrelatedPositiveAsync(
        SecurityArchitectureSignalRFixture app, HttpClient member, string database, Guid workspace,
        params RealtimeSocket[] sockets)
    {
        // This separate File is legitimately readable by its uploader. It never
        // gives the member ownership or a grant on the owner's target File.
        var control = await UploadAsync(member, database, workspace, "sec17-file-policy-control-" + Guid.NewGuid().ToString("N") + ".txt");
        await DeliverAsync(app, control.UploadEvent, control.Tenant, control.File, sockets);
        await AssertReadableAsync(member, control);
    }

    private static async Task AssertWorkspaceAuthorityAsync(Guid workspace, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            Assert.True(await socket.SubscribeAsync("SubscribeUser"), "Each authenticated connection must remain live.");
            Assert.True(await socket.SubscribeAsync("SubscribeWorkspace", workspace),
                "The File sharing change must not revoke the member's unchanged Workspace authority.");
        }
    }

    private static async Task ObserveExclusionAsync(Guid id, params RealtimeSocket[] sockets)
    {
        var observations = new List<Task>();
        foreach (var socket in sockets) observations.Add(socket.AssertNotReceivedAsync(id));
        await Task.WhenAll(observations);
    }

    private static async Task AssertReadableAsync(HttpClient actor, Target target)
    {
        using var file = await actor.GetAsync($"/api/files/{target.File}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        using var fileJson = JsonDocument.Parse(await file.Content.ReadAsStringAsync());
        Assert.True(fileJson.RootElement.GetProperty("id").GetGuid() == target.File, "Current File HTTP reads must resolve the exact target.");
        using var attachment = await actor.GetAsync($"/api/attachments/{target.Attachment}");
        Assert.Equal(HttpStatusCode.OK, attachment.StatusCode);
        var detail = await attachment.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(detail);
        Assert.True(detail.Id == target.Attachment && detail.FileObjectId == target.File &&
            detail.OwnerType == AttachmentOwnerType.Workspace && detail.OwnerId == target.Workspace,
            "Current Attachment HTTP reads must resolve the same File and Workspace.");
    }

    private static async Task AssertOwnerBytesAsync(HttpClient owner, Target target)
    {
        await AssertReadableAsync(owner, target);
        using var response = await owner.GetAsync($"/api/files/{target.File}/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(Bytes),
            "The authorized owner must retain the actual physical File bytes.");
    }

    private static async Task AssertDeniedHttpAsync(HttpClient member, Target target)
    {
        using var identity = await member.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        foreach (var path in new[] { $"/api/files/{target.File}", $"/api/attachments/{target.Attachment}" })
        {
            using var response = await member.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Length <= 8192, "Denied HTTP evidence must remain bounded.");
            using var json = JsonDocument.Parse(body);
            if (path.StartsWith("/api/files/", StringComparison.Ordinal))
                Assert.True(json.RootElement.GetProperty("error").GetProperty("code").GetString() == "FileMetadataFailed" &&
                    json.RootElement.GetProperty("error").GetProperty("message").GetString() == "The request could not be completed." &&
                    json.RootElement.GetProperty("error").GetProperty("redactionApplied").GetBoolean() &&
                    json.RootElement.GetProperty("error").GetProperty("target").ValueKind == JsonValueKind.Null &&
                    json.RootElement.GetProperty("error").GetProperty("details").GetArrayLength() == 0,
                    "Denied File metadata must retain the current generic canonical error.");
            else
                Assert.True(json.RootElement.GetProperty("error").GetString() == "Attachment not found.",
                    "Denied Attachment reads must retain the current generic error.");
            foreach (var marker in new[] { target.Name, target.StorageKey, target.File.ToString("D"),
                target.Attachment.ToString("D"), Encoding.UTF8.GetString(Bytes).TrimEnd(), "Npgsql", "stackTrace", "routingJson" })
                Assert.True(!body.Contains(marker, StringComparison.OrdinalIgnoreCase), "Denied HTTP must not expose protected File or routing data.");
        }
    }

    private static async Task AssertCurrentPolicyAsync(string database, Target target, FileSharingPolicy policy, long version)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        Assert.True(await db.FileObjects.AsNoTracking().AnyAsync(row => row.Id == target.File &&
            row.SharingPolicy == policy && row.SharingVersion == version),
            "The asserted delivery phase must use the exact committed current File policy and sharing version.");
    }

    private static async Task AssertOrdinaryTargetMemberAsync(string database, Target target, Guid[] sessionIds)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var actor = SecurityCiFixtureSeed.TenantAMemberUserId;
        var membership = await db.WorkspaceMembers.AsNoTracking().SingleAsync(row => row.WorkspaceId == target.Workspace && row.UserId == actor);
        var user = await db.Users.AsNoTracking().SingleAsync(row => row.Id == actor);
        var tenant = await db.TenantUsers.AsNoTracking().SingleAsync(row => row.TenantId == target.Tenant && row.UserId == actor);
        var file = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == target.File);
        var attachment = await db.Attachments.AsNoTracking().SingleAsync(row => row.Id == target.Attachment);
        var sessions = await db.Sessions.AsNoTracking().Where(row => row.UserId == actor).OrderBy(row => row.Id).ToListAsync();
        const string actorInvariant = "The unchanged actor must remain an ordinary active Tenant/Workspace member.";
        Assert.True(membership.Status == MembershipStatus.Active, actorInvariant);
        Assert.True(membership.Role == WorkspaceRole.Member, actorInvariant);
        Assert.True(user.Status == UserStatus.Active, actorInvariant);
        Assert.True(user.SystemRole == SystemRole.User, actorInvariant);
        Assert.True(user.DeletedAt is null, actorInvariant);
        Assert.True(tenant.Status == TenantUserStatus.Active, actorInvariant);
        Assert.True(tenant.Role == TenantUserRole.Member, actorInvariant);
        Assert.True(sessions.Select(row => row.Id).SequenceEqual(sessionIds) &&
            sessions.All(row => row.RevokedAt is null && row.ExpiresAt > DateTimeOffset.UtcNow),
            "File policy changes must preserve the same live authenticated sessions.");
        Assert.True(file.UploadedByUserId == SecurityCiFixtureSeed.TenantAOwnerUserId && file.UploadedByUserId != actor &&
            attachment.UploadedByUserId != actor && attachment.OwnerUserId != actor &&
            attachment.OwnerType == AttachmentOwnerType.Workspace && attachment.OwnerId == target.Workspace &&
            attachment.WorkspaceId == target.Workspace && file.WorkspaceId == target.Workspace && file.ProjectId is null &&
            !await db.FileAccessGrants.AsNoTracking().AnyAsync(row => row.FileObjectId == target.File && row.RecipientUserId == actor),
            "The ordinary member must have no target uploader, owner, manager, Project or explicit-sharing-grant escape.");
    }

    private static async Task ReplayImmutableAsync(string database, Guid id, DateTimeOffset started)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var before = await db.OutboxEvents.AsNoTracking().Where(row => row.Id == id).Select(row => new
            { row.TenantId, row.EventType, row.PayloadSchemaVersion, row.AggregateType, row.AggregateId,
              row.AggregateVersion, row.OccurredAt, row.PayloadJson, row.RoutingJson }).SingleAsync();
        Assert.True(await new OutboxEventRepository(db).ReplayAsync(id, started), "Only the existing fixture replay path may requeue the delivered event.");
        var after = await db.OutboxEvents.AsNoTracking().Where(row => row.Id == id).Select(row => new
            { row.TenantId, row.EventType, row.PayloadSchemaVersion, row.AggregateType, row.AggregateId,
              row.AggregateVersion, row.OccurredAt, row.PayloadJson, row.RoutingJson }).SingleAsync();
        Assert.True(before.Equals(after), "Repository replay must preserve the actual immutable File event and routing context.");
    }

    private sealed record Target(Guid File, Guid Attachment, Guid Workspace, Guid Tenant, long Version, string StorageKey, string Name, Guid UploadEvent);
}
