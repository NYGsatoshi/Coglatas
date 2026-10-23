using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    public Task FileObjectRetainedGrantRemainsReadableButHistoricalUploaderCannotDeleteWhileReadOnly() =>
        AssertReadOnlyRetainedCredentialAsync(byFileObject: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task AttachmentRetainedGrantRemainsReadableButHistoricalUploaderCannotDeleteWhileReadOnly() =>
        AssertReadOnlyRetainedCredentialAsync(byFileObject: false);

    private static Task AssertReadOnlyRetainedCredentialAsync(bool byFileObject) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                var session = await ReadOnlyActorSessionAsync(database);
                var target = await UploadPrivateAsync(member, database);
                var workspaceId = await AssertReadOnlyTargetAsync(database, target);
                var issueRoute = byFileObject
                    ? $"/api/files/{target.FileId:D}/download-grants"
                    : AttachmentIssueRoute(target);
                var retained = await IssueAsync(member, issueRoute, target);
                var ownerGrant = await IssueAsync(owner, issueRoute, target);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, retained), retained.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, ownerGrant), ownerGrant.Token);

                // The same route must work before the role transition. Deleting
                // a separate physical File preserves the retained target grant.
                var expendable = await UploadPrivateAsync(member, database);
                Assert.True(expendable.FileId != target.FileId && expendable.AttachmentId != target.AttachmentId);
                var positive = await IssueAsync(member, byFileObject
                    ? $"/api/files/{expendable.FileId:D}/download-grants" : AttachmentIssueRoute(expendable), expendable);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, positive), positive.Token);
                using (var deleted = await member.DeleteAsync(ReadOnlyDeleteRoute(byFileObject, expendable)))
                    Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                await AssertReadOnlyDeletionAsync(database, expendable);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, retained), retained.Token);

                await SetReadOnlyActorRoleAsync(owner, database, workspaceId, WorkspaceRole.ReadOnly);
                await AssertReadOnlySessionAsync(member, database, session);
                // ReadOnly retains current Workspace view and uploader read
                // authority. A role-only change must not invent grant revocation.
                await AssertBytesAsync(member, DownloadRoute(byFileObject, retained), retained.Token);
                var fresh = await IssueAsync(member, issueRoute, target);
                Assert.True(fresh.FileDownloadGrantId != retained.FileDownloadGrantId && fresh.Token != retained.Token);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, fresh), fresh.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, ownerGrant), ownerGrant.Token);
                var before = await ProtectedStateAsync(database, target.FileId);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"),
                    retained.FileDownloadGrantId.ToString("D"), fresh.FileDownloadGrantId.ToString("D"),
                    retained.Token, fresh.Token, ownerGrant.Token, positive.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                Assert.True(member.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName)
                    .Single().Length > 0, "Mutation denial must use the existing valid CSRF credential.");
                using (var denied = await member.DeleteAsync(ReadOnlyDeleteRoute(byFileObject, target)))
                {
                    await AssertSafeDenialAsync(denied, forbidden);
                    using var body = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                    var error = body.RootElement.GetProperty("error");
                    if (byFileObject)
                    {
                        Assert.True(error.GetProperty("code").GetString() == "FileOperationFailed" &&
                            error.GetProperty("message").GetString() == "The request could not be completed." &&
                            error.GetProperty("redactionApplied").GetBoolean() &&
                            error.GetProperty("target").ValueKind == JsonValueKind.Null &&
                            error.GetProperty("details").GetArrayLength() == 0,
                            "FileObject contribution denial must retain the exact canonical redacted envelope.");
                    }
                    else
                    {
                        Assert.True(error.GetString() == "Attachment not found.",
                            "Attachment contribution denial must retain its established safe envelope.");
                    }
                }
                await AssertUnchangedAsync(database, target, before);
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                    Assert.False(await db.AuditLogs.AnyAsync(row => row.EntityId == target.FileId && row.Action == "FileDeleted"),
                        "A denied deletion must not record a successful File deletion.");
                await AssertReadOnlySessionAsync(member, database, session);

                await SetReadOnlyActorRoleAsync(owner, database, workspaceId, WorkspaceRole.Member);
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, retained), retained.Token);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, fresh), fresh.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, ownerGrant), ownerGrant.Token);
                using (var deleted = await member.DeleteAsync(ReadOnlyDeleteRoute(byFileObject, target)))
                    Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                await AssertReadOnlyDeletionAsync(database, target);
                await AssertReadOnlyAuditPrivacyAsync(database, target, forbidden);
            });

    private static string ReadOnlyDeleteRoute(bool byFileObject, Target target) => byFileObject
        ? $"/api/files/{target.FileId:D}" : $"/api/attachments/{target.AttachmentId:D}";

    private static async Task<Guid> AssertReadOnlyTargetAsync(string database, Target target)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var attachment = await db.Attachments.AsNoTracking().Include(row => row.FileObject)
            .SingleAsync(row => row.Id == target.AttachmentId);
        Assert.True(attachment.OwnerType == AttachmentOwnerType.Workspace && attachment.OwnerId == attachment.WorkspaceId &&
            attachment.UploadedByUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
            attachment.OwnerUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
            attachment.FileObject is { Status: FileObjectStatus.Active, SharingPolicy: FileSharingPolicy.Private } &&
            attachment.FileObject.WorkspaceId == attachment.WorkspaceId,
            "The test must exercise the current private Workspace File's historical uploader.");
        Assert.False(await db.FileAccessGrants.AnyAsync(row => row.FileObjectId == target.FileId),
            "An explicit sharing recipient must not mask this current uploader-view boundary.");
        return attachment.WorkspaceId;
    }

    private static async Task SetReadOnlyActorRoleAsync(HttpClient owner, string database, Guid workspaceId, WorkspaceRole role)
    {
        using var response = await owner.PatchAsJsonAsync(
            $"/api/workspaces/{workspaceId:D}/members/{SecurityCiFixtureSeed.TenantAMemberUserId:D}",
            new UpdateWorkspaceMemberRequest(role, MembershipStatus.Active));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The production response DTO has multiple compatibility constructors.
        // Inspect its existing wire projection without adding a JSON constructor.
        using var projection = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(projection.RootElement.GetProperty("userId").GetGuid() == SecurityCiFixtureSeed.TenantAMemberUserId &&
            projection.RootElement.GetProperty("role").GetInt32() == (int)role &&
            projection.RootElement.GetProperty("status").GetInt32() == (int)MembershipStatus.Active);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var stored = await db.WorkspaceMembers.AsNoTracking().SingleAsync(row =>
            row.WorkspaceId == workspaceId && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
        Assert.True(stored.Role == role && stored.Status == MembershipStatus.Active,
            "The actual HTTP role transition must commit before subsequent File operations.");
    }

    private static async Task<Guid> ReadOnlyActorSessionAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.Sessions.AsNoTracking().Where(row => row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
            row.RevokedAt == null && row.ExpiresAt > DateTimeOffset.UtcNow).Select(row => row.Id).SingleAsync();
    }

    private static async Task AssertReadOnlySessionAsync(HttpClient member, string database, Guid session)
    {
        await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
        Assert.True(await ReadOnlyActorSessionAsync(database) == session,
            "Role transitions must retain the independently authenticated original session without another login.");
    }

    private static async Task AssertReadOnlyDeletionAsync(string database, Target target)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var file = await db.FileObjects.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == target.FileId);
        var attachment = await db.Attachments.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == target.AttachmentId);
        Assert.True(file.Status == FileObjectStatus.Deleted && file.DeletedAt.HasValue && attachment.DeletedAt.HasValue,
            "Successful deletion must persist both File and Attachment soft deletion.");
        Assert.True(await db.AuditLogs.CountAsync(row => row.EntityId == target.FileId && row.Action == "FileDeleted" &&
            row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId) == 1,
            "The single successful uploader deletion must have its own audit record.");
    }

    private static async Task AssertReadOnlyAuditPrivacyAsync(string database, Target target, IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var grantIds = await db.FileDownloadGrants.Where(row => row.FileObjectId == target.FileId)
            .Select(row => row.Id).ToListAsync();
        var entries = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == target.FileId ||
            (row.EntityId.HasValue && grantIds.Contains(row.EntityId.Value)))
            .Select(row => new { row.MetadataJson, row.Summary }).ToListAsync();
        Assert.True(entries.Count > 0, "Actual File/grant operations must supply audit records.");
        // Scoped identifiers are intentional audit metadata. File bytes,
        // credentials and storage details are never valid audit content.
        foreach (var entry in entries.SelectMany(row => new[] { row.MetadataJson, row.Summary }).Where(value => value is not null))
            foreach (var marker in forbidden.Where(value => !Guid.TryParse(value, out _)))
                Assert.True(!entry!.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    "ReadOnly transition audit disclosed protected content or credentials.");
    }
}
