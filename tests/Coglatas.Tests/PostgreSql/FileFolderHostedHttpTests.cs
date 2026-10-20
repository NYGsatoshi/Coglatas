using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.TenantAdministration;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed class FileFolderHostedHttpTests
{
    private static readonly byte[] SyntheticBytes = "Prevalidated synthetic folder-placement sample.\n"u8.ToArray();

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentMemberPlacementOperationsRespectPersistedRoleScopeAndConcurrencyWhileIntakeIsClosed() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                var fixture = await PrepareAsync(host);
                var workspaceId = host.Workspace.Id;
                var actorId = host.Actor.Id;
                await host.LoginAsync();
                await AssertIntakeClosedAsync(host);
                await AssertBytesAsync(host, fixture.FileId);
                var bytesAndMetadata = await FileStateAsync(host);

                // A contributing Member may organize a visible Workspace File
                // uploaded by somebody else. Intake closure does not revoke this
                // existing placement contract or grant sharing/delete authority.
                var first = await CreateAsync(host, workspaceId, null, "First");
                var second = await CreateAsync(host, workspaceId, null, "Second");
                var child = await CreateAsync(host, workspaceId, first.Id, "Child");
                var movedChild = await PostAsync<FileFolderResponse>(host, $"/api/file-folders/{child.Id}/move",
                    new FileFolderMoveRequest(second.Id, child.Version, second.Version));
                Assert.Equal(second.Id, movedChild.ParentFolderId);
                Assert.Equal(child.Version + 1, movedChild.Version);
                Assert.Equal(child.Id, movedChild.Id);
                Assert.Equal(workspaceId, movedChild.WorkspaceId);

                var initial = await host.Client.GetFromJsonAsync<FileLocationResponse>($"/api/files/{fixture.FileId}/location");
                Assert.NotNull(initial);
                Assert.Null(initial.FolderId);
                Assert.Equal(0, initial.Version);
                var movedFile = await PostAsync<FileLocationResponse>(host, $"/api/files/{fixture.FileId}/move",
                    new FileMoveRequest(child.Id, initial.Version, movedChild.Version));
                Assert.Equal(new FileLocationResponse(fixture.FileId, workspaceId, child.Id, 1), movedFile);
                Assert.Equal(movedFile, await host.Client.GetFromJsonAsync<FileLocationResponse>($"/api/files/{fixture.FileId}/location"));
                var navigation = (await host.Client.GetFromJsonAsync<FileFolderNavigationResponse>($"/api/file-folders?workspaceId={workspaceId}"))!;
                Assert.Equal(second.Id, Assert.Single(navigation.Folders, row => row.Id == child.Id).ParentFolderId);

                await DeniedAsync(host, $"/api/files/{fixture.FileId}/move", new FileMoveRequest(null, 0, navigation.RootVersion));
                await DeniedAsync(host, $"/api/file-folders/{child.Id}/move", new FileFolderMoveRequest(null, child.Version, navigation.RootVersion));
                await DeniedAsync(host, $"/api/files/{fixture.FileId}/move", new FileMoveRequest(null, movedFile.Version, navigation.RootVersion + 1));
                await DeniedAsync(host, $"/api/file-folders/{second.Id}/move", new FileFolderMoveRequest(child.Id,
                    Assert.Single(navigation.Folders, row => row.Id == second.Id).Version,
                    Assert.Single(navigation.Folders, row => row.Id == child.Id).Version));

                var returned = await PostAsync<FileLocationResponse>(host, $"/api/files/{fixture.FileId}/move",
                    new FileMoveRequest(null, movedFile.Version, navigation.RootVersion));
                Assert.Equal(new FileLocationResponse(fixture.FileId, workspaceId, null, 2), returned);
                navigation = (await host.Client.GetFromJsonAsync<FileFolderNavigationResponse>($"/api/file-folders?workspaceId={workspaceId}"))!;
                var currentChild = Assert.Single(navigation.Folders, row => row.Id == child.Id);

                // The same authenticated cookie observes each committed role
                // change; no fake authorization or alternate controller is used.
                foreach (var role in new[] { WorkspaceRole.ReadOnly, WorkspaceRole.Member })
                {
                    await WithDbAsync(host, async db =>
                    {
                        var member = await db.WorkspaceMembers.SingleAsync(row => row.WorkspaceId == workspaceId && row.UserId == actorId);
                        member.Role = role;
                        member.Status = role == WorkspaceRole.ReadOnly ? MembershipStatus.Active : MembershipStatus.Suspended;
                        await db.SaveChangesAsync();
                    });
                    if (role == WorkspaceRole.ReadOnly)
                    {
                        using var readable = await host.Client.GetAsync($"/api/file-folders?workspaceId={workspaceId}");
                        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
                        await AssertBytesAsync(host, fixture.FileId);
                    }
                    else
                    {
                        using var unreadable = await host.Client.GetAsync($"/api/files/{fixture.FileId}/location");
                        Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode);
                    }
                    await DeniedAsync(host, "/api/file-folders", new FileFolderCreateRequest(workspaceId, null, "Denied"));
                    await DeniedAsync(host, $"/api/file-folders/{child.Id}/move", new FileFolderMoveRequest(null, currentChild.Version, navigation.RootVersion));
                    await DeniedAsync(host, $"/api/files/{fixture.FileId}/move", new FileMoveRequest(child.Id, returned.Version, currentChild.Version));
                }
                await WithDbAsync(host, async db =>
                {
                    var member = await db.WorkspaceMembers.SingleAsync(row => row.WorkspaceId == workspaceId && row.UserId == actorId);
                    member.Role = WorkspaceRole.Member;
                    member.Status = MembershipStatus.Active;
                    await db.SaveChangesAsync();
                });

                // Both foreign identifiers and an unreadable same-Tenant source
                // fail before placement changes. Then authorize both Workspaces
                // to distinguish a scope boundary from missing membership alone.
                foreach (var boundary in new[] { fixture.OtherWorkspace, fixture.ForeignWorkspace })
                {
                    await DeniedAsync(host, "/api/file-folders", new FileFolderCreateRequest(boundary.WorkspaceId, null, "Denied"));
                    await DeniedAsync(host, $"/api/file-folders/{boundary.FolderId}/move", new FileFolderMoveRequest(null, 1, 0));
                    await DeniedAsync(host, $"/api/files/{boundary.FileId}/move", new FileMoveRequest(child.Id, 0, currentChild.Version));
                    await DeniedAsync(host, "/api/file-folders", new FileFolderCreateRequest(workspaceId, boundary.FolderId, "Denied"));
                    await DeniedAsync(host, $"/api/file-folders/{child.Id}/move", new FileFolderMoveRequest(boundary.FolderId, currentChild.Version, 1));
                    await DeniedAsync(host, $"/api/files/{fixture.FileId}/move", new FileMoveRequest(boundary.FolderId, returned.Version, 1));
                }
                var otherWorkspaceId = fixture.OtherWorkspace.WorkspaceId;
                await WithDbAsync(host, async db =>
                {
                    db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = otherWorkspaceId, UserId = actorId,
                        Role = WorkspaceRole.Member, Status = MembershipStatus.Active });
                    await db.SaveChangesAsync();
                });
                using (var readable = await host.Client.GetAsync($"/api/file-folders?workspaceId={otherWorkspaceId}"))
                    Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
                await DeniedAsync(host, "/api/file-folders", new FileFolderCreateRequest(workspaceId, fixture.OtherWorkspace.FolderId, "Denied"));
                await DeniedAsync(host, $"/api/file-folders/{child.Id}/move", new FileFolderMoveRequest(fixture.OtherWorkspace.FolderId, currentChild.Version, 1));
                await DeniedAsync(host, $"/api/files/{fixture.FileId}/move", new FileMoveRequest(fixture.OtherWorkspace.FolderId, returned.Version, 1));
                await DeniedAsync(host, $"/api/files/{fixture.OtherWorkspace.FileId}/move", new FileMoveRequest(child.Id, 0, currentChild.Version));

                Assert.Equal(bytesAndMetadata, await FileStateAsync(host));
                await AssertIntakeClosedAsync(host);
                await AssertBytesAsync(host, fixture.FileId);
                await WithDbAsync(host, async db =>
                {
                    var provider = await db.Database.SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"").SingleAsync();
                    Assert.StartsWith("18.", provider, StringComparison.Ordinal);
                    Console.WriteLine($"FILE_FOLDER_PROVIDER={provider}");
                });
                var assembly = typeof(FileFoldersController).Assembly;
                Console.WriteLine($"FILE_FOLDER_WEB_MVID={assembly.ManifestModule.ModuleVersionId:D}");
                await using var assemblyStream = File.OpenRead(assembly.Location);
                Console.WriteLine($"FILE_FOLDER_WEB_SHA256={Convert.ToHexStringLower(await SHA256.HashDataAsync(assemblyStream))}");
            });

    private static async Task<Fixture> PrepareAsync(AdmissionHost host)
    {
        var actorId = host.Actor.Id;
        var uploaderId = host.Recipient.Id;
        var workspaceId = host.Workspace.Id;
        var foreignUserId = host.ForeignRecipient.Id;
        var fileId = Guid.NewGuid();
        Boundary? other = null;
        Boundary? foreign;
        await WithDbAsync(host, async db =>
        {
            Assert.Equal(SystemRole.NormalUser, (await db.Users.SingleAsync(row => row.Id == actorId)).SystemRole);
            Assert.Equal(TenantUserRole.Member, (await db.TenantUsers.SingleAsync(row => row.UserId == actorId)).Role);
            var member = await db.WorkspaceMembers.SingleAsync(row => row.WorkspaceId == workspaceId && row.UserId == actorId);
            Assert.Equal(WorkspaceRole.Member, member.Role);
            Assert.Equal(MembershipStatus.Active, member.Status);
            var settings = await db.Set<TenantSettings>().SingleOrDefaultAsync();
            if (settings is null) db.Add(new TenantSettings { FeatureFlagsJson = "{\"FileSharing\":false}" });
            else settings.FeatureFlagsJson = "{\"FileSharing\":false}";
            var otherWorkspace = await db.Workspaces.SingleAsync(row => row.Id != workspaceId);
            var folder = new FileFolder { WorkspaceId = otherWorkspace.Id, Name = "Other Workspace" };
            db.Add(folder);
            var otherFileId = Guid.NewGuid();
            AddFile(db, otherFileId, otherWorkspace.Id, uploaderId);
            other = new Boundary(otherWorkspace.Id, folder.Id, otherFileId);
            AddFile(db, fileId, workspaceId, uploaderId);
            await db.SaveChangesAsync();
        });
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantService>();
            tenant.SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var foreignMembership = await db.TenantUsers.AsNoTracking().SingleAsync(row => row.UserId == foreignUserId);
            var foreignTenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Id == foreignMembership.TenantId);
            tenant.SetTenant(foreignTenant.Id, foreignTenant.Slug);
            var workspace = new Workspace { Name = "Foreign placement", Slug = "foreign-placement", CreatedByUserId = foreignUserId };
            db.Add(workspace);
            var folder = new FileFolder { WorkspaceId = workspace.Id, Name = "Foreign folder" };
            db.Add(folder);
            var foreignFileId = Guid.NewGuid();
            AddFile(db, foreignFileId, workspace.Id, foreignUserId);
            await db.SaveChangesAsync();
            foreign = new Boundary(workspace.Id, folder.Id, foreignFileId);
        }
        Assert.NotNull(other);
        Assert.NotNull(foreign);
        // This direct fixture preparation is neither user upload nor a scanner:
        // every stored sample is fixed synthetic data, and intake stays disabled.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            foreach (var id in new[] { fileId, other.FileId, foreign.FileId })
            {
                using var stream = new MemoryStream(SyntheticBytes, writable: false);
                Assert.True((await storage.SaveAsync(StorageKey(id), stream, "text/plain")).IsSuccess);
            }
        }
        return new Fixture(fileId, other, foreign);
    }

    private static void AddFile(AppDbContext db, Guid id, Guid workspaceId, Guid uploaderId)
    {
        var file = new FileObject { Id = id, WorkspaceId = workspaceId, UploadedByUserId = uploaderId,
            OriginalFileName = "synthetic-placement.txt", StorageKey = StorageKey(id), ContentType = "text/plain",
            SizeBytes = SyntheticBytes.Length, Classification = DataClassification.Internal,
            SharingPolicy = FileSharingPolicy.Workspace };
        db.Add(file);
        db.Add(new Attachment { FileObjectId = id, WorkspaceId = workspaceId, OwnerType = AttachmentOwnerType.Workspace,
            OwnerId = workspaceId, OwnerUserId = uploaderId, UploadedByUserId = uploaderId, FileObject = file,
            FileName = file.OriginalFileName, StoredFileName = id.ToString("N"), FilePath = file.StorageKey,
            ContentType = file.ContentType, Extension = ".txt", SizeBytes = file.SizeBytes,
            StorageProvider = "LocalFileSystem", StorageKey = file.StorageKey, ScanStatus = FileScanStatus.Clean });
    }

    private static string StorageKey(Guid id) => $"synthetic-placement/{id:N}.txt";

    private static Task<FileFolderResponse> CreateAsync(AdmissionHost host, Guid workspaceId, Guid? parentId, string name) =>
        PostAsync<FileFolderResponse>(host, "/api/file-folders", new FileFolderCreateRequest(workspaceId, parentId, name));

    private static async Task<T> PostAsync<T>(AdmissionHost host, string route, object request)
    {
        using var response = await host.PostAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(result);
        return result;
    }

    private static async Task DeniedAsync(AdmissionHost host, string route, object request)
    {
        var before = await GraphStateAsync(host);
        using var response = await host.PostAsync(route, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("error").ValueKind);
        Assert.Equal(before, await GraphStateAsync(host));
    }

    private static async Task AssertIntakeClosedAsync(AdmissionHost host)
    {
        var features = (await host.Client.GetFromJsonAsync<TenantFeaturesResponse>("/api/tenant/features"))!;
        Assert.Equal(host.Tenant.Id, features.TenantId);
        Assert.DoesNotContain(FeatureKeys.FileSharing, features.EnabledFeatures);
    }

    private static async Task AssertBytesAsync(AdmissionHost host, Guid fileId)
    {
        using var metadata = await host.Client.GetAsync($"/api/files/{fileId}");
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        using var download = await host.Client.GetAsync($"/api/files/{fileId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True(download.Headers.CacheControl!.NoStore);
        Assert.Equal(SyntheticBytes, await download.Content.ReadAsByteArrayAsync());
    }

    private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<string> GraphStateAsync(AdmissionHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var graph = new { Folders = await db.Set<FileFolder>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Roots = await db.Set<FileFolderRootState>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Placements = await db.Set<FileFolderPlacement>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Files = await FileStateAsync(host) };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(graph)));
    }

    private static async Task<string> FileStateAsync(AdmissionHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = scope.ServiceProvider.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
        var storage = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            await using var stream = File.OpenRead(path);
            storage.Add(Path.GetRelativePath(root, path) + ":" + Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)));
        }
        var state = new { Files = await db.FileObjects.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Attachments = await db.Attachments.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Settings = await db.Set<TenantSettings>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(), Storage = storage };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
    }

    private sealed record Boundary(Guid WorkspaceId, Guid FolderId, Guid FileId);
    private sealed record Fixture(Guid FileId, Boundary OtherWorkspace, Boundary ForeignWorkspace);
}
