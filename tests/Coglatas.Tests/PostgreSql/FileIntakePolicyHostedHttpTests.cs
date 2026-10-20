using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.TenantAdministration;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Files;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed class FileIntakePolicyHostedHttpTests
{
    private static readonly byte[] SyntheticBytes = Encoding.UTF8.GetBytes("Prevalidated synthetic intake-policy sample.\n");

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PersistedDisabledFeatureDeniesEveryMultipartRouteWithoutEffectsAndPreservesExistingAuthorizedBytes() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                var artifactId = await PrepareAsync(host);
                await host.LoginAsync();
                using var admin = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = host.Client.BaseAddress };
                admin.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
                using (var login = await AdmissionHost.PostAsync(admin, "/api/auth/login", new Coglatas.Application.Auth.LoginRequest(host.Recipient.Email, AdmissionHost.Password)))
                    Assert.Equal(HttpStatusCode.OK, login.StatusCode);

                // Real positive controls make every subsequent negative about the
                // feature, not an invalid multipart body or an unauthorized owner.
                var routes = new[] { "/api/files", "/api/attachments", $"/api/artifacts/{artifactId}/versions" };
                var attachments = new List<AttachmentResponse>();
                ArtifactVersionResponse? version = null;
                foreach (var route in routes)
                {
                    using var upload = await UploadAsync(host, route, includeCsrf: true);
                    Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
                    if (route.Contains("/versions", StringComparison.Ordinal))
                        version = await upload.Content.ReadFromJsonAsync<ArtifactVersionResponse>();
                    else
                        attachments.Add((await upload.Content.ReadFromJsonAsync<AttachmentResponse>())!);
                }
                Assert.NotNull(version);
                Assert.Equal(2, attachments.Count);
                var enabled = (await host.Client.GetFromJsonAsync<TenantFeaturesResponse>("/api/tenant/features"))!;
                Assert.Equal(host.Tenant.Id, enabled.TenantId);
                Assert.Contains(FeatureKeys.FileSharing, enabled.EnabledFeatures);

                // The existing Tenant Admin endpoint is the only policy writer in
                // this scenario. No fake IFeatureFlagService is registered.
                using (var saved = await PatchSettingsAsync(admin, "{\"FileSharing\":false,\"Calendar\":true}"))
                {
                    Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
                    var settings = (await saved.Content.ReadFromJsonAsync<TenantSettingsResponse>())!;
                    Assert.Equal(host.Tenant.Id, settings.TenantId);
                    using var flags = JsonDocument.Parse(settings.FeatureFlagsJson);
                    Assert.False(flags.RootElement.GetProperty(FeatureKeys.FileSharing).GetBoolean());
                }
                var closed = (await host.Client.GetFromJsonAsync<TenantFeaturesResponse>("/api/tenant/features"))!;
                Assert.DoesNotContain(FeatureKeys.FileSharing, closed.EnabledFeatures);
                Assert.Contains(FeatureKeys.Calendar, closed.EnabledFeatures);
                var before = await StateAsync(host);
                foreach (var route in routes)
                {
                    using (var csrfDenied = await UploadAsync(host, route, includeCsrf: false))
                        Assert.Equal(HttpStatusCode.Forbidden, csrfDenied.StatusCode);
                    using var denied = await UploadAsync(host, route, includeCsrf: true);
                    Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                    var error = await denied.Content.ReadAsStringAsync();
                    if (route == "/api/files")
                    {
                        using var envelope = JsonDocument.Parse(error);
                        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("redactionApplied").GetBoolean());
                        Assert.DoesNotContain("Feature 'FileSharing'", error, StringComparison.Ordinal);
                    }
                    else
                    {
                        Assert.Contains("FileSharing", error, StringComparison.Ordinal);
                        Assert.Contains("disabled", error, StringComparison.OrdinalIgnoreCase);
                    }
                    Assert.DoesNotContain(Encoding.UTF8.GetString(SyntheticBytes), error, StringComparison.Ordinal);
                    Assert.Equal(before, await StateAsync(host));
                }

                // Workspace Admin is a real management role, but the actor stays
                // an ordinary Tenant Member and cannot change Tenant settings.
                foreach (var role in new[] { WorkspaceRole.Member, WorkspaceRole.Admin })
                {
                    await WithDbAsync(host, async db =>
                    {
                        var membership = await db.WorkspaceMembers.SingleAsync(row => row.WorkspaceId == host.Workspace.Id && row.UserId == host.Actor.Id);
                        membership.Role = role;
                        Assert.Equal(TenantUserRole.Member, (await db.TenantUsers.SingleAsync(row => row.UserId == host.Actor.Id)).Role);
                        await db.SaveChangesAsync();
                    });
                    using var denied = await PatchSettingsAsync(host.Client, "{\"FileSharing\":true,\"Calendar\":true}");
                    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                    Assert.Equal(before, await StateAsync(host));
                    Assert.DoesNotContain(FeatureKeys.FileSharing, (await host.Client.GetFromJsonAsync<TenantFeaturesResponse>("/api/tenant/features"))!.EnabledFeatures);
                }
                await WithDbAsync(host, async db =>
                {
                    var member = await db.WorkspaceMembers.SingleAsync(row => row.WorkspaceId == host.Workspace.Id && row.UserId == host.Actor.Id);
                    member.Role = WorkspaceRole.Member;
                    await db.SaveChangesAsync();
                });
                await AssertExistingReadsAsync(host, attachments, version!);
                var after = await StateAsync(host);
                Assert.Equal(before.Metadata, after.Metadata);
                Assert.Equal(before.Storage, after.Storage);
                Assert.Equal(before.Settings, after.Settings);
                await WithDbAsync(host, async db =>
                {
                    var serverVersion = await db.Database.SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"").SingleAsync();
                    Assert.StartsWith("18.6", serverVersion, StringComparison.Ordinal);
                    Console.WriteLine($"FILE_INTAKE_PROVIDER={serverVersion}");
                });
                var assembly = typeof(FilesController).Assembly;
                Console.WriteLine($"FILE_INTAKE_WEB_MVID={assembly.ManifestModule.ModuleVersionId:D}");
                await using var assemblyStream = File.OpenRead(assembly.Location);
                Console.WriteLine($"FILE_INTAKE_WEB_SHA256={Convert.ToHexStringLower(await SHA256.HashDataAsync(assemblyStream))}");
            });

    private static async Task<Guid> PrepareAsync(AdmissionHost host)
    {
        var artifactId = Guid.NewGuid();
        await WithDbAsync(host, async db =>
        {
            var project = new Project { WorkspaceId = host.Workspace.Id, OwnerUserId = host.Actor.Id, CreatedByUserId = host.Actor.Id,
                Name = "Synthetic intake policy", Slug = "synthetic-intake-policy", Visibility = ProjectVisibility.WorkspaceVisible,
                Status = ProjectStatus.Active, ActivationState = ProjectActivationState.Activated };
            db.Projects.Add(project);
            db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = host.Actor.Id, Role = ProjectRole.Owner });
            db.Artifacts.Add(new Artifact { Id = artifactId, ProjectId = project.Id, Name = "Existing legacy synthetic Artifact", CreatedByUserId = host.Actor.Id });
            // A plan that enables FileSharing must still lose to the saved false override.
            var plan = new Plan { Name = "Synthetic intake plan " + Guid.NewGuid().ToString("N"), EnabledFeaturesJson = "[\"FileSharing\",\"Calendar\"]",
                MaxUsers = 100, MaxProjects = 100, MaxStorageBytes = 104857600 };
            db.Plans.Add(plan);
            db.Subscriptions.Add(new Subscription { PlanId = plan.Id, Status = SubscriptionStatus.Active, StartedAt = DateTimeOffset.UtcNow });
            db.TenantSettings.Add(new TenantSettings { DisplayName = "Synthetic intake", FeatureFlagsJson = "{\"Calendar\":true}" });
            (await db.TenantUsers.SingleAsync(row => row.UserId == host.Recipient.Id)).Role = TenantUserRole.Admin;
            await using var scope = host.Services.CreateAsyncScope();
            (await db.Users.SingleAsync(row => row.Id == host.Recipient.Id)).PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(AdmissionHost.Password);
            await db.SaveChangesAsync();
        });
        return artifactId;
    }

    private static async Task<HttpResponseMessage> UploadAsync(AdmissionHost host, string route, bool includeCsrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        using var form = new MultipartFormDataContent();
        if (!route.Contains("/versions", StringComparison.Ordinal))
        {
            form.Add(new StringContent(nameof(AttachmentOwnerType.Workspace)), "OwnerType");
            form.Add(new StringContent(host.Workspace.Id.ToString()), "OwnerId");
        }
        var bytes = new ByteArrayContent(SyntheticBytes);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(bytes, "File", "synthetic-intake.txt");
        request.Content = form;
        if (includeCsrf) await AddCsrfAsync(host.Client, request);
        return await host.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchSettingsAsync(HttpClient client, string flags)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/tenant/settings")
        { Content = JsonContent.Create(new UpdateTenantSettingsRequest(null, null, null, null, null, null, null, null, null, null, flags, null)) };
        await AddCsrfAsync(client, request);
        return await client.SendAsync(request);
    }

    private static async Task AddCsrfAsync(HttpClient client, HttpRequestMessage request)
    {
        var token = (await client.GetFromJsonAsync<CsrfTokenResponse>("/api/security/csrf-token"))!;
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token.Token);
    }

    private static async Task AssertExistingReadsAsync(AdmissionHost host, IReadOnlyList<AttachmentResponse> attachments, ArtifactVersionResponse version)
    {
        using var inventory = await host.Client.GetAsync($"/api/files?workspaceId={host.Workspace.Id}");
        Assert.Equal(HttpStatusCode.OK, inventory.StatusCode);
        var listed = await inventory.Content.ReadFromJsonAsync<Coglatas.Application.Common.PagedResponse<FileListItemResponse>>();
        foreach (var attachment in attachments)
        {
            Assert.Contains(listed!.Items, row => row.FileObjectId == attachment.FileObjectId);
            using var detail = await host.Client.GetAsync($"/api/files/{attachment.FileObjectId}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var metadata = (await detail.Content.ReadFromJsonAsync<FileObjectResponse>())!;
            Assert.Equal(attachment.FileObjectId, metadata.Id);
            using var attachmentDetail = await host.Client.GetAsync($"/api/attachments/{attachment.Id}");
            Assert.Equal(HttpStatusCode.OK, attachmentDetail.StatusCode);
            foreach (var byFile in new[] { true, false })
            {
                var grantRoute = byFile ? $"/api/files/{attachment.FileObjectId}/download-grants" : $"/api/attachments/{attachment.Id}/download-grants";
                using var issued = await AdmissionHost.PostAsync(host.Client, grantRoute, new FileDownloadGrantRequest("synthetic-intake-policy"));
                Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
                var grant = (await issued.Content.ReadFromJsonAsync<FileDownloadGrantResponse>())!;
                var contentRoute = byFile ? $"/api/file-download-grants/{grant.FileDownloadGrantId}/download" : $"/api/attachment-download-grants/{grant.FileDownloadGrantId}/download";
                using var content = await AdmissionHost.PostAsync(host.Client, contentRoute, new FileDownloadGrantTokenRequest(grant.Token));
                Assert.Equal(HttpStatusCode.OK, content.StatusCode);
                Assert.Equal(SyntheticBytes, await content.Content.ReadAsByteArrayAsync());
                Assert.True(content.Headers.CacheControl!.NoStore);
            }
            using var direct = await host.Client.GetAsync($"/api/attachments/{attachment.Id}/download");
            Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
            Assert.Equal(SyntheticBytes, await direct.Content.ReadAsByteArrayAsync());
        }
        using var historical = await host.Client.GetAsync($"/api/artifact-versions/{version.Id}/download");
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        Assert.Equal(SyntheticBytes, await historical.Content.ReadAsByteArrayAsync());
    }

    private sealed record Snapshot(string Metadata, string Audit, string Storage, string Settings);

    private static async Task<Snapshot> StateAsync(AdmissionHost host)
    {
        Snapshot snapshot = null!;
        await WithDbAsync(host, async db =>
        {
            var metadata = JsonSerializer.Serialize(new {
                files = await db.FileObjects.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Status, row.SizeBytes, row.StorageKey, row.DeletedAt }).ToListAsync(),
                attachments = await db.Attachments.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.FileObjectId, row.ScanStatus, row.DeletedAt }).ToListAsync(),
                artifacts = await db.Artifacts.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.CurrentVersionId, row.DeletedAt }).ToListAsync(),
                versions = await db.ArtifactVersions.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.FileObjectId, row.AttachmentId, row.VersionNumber, row.DeletedAt }).ToListAsync()
            });
            var audit = JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Action, row.EntityId }).ToListAsync());
            await using var scope = host.Services.CreateAsyncScope();
            var root = scope.ServiceProvider.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
            var stored = new SortedDictionary<string, string>();
            foreach (var file in Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [])
            {
                await using var stream = File.OpenRead(file);
                stored[Path.GetRelativePath(root, file)] = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            }
            snapshot = new Snapshot(metadata, audit, JsonSerializer.Serialize(stored), (await db.TenantSettings.AsNoTracking().SingleAsync()).FeatureFlagsJson);
        });
        return snapshot;
    }

    private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
