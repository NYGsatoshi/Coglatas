using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Auth;
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
    public Task AttachmentGrantRejectsAnotherAuthorizedActorWithoutRevokingTheOriginalCredential() =>
        AssertAttachmentActorScopeAsync(AttachmentActorScope.AuthorizedOwner);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task AttachmentGrantRejectsSameTenantOtherWorkspaceActorWithoutProtectedEffects() =>
        AssertAttachmentActorScopeAsync(AttachmentActorScope.OtherWorkspaceMember);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task AttachmentGrantRejectsForeignTenantOwnerWithoutProtectedEffects() =>
        AssertAttachmentActorScopeAsync(AttachmentActorScope.ForeignTenantOwner);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC16")]
    public Task AttachmentGrantRejectsAnotherResourceCredentialForTheSameAuthorizedActor() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                var first = await TargetAsync(database);
                var second = await UploadPrivateAsync(member, database);
                Assert.True(first.FileId != second.FileId && first.AttachmentId != second.AttachmentId);
                var firstGrant = await IssueAsync(member, AttachmentIssueRoute(first), first);
                var secondGrant = await IssueAsync(member, AttachmentIssueRoute(second), second);
                Assert.True(firstGrant.FileDownloadGrantId != secondGrant.FileDownloadGrantId && firstGrant.Token != secondGrant.Token);
                await AssertScopeActorAsync(member, SecurityCiFixtureSeed.TenantAMemberUserId);
                await AssertBytesAsync(member, DownloadRoute(false, firstGrant), firstGrant.Token);
                await AssertBytesAsync(member, DownloadRoute(false, secondGrant), secondGrant.Token);
                var firstState = await ProtectedStateAsync(database, first.FileId);
                var secondState = await ProtectedStateAsync(database, second.FileId);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), first.FileName, first.StorageKey, second.FileName, second.StorageKey,
                    first.FileId.ToString("D"), first.AttachmentId.ToString("D"), second.FileId.ToString("D"), second.AttachmentId.ToString("D"),
                    firstGrant.FileDownloadGrantId.ToString("D"), secondGrant.FileDownloadGrantId.ToString("D"), firstGrant.Token, secondGrant.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };
                foreach (var (routeGrant, wrongToken) in new[] { (secondGrant, firstGrant.Token), (firstGrant, secondGrant.Token) })
                {
                    using var denied = await member.PostAsJsonAsync(DownloadRoute(false, routeGrant),
                        new FileDownloadGrantTokenRequest(wrongToken));
                    await AssertSafeDenialAsync(denied, forbidden);
                    await AssertUnchangedAsync(database, first, firstState);
                    await AssertUnchangedAsync(database, second, secondState);
                }
                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var grantIds = new[] { firstGrant.FileDownloadGrantId, secondGrant.FileDownloadGrantId };
                var entries = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId.HasValue && grantIds.Contains(row.EntityId.Value) &&
                        row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId && row.Action == "file_download.grant_use_denied")
                    .Select(row => new { row.EntityId, row.MetadataJson }).ToListAsync();
                Assert.True(entries.Count == 2 && entries.Select(row => row.EntityId).Distinct().Count() == 2,
                    "Each substituted resource credential must record exactly one denied grant use.");
                var protectedMarkers = forbidden.Where(value => !Guid.TryParse(value, out _)).ToArray();
                foreach (var entry in entries)
                {
                    Assert.NotNull(entry.MetadataJson);
                    using var metadata = JsonDocument.Parse(entry.MetadataJson);
                    Assert.True(metadata.RootElement.GetProperty("decisionReason").GetString() == "invalid_grant_token");
                    foreach (var marker in protectedMarkers)
                        Assert.True(!entry.MetadataJson.Contains(marker, StringComparison.OrdinalIgnoreCase),
                            "Resource-substitution denial audit disclosed protected content or credentials.");
                }
                await AssertBytesAsync(member, DownloadRoute(false, firstGrant), firstGrant.Token);
                await AssertBytesAsync(member, DownloadRoute(false, secondGrant), secondGrant.Token);
            });

    private static Task AssertAttachmentActorScopeAsync(AttachmentActorScope scope) =>
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
                var alpha = await TargetAsync(database);
                var target = scope == AttachmentActorScope.OtherWorkspaceMember
                    ? await UploadOtherWorkspaceAsync(owner, database) : alpha;
                var issuer = scope == AttachmentActorScope.OtherWorkspaceMember ? owner : member;
                var actor = scope switch
                {
                    AttachmentActorScope.AuthorizedOwner => owner,
                    AttachmentActorScope.OtherWorkspaceMember => member,
                    _ => beta
                };
                var actorId = scope switch
                {
                    AttachmentActorScope.AuthorizedOwner => SecurityCiFixtureSeed.TenantAOwnerUserId,
                    AttachmentActorScope.OtherWorkspaceMember => SecurityCiFixtureSeed.TenantAMemberUserId,
                    _ => SecurityCiFixtureSeed.TenantBOwnerUserId
                };
                var control = scope == AttachmentActorScope.ForeignTenantOwner
                    ? await BetaTargetAsync(database) : alpha;
                var controlBytes = scope == AttachmentActorScope.ForeignTenantOwner
                    ? "SEC02_BETA_FILE_CANARY_DO_NOT_LEAK\n"u8.ToArray() : SyntheticBytes;
                var retained = await IssueAsync(issuer, AttachmentIssueRoute(target), target);
                var current = await IssueAsync(actor, AttachmentIssueRoute(control), control);
                await AssertScopeArrangementAsync(database, target, control, retained, actorId, scope);
                await AssertScopeActorAsync(actor, actorId);
                await AssertBytesAsync(issuer, DownloadRoute(false, retained), retained.Token);
                await AssertScopeBytesAsync(actor, DownloadRoute(false, current), current.Token, controlBytes);
                var targetState = await ProtectedStateAsync(database, target.FileId);
                var controlState = await ProtectedStateAsync(database, control.FileId);
                var forbidden = new[]
                {
                    Encoding.UTF8.GetString(SyntheticBytes).TrimEnd(), Encoding.UTF8.GetString(controlBytes).TrimEnd(),
                    target.FileName, target.StorageKey, control.FileName, control.StorageKey,
                    target.FileId.ToString("D"), target.AttachmentId.ToString("D"),
                    retained.FileDownloadGrantId.ToString("D"), retained.Token, current.Token,
                    "storageKey", "storage_key", "Npgsql", "stackTrace", "connection string"
                };

                // A separately authenticated actor has its own CSRF cookie/header.
                // Its 403 control must not substitute for the authorized-request
                // 400 boundary exercised with that header restored below.
                var csrf = actor.DefaultRequestHeaders.GetValues(SecurityOptions.CsrfHeaderName).Single();
                actor.DefaultRequestHeaders.Remove(SecurityOptions.CsrfHeaderName);
                try
                {
                    using var missingCsrf = await actor.PostAsJsonAsync(DownloadRoute(false, retained),
                        new FileDownloadGrantTokenRequest(retained.Token));
                    Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
                    await AssertSafeEnvelopeAsync(missingCsrf, forbidden);
                }
                finally { actor.DefaultRequestHeaders.Add(SecurityOptions.CsrfHeaderName, csrf); }
                await AssertUnchangedAsync(database, target, targetState);
                await AssertUnchangedAsync(database, control, controlState);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var denied = await actor.PostAsJsonAsync(DownloadRoute(false, retained),
                        new FileDownloadGrantTokenRequest(retained.Token));
                    await AssertSafeDenialAsync(denied, forbidden);
                    await AssertUnchangedAsync(database, target, targetState);
                    await AssertUnchangedAsync(database, control, controlState);
                }
                if (scope == AttachmentActorScope.ForeignTenantOwner)
                {
                    // A tenant-filtered missing lookup returns before any target
                    // grant-use audit; no foreign target identity is observable.
                    await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                    Assert.True(!await db.AuditLogs.AnyAsync(row => row.EntityId == retained.FileDownloadGrantId &&
                        row.ActorUserId == actorId && row.Action == "file_download.grant_use_denied"));
                }
                else
                    await AssertScopeAuditAsync(database, retained.FileDownloadGrantId, actorId, forbidden);
                if (scope != AttachmentActorScope.AuthorizedOwner)
                {
                    using (var denied = await actor.PostAsJsonAsync(AttachmentIssueRoute(target), new FileDownloadGrantRequest()))
                    {
                        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                        await AssertSafeEnvelopeAsync(denied, forbidden);
                        using var envelope = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("code").GetString() ==
                            "FILE_DOWNLOAD_GRANT_NOT_FOUND");
                    }
                    await AssertUnchangedAsync(database, target, targetState);
                    await AssertUnchangedAsync(database, control, controlState);
                    using (var denied = await actor.GetAsync($"/api/attachments/{target.AttachmentId:D}/download"))
                        await AssertSafeDenialAsync(denied, forbidden);
                    await AssertUnchangedAsync(database, target, targetState);
                    await AssertUnchangedAsync(database, control, controlState);
                }
                await AssertScopeActorAsync(actor, actorId);
                // Failed transfer never consumes/revokes this reusable credential.
                await AssertBytesAsync(issuer, DownloadRoute(false, retained), retained.Token);
                await AssertScopeBytesAsync(actor, DownloadRoute(false, current), current.Token, controlBytes);
                if (scope == AttachmentActorScope.AuthorizedOwner)
                {
                    var fresh = await IssueAsync(owner, AttachmentIssueRoute(target), target);
                    await AssertBytesAsync(owner, DownloadRoute(false, fresh), fresh.Token);
                    await AssertScopeBytesAsync(owner, $"/api/attachments/{target.AttachmentId:D}/download", null, SyntheticBytes);
                }
            });

    private static string AttachmentIssueRoute(Target target) => $"/api/attachments/{target.AttachmentId:D}/download-grants";

    private static async Task AssertScopeActorAsync(HttpClient client, Guid actorId)
    {
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actor = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(actor);
        Assert.True(actor.UserId == actorId, "The denied request must use the independently authenticated current actor.");
    }

    private static async Task AssertScopeBytesAsync(HttpClient client, string route, string? token, byte[] expected)
    {
        using var response = token is null ? await client.GetAsync(route)
            : await client.PostAsJsonAsync(route, new FileDownloadGrantTokenRequest(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(expected),
            "The current actor must retain byte-identical access to its authorized physical File.");
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    private static async Task AssertScopeArrangementAsync(string database, Target target, Target control,
        FileDownloadGrantResponse retained, Guid actorId, AttachmentActorScope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var grant = await db.FileDownloadGrants.AsNoTracking().SingleAsync(row => row.Id == retained.FileDownloadGrantId);
        var file = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == target.FileId);
        var ownFile = await db.FileObjects.AsNoTracking().SingleAsync(row => row.Id == control.FileId);
        Assert.True(grant.ActorUserId != actorId && grant.FileObjectId == target.FileId && grant.AttachmentId == target.AttachmentId);
        Assert.True(await db.WorkspaceMembers.AnyAsync(row => row.TenantId == ownFile.TenantId &&
            row.WorkspaceId == ownFile.WorkspaceId && row.UserId == actorId && row.Status == MembershipStatus.Active));
        if (scope == AttachmentActorScope.AuthorizedOwner)
            Assert.True(file.Id == ownFile.Id && file.TenantId == ownFile.TenantId && file.WorkspaceId == ownFile.WorkspaceId);
        else if (scope == AttachmentActorScope.OtherWorkspaceMember)
        {
            Assert.True(file.TenantId == ownFile.TenantId && file.WorkspaceId != ownFile.WorkspaceId);
            Assert.True(!await db.WorkspaceMembers.AnyAsync(row => row.WorkspaceId == file.WorkspaceId &&
                row.UserId == actorId && row.Status == MembershipStatus.Active));
        }
        else
            Assert.True(file.TenantId != ownFile.TenantId && file.WorkspaceId != ownFile.WorkspaceId);
    }

    private static async Task AssertScopeAuditAsync(string database, Guid grantId, Guid actorId, IEnumerable<string> forbidden)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var entries = await db.AuditLogs.AsNoTracking().Where(row => row.EntityId == grantId && row.ActorUserId == actorId &&
            row.Action == "file_download.grant_use_denied").Select(row => row.MetadataJson).ToListAsync();
        Assert.True(entries.Count == 2, "Both wrong-actor requests must record the current actor-binding denial.");
        var protectedMarkers = forbidden.Where(value => !Guid.TryParse(value, out _)).ToArray();
        foreach (var entry in entries)
        {
            Assert.NotNull(entry);
            using var metadata = JsonDocument.Parse(entry);
            Assert.True(metadata.RootElement.GetProperty("decisionReason").GetString() == "actor_mismatch");
            Assert.True(metadata.RootElement.GetProperty("actorUserId").GetGuid() == actorId);
            foreach (var marker in protectedMarkers)
                Assert.True(!entry.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    "Actor/scope denial audit disclosed protected content or credentials.");
        }
    }

    private static async Task<Target> BetaTargetAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.Attachments.AsNoTracking().Where(row => row.FileObject!.OriginalFileName == "sec02-beta-private.txt")
            .Select(row => new Target(row.FileObjectId, row.Id, row.FileObject!.OriginalFileName, row.FileObject.StorageKey)).SingleAsync();
    }

    private static async Task<Target> UploadOtherWorkspaceAsync(HttpClient owner, string database)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new CreateWorkspaceRequest("SEC16 owner-only Workspace", null, null));
        using var created = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var envelope = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var workspaceId = envelope.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "ownerType");
        form.Add(new StringContent(workspaceId.ToString("D")), "ownerId");
        using var content = new ByteArrayContent(SyntheticBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", "sec16-other-workspace.txt");
        using var uploaded = await owner.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var attachment = await uploaded.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.NotNull(attachment);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.FileObjects.AsNoTracking().Where(row => row.Id == attachment.FileObjectId)
            .Select(row => new Target(row.Id, attachment.Id, row.OriginalFileName, row.StorageKey)).SingleAsync();
    }

    private enum AttachmentActorScope { AuthorizedOwner, OtherWorkspaceMember, ForeignTenantOwner }
}
