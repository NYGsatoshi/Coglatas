using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
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
    public Task WorkspaceFolderPlacementPreservesRetainedGrantsAndRejectsStaleAndOtherWorkspaceDestinations() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                var target = await UploadPrivateAsync(member, database);
                var control = await UploadOtherWorkspaceAsync(owner, database);
                var workspace = await PlacementWorkspaceAsync(database, target);
                var otherWorkspace = await PlacementWorkspaceAsync(database, control);
                Assert.True(workspace != otherWorkspace && target.FileId != control.FileId);
                await AssertPlacementOwnerScopesAsync(database, workspace, otherWorkspace);
                await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                var session = await ReadOnlyActorSessionAsync(database);
                await AssertReadOnlySessionAsync(member, database, session);

                var destination = await CreatePlacementFolderAsync(owner, workspace, "SEC16 placement destination");
                var alternate = await CreatePlacementFolderAsync(owner, workspace, "SEC16 placement alternate");
                var otherDestination = await CreatePlacementFolderAsync(owner, otherWorkspace, "SEC16 other Workspace destination");
                var navigation = await ReadPlacementNavigationAsync(owner, workspace);
                var otherNavigation = await ReadPlacementNavigationAsync(owner, otherWorkspace);
                Assert.Contains(otherNavigation.Folders, row => row.Id == otherDestination.Id);
                var initial = await ReadPlacementAsync(owner, target);
                Assert.True(initial.WorkspaceId == workspace && initial.FolderId is null && initial.Version == 0);

                var memberGrants = await PlacementGrantsAsync(member, target);
                var ownerGrants = await PlacementGrantsAsync(owner, target);
                var controlGrants = await PlacementGrantsAsync(owner, control);
                await AssertPlacementBytesAsync(member, memberGrants);
                await AssertPlacementBytesAsync(owner, ownerGrants);
                await AssertPlacementBytesAsync(owner, controlGrants);
                var grants = memberGrants.Concat(ownerGrants).Concat(controlGrants).ToArray();
                var grantIds = grants.Select(item => item.Grant.FileDownloadGrantId).ToArray();
                Assert.True(grantIds.Distinct().Count() == 6);
                var bindings = await PlacementGrantBindingsAsync(database, grantIds);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    control.FileName, control.StorageKey, target.FileId.ToString("D"), target.AttachmentId.ToString("D"),
                    control.FileId.ToString("D"), control.AttachmentId.ToString("D"),
                    destination.Id.ToString("D"), alternate.Id.ToString("D"), otherDestination.Id.ToString("D"),
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                }.Concat(grants.SelectMany(item => new[] { item.Grant.Token, item.Grant.FileDownloadGrantId.ToString("D") })).ToArray();

                var moveRoute = $"/api/files/{target.FileId:D}/move";
                var firstMove = new FileMoveRequest(destination.Id, initial.Version, destination.Version);
                var beforeCsrf = await PlacementStateAsync(database, target, control);
                var csrf = owner.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName).Single();
                owner.DefaultRequestHeaders.Remove(SecurityOptions.CsrfHeaderName);
                try
                {
                    using var denied = await owner.PostAsJsonAsync(moveRoute, firstMove);
                    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                    await AssertSafeEnvelopeAsync(denied, forbidden);
                }
                finally { owner.DefaultRequestHeaders.Add(SecurityOptions.CsrfHeaderName, csrf); }
                await AssertPlacementStateAsync(database, target, control, beforeCsrf);

                var targetBeforeMove = await ProtectedStateAsync(database, target.FileId);
                var controlBeforeMove = await ProtectedStateAsync(database, control.FileId);
                var moved = await MovePlacementAsync(owner, target, firstMove);
                Assert.True(moved.FileObjectId == target.FileId && moved.WorkspaceId == workspace &&
                    moved.FolderId == destination.Id && moved.Version == initial.Version + 1);
                var persisted = await ReadPlacementAsync(member, target);
                Assert.True(persisted == moved, "A separate current actor must observe the committed placement.");
                var movedNavigation = await ReadPlacementNavigationAsync(owner, workspace);
                Assert.True(movedNavigation.RootVersion == navigation.RootVersion + 1 &&
                    movedNavigation.Folders.Single(row => row.Id == destination.Id).Version == destination.Version + 1 &&
                    movedNavigation.Folders.Single(row => row.Id == alternate.Id).Version == alternate.Version);
                await AssertUnchangedAsync(database, target, targetBeforeMove);
                await AssertUnchangedAsync(database, control, controlBeforeMove);
                var afterMoveBindings = await PlacementGrantBindingsAsync(database, grantIds);
                Assert.True(bindings.AsSpan().SequenceEqual(afterMoveBindings),
                    "Logical placement must preserve existing actor/resource/policy credential bindings.");
                await AssertPlacementBytesAsync(member, memberGrants);
                await AssertPlacementBytesAsync(owner, ownerGrants);
                await AssertPlacementBytesAsync(owner, controlGrants);

                // Both Workspaces are currently visible to this actor. The
                // rejected destination is a scope mismatch, not missing access.
                foreach (var rejected in new[]
                {
                    new FileMoveRequest(alternate.Id, initial.Version, alternate.Version),
                    new FileMoveRequest(alternate.Id, moved.Version, alternate.Version + 1),
                    new FileMoveRequest(otherDestination.Id, moved.Version, otherDestination.Version)
                })
                {
                    var before = await PlacementStateAsync(database, target, control);
                    using var denied = await owner.PostAsJsonAsync(moveRoute, rejected);
                    await AssertSafeDenialAsync(denied, forbidden);
                    using var envelope = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                    Assert.True(envelope.RootElement.GetProperty("error").ValueKind == JsonValueKind.Object);
                    await AssertPlacementStateAsync(database, target, control, before);
                    await AssertReadOnlySessionAsync(member, database, session);
                    await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                    await AssertPlacementBytesAsync(member, memberGrants);
                    await AssertPlacementBytesAsync(owner, controlGrants);
                }

                var finalNavigation = await ReadPlacementNavigationAsync(owner, workspace);
                var targetBeforeReturn = await ProtectedStateAsync(database, target.FileId);
                var returned = await MovePlacementAsync(owner, target,
                    new FileMoveRequest(null, moved.Version, finalNavigation.RootVersion));
                Assert.True(returned.FileObjectId == target.FileId && returned.WorkspaceId == workspace &&
                    returned.FolderId is null && returned.Version == moved.Version + 1);
                await AssertUnchangedAsync(database, target, targetBeforeReturn);
                var finalBindings = await PlacementGrantBindingsAsync(database, grantIds);
                Assert.True(bindings.AsSpan().SequenceEqual(finalBindings));
                await AssertPlacementBytesAsync(member, memberGrants);
                await AssertPlacementBytesAsync(owner, ownerGrants);
                await AssertPlacementBytesAsync(owner, controlGrants);
                await AssertPlacementBytesAsync(member, await PlacementGrantsAsync(member, target));
                await AssertScopeBytesAsync(member, $"/api/files/{target.FileId:D}/download", null, SyntheticBytes);
                await AssertScopeBytesAsync(member, $"/api/attachments/{target.AttachmentId:D}/download", null, SyntheticBytes);
                var controlLocation = await ReadPlacementAsync(owner, control);
                Assert.True(controlLocation.WorkspaceId == otherWorkspace && controlLocation.FolderId is null && controlLocation.Version == 0);
                var finalOtherNavigation = await ReadPlacementNavigationAsync(owner, otherWorkspace);
                Assert.True(finalOtherNavigation.RootVersion == otherNavigation.RootVersion &&
                    finalOtherNavigation.Folders.Single(row => row.Id == otherDestination.Id).Version == otherDestination.Version);
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertReadOnlyAuditPrivacyAsync(database, target, forbidden);
                await AssertReadOnlyAuditPrivacyAsync(database, control, forbidden);
            });

    private static async Task<Guid> PlacementWorkspaceAsync(string database, Target target)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var row = await db.Attachments.AsNoTracking().Include(item => item.FileObject).SingleAsync(item => item.Id == target.AttachmentId);
        Assert.True(row.OwnerType == AttachmentOwnerType.Workspace && row.OwnerId == row.WorkspaceId &&
            row.FileObject is { Status: FileObjectStatus.Active, SharingPolicy: FileSharingPolicy.Private } &&
            row.FileObject.WorkspaceId == row.WorkspaceId);
        return row.WorkspaceId;
    }

    private static async Task AssertPlacementOwnerScopesAsync(string database, Guid workspace, Guid otherWorkspace)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var rows = await db.WorkspaceMembers.AsNoTracking().Where(row => row.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId &&
            (row.WorkspaceId == workspace || row.WorkspaceId == otherWorkspace)).ToListAsync();
        Assert.True(rows.Count == 2 && rows.All(row => row.Role == WorkspaceRole.Owner && row.Status == MembershipStatus.Active) &&
            rows.Select(row => row.TenantId).Distinct().Count() == 1,
            "The move actor must be a current Owner in both same-Tenant Workspaces.");
    }

    private static async Task<FileFolderResponse> CreatePlacementFolderAsync(HttpClient actor, Guid workspace, string name)
    {
        using var response = await actor.PostAsJsonAsync("/api/file-folders", new FileFolderCreateRequest(workspace, null, name));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var folder = await response.Content.ReadFromJsonAsync<FileFolderResponse>();
        Assert.NotNull(folder);
        Assert.True(folder.WorkspaceId == workspace && folder.ParentFolderId is null && folder.Version == 1);
        return folder;
    }

    private static async Task<FileFolderNavigationResponse> ReadPlacementNavigationAsync(HttpClient actor, Guid workspace)
    {
        using var response = await actor.GetAsync($"/api/file-folders?workspaceId={workspace:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var navigation = await response.Content.ReadFromJsonAsync<FileFolderNavigationResponse>();
        Assert.NotNull(navigation);
        Assert.True(navigation.WorkspaceId == workspace);
        return navigation;
    }

    private static async Task<FileLocationResponse> ReadPlacementAsync(HttpClient actor, Target target)
    {
        using var response = await actor.GetAsync($"/api/files/{target.FileId:D}/location");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var location = await response.Content.ReadFromJsonAsync<FileLocationResponse>();
        Assert.NotNull(location);
        Assert.True(location.FileObjectId == target.FileId);
        return location;
    }

    private static async Task<FileLocationResponse> MovePlacementAsync(HttpClient actor, Target target, FileMoveRequest request)
    {
        using var response = await actor.PostAsJsonAsync($"/api/files/{target.FileId:D}/move", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await response.Content.ReadFromJsonAsync<FileLocationResponse>();
        Assert.NotNull(moved);
        return moved;
    }

    private static async Task<(bool ByFileObject, FileDownloadGrantResponse Grant)[]> PlacementGrantsAsync(HttpClient actor, Target target)
    {
        var file = await IssueAsync(actor, $"/api/files/{target.FileId:D}/download-grants", target);
        var attachment = await IssueAsync(actor, AttachmentIssueRoute(target), target);
        return [(true, file), (false, attachment)];
    }

    private static async Task AssertPlacementBytesAsync(HttpClient actor, (bool ByFileObject, FileDownloadGrantResponse Grant)[] grants)
    {
        foreach (var item in grants)
            await AssertBytesAsync(actor, DownloadRoute(item.ByFileObject, item.Grant), item.Grant.Token);
    }

    private static async Task<byte[]> PlacementGrantBindingsAsync(string database, Guid[] grantIds)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var rows = await db.FileDownloadGrants.AsNoTracking().Where(row => grantIds.Contains(row.Id)).OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.ActorUserId, row.TenantId, row.WorkspaceId, row.FileObjectId, row.AttachmentId,
                row.TargetScopeType, row.TargetScopeId, row.Classification, row.AllowedOperation, row.TokenHash,
                row.PolicyStamp, row.Purpose, row.ExpiresAt, row.RevokedAt }).ToListAsync();
        Assert.True(rows.Count == grantIds.Length && rows.All(row => row.RevokedAt is null));
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows));
    }

    private static async Task<byte[]> PlacementStateAsync(string database, Target target, Target control)
    {
        var targetState = await ProtectedStateAsync(database, target.FileId);
        var controlState = await ProtectedStateAsync(database, control.FileId);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var state = new { targetState, controlState,
            Folders = await db.Set<FileFolder>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Roots = await db.Set<FileFolderRootState>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Placements = await db.Set<FileFolderPlacement>().AsNoTracking().OrderBy(row => row.Id).ToListAsync() };
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state));
    }

    private static async Task AssertPlacementStateAsync(string database, Target target, Target control, byte[] before)
    {
        var after = await PlacementStateAsync(database, target, control);
        Assert.True(before.AsSpan().SequenceEqual(after),
            "A rejected move must preserve both Files, grants, uses and the selected folder/placement graph.");
    }
}
