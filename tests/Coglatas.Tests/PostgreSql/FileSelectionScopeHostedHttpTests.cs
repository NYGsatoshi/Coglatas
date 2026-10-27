using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Files;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class FileDownloadGrantLifecycleHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task ServerOwnedBatchSelectionRejectsActorAndTenantTransferAndPreservesOriginalOneUse() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                using var beta = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug,
                    SecurityCiFixtureSeed.TenantBOwnerEmail);

                // This existing helper creates a fresh owner Workspace and a real
                // owner-uploaded File through HTTP. No client supplies File IDs to capture.
                var ownerFile = await UploadOtherWorkspaceAsync(owner, database);
                Guid workspace;
                Guid tenant;
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var attachment = await db.Attachments.AsNoTracking().SingleAsync(row => row.Id == ownerFile.AttachmentId);
                    workspace = attachment.WorkspaceId;
                    tenant = attachment.TenantId;
                    Assert.True(attachment.UploadedByUserId == SecurityCiFixtureSeed.TenantAOwnerUserId &&
                        attachment.OwnerUserId == SecurityCiFixtureSeed.TenantAOwnerUserId);
                }
                using (var admitted = await owner.PostAsJsonAsync($"/api/workspaces/{workspace:D}/members",
                           new AddWorkspaceMemberRequest(SecurityCiFixtureSeed.TenantAMemberUserId, WorkspaceRole.Member)))
                    Assert.True(admitted.StatusCode == HttpStatusCode.OK,
                        "The existing Workspace membership command must admit the current synthetic Member.");
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                    Assert.True(await db.WorkspaceMembers.AsNoTracking().AnyAsync(row =>
                        row.TenantId == tenant && row.WorkspaceId == workspace &&
                        row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                        row.Role == WorkspaceRole.Member && row.Status == MembershipStatus.Active));

                var first = await UploadSelectionTargetAsync(member, database, workspace);
                var second = await UploadSelectionTargetAsync(member, database, workspace);
                Target[] selected = [ownerFile, first, second];
                var otherWorkspace = await UploadOtherWorkspaceAsync(owner, database);
                var betaWorkspace = await CreateSelectionWorkspaceAsync(beta);
                var betaBytes = "SEC16_BETA_SELECTION_CONTROL_DO_NOT_LEAK\n"u8.ToArray();
                var otherTenant = await UploadSelectionTargetAsync(beta, database, betaWorkspace, betaBytes);
                Guid otherWorkspaceId;
                Guid betaTenant;
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var alpha = await db.Attachments.AsNoTracking().SingleAsync(row => row.Id == otherWorkspace.AttachmentId);
                    var foreign = await db.Attachments.AsNoTracking().SingleAsync(row => row.Id == otherTenant.AttachmentId);
                    otherWorkspaceId = alpha.WorkspaceId;
                    betaTenant = foreign.TenantId;
                    var uploads = await db.Attachments.AsNoTracking().Where(row =>
                        row.Id == first.AttachmentId || row.Id == second.AttachmentId).ToListAsync();
                    Assert.True(uploads.Count == 2 && uploads.All(row => row.TenantId == tenant &&
                        row.WorkspaceId == workspace && row.OwnerType == AttachmentOwnerType.Workspace &&
                        row.OwnerId == workspace && row.UploadedByUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                        row.OwnerUserId == SecurityCiFixtureSeed.TenantAMemberUserId));
                    var arrangedIds = selected.Concat([otherWorkspace, otherTenant]).Select(target => target.FileId).ToArray();
                    var dateMatches = await db.FileObjects.AsNoTracking().Where(row => arrangedIds.Contains(row.Id))
                        .Select(row => row.UpdatedAt ?? row.CreatedAt).ToListAsync();
                    Assert.True(arrangedIds.Distinct().Count() == 5 && dateMatches.Count == 5 &&
                        dateMatches.All(value => value >= DateTimeOffset.UnixEpoch),
                        "All five arrangements must satisfy the same date facet before scope exclusion is credited.");
                    Assert.True(alpha.TenantId == tenant && alpha.WorkspaceId != workspace &&
                        foreign.TenantId != tenant && foreign.WorkspaceId == betaWorkspace && foreign.WorkspaceId != workspace &&
                        alpha.OwnerType == AttachmentOwnerType.Workspace && alpha.OwnerId == alpha.WorkspaceId &&
                        foreign.OwnerType == AttachmentOwnerType.Workspace && foreign.OwnerId == foreign.WorkspaceId,
                        "The exclusion controls must be real distinct Workspace and Tenant resources.");
                    Assert.True(!await db.WorkspaceMembers.AsNoTracking().AnyAsync(row =>
                        row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId && row.Status == MembershipStatus.Active &&
                        (row.WorkspaceId == otherWorkspaceId || row.WorkspaceId == betaWorkspace)),
                        "The capture actor must have no active membership in either excluded scope.");
                }
                var firstGrant = await IssueAsync(member, $"/api/files/{first.FileId:D}/download-grants", first);
                var secondGrant = await IssueAsync(member, $"/api/files/{second.FileId:D}/download-grants", second);
                var ownerGrant = await IssueAsync(owner, $"/api/files/{ownerFile.FileId:D}/download-grants", ownerFile);
                var alphaGrant = await IssueAsync(owner, $"/api/files/{otherWorkspace.FileId:D}/download-grants", otherWorkspace);
                var betaGrant = await IssueAsync(beta, $"/api/files/{otherTenant.FileId:D}/download-grants", otherTenant);
                await AssertBytesAsync(member, DownloadRoute(true, firstGrant), firstGrant.Token);
                await AssertBytesAsync(member, DownloadRoute(true, secondGrant), secondGrant.Token);
                await AssertBytesAsync(owner, DownloadRoute(true, ownerGrant), ownerGrant.Token);
                await AssertBytesAsync(owner, DownloadRoute(true, alphaGrant), alphaGrant.Token);
                await AssertScopeBytesAsync(beta, DownloadRoute(true, betaGrant), betaGrant.Token,
                    betaBytes);
                await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                await AssertScopeActorAsync(beta, SecurityCiFixtureSeed.TenantBOwnerUserId);

                var forbidden = selected.Concat([otherWorkspace, otherTenant])
                    .SelectMany(target => new[] { target.FileName, target.StorageKey,
                        target.FileId.ToString("D"), target.AttachmentId.ToString("D") })
                    .Concat([firstGrant.Token, secondGrant.Token, ownerGrant.Token, alphaGrant.Token, betaGrant.Token,
                        Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), "SEC16_BETA_SELECTION_CONTROL_DO_NOT_LEAK",
                        workspace.ToString("D"), otherWorkspaceId.ToString("D"), betaWorkspace.ToString("D"),
                        tenant.ToString("D"), betaTenant.ToString("D"),
                        SecurityCiFixtureSeed.TenantAOwnerUserId.ToString("D"),
                        SecurityCiFixtureSeed.TenantAMemberUserId.ToString("D"),
                        SecurityCiFixtureSeed.TenantBOwnerUserId.ToString("D"),
                        "SEC16 owner-only Workspace", "SEC16 selection scope", "storageKey", "storage_key",
                        "Npgsql", "stackTrace", "connection string"]).ToArray();
                var allIds = selected.Concat([otherWorkspace, otherTenant]).Select(target => target.FileId).ToArray();
                var noCapture = await SelectionInventoryStateAsync(database);
                var beforeCaptureDenial = await SelectionFileStateAsync(database, allIds);
                foreach (var excludedWorkspace in new[] { otherWorkspaceId, betaWorkspace })
                {
                    using var denied = await member.PostAsync(SelectionCaptureRoute(excludedWorkspace), null);
                    await AssertSelectionDenialAsync(denied, forbidden, "FileSelectionSnapshotFailed");
                    await AssertSelectionStateAsync(database, allIds, beforeCaptureDenial, noCapture);
                }

                Guid snapshotId;
                using (var response = await member.PostAsync(SelectionCaptureRoute(workspace), null))
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK);
                    var captured = await response.Content.ReadFromJsonAsync<FileSelectionSnapshotCaptureResponse>();
                    Assert.NotNull(captured);
                    Assert.True(string.Equals(captured.Outcome, "Captured", StringComparison.Ordinal) &&
                        captured.SelectedCount == selected.Length && captured.SelectionSnapshotId.HasValue &&
                        captured.ExpiresAt > DateTimeOffset.UtcNow && captured.MaximumSelectionCount == 100,
                        "The server must capture the fresh Workspace's exact three File identities.");
                    snapshotId = captured.SelectionSnapshotId.Value;
                    Assert.True(snapshotId != Guid.Empty);
                }
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var snapshot = await db.FileSelectionSnapshots.AsNoTracking().Include(row => row.Items)
                        .SingleAsync(row => row.Id == snapshotId);
                    Assert.False(snapshot.OnlyMyUploads);
                    Assert.Null(snapshot.ConsumedAt);
                    Assert.Equal(0, snapshot.ConsumptionVersion);
                    Assert.True(snapshot.TenantId == tenant &&
                        snapshot.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                        snapshot.WorkspaceId == workspace && snapshot.NormalizedQuery == string.Empty &&
                        string.Equals(snapshot.FileKind, "All", StringComparison.Ordinal) && snapshot.FromDateUtc == DateTimeOffset.UnixEpoch &&
                        snapshot.Items.Select(row => row.FileObjectId).Order()
                            .SequenceEqual(selected.Select(target => target.FileId).Order()) &&
                        !snapshot.Items.Any(row => row.FileObjectId == otherWorkspace.FileId || row.FileObjectId == otherTenant.FileId),
                        "A server-owned selection must exclude Files from another Workspace and Tenant.");
                }
                forbidden = [.. forbidden, snapshotId.ToString("D")];
                var beforeTransfer = await SelectionFileStateAsync(database, allIds);
                var originalSnapshot = await SelectionInventoryStateAsync(database);
                var csrf = owner.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName).Single();
                owner.DefaultRequestHeaders.Remove(SecurityOptions.CsrfHeaderName);
                try
                {
                    using var denied = await owner.PostAsync($"/api/files/selection-snapshots/{snapshotId:D}/delete", null);
                    Assert.True(denied.StatusCode == HttpStatusCode.Forbidden,
                        "The separate missing-CSRF control must fail before actor-bound execution.");
                    await AssertSafeEnvelopeAsync(denied, forbidden);
                }
                finally { owner.DefaultRequestHeaders.Add(SecurityOptions.CsrfHeaderName, csrf); }
                await AssertSelectionStateAsync(database, allIds, beforeTransfer, originalSnapshot);
                foreach (var actor in new[] { owner, beta })
                {
                    using var denied = await actor.PostAsync($"/api/files/selection-snapshots/{snapshotId:D}/delete", null);
                    await AssertSelectionDenialAsync(denied, forbidden, "FileSelectionSnapshotDeleteFailed");
                    await AssertSelectionStateAsync(database, allIds, beforeTransfer, originalSnapshot);
                }
                await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertScopeActorAsync(beta, SecurityCiFixtureSeed.TenantBOwnerUserId);

                // A batch is best-effort. This Member owns two captured uploads,
                // while the third captured upload still belongs to the Owner.
                var ownerBefore = await SelectionFileStateAsync(database, [ownerFile.FileId]);
                var excludedBefore = await SelectionFileStateAsync(database, [otherWorkspace.FileId, otherTenant.FileId]);
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                    Assert.True(!await db.AuditLogs.AsNoTracking().AnyAsync(row => row.Action == "FileDeleted" &&
                        (row.EntityId == first.FileId || row.EntityId == second.FileId || row.EntityId == ownerFile.FileId)));
                using (var response = await member.PostAsync($"/api/files/selection-snapshots/{snapshotId:D}/delete", null))
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK);
                    var deleted = await response.Content.ReadFromJsonAsync<FileSelectionSnapshotDeleteResponse>();
                    Assert.NotNull(deleted);
                    Assert.Equal(snapshotId, deleted.SelectionSnapshotId);
                    Assert.Equal(3, deleted.AttemptedCount);
                    Assert.Equal(2, deleted.SucceededCount);
                    Assert.Equal(1, deleted.FailedCount);
                    Assert.Equal(3, deleted.Items.Count);
                    Assert.True(
                        deleted.Items.Select(row => row.FileObjectId).Order()
                            .SequenceEqual(selected.Select(target => target.FileId).Order()) &&
                        deleted.Items.Count(row => row.FileObjectId == ownerFile.FileId && !row.Succeeded &&
                            string.Equals(row.Outcome, "NotDeleted", StringComparison.Ordinal)) == 1 &&
                        deleted.Items.Count(row => (row.FileObjectId == first.FileId || row.FileObjectId == second.FileId) &&
                            row.Succeeded && string.Equals(row.Outcome, "Deleted", StringComparison.Ordinal)) == 2,
                        "The original actor must retain exactly one current-authority, non-atomic execution.");
                }
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var snapshot = await db.FileSelectionSnapshots.AsNoTracking().SingleAsync(row => row.Id == snapshotId);
                    Assert.True(snapshot.ConsumedAt.HasValue);
                    Assert.Equal(1, snapshot.ConsumptionVersion);
                    var removed = await db.FileObjects.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => row.Id == first.FileId || row.Id == second.FileId).ToListAsync();
                    Assert.Equal(2, removed.Count);
                    foreach (var removedFile in removed)
                    {
                        Assert.Equal(FileObjectStatus.Deleted, removedFile.Status);
                        Assert.True(removedFile.DeletedAt.HasValue);
                    }
                    var references = await db.Attachments.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => row.Id == first.AttachmentId || row.Id == second.AttachmentId).ToListAsync();
                    Assert.True(references.Count == 2 && references.All(row => row.DeletedAt.HasValue));
                    var audits = await db.AuditLogs.AsNoTracking().Where(row => row.Action == "FileDeleted" &&
                        (row.EntityId == first.FileId || row.EntityId == second.FileId || row.EntityId == ownerFile.FileId)).ToListAsync();
                    Assert.True(audits.Count == 2 && audits.All(row => row.TenantId == tenant &&
                        row.WorkspaceId == workspace && row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                        row.EntityType == "FileObject" && (row.EntityId == first.FileId || row.EntityId == second.FileId)) &&
                        audits.Select(row => row.EntityId).Distinct().Count() == 2,
                        "The two successful current-actor deletions must append their exact business audit identities.");
                }
                var ownerAfter = await SelectionFileStateAsync(database, [ownerFile.FileId]);
                Assert.True(ownerBefore.AsSpan().SequenceEqual(ownerAfter),
                    "The denied item must preserve its complete selected File state.");
                var excludedAfter = await SelectionFileStateAsync(database, [otherWorkspace.FileId, otherTenant.FileId]);
                Assert.True(excludedBefore.AsSpan().SequenceEqual(excludedAfter),
                    "Excluded resources must never become batch items.");
                var afterExecution = await SelectionFileStateAsync(database, allIds);
                var consumedSnapshot = await SelectionInventoryStateAsync(database);
                using (var replay = await member.PostAsync($"/api/files/selection-snapshots/{snapshotId:D}/delete", null))
                    await AssertSelectionDenialAsync(replay, forbidden, "FileSelectionSnapshotDeleteFailed");
                await AssertSelectionStateAsync(database, allIds, afterExecution, consumedSnapshot);
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                await AssertBytesAsync(owner, DownloadRoute(true, ownerGrant), ownerGrant.Token);
                await AssertBytesAsync(owner, DownloadRoute(true, alphaGrant), alphaGrant.Token);
                await AssertScopeBytesAsync(beta, DownloadRoute(true, betaGrant), betaGrant.Token,
                    betaBytes);
            });

    private static string SelectionCaptureRoute(Guid workspace) =>
        $"/api/files/selection-snapshots?workspaceId={workspace:D}&fromDate=1970-01-01T00:00:00Z";

    private static async Task<Guid> CreateSelectionWorkspaceAsync(HttpClient actor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new CreateWorkspaceRequest("SEC16 selection scope", null, null));
        using var response = await actor.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Created);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return envelope.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<Target> UploadSelectionTargetAsync(HttpClient member, string database, Guid workspace,
        byte[]? bytes = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "ownerType");
        form.Add(new StringContent(workspace.ToString("D")), "ownerId");
        using var content = new ByteArrayContent(bytes ?? SyntheticBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", "sec16-selection-private.txt");
        using var response = await member.PostAsync("/api/attachments", form);
        Assert.True(response.StatusCode == HttpStatusCode.OK);
        var attachment = await response.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(attachment);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.FileObjects.AsNoTracking().Where(row => row.Id == attachment.FileObjectId)
            .Select(row => new Target(row.Id, attachment.Id, row.OriginalFileName, row.StorageKey)).SingleAsync();
    }

    private static async Task AssertSelectionDenialAsync(HttpResponseMessage response, IEnumerable<string> forbidden, string code)
    {
        await AssertSafeDenialAsync(response, forbidden);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("code").ValueKind == JsonValueKind.String &&
            string.Equals(envelope.RootElement.GetProperty("error").GetProperty("code").GetString(), code, StringComparison.Ordinal),
            "A denied selection must retain the exact canonical, metadata-minimized route failure.");
    }

    private static async Task AssertSelectionStateAsync(string database, Guid[] files, byte[] fileState, byte[] selections)
    {
        var currentFiles = await SelectionFileStateAsync(database, files);
        var currentSelections = await SelectionInventoryStateAsync(database);
        Assert.True(fileState.AsSpan().SequenceEqual(currentFiles),
            "Denied selection execution must preserve selected File, Attachment, sharing, grant and success-audit state.");
        Assert.True(selections.AsSpan().SequenceEqual(currentSelections),
            "A denied capture/transfer/replay must not create, consume or change a persisted selection.");
    }

    private static async Task<byte[]> SelectionInventoryStateAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var state = new
        {
            Snapshots = await db.FileSelectionSnapshots.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.TenantId, row.ActorUserId, row.WorkspaceId, row.NormalizedQuery, row.FileKind,
                row.FromDateUtc, row.OnlyMyUploads, row.ExpiresAt, row.ConsumedAt, row.ConsumptionVersion,
                row.CreatedAt, row.UpdatedAt
            }).ToListAsync(),
            Items = await db.FileSelectionSnapshotItems.AsNoTracking().OrderBy(row => row.SelectionSnapshotId)
                .ThenBy(row => row.FileObjectId).Select(row => new { row.SelectionSnapshotId, row.FileObjectId }).ToListAsync()
        };
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state));
    }

    private static async Task<byte[]> SelectionFileStateAsync(string database, Guid[] ids)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var matchingGrantIds = await db.FileDownloadGrants.AsNoTracking().Where(row => ids.Contains(row.FileObjectId))
            .Select(row => row.Id).ToArrayAsync();
        var state = new
        {
            Files = await db.FileObjects.IgnoreQueryFilters().AsNoTracking().Where(row => ids.Contains(row.Id))
                .OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, row.TenantId, row.WorkspaceId, row.GroupId, row.ProjectId, row.UploadedByUserId,
                    row.OriginalFileName, row.StorageKey, row.ContentType, row.SizeBytes, row.HashSha256,
                    row.Classification, row.SharingPolicy, row.SharingVersion, row.Status, row.DeletedAt,
                    row.DeletedByUserId, row.DeleteReason, row.CreatedAt, row.UpdatedAt
                }).ToListAsync(),
            Attachments = await db.Attachments.IgnoreQueryFilters().AsNoTracking().Where(row => ids.Contains(row.FileObjectId))
                .OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, row.TenantId, row.FileObjectId, row.WorkspaceId, row.OwnerType, row.OwnerId,
                    row.OwnerUserId, row.UploadedByUserId, row.FileName, row.StoredFileName, row.FilePath,
                    row.ContentType, row.Extension, row.SizeBytes, row.StorageProvider, row.StorageKey, row.ScanStatus,
                    row.DeletedAt, row.DeletedByUserId, row.DeleteReason, row.CreatedAt, row.UpdatedAt
                }).ToListAsync(),
            Shares = await db.FileAccessGrants.AsNoTracking().Where(row => ids.Contains(row.FileObjectId))
                .OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, row.TenantId, row.WorkspaceId, row.FileObjectId, row.RecipientUserId, row.RecipientKind,
                    row.GrantedByUserId, row.RevokedAt, row.RevokedByUserId, row.CreatedAt, row.UpdatedAt
                }).ToListAsync(),
            Grants = await db.FileDownloadGrants.AsNoTracking().Where(row => ids.Contains(row.FileObjectId))
                .OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, row.TenantId, row.ActorUserId, row.WorkspaceId, row.FileObjectId, row.AttachmentId,
                    row.TargetScopeType, row.TargetScopeId, row.Classification, row.AllowedOperation,
                    row.TokenHash, row.PolicyStamp, row.Purpose, row.ExpiresAt, row.RevokedAt, row.DownloadedAt,
                    row.CreatedAt, row.UpdatedAt
                }).ToListAsync(),
            // This is the identity inventory of successful relevant audit records,
            // not a claim that all mutable audit or Outbox fields are frozen.
            SuccessfulAuditIds = await db.AuditLogs.AsNoTracking().Where(row =>
                    (row.Action == "FileDeleted" && row.EntityId.HasValue && ids.Contains(row.EntityId.Value)) ||
                    (row.Action == "file_download.grant_used" && row.EntityId.HasValue &&
                        matchingGrantIds.Contains(row.EntityId.Value)))
                .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync()
        };
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state));
    }
}
