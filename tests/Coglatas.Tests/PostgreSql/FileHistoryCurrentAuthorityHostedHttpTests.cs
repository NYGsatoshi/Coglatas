using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public Task RetainedActivityVersionPathReauthorizesAfterHttpWorkspaceMembershipRevocation() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                var workspace = await HistoryWorkspaceAsync(database, SecurityCiFixtureSeed.TenantAWorkspaceSlug);
                var file = await UploadHistoryFileAsync(member, database, workspace, "history-member.txt",
                    "SEC16_HISTORY_MEMBER_BYTES\n"u8.ToArray());
                var session = await ReadOnlyActorSessionAsync(database);
                var path = await AssertHistoryActivityAsync(member, file);
                await AssertHistoryBytesAsync(member, path, file.Bytes);
                await AssertHistoryBytesAsync(owner, path, file.Bytes);
                var beforeRemoval = await HistoryStateAsync(database, file);

                using (var removed = await owner.DeleteAsync(
                    $"/api/workspaces/{workspace:D}/members/{SecurityCiFixtureSeed.TenantAMemberUserId:D}"))
                    Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
                await AssertHistoryMembershipAsync(database, workspace, MembershipStatus.Suspended);
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertHistoryUnchangedAsync(database, file, beforeRemoval);

                var forbidden = HistoryForbidden([file]);
                foreach (var route in new[] { path, HistoryActivityRoute(file) })
                {
                    var before = await HistoryStateAsync(database, file);
                    using var denied = await member.GetAsync(route);
                    await AssertHistoryDeniedAsync(denied, forbidden);
                    await AssertHistoryUnchangedAsync(database, file, before);
                }
                // Workspace removal does not revoke this live Tenant session.
                // The independent Owner remains a byte-positive control.
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertHistoryBytesAsync(owner, path, file.Bytes);
                await AssertHistoryActivityAsync(owner, file);

                await SetReadOnlyActorRoleAsync(owner, database, workspace, WorkspaceRole.Member);
                await AssertHistoryMembershipAsync(database, workspace, MembershipStatus.Active);
                await AssertReadOnlySessionAsync(member, database, session);
                Assert.True(await AssertHistoryActivityAsync(member, file) == path,
                    "Restoration must expose the same immutable initial-version path.");
                await AssertHistoryBytesAsync(member, path, file.Bytes);
                await AssertHistoryBytesAsync(owner, path, file.Bytes);
                await AssertHistoryUnchangedAsync(database, file, beforeRemoval);
                await AssertReadOnlyAuditPrivacyAsync(database, file.Target, forbidden);
            });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task InitialVersionContentBindsFileResourceAndCurrentActorAndTenantWithoutProtectedEffects() =>
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
                await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                await AssertScopeActorAsync(beta, SecurityCiFixtureSeed.TenantBOwnerUserId);
                var alphaWorkspace = await HistoryWorkspaceAsync(database, SecurityCiFixtureSeed.TenantAWorkspaceSlug);
                var betaWorkspace = await HistoryWorkspaceAsync(database, SecurityCiFixtureSeed.TenantBWorkspaceSlug);
                var first = await UploadHistoryFileAsync(member, database, alphaWorkspace, "history-first.txt",
                    "SEC16_HISTORY_FIRST_DISTINCT_BYTES\n"u8.ToArray());
                var second = await UploadHistoryFileAsync(owner, database, alphaWorkspace, "history-second.txt",
                    "SEC16_HISTORY_SECOND_DISTINCT_BYTES\n"u8.ToArray());
                var foreign = await UploadHistoryFileAsync(beta, database, betaWorkspace, "history-foreign.txt",
                    "SEC16_HISTORY_FOREIGN_DISTINCT_BYTES\n"u8.ToArray());
                Assert.True(first.Target.FileId != second.Target.FileId && first.Target.FileId != foreign.Target.FileId &&
                    !first.Bytes.AsSpan().SequenceEqual(second.Bytes) && !first.Bytes.AsSpan().SequenceEqual(foreign.Bytes));
                var firstPath = await AssertHistoryActivityAsync(member, first);
                var secondPath = await AssertHistoryActivityAsync(owner, second);
                var foreignPath = await AssertHistoryActivityAsync(beta, foreign);
                await AssertHistoryBytesAsync(member, firstPath, first.Bytes);
                await AssertHistoryBytesAsync(owner, firstPath, first.Bytes);
                await AssertHistoryBytesAsync(owner, secondPath, second.Bytes);
                await AssertHistoryBytesAsync(beta, foreignPath, foreign.Bytes);

                var files = new[] { first, second, foreign };
                var forbidden = HistoryForbidden(files);
                // Owner can read both resources independently. Exchanging their
                // actual version identities still must not open either blob.
                var requests = new[]
                {
                    (owner, HistoryVersionRoute(first.Target.FileId, second.Target.FileId)),
                    (owner, HistoryVersionRoute(second.Target.FileId, first.Target.FileId)),
                    (member, secondPath), (member, HistoryActivityRoute(second)),
                    (beta, firstPath), (beta, HistoryActivityRoute(first)),
                    (owner, foreignPath), (owner, HistoryActivityRoute(foreign))
                };
                foreach (var (actor, route) in requests)
                {
                    var before = await Task.WhenAll(files.Select(file => HistoryStateAsync(database, file)));
                    using var denied = await actor.GetAsync(route);
                    await AssertHistoryDeniedAsync(denied, forbidden);
                    for (var index = 0; index < files.Length; index++)
                        await AssertHistoryUnchangedAsync(database, files[index], before[index]);
                }
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                await AssertScopeActorAsync(beta, SecurityCiFixtureSeed.TenantBOwnerUserId);
                await AssertHistoryBytesAsync(member, firstPath, first.Bytes);
                await AssertHistoryBytesAsync(owner, secondPath, second.Bytes);
                await AssertHistoryBytesAsync(beta, foreignPath, foreign.Bytes);
                foreach (var file in files)
                    await AssertReadOnlyAuditPrivacyAsync(database, file.Target, forbidden);
            });

    private static string HistoryActivityRoute(HistoryFile file) => $"/api/files/{file.Target.FileId:D}/activity";
    private static string HistoryVersionRoute(Guid file, Guid version) => $"/api/files/{file:D}/versions/{version:D}/content";

    private static async Task<Guid> HistoryWorkspaceAsync(string database, string slug)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.Workspaces.AsNoTracking().Where(row => row.Slug == slug).Select(row => row.Id).SingleAsync();
    }

    private static async Task<HistoryFile> UploadHistoryFileAsync(
        HttpClient actor, string database, Guid workspace, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "ownerType");
        form.Add(new StringContent(workspace.ToString("D")), "ownerId");
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", name);
        using var response = await actor.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(uploaded);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var target = await db.FileObjects.AsNoTracking().Where(row => row.Id == uploaded.FileObjectId)
            .Select(row => new Target(row.Id, uploaded.Id, row.OriginalFileName, row.StorageKey)).SingleAsync();
        var captured = await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT EXISTS(SELECT 1 FROM file_versions v JOIN file_objects f ON f."Id"=v."FileObjectId"
                WHERE v."Id"=@file AND v."FileObjectId"=@file AND v."VersionNumber"=1
                  AND v."TenantId"=f."TenantId" AND f."WorkspaceId"=@workspace
                  AND v."StorageKey"=f."StorageKey" AND v."SizeBytes"=@size)
            """, ("file", target.FileId), ("workspace", workspace), ("size", bytes.LongLength));
        Assert.True(captured, "The existing upload trigger must create the native immutable initial version.");
        return new HistoryFile(target, bytes);
    }

    private static async Task<string> AssertHistoryActivityAsync(HttpClient actor, HistoryFile file)
    {
        using var response = await actor.GetAsync(HistoryActivityRoute(file));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(!text.Contains(file.Target.StorageKey, StringComparison.Ordinal) &&
            !text.Contains("storageKey", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains(Encoding.UTF8.GetString(file.Bytes).TrimEnd(), StringComparison.Ordinal),
            "Activity must expose typed metadata without storage credentials or File bytes.");
        using var body = JsonDocument.Parse(text);
        Assert.True(body.RootElement.GetProperty("fileObjectId").GetGuid() == file.Target.FileId);
        var entry = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        var version = entry.GetProperty("version");
        var path = version.GetProperty("viewPath").GetString();
        Assert.True(entry.GetProperty("kind").GetString() == "uploaded" &&
            version.GetProperty("versionId").GetGuid() == file.Target.FileId &&
            version.GetProperty("versionNumber").GetInt32() == 1 && version.GetProperty("isCurrent").GetBoolean() &&
            path == HistoryVersionRoute(file.Target.FileId, file.Target.FileId),
            "Activity must bind its cached path to this File's actual native initial version.");
        return path;
    }

    private static async Task AssertHistoryBytesAsync(HttpClient actor, string route, byte[] bytes)
    {
        using var response = await actor.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(bytes),
            "The authorized version route must return this resource's distinct physical bytes.");
        Assert.True(response.Headers.CacheControl?.NoStore == true &&
            response.Content.Headers.ContentType?.MediaType == "text/plain");
    }

    private static async Task AssertHistoryDeniedAsync(HttpResponseMessage response, string[] forbidden)
    {
        await AssertSafeDenialAsync(response, forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = body.RootElement.GetProperty("error");
        Assert.True(error.GetProperty("code").GetString() == "FILE_NOT_FOUND" &&
            error.GetProperty("message").GetString() == "The request could not be completed." &&
            error.GetProperty("redactionApplied").GetBoolean() &&
            error.GetProperty("target").ValueKind == JsonValueKind.Null && error.GetProperty("details").GetArrayLength() == 0,
            "Activity/version denial must preserve the exact current canonical safe envelope.");
    }

    private static string[] HistoryForbidden(HistoryFile[] files) => [.. files.SelectMany(file => new[]
    {
        Encoding.UTF8.GetString(file.Bytes).TrimEnd(), file.Target.FileName, file.Target.StorageKey,
        file.Target.FileId.ToString("D"), file.Target.AttachmentId.ToString("D")
    }).Concat(["storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"])];

    private static async Task<byte[]> HistoryStateAsync(string database, HistoryFile file)
    {
        var protectedState = await ProtectedStateAsync(database, file.Target.FileId);
        var versions = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
            SELECT encode(sha256(convert_to(COALESCE(string_agg(to_jsonb(v)::text,'|' ORDER BY v."Id"),''),'UTF8')),'hex')
            FROM file_versions v WHERE v."FileObjectId"=@file
            """, ("file", file.Target.FileId));
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var audits = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == file.Target.FileId)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.Action, row.Summary, row.MetadataJson }).ToListAsync();
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { protectedState, versions, audits }));
    }

    private static async Task AssertHistoryUnchangedAsync(string database, HistoryFile file, byte[] before)
    {
        var after = await HistoryStateAsync(database, file);
        Assert.True(before.AsSpan().SequenceEqual(after),
            "A denied or read-only history operation must preserve selected File/version/grant/use/audit state.");
    }

    private static async Task AssertHistoryMembershipAsync(string database, Guid workspace, MembershipStatus status)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var membership = await db.WorkspaceMembers.AsNoTracking().SingleAsync(row => row.WorkspaceId == workspace &&
            row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
        Assert.True(membership.Status == status && membership.Role == WorkspaceRole.Member);
        Assert.True(await db.TenantUsers.AsNoTracking().AnyAsync(row => row.UserId == membership.UserId &&
            row.TenantId == membership.TenantId && row.Status == TenantUserStatus.Active),
            "Workspace revocation must leave the current actor's Tenant membership active for this isolation control.");
    }

    private sealed record HistoryFile(Target Target, byte[] Bytes);
}
