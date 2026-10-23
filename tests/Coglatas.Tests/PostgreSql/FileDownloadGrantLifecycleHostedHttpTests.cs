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
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class FileDownloadGrantLifecycleHostedHttpTests
{
    private static readonly byte[] SyntheticBytes = "SEC02_ALPHA_FILE_CANARY_DO_NOT_LEAK\n"u8.ToArray();

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task ExpiredFileObjectGrantRejectsRetainedCredentialThroughRealHttp() =>
        AssertTerminalGrantAsync(byFileObject: true, expired: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task RevokedFileObjectGrantRejectsRetainedCredentialThroughRealHttp() =>
        AssertTerminalGrantAsync(byFileObject: true, expired: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task ExpiredAttachmentGrantRejectsRetainedCredentialThroughRealHttp() =>
        AssertTerminalGrantAsync(byFileObject: false, expired: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task RevokedAttachmentGrantRejectsRetainedCredentialThroughRealHttp() =>
        AssertTerminalGrantAsync(byFileObject: false, expired: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task FileObjectGrantRemainsInvalidAfterRealHttpSharingRevokeAndRegrant() =>
        AssertSharingGrantAsync(byFileObject: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task AttachmentGrantRemainsInvalidAfterRealHttpSharingRevokeAndRegrant() =>
        AssertSharingGrantAsync(byFileObject: false);

    private static Task AssertSharingGrantAsync(bool byFileObject) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                var target = await UploadPrivateAsync(owner, database);
                var sharingRoute = $"/api/files/{target.FileId:D}/sharing";
                var issueRoute = byFileObject
                    ? $"/api/files/{target.FileId:D}/download-grants"
                    : $"/api/attachments/{target.AttachmentId:D}/download-grants";
                var directRoute = byFileObject
                    ? $"/api/files/{target.FileId:D}/download"
                    : $"/api/attachments/{target.AttachmentId:D}/download";
                var initial = await SharingAsync(owner, sharingRoute);
                Assert.True(initial.SharingPolicy == "Private" && initial.Recipients.Count == 0);

                var beforeCsrf = await ProtectedStateAsync(database, target.FileId);
                var csrf = owner.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName).Single();
                owner.DefaultRequestHeaders.Remove(SecurityOptions.CsrfHeaderName);
                try
                {
                    using var denied = await owner.PostAsJsonAsync(sharingRoute + "/recipients",
                        new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantAMemberUserId, initial.SharingVersion));
                    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                }
                finally { owner.DefaultRequestHeaders.Add(SecurityOptions.CsrfHeaderName, csrf); }
                await AssertUnchangedAsync(database, target, beforeCsrf);

                var shared = await ShareAsync(owner, sharingRoute, initial.SharingVersion);
                Assert.True(shared.Recipients.Count == 1, "Sharing must create exactly one current recipient.");
                var recipientGrantId = shared.Recipients[0].GrantId;
                Assert.True(shared.SharingVersion == initial.SharingVersion + 1);
                var retained = await IssueAsync(member, issueRoute, target);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, retained), retained.Token);

                using var revokedResponse = await owner.DeleteAsync(
                    $"{sharingRoute}/recipients/{recipientGrantId:D}?expectedSharingVersion={shared.SharingVersion}");
                var revoked = await ReadSharingAsync(revokedResponse);
                Assert.True(revoked.Recipients.Count == 0 && revoked.SharingPolicy == "Private" &&
                    revoked.SharingVersion == shared.SharingVersion + 1);
                var forbidden = new List<string>
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"), recipientGrantId.ToString("D"),
                    retained.FileDownloadGrantId.ToString("D"), retained.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                var revokedState = await ProtectedStateAsync(database, target.FileId);
                using (var denied = await member.PostAsJsonAsync(DownloadRoute(byFileObject, retained),
                    new FileDownloadGrantTokenRequest(retained.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, revokedState);
                using (var denied = await member.PostAsJsonAsync(issueRoute, new FileDownloadGrantRequest()))
                {
                    // Attachment issuance deliberately masks denied resources as 404;
                    // FileObject issuance and both download surfaces retain 400.
                    if (byFileObject)
                    {
                        await AssertSafeDenialAsync(denied, forbidden);
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                        await AssertSafeEnvelopeAsync(denied, forbidden);
                        using var envelope = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("code").GetString() ==
                            "FILE_DOWNLOAD_GRANT_NOT_FOUND");
                    }
                }
                await AssertUnchangedAsync(database, target, revokedState);
                using (var denied = await member.GetAsync(directRoute))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, revokedState);
                using (var identity = await member.GetAsync("/api/auth/me"))
                    Assert.Equal(HttpStatusCode.OK, identity.StatusCode);

                // SharingVersion invalidates every earlier policy stamp, including
                // an owner's. Obtain a current owner grant after the mutation.
                var currentOwner = await IssueAsync(owner, issueRoute, target);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, currentOwner), currentOwner.Token);
                var restored = await ShareAsync(owner, sharingRoute, revoked.SharingVersion);
                Assert.True(restored.SharingVersion == revoked.SharingVersion + 1 &&
                    restored.Recipients.Count == 1 && restored.Recipients[0].GrantId != recipientGrantId);
                var replacement = await IssueAsync(member, issueRoute, target);
                Assert.True(replacement.FileDownloadGrantId != retained.FileDownloadGrantId &&
                    replacement.Token != retained.Token);
                forbidden.Add(currentOwner.Token);
                forbidden.Add(replacement.Token);
                var restoredState = await ProtectedStateAsync(database, target.FileId);
                using (var denied = await member.PostAsJsonAsync(DownloadRoute(byFileObject, retained),
                    new FileDownloadGrantTokenRequest(retained.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, restoredState);
                await AssertSharingAuditAsync(database, retained.FileDownloadGrantId, forbidden);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, replacement), replacement.Token);
            });

    private static async Task<Target> UploadPrivateAsync(HttpClient owner, string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var workspaceId = await db.Attachments.AsNoTracking().Where(row =>
                row.FileObject!.OriginalFileName == "sec02-alpha-private.txt")
            .Select(row => row.WorkspaceId).SingleAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "ownerType");
        form.Add(new StringContent(workspaceId.ToString("D")), "ownerId");
        using var content = new ByteArrayContent(SyntheticBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", "sec16-sharing-private.txt");
        using var uploaded = await owner.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var attachment = await uploaded.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(attachment);
        return await db.FileObjects.AsNoTracking().Where(row => row.Id == attachment.FileObjectId)
            .Select(row => new Target(row.Id, attachment.Id, row.OriginalFileName, row.StorageKey)).SingleAsync();
    }

    private static async Task<FileSharingResponse> SharingAsync(HttpClient owner, string route)
    {
        using var response = await owner.GetAsync(route);
        return await ReadSharingAsync(response);
    }

    private static async Task<FileSharingResponse> ShareAsync(HttpClient owner, string route, long version)
    {
        using var response = await owner.PostAsJsonAsync(route + "/recipients",
            new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantAMemberUserId, version));
        return await ReadSharingAsync(response);
    }

    private static async Task<FileSharingResponse> ReadSharingAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sharing = await response.Content.ReadFromJsonAsync<FileSharingResponse>();
        Assert.NotNull(sharing);
        Assert.True(sharing.CanManageSharing && sharing.CanInspectSharing && sharing.SharingVersion > 0);
        return sharing;
    }

    private static async Task AssertUnchangedAsync(string database, Target target, byte[] before)
    {
        var after = await ProtectedStateAsync(database, target.FileId);
        Assert.True(before.AsSpan().SequenceEqual(after),
            "Denied sharing/grant operation must preserve selected File, Attachment, share and grant state.");
    }

    private static async Task AssertSharingAuditAsync(string database, Guid grantId, IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var entries = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == grantId &&
                row.Action == "file_download.grant_use_denied").OrderBy(row => row.CreatedAt)
            .Select(row => row.MetadataJson).ToListAsync();
        Assert.True(entries.Count == 2, "Both retained-grant uses must have denial audit evidence.");
        var reasons = new List<string?>();
        var protectedMarkers = forbidden.Where(value => !Guid.TryParse(value, out _)).ToArray();
        foreach (var entry in entries)
        {
            Assert.NotNull(entry);
            using var metadata = JsonDocument.Parse(entry);
            reasons.Add(metadata.RootElement.GetProperty("decisionReason").GetString());
            foreach (var marker in protectedMarkers)
                Assert.True(!entry.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    "Sharing/grant denial audit disclosed protected content or credentials.");
        }
        Assert.True(reasons.SequenceEqual(new[] { "current_authorization_failed", "policy_changed" }),
            "Revoked sharing must deny current authority; regrant must not revive the old policy stamp.");
    }

    private static Task AssertTerminalGrantAsync(bool byFileObject, bool expired) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                // Reuse the existing isolated Web host, physical SEC-02 files,
                // cookie sessions and CSRF handling. No Hub connection is needed.
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                var target = await TargetAsync(database);
                var issueRoute = byFileObject
                    ? $"/api/files/{target.FileId:D}/download-grants"
                    : $"/api/attachments/{target.AttachmentId:D}/download-grants";

                // Prove that this actual modifying route enforces CSRF before
                // issuing the real current-authority member and owner grants.
                var csrf = member.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName).Single();
                member.DefaultRequestHeaders.Remove(SecurityOptions.CsrfHeaderName);
                try
                {
                    using var missingCsrf = await member.PostAsJsonAsync(issueRoute, new FileDownloadGrantRequest());
                    Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
                }
                finally { member.DefaultRequestHeaders.Add(SecurityOptions.CsrfHeaderName, csrf); }

                var memberGrant = await IssueAsync(member, issueRoute, target);
                var ownerGrant = await IssueAsync(owner, issueRoute, target);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, memberGrant), memberGrant.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, ownerGrant), ownerGrant.Token);

                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var stored = await db.FileDownloadGrants.SingleAsync(row => row.Id == memberGrant.FileDownloadGrantId);
                    Assert.True(stored.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                    Assert.True(stored.DownloadedAt.HasValue && stored.ExpiresAt > DateTimeOffset.UtcNow && !stored.RevokedAt.HasValue);
                    // There is no public download-grant revocation/expiry writer.
                    // Arrange only this synthetic persisted terminal state; the
                    // authorization oracle remains the real HTTP consumption.
                    if (expired) stored.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                    else stored.RevokedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }

                var before = await ProtectedStateAsync(database, target.FileId);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"),
                    memberGrant.FileDownloadGrantId.ToString("D"), memberGrant.Token, ownerGrant.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var denied = await member.PostAsJsonAsync(DownloadRoute(byFileObject, memberGrant),
                        new FileDownloadGrantTokenRequest(memberGrant.Token));
                    await AssertSafeDenialAsync(denied, forbidden);
                    var after = await ProtectedStateAsync(database, target.FileId);
                    Assert.True(before.AsSpan().SequenceEqual(after),
                        "Denied terminal-grant use must preserve File, share and grant state.");
                }

                await AssertLifecycleAuditAsync(database, memberGrant.FileDownloadGrantId,
                    expired ? "grant_expired" : "grant_revoked", forbidden);
                using (var identity = await member.GetAsync("/api/auth/me"))
                    Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
                // An independent still-current grant must keep reading the same
                // physical bytes: missing files or broken auth cannot pass denial.
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, ownerGrant), ownerGrant.Token);
                var replacement = await IssueAsync(member, issueRoute, target);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, replacement), replacement.Token);
            });

    private static async Task<FileDownloadGrantResponse> IssueAsync(HttpClient client, string route, Target target)
    {
        using var response = await client.PostAsJsonAsync(route, new FileDownloadGrantRequest("sec16-terminal-http"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grant = await response.Content.ReadFromJsonAsync<FileDownloadGrantResponse>();
        Assert.NotNull(grant);
        Assert.True(grant.FileDownloadGrantId != Guid.Empty && !string.IsNullOrWhiteSpace(grant.Token));
        Assert.True(grant.FileObjectId == target.FileId && grant.AttachmentId == target.AttachmentId);
        return grant;
    }

    private static string DownloadRoute(bool byFileObject, FileDownloadGrantResponse grant) => byFileObject
        ? $"/api/file-download-grants/{grant.FileDownloadGrantId:D}/download"
        : $"/api/attachment-download-grants/{grant.FileDownloadGrantId:D}/download";

    private static async Task AssertBytesAsync(HttpClient client, string route, string token)
    {
        using var response = await client.PostAsJsonAsync(route, new FileDownloadGrantTokenRequest(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(SyntheticBytes),
            "Authorized synthetic download must match the physical seeded bytes.");
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    private static async Task AssertSafeDenialAsync(HttpResponseMessage response, IEnumerable<string> forbidden)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSafeEnvelopeAsync(response, forbidden);
    }

    private static async Task AssertSafeEnvelopeAsync(HttpResponseMessage response, IEnumerable<string> forbidden)
    {
        Assert.True(response.Headers.Location is null, "A denied grant cannot redirect to storage.");
        Assert.True(response.Content.Headers.ContentDisposition is null, "A denied grant cannot disclose a filename.");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length <= 2048, "A terminal-grant denial must use a bounded envelope.");
        var body = Encoding.UTF8.GetString(bytes);
        foreach (var marker in forbidden)
            Assert.True(!body.Contains(marker, StringComparison.OrdinalIgnoreCase),
                "Terminal-grant denial disclosed a protected marker.");
    }

    private static async Task<Target> TargetAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var tenant = await db.Tenants.AsNoTracking().Where(row => row.Slug == SecurityCiFixtureSeed.TenantASlug)
            .Select(row => row.Id).SingleAsync();
        return await db.Attachments.AsNoTracking().Where(row => row.TenantId == tenant &&
                row.FileObject!.OriginalFileName == "sec02-alpha-private.txt")
            .Select(row => new Target(row.FileObjectId, row.Id, row.FileObject!.OriginalFileName,
                row.FileObject.StorageKey)).SingleAsync();
    }

    private static async Task<byte[]> ProtectedStateAsync(string database, Guid fileId)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var state = new
        {
            File = await db.FileObjects.AsNoTracking().Where(row => row.Id == fileId).Select(row => new
            {
                row.Id, row.WorkspaceId, row.ProjectId, row.Status, row.Classification,
                row.SharingPolicy, row.SharingVersion, row.DeletedAt, row.UpdatedAt
            }).SingleAsync(),
            Attachments = await db.Attachments.AsNoTracking().Where(row => row.FileObjectId == fileId)
                .OrderBy(row => row.Id).Select(row => new { row.Id, row.OwnerType, row.OwnerId, row.ScanStatus, row.DeletedAt, row.UpdatedAt }).ToListAsync(),
            Shares = await db.FileAccessGrants.AsNoTracking().Where(row => row.FileObjectId == fileId)
                .OrderBy(row => row.Id).Select(row => new { row.Id, row.RevokedAt }).ToListAsync(),
            Grants = await db.FileDownloadGrants.AsNoTracking().Where(row => row.FileObjectId == fileId)
                .OrderBy(row => row.Id).Select(row => new { row.Id, row.ExpiresAt, row.RevokedAt, row.DownloadedAt, row.UpdatedAt }).ToListAsync(),
            SuccessfulUses = await db.AuditLogs.CountAsync(row => row.Action == "file_download.grant_used" &&
                db.FileDownloadGrants.Any(grant => grant.FileObjectId == fileId && grant.Id == row.EntityId))
        };
        // Keep even failure diagnostics free of protected snapshot values.
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state));
    }

    private static async Task AssertLifecycleAuditAsync(string database, Guid grantId, string reason, IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var entries = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == grantId &&
                (row.Action == "file_download.grant_use_denied" || row.Action == "file_download." + reason))
            .Select(row => new { row.Action, row.MetadataJson }).ToListAsync();
        Assert.True(entries.Count(row => row.Action == "file_download." + reason) == 2,
            "Each retained-credential denial must record its actual persisted lifecycle reason.");
        var protectedMarkers = forbidden.Where(value => !Guid.TryParse(value, out _)).ToArray();
        foreach (var entry in entries)
        {
            Assert.NotNull(entry.MetadataJson);
            using var metadata = JsonDocument.Parse(entry.MetadataJson);
            Assert.True(metadata.RootElement.GetProperty("decisionReason").GetString() == reason);
            // Resource/actor IDs are intentional audit metadata. Credentials,
            // storage details and File content must never be audit evidence.
            foreach (var marker in protectedMarkers)
                Assert.True(!entry.MetadataJson.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    "Terminal-grant audit disclosed protected content or credentials.");
        }
    }

    private sealed record Target(Guid FileId, Guid AttachmentId, string FileName, string StorageKey);
}
