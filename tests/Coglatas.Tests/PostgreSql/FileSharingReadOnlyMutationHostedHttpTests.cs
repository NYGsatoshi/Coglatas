using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
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
    public Task SharingMutationsReauthorizeDowngradedManagerWhileIntakeClosurePreservesCurrentManagerContract() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                var session = await ReadOnlyActorSessionAsync(database);
                var target = await UploadPrivateAsync(member, database);
                var workspace = await AssertReadOnlyTargetAsync(database, target);
                var route = $"/api/files/{target.FileId:D}/sharing";
                await SetReadOnlyActorRoleAsync(owner, database, workspace, WorkspaceRole.Admin);
                await AssertReadOnlySessionAsync(member, database, session);
                var initial = await SharingAsync(member, route);
                var shared = await PutPolicyAsync(member, route, true, initial.SharingVersion);
                var privatePolicy = await PutPolicyAsync(member, route, false, shared.SharingVersion);
                var self = await GrantSharingActorAsync(member, route, SecurityCiFixtureSeed.TenantAMemberUserId,
                    privatePolicy.SharingVersion);
                var selfGrant = Assert.Single(self.Recipients).GrantId;
                var revoked = await RevokeSharingActorAsync(member, route, selfGrant, self.SharingVersion);
                Assert.Empty(revoked.Recipients);
                var granted = await GrantSharingActorAsync(member, route, SecurityCiFixtureSeed.TenantAOwnerUserId,
                    revoked.SharingVersion);
                var recipient = Assert.Single(granted.Recipients).GrantId;
                var retainedMember = await IssueAsync(member, $"/api/files/{target.FileId:D}/download-grants", target);
                var retainedOwner = await IssueAsync(owner, AttachmentIssueRoute(target), target);
                await AssertBytesAsync(member, DownloadRoute(true, retainedMember), retainedMember.Token);
                await AssertBytesAsync(owner, DownloadRoute(false, retainedOwner), retainedOwner.Token);

                // FileSharing is the existing upload admission flag. Closing it
                // does not define a blanket read-only policy for other routes.
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var tenant = await db.FileObjects.Where(row => row.Id == target.FileId).Select(row => row.TenantId).SingleAsync();
                    var settings = await db.Set<TenantSettings>().SingleOrDefaultAsync(row => row.TenantId == tenant);
                    if (settings is null) db.Add(new TenantSettings { TenantId = tenant, FeatureFlagsJson = "{\"FileSharing\":false}" });
                    else settings.FeatureFlagsJson = "{\"FileSharing\":false}";
                    await db.SaveChangesAsync();
                }
                await SetReadOnlyActorRoleAsync(owner, database, workspace, WorkspaceRole.ReadOnly);
                await AssertReadOnlySessionAsync(member, database, session);
                using (var read = await member.GetAsync(route))
                {
                    Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                    var projection = await read.Content.ReadFromJsonAsync<FileSharingResponse>();
                    Assert.NotNull(projection);
                    var managerDetailsHidden = projection is
                    {
                        CanManageSharing: false,
                        CanInspectSharing: false,
                        Recipients.Count: 0,
                        AvailableRecipients.Count: 0,
                        ExternalRecipientCount: null
                    };
                    var sharingVersionUnchanged = projection.SharingVersion == granted.SharingVersion;
                    Assert.True(managerDetailsHidden && sharingVersionUnchanged,
                        "Current uploader reads must not disclose manager-only recipient metadata after downgrade.");
                }
                await AssertBytesAsync(member, DownloadRoute(true, retainedMember), retainedMember.Token);
                await AssertBytesAsync(owner, DownloadRoute(false, retainedOwner), retainedOwner.Token);
                var before = await ProtectedStateAsync(database, target.FileId);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), target.FileName, target.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"), recipient.ToString("D"),
                    retainedMember.Token, retainedOwner.Token, "storageKey", "storage_key", "Npgsql", "stackTrace"
                };
                using (var denied = await member.PutAsJsonAsync(route,
                    new FileSharingPolicyUpdateRequest(true, granted.SharingVersion)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, before);
                using (var denied = await member.PostAsJsonAsync(route + "/recipients",
                    new FileShareGrantCreateRequest(SecurityCiFixtureSeed.TenantAMemberUserId, granted.SharingVersion)))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, before);
                using (var denied = await member.DeleteAsync(route + $"/recipients/{recipient:D}?expectedSharingVersion={granted.SharingVersion}"))
                    await AssertSafeDenialAsync(denied, forbidden);
                await AssertUnchangedAsync(database, target, before);
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    Assert.True(await db.AuditLogs.CountAsync(row => row.EntityId == target.FileId && row.Action == "FileSharingChanged") == 5,
                        "Only the five actual authorized setup mutations may have a success audit after denied commands.");
                    Assert.True(await db.FileAccessGrants.AnyAsync(row => row.Id == recipient && row.RevokedAt == null));
                }
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertBytesAsync(member, DownloadRoute(true, retainedMember), retainedMember.Token);
                await AssertBytesAsync(owner, DownloadRoute(false, retainedOwner), retainedOwner.Token);

                // Restore current management through HTTP. The same three
                // routes still work with upload admission disabled; no policy
                // change or UI flag may be inferred from intake closure alone.
                await SetReadOnlyActorRoleAsync(owner, database, workspace, WorkspaceRole.Admin);
                await AssertReadOnlySessionAsync(member, database, session);
                await AssertSharingIntakeClosedAsync(member, database, workspace, forbidden);
                var restored = await PutPolicyAsync(member, route, true, granted.SharingVersion);
                var current = await GrantSharingActorAsync(member, route, SecurityCiFixtureSeed.TenantAMemberUserId,
                    restored.SharingVersion);
                var final = await RevokeSharingActorAsync(member, route, recipient, current.SharingVersion);
                Assert.True(final.SharingVersion == granted.SharingVersion + 3 && final.Recipients.Count == 1 &&
                    final.Recipients[0].GrantId != recipient);
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var file = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == target.FileId);
                    Assert.True(file.SharingPolicy == FileSharingPolicy.Workspace && file.SharingVersion == final.SharingVersion);
                    Assert.True(await db.FileAccessGrants.AnyAsync(row => row.Id == recipient && row.RevokedAt.HasValue));
                    Assert.True(await db.AuditLogs.CountAsync(row => row.EntityId == target.FileId && row.Action == "FileSharingChanged") == 8);
                }
                var fresh = await IssueAsync(member, $"/api/files/{target.FileId:D}/download-grants", target);
                await AssertBytesAsync(member, DownloadRoute(true, fresh), fresh.Token);
                await AssertReadOnlyAuditPrivacyAsync(database, target, forbidden.Append(fresh.Token));
            });

    private static async Task AssertSharingIntakeClosedAsync(HttpClient manager, string database, Guid workspace,
        IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var files = await db.FileObjects.CountAsync();
        var attachments = await db.Attachments.CountAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "ownerType");
        form.Add(new StringContent(workspace.ToString("D")), "ownerId");
        using var content = new ByteArrayContent(SyntheticBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", "sec16-closed-intake.txt");
        using var denied = await manager.PostAsync("/api/attachments", form);
        await AssertSafeDenialAsync(denied, forbidden);
        using var envelope = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.True(envelope.RootElement.GetProperty("error").GetString() ==
            "Feature 'FileSharing' is disabled for this tenant.",
            "The otherwise valid Admin upload must fail at the actual persisted intake feature boundary.");
        Assert.True(await db.FileObjects.CountAsync() == files && await db.Attachments.CountAsync() == attachments,
            "An otherwise authorized manager's closed-intake request must not create File or Attachment rows.");
    }

    private static async Task<FileSharingResponse> GrantSharingActorAsync(HttpClient actor, string route, Guid recipient, long version)
    {
        using var response = await actor.PostAsJsonAsync(route + "/recipients", new FileShareGrantCreateRequest(recipient, version));
        var sharing = await ReadSharingAsync(response);
        Assert.True(sharing.SharingVersion == version + 1);
        return sharing;
    }

    private static async Task<FileSharingResponse> RevokeSharingActorAsync(HttpClient actor, string route, Guid recipient, long version)
    {
        using var response = await actor.DeleteAsync(route + $"/recipients/{recipient:D}?expectedSharingVersion={version}");
        var sharing = await ReadSharingAsync(response);
        Assert.True(sharing.SharingVersion == version + 1);
        return sharing;
    }
}
