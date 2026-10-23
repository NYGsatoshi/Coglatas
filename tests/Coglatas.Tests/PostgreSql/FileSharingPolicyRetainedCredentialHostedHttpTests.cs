using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Files;
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
    public Task FileObjectPolicyPutInvalidatesRetainedMemberAndOwnerCredentialsAcrossRestoration() =>
        AssertPolicyPutCredentialsAsync(byFileObject: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task AttachmentPolicyPutInvalidatesRetainedMemberAndOwnerCredentialsAcrossRestoration() =>
        AssertPolicyPutCredentialsAsync(byFileObject: false);

    private static Task AssertPolicyPutCredentialsAsync(bool byFileObject) =>
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
                var target = await UploadPrivateAsync(owner, database);
                var sharingRoute = $"/api/files/{target.FileId:D}/sharing";
                var issueRoute = byFileObject
                    ? $"/api/files/{target.FileId:D}/download-grants"
                    : $"/api/attachments/{target.AttachmentId:D}/download-grants";
                var directRoute = byFileObject
                    ? $"/api/files/{target.FileId:D}/download"
                    : $"/api/attachments/{target.AttachmentId:D}/download";
                var initial = await SharingAsync(owner, sharingRoute);
                await AssertPolicyStateAsync(database, target, initial, "Private", initial.SharingVersion);
                var shared = await PutPolicyAsync(owner, sharingRoute, true, initial.SharingVersion);
                await AssertPolicyStateAsync(database, target, shared, "Workspace", initial.SharingVersion + 1);
                var retainedMember = await IssueAsync(member, issueRoute, target);
                var retainedOwner = await IssueAsync(owner, issueRoute, target);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, retainedMember), retainedMember.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, retainedOwner), retainedOwner.Token);
                var forbidden = new List<string>
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"),
                    retainedMember.FileDownloadGrantId.ToString("D"), retainedOwner.FileDownloadGrantId.ToString("D"),
                    retainedMember.Token, retainedOwner.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                var beforeCsrf = await ProtectedStateAsync(database, target.FileId);
                var csrf = owner.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName).Single();
                owner.DefaultRequestHeaders.Remove(SecurityOptions.CsrfHeaderName);
                try
                {
                    using var denied = await owner.PutAsJsonAsync(sharingRoute,
                        new FileSharingPolicyUpdateRequest(false, shared.SharingVersion));
                    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                    await AssertSafeEnvelopeAsync(denied, forbidden);
                }
                finally { owner.DefaultRequestHeaders.Add(SecurityOptions.CsrfHeaderName, csrf); }
                await AssertUnchangedAsync(database, target, beforeCsrf);

                var privatePolicy = await PutPolicyAsync(owner, sharingRoute, false, shared.SharingVersion);
                await AssertPolicyStateAsync(database, target, privatePolicy, "Private", shared.SharingVersion + 1);
                var privateState = await ProtectedStateAsync(database, target.FileId);
                using (var denied = await member.PostAsJsonAsync(DownloadRoute(byFileObject, retainedMember),
                    new FileDownloadGrantTokenRequest(retainedMember.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, privateState);
                using (var denied = await owner.PostAsJsonAsync(DownloadRoute(byFileObject, retainedOwner),
                    new FileDownloadGrantTokenRequest(retainedOwner.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, privateState);
                using (var denied = await member.PostAsJsonAsync(issueRoute, new FileDownloadGrantRequest()))
                {
                    if (byFileObject)
                    {
                        await AssertSafeDenialAsync(denied, forbidden);
                    }
                    else
                    {
                        // The Attachment issuer masks an unavailable resource;
                        // its consumption and direct-read routes still use 400.
                        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                        await AssertSafeEnvelopeAsync(denied, forbidden);
                        using var envelope = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("code").GetString() ==
                            "FILE_DOWNLOAD_GRANT_NOT_FOUND");
                    }
                }
                await AssertUnchangedAsync(database, target, privateState);
                using (var denied = await member.GetAsync(directRoute))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, privateState);
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                var currentOwner = await IssueAsync(owner, issueRoute, target);
                forbidden.Add(currentOwner.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, currentOwner), currentOwner.Token);

                var restored = await PutPolicyAsync(owner, sharingRoute, true, privatePolicy.SharingVersion);
                await AssertPolicyStateAsync(database, target, restored, "Workspace", privatePolicy.SharingVersion + 1);
                var freshMember = await IssueAsync(member, issueRoute, target);
                var freshOwner = await IssueAsync(owner, issueRoute, target);
                Assert.True(freshMember.FileDownloadGrantId != retainedMember.FileDownloadGrantId && freshMember.Token != retainedMember.Token);
                Assert.True(freshOwner.FileDownloadGrantId != retainedOwner.FileDownloadGrantId && freshOwner.Token != retainedOwner.Token);
                forbidden.Add(freshMember.Token);
                forbidden.Add(freshOwner.Token);
                var restoredState = await ProtectedStateAsync(database, target.FileId);
                using (var denied = await member.PostAsJsonAsync(DownloadRoute(byFileObject, retainedMember),
                    new FileDownloadGrantTokenRequest(retainedMember.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, restoredState);
                using (var denied = await owner.PostAsJsonAsync(DownloadRoute(byFileObject, retainedOwner),
                    new FileDownloadGrantTokenRequest(retainedOwner.Token)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, restoredState);
                await AssertSharingAuditAsync(database, retainedMember.FileDownloadGrantId, forbidden);
                await AssertPolicyOwnerAuditAsync(database, target, retainedOwner.FileDownloadGrantId, forbidden);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, freshMember), freshMember.Token);
                await AssertBytesAsync(member, DownloadRoute(byFileObject, freshMember), freshMember.Token);
                await AssertBytesAsync(owner, DownloadRoute(byFileObject, freshOwner), freshOwner.Token);
            });

    private static async Task<FileSharingResponse> PutPolicyAsync(HttpClient owner, string route, bool workspace, long version)
    {
        using var response = await owner.PutAsJsonAsync(route, new FileSharingPolicyUpdateRequest(workspace, version));
        return await ReadSharingAsync(response);
    }

    private static async Task AssertPolicyStateAsync(string database, Target target, FileSharingResponse sharing, string policy, long version)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var file = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == target.FileId);
        Assert.True(sharing.SharingPolicy == policy && sharing.SharingVersion == version && sharing.Recipients.Count == 0);
        Assert.True(file.SharingPolicy.ToString() == policy && file.SharingVersion == version);
        Assert.False(await db.FileAccessGrants.AnyAsync(row => row.FileObjectId == target.FileId),
            "An explicit recipient must not mask a sharing-policy transition.");
    }

    private static async Task AssertPolicyOwnerAuditAsync(string database, Target target, Guid grantId, IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var denied = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == grantId &&
                row.Action == "file_download.grant_use_denied").Select(row => row.MetadataJson).ToListAsync();
        Assert.True(denied.Count == 2, "Both old owner-stamp uses must record a policy denial.");
        var changed = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == target.FileId &&
                row.Action == "FileSharingChanged").Select(row => row.MetadataJson).ToListAsync();
        Assert.True(changed.Count == 3, "Only the three authorized policy changes may record sharing mutations.");
        var protectedMarkers = forbidden.Where(value => !Guid.TryParse(value, out _)).ToArray();
        foreach (var (entry, isDenial) in denied.Select(value => (value, true)).Concat(changed.Select(value => (value, false))))
        {
            Assert.NotNull(entry);
            using var metadata = JsonDocument.Parse(entry);
            Assert.True(isDenial
                ? metadata.RootElement.GetProperty("decisionReason").GetString() == "policy_changed"
                : metadata.RootElement.GetProperty("change").GetString() == "policyChanged");
            foreach (var marker in protectedMarkers)
                Assert.True(!entry.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    "Policy transition audit disclosed protected content or credentials.");
        }
    }
}
