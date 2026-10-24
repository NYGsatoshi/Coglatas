using System.Net;
using System.Net.Http.Json;
using System.Text;
using Coglatas.Application.Files;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class FileDownloadGrantLifecycleHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task RetainedBatchSelectionReauthorizesReadOnlyUploaderWithoutRevokingAuthorizedDownloads() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                var session = await ReadOnlyActorSessionAsync(database);

                // Exercise the actual batch writer before the role transition.
                var expendable = await UploadPrivateAsync(member, database);
                var workspace = await AssertReadOnlyTargetAsync(database, expendable);
                var positive = await CaptureUploaderSelectionAsync(member, database, workspace, [expendable]);
                await AssertBatchResultAsync(member, positive, [expendable], deleted: true);
                await AssertReadOnlyDeletionAsync(database, expendable);

                var first = await UploadPrivateAsync(member, database);
                var second = await UploadPrivateAsync(member, database);
                Target[] targets = [first, second];
                var fileGrant = await IssueAsync(member, $"/api/files/{first.FileId:D}/download-grants", first);
                var attachmentGrant = await IssueAsync(member, AttachmentIssueRoute(second), second);
                await AssertBytesAsync(member, DownloadRoute(true, fileGrant), fileGrant.Token);
                await AssertBytesAsync(member, DownloadRoute(false, attachmentGrant), attachmentGrant.Token);
                var retained = await CaptureUploaderSelectionAsync(member, database, workspace, targets);

                await SetReadOnlyActorRoleAsync(owner, database, workspace, WorkspaceRole.ReadOnly);
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertBytesAsync(member, DownloadRoute(true, fileGrant), fileGrant.Token);
                await AssertBytesAsync(member, DownloadRoute(false, attachmentGrant), attachmentGrant.Token);
                var beforeFirst = await ProtectedStateAsync(database, first.FileId);
                var beforeSecond = await ProtectedStateAsync(database, second.FileId);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), first.FileName, first.StorageKey,
                    second.FileName, second.StorageKey, fileGrant.Token, attachmentGrant.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                foreach (var target in targets)
                {
                    using var denied = await member.DeleteAsync($"/api/files/{target.FileId:D}");
                    await AssertSafeDenialAsync(denied, forbidden);
                }
                // A retained snapshot is an actor-bound selection, never a grant
                // to bypass the current contribution authority of either item.
                await AssertBatchResultAsync(member, retained, targets, deleted: false, forbidden);
                await AssertUnchangedAsync(database, first, beforeFirst);
                await AssertUnchangedAsync(database, second, beforeSecond);
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var snapshot = await db.FileSelectionSnapshots.AsNoTracking().SingleAsync(row => row.Id == retained);
                    Assert.True(snapshot.ConsumedAt.HasValue && snapshot.ConsumptionVersion == 1,
                        "The existing contract consumes a snapshot once even when every item is denied.");
                    var ids = targets.Select(target => target.FileId).ToArray();
                    Assert.False(await db.AuditLogs.AnyAsync(row => row.EntityId.HasValue &&
                        ids.Contains(row.EntityId.Value) && row.Action == "FileDeleted"));
                }
                using (var replay = await member.PostAsync($"/api/files/selection-snapshots/{retained:D}/delete", null))
                    await AssertSafeDenialAsync(replay, forbidden);
                await AssertUnchangedAsync(database, first, beforeFirst);
                await AssertUnchangedAsync(database, second, beforeSecond);

                // The same session and physical bytes remain valid; role-only
                // downgrade must not be mistaken for download-grant revocation.
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertBytesAsync(member, DownloadRoute(true, fileGrant), fileGrant.Token);
                await AssertBytesAsync(member, DownloadRoute(false, attachmentGrant), attachmentGrant.Token);
                await SetReadOnlyActorRoleAsync(owner, database, workspace, WorkspaceRole.Member);
                await AssertReadOnlySessionAsync(member, database, session);
                var current = await CaptureUploaderSelectionAsync(member, database, workspace, targets);
                Assert.True(current != retained);
                await AssertBatchResultAsync(member, current, targets, deleted: true);
                foreach (var target in targets)
                {
                    await AssertReadOnlyDeletionAsync(database, target);
                    await AssertReadOnlyAuditPrivacyAsync(database, target, forbidden);
                }
                using (var denied = await member.PostAsJsonAsync(DownloadRoute(true, fileGrant),
                    new FileDownloadGrantTokenRequest(fileGrant.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                using (var denied = await member.PostAsJsonAsync(DownloadRoute(false, attachmentGrant),
                    new FileDownloadGrantTokenRequest(attachmentGrant.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
            });

    private static async Task<Guid> CaptureUploaderSelectionAsync(HttpClient member, string database,
        Guid workspace, Target[] expected)
    {
        using var response = await member.PostAsync(
            $"/api/files/selection-snapshots?workspaceId={workspace:D}&onlyMyUploads=true&q=sec16-sharing-private", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var captured = await response.Content.ReadFromJsonAsync<FileSelectionSnapshotCaptureResponse>();
        Assert.NotNull(captured);
        Assert.True(captured.Outcome == "Captured" && captured.SelectedCount == expected.Length &&
            captured.SelectionSnapshotId.HasValue && captured.ExpiresAt > DateTimeOffset.UtcNow);
        var id = captured.SelectionSnapshotId.Value;
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var snapshot = await db.FileSelectionSnapshots.AsNoTracking().Include(row => row.Items).SingleAsync(row => row.Id == id);
        Assert.True(snapshot.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId && snapshot.WorkspaceId == workspace &&
            snapshot.ConsumedAt is null && snapshot.ConsumptionVersion == 0 &&
            snapshot.Items.Select(row => row.FileObjectId).Order().SequenceEqual(expected.Select(row => row.FileId).Order()),
            "The actual capture must persist exactly the current uploader's intended File identities.");
        return id;
    }

    private static async Task AssertBatchResultAsync(HttpClient member, Guid snapshot, Target[] targets,
        bool deleted, IEnumerable<string>? forbidden = null)
    {
        using var response = await member.PostAsync($"/api/files/selection-snapshots/{snapshot:D}/delete", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (forbidden is not null) await AssertSafeEnvelopeAsync(response, forbidden);
        var result = await response.Content.ReadFromJsonAsync<FileSelectionSnapshotDeleteResponse>();
        Assert.NotNull(result);
        Assert.True(result.SelectionSnapshotId == snapshot && result.AttemptedCount == targets.Length &&
            result.SucceededCount == (deleted ? targets.Length : 0) && result.FailedCount == (deleted ? 0 : targets.Length));
        Assert.True(result.Items.Count == targets.Length &&
            result.Items.Select(row => row.FileObjectId).Order().SequenceEqual(targets.Select(row => row.FileId).Order()) &&
            result.Items.All(row => row.Succeeded == deleted && row.Outcome == (deleted ? "Deleted" : "NotDeleted")),
            "Each selected File must have exactly one bounded outcome reflecting current contribution authority.");
    }
}
