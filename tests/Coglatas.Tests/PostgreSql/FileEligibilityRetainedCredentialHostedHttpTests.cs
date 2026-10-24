using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Files;
using Coglatas.Domain.Enums;
using Coglatas.Tests.SecurityArchitecture;
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class FileDownloadGrantLifecycleHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task PersistedWorkspaceFileEligibilityReauthorizesRetainedUploaderAndOwnerCredentials() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                var workspace = await HistoryWorkspaceAsync(database, SecurityCiFixtureSeed.TenantASlug);
                var uploaded = await UploadHistoryFileAsync(member, database, workspace,
                    "eligibility-target.txt", SyntheticBytes);
                var target = uploaded.Target;
                await AssertReadOnlyTargetAsync(database, target);
                await AssertEligibilityStatusAsync(database, target, FileObjectStatus.Active);
                var session = await ReadOnlyActorSessionAsync(database);
                var other = await UploadPrivateAsync(owner, database);
                Assert.True(target.FileId != other.FileId && target.AttachmentId != other.AttachmentId);
                var otherGrant = await IssueAsync(owner, $"/api/files/{other.FileId:D}/download-grants", other);
                var credentials = new List<(HttpClient Actor, Guid ActorId, bool ByFileObject, FileDownloadGrantResponse Grant)>();
                foreach (var actor in new[]
                {
                    (Client: member, Id: SecurityCiFixtureSeed.TenantAMemberUserId),
                    (Client: owner, Id: SecurityCiFixtureSeed.TenantAOwnerUserId)
                })
                {
                    await AssertScopeActorAsync(actor.Client, actor.Id);
                    Assert.True(actor.Client.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName)
                        .Single().Length > 0, "Grant operations must retain their valid CSRF credential.");
                    foreach (var byFileObject in new[] { true, false })
                    {
                        var grant = await IssueAsync(actor.Client, EligibilityIssueRoute(byFileObject, target), target);
                        credentials.Add((actor.Client, actor.Id, byFileObject, grant));
                        await AssertBytesAsync(actor.Client, DownloadRoute(byFileObject, grant), grant.Token);
                    }
                }
                await AssertBytesAsync(owner, DownloadRoute(true, otherGrant), otherGrant.Token);
                var forbidden = new List<string>
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"), otherGrant.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                foreach (var credential in credentials)
                {
                    forbidden.Add(credential.Grant.Token);
                    forbidden.Add(credential.Grant.FileDownloadGrantId.ToString("D"));
                }
                foreach (var status in new[] { FileObjectStatus.Archived, FileObjectStatus.Quarantined })
                {
                    // Fixture persistence exercises current readers, not a new status/scanner writer.
                    await SetEligibilityStatusAsync(database, target, status);
                    var before = await ProtectedStateAsync(database, target.FileId);
                    var otherBefore = await ProtectedStateAsync(database, other.FileId);
                    foreach (var credential in credentials)
                    {
                        using var denied = await credential.Actor.PostAsJsonAsync(
                            DownloadRoute(credential.ByFileObject, credential.Grant),
                            new FileDownloadGrantTokenRequest(credential.Grant.Token));
                        await AssertEligibilityDenialAsync(denied, credential.ByFileObject, false, forbidden);
                        await AssertUnchangedAsync(database, target, before);
                    }
                    foreach (var byFileObject in new[] { true, false })
                    {
                        using (var denied = await member.PostAsJsonAsync(EligibilityIssueRoute(byFileObject, target),
                            new FileDownloadGrantRequest()))
                            await AssertEligibilityDenialAsync(denied, byFileObject, true, forbidden);
                        await AssertUnchangedAsync(database, target, before);
                        var direct = byFileObject ? $"/api/files/{target.FileId:D}/download"
                            : $"/api/attachments/{target.AttachmentId:D}/download";
                        using (var denied = await member.GetAsync(direct))
                            await AssertEligibilityDenialAsync(denied, byFileObject, false, forbidden);
                        await AssertUnchangedAsync(database, target, before);
                    }
                    await AssertReadOnlySessionAsync(member, database, session);
                    await AssertScopeActorAsync(owner, SecurityCiFixtureSeed.TenantAOwnerUserId);
                    await AssertUnchangedAsync(database, other, otherBefore);
                    await AssertBytesAsync(owner, DownloadRoute(true, otherGrant), otherGrant.Token);
                    await AssertUnchangedAsync(database, target, before);
                    await AssertEligibilityStatusAsync(database, target, status);
                    await SetEligibilityStatusAsync(database, target, FileObjectStatus.Active);
                    // Exact restoration reproduces the current stamp; status alone is not permanent revocation.
                    foreach (var credential in credentials)
                    {
                        await AssertBytesAsync(credential.Actor,
                            DownloadRoute(credential.ByFileObject, credential.Grant), credential.Grant.Token);
                        var fresh = await IssueAsync(credential.Actor,
                            EligibilityIssueRoute(credential.ByFileObject, target), target);
                        Assert.True(fresh.FileDownloadGrantId != credential.Grant.FileDownloadGrantId &&
                            fresh.Token != credential.Grant.Token);
                        forbidden.Add(fresh.Token);
                        await AssertBytesAsync(credential.Actor, DownloadRoute(credential.ByFileObject, fresh), fresh.Token);
                    }
                }
                foreach (var credential in credentials)
                    await AssertEligibilityAuditAsync(database, credential.Grant.FileDownloadGrantId,
                        credential.ActorId, forbidden);
            });

    private static string EligibilityIssueRoute(bool byFileObject, Target target) => byFileObject
        ? $"/api/files/{target.FileId:D}/download-grants"
        : $"/api/attachments/{target.AttachmentId:D}/download-grants";

    private static async Task SetEligibilityStatusAsync(string database, Target target, FileObjectStatus status)
    {
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            var file = await db.FileObjects.SingleAsync(row => row.Id == target.FileId);
            file.Status = status;
            await db.SaveChangesAsync();
        }
        await AssertEligibilityStatusAsync(database, target, status);
    }

    private static async Task AssertEligibilityStatusAsync(string database, Target target, FileObjectStatus status)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var attachment = await db.Attachments.AsNoTracking().Include(row => row.FileObject)
            .SingleAsync(row => row.Id == target.AttachmentId);
        Assert.True(attachment.OwnerType == AttachmentOwnerType.Workspace &&
            attachment.OwnerId == attachment.WorkspaceId && attachment.ScanStatus == FileScanStatus.Clean &&
            attachment.DeletedAt == null && attachment.FileObject is not null &&
            attachment.FileObject.Id == target.FileId && attachment.FileObject.Status == status &&
            attachment.FileObject.DeletedAt == null,
            "Only the persisted ordinary Workspace File eligibility status may change.");
    }

    private static async Task AssertEligibilityDenialAsync(
        HttpResponseMessage response, bool byFileObject, bool issuing, IEnumerable<string> forbidden)
    {
        Assert.Equal(!byFileObject && issuing ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSafeEnvelopeAsync(response, forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = body.RootElement.GetProperty("error");
        if (!byFileObject && !issuing)
        {
            Assert.True(error.ValueKind == JsonValueKind.String && error.GetString() == "Attachment not found.",
                "Attachment consumption must retain its current masked failure shape.");
            return;
        }
        Assert.True(error.GetProperty("code").GetString() == (byFileObject
                ? issuing ? "FileGrantFailed" : "FileDownloadFailed" : "FILE_DOWNLOAD_GRANT_NOT_FOUND") &&
            error.GetProperty("message").GetString() == (byFileObject
                ? "The request could not be completed." : "Attachment not found.") &&
            error.GetProperty("redactionApplied").GetBoolean() &&
            error.GetProperty("target").ValueKind == JsonValueKind.Null &&
            error.GetProperty("details").GetArrayLength() == 0,
            "Eligibility denial must retain the exact current route-specific safe envelope.");
    }

    private static async Task AssertEligibilityAuditAsync(
        string database, Guid grantId, Guid actorId, IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var entries = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == grantId &&
                row.Action == "file_download.grant_use_denied")
            .Select(row => new { row.ActorUserId, row.MetadataJson, row.Summary }).ToListAsync();
        Assert.True(entries.Count == 2, "Each retained credential must record both persisted eligibility denials.");
        var reasons = new List<string?>();
        string[] markers = [.. forbidden.Where(value => !Guid.TryParse(value, out _))];
        foreach (var entry in entries)
        {
            Assert.True(entry.ActorUserId == actorId && entry.MetadataJson is not null);
            using var metadata = JsonDocument.Parse(entry.MetadataJson!);
            reasons.Add(metadata.RootElement.GetProperty("decisionReason").GetString());
            Assert.True(metadata.RootElement.GetProperty("decision").GetString() == "deny" &&
                metadata.RootElement.GetProperty("actorUserId").GetGuid() == actorId &&
                metadata.RootElement.GetProperty("grantId").GetGuid() == grantId);
            foreach (var marker in markers)
                Assert.True(!entry.MetadataJson!.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
                    !(entry.Summary?.Contains(marker, StringComparison.OrdinalIgnoreCase) ?? false),
                    "Eligibility audit disclosed protected content or credentials.");
        }
        Assert.True(reasons.Count(reason => reason == "target_archived") == 1 &&
            reasons.Count(reason => reason == "target_quarantined") == 1);
    }
}
