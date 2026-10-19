using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Files;

public sealed class FileFilterCandidateTests
{
    [Fact]
    public async Task ProjectInventoryAdmitsOnlyExactOwnedLiveTaskAndDeduplicatesBeforePaging()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var canonical = f.Add("owned.txt", f.Project.Id, f.Task);
        var later = f.Associate(canonical.FileObject!, f.Task);
        later.CreatedAt = canonical.CreatedAt.AddMinutes(1);
        f.Add("workspace-link.txt", task: f.Task); // link, not Project-owned inventory
        f.Add("artifact.txt", f.Project.Id, f.Task, AttachmentOwnerType.ArtifactVersion);
        f.Add("comment.txt", f.Project.Id, f.Task, AttachmentOwnerType.Comment);
        f.Add("log.txt", f.Project.Id, f.Task, AttachmentOwnerType.ActivityLog);
        f.Add("wrong-project.txt", f.OtherProject.Id, f.Task);
        var deletedTask = new TaskItem { WorkspaceId = f.Workspace.Id, ProjectId = f.Project.Id, Title = "Deleted", CreatedByUserId = f.ActorId };
        deletedTask.MarkDeleted(DateTimeOffset.UtcNow, f.ActorId, "Synthetic test");
        f.Db.TaskItems.Add(deletedTask);
        f.Add("deleted-task.txt", f.Project.Id, deletedTask);
        await f.Db.SaveChangesAsync();

        // Added audit timestamps are stamped by AppDbContext. Establish the controlled
        // persisted order afterwards instead of relying on ignored seed timestamps.
        canonical.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        later.CreatedAt = canonical.CreatedAt.AddDays(1);
        await f.Db.SaveChangesAsync();
        Assert.True(canonical.CreatedAt < later.CreatedAt);
        var result = await f.List("project", f.Project.Id, pageSize: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(canonical.Id, item.Id);
        Assert.Equal(item.Id, item.AttachmentId);
        Assert.Equal(canonical.FileObjectId, item.FileObjectId);
        Assert.Equal(f.Project.Id, item.ProjectId);
        Assert.False(item.CanManageSharing);
        Assert.Null(item.ExternalRecipientCount);
        Assert.Empty((await f.List("project", f.Project.Id, page: 2, pageSize: 1)).Value!.Items);
    }

    [Fact]
    public async Task UnauthorizedEarlierAssociationCannotHideAuthorizedCanonicalTask()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var valid = f.Add("owned.txt", f.Project.Id, f.Task);
        var wrong = f.Associate(valid.FileObject!, f.OtherTask);
        wrong.CreatedAt = valid.CreatedAt.AddDays(-1);
        await f.Db.SaveChangesAsync();
        wrong.CreatedAt = valid.CreatedAt.AddDays(-1);
        await f.Db.SaveChangesAsync();
        Assert.True(wrong.CreatedAt < valid.CreatedAt);
        Assert.Equal(valid.Id, Assert.Single((await f.List("project", f.Project.Id)).Value!.Items).Id);
    }

    [Fact]
    public async Task EqualTimeCanonicalAssociationUsesAttachmentIdTieBreaker()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var a = f.Add("tie.txt", f.Project.Id, f.Task);
        var b = f.Associate(a.FileObject!, f.Task);
        await f.Db.SaveChangesAsync();
        Assert.Equal(a.CreatedAt, b.CreatedAt);
        var expected = a.Id.CompareTo(b.Id) < 0 ? a.Id : b.Id;
        var result = (await f.List("project", f.Project.Id)).Value!;
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(expected, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task MalformedCrossWorkspaceTaskAndPlacementCannotEnterProjectOrRootInventory()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var wrongTask = new TaskItem { WorkspaceId = f.OtherWorkspace.Id, ProjectId = f.Project.Id, Title = "Mismatched scope", CreatedByUserId = f.ActorId };
        f.Db.TaskItems.Add(wrongTask);
        f.Add("wrong-task-scope.txt", f.Project.Id, wrongTask);
        var wrongPlacement = f.Add("wrong-placement-scope.txt");
        f.Db.Set<FileFolderPlacement>().Add(new FileFolderPlacement { WorkspaceId = f.OtherWorkspace.Id,
            FileObjectId = wrongPlacement.FileObjectId, FolderId = null });
        await f.Db.SaveChangesAsync();
        Assert.Empty((await f.List("project", f.Project.Id)).Value!.Items);
        Assert.Empty((await f.List(atRoot: true)).Value!.Items);
    }

    [Fact]
    public async Task FolderIntersectionAndRootDistinguishMissingAndNullPlacementWithoutRecursion()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var folder = f.Folder("Parent");
        var child = f.Folder("Child", folder.Id);
        var missing = f.Add("missing.txt");
        var explicitRoot = f.Add("root.txt");
        var placed = f.Add("placed.txt");
        var nested = f.Add("nested.txt");
        f.Place(explicitRoot, null); f.Place(placed, folder.Id); f.Place(nested, child.Id);
        await f.Db.SaveChangesAsync();
        var root = (await f.List(atRoot: true)).Value!;
        Assert.Equal(2, root.TotalCount);
        Assert.Equal(new[] { missing.FileObjectId, explicitRoot.FileObjectId }.Order(), root.Items.Select(x => x.FileObjectId).Order());
        Assert.All(root.Items, x => Assert.Null(x.FolderId));
        var result = (await f.List(folderId: folder.Id)).Value!;
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(folder.Id, Assert.Single(result.Items).FolderId);
    }

    [Fact]
    public async Task RevokedPrivateGrantIsRemovedFromCanonicalCountAndPage()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var visible = f.Add("public.txt");
        var secret = f.Add("private-sentinel.txt", visible: false);
        var grant = new FileAccessGrant { WorkspaceId = f.Workspace.Id, FileObjectId = secret.FileObjectId,
            RecipientUserId = f.ActorId, GrantedByUserId = f.OtherId, RecipientKind = FileAccessGrantRecipientKind.WorkspaceMember };
        f.Db.FileAccessGrants.Add(grant);
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, (await f.List()).Value!.TotalCount);
        grant.RevokedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();
        var denied = (await f.List(pageSize: 1)).Value!;
        Assert.Equal(1, denied.TotalCount);
        Assert.Equal(visible.Id, Assert.Single(denied.Items).Id);
        Assert.DoesNotContain("private-sentinel", JsonSerializer.Serialize(denied));
    }

    [Fact]
    public async Task ScopeAuthorizationDeniesWorkspaceProjectAndFolderWithoutExistenceDetails()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        f.Add("protected.txt", f.Project.Id, f.Task);
        await f.Db.SaveChangesAsync();
        f.ProjectAuthority.Allowed = false;
        var denied = await f.List("project", f.Project.Id);
        f.ProjectAuthority.Allowed = true;
        var missing = await f.List("project", Guid.NewGuid());
        var folderMissing = await f.List(folderId: Guid.NewGuid());
        f.WorkspaceAuthority.Allowed = false;
        var workspaceDenied = await f.List();
        Assert.All(new[] { denied, missing, folderMissing, workspaceDenied }, x => {
            Assert.False(x.IsSuccess); Assert.Equal("FileScopeUnavailable", x.ErrorDetail!.Code);
            Assert.Equal("Files not found.", x.ErrorDetail.Message); Assert.Null(x.Value);
        });
    }

    [Fact]
    public async Task TenantAndWorkspaceMismatchCannotUseKnownProjectOrFolderIdentities()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var foreignFolder = f.Folder("Other scope");
        foreignFolder.WorkspaceId = f.OtherWorkspace.Id;
        await f.Db.SaveChangesAsync();
        Assert.False((await f.List(folderId: foreignFolder.Id)).IsSuccess);
        var wrongWorkspace = await f.Service.ListFilteredFileObjectsAsync(f.OtherWorkspace.Id, "project", f.Project.Id,
            null, null, 1, 20, null, null);
        Assert.False(wrongWorkspace.IsSuccess);
        f.CurrentTenant.SetTenant(Guid.NewGuid(), "different-synthetic-tenant");
        Assert.False((await f.List("project", f.Project.Id)).IsSuccess);
    }

    [Fact]
    public async Task NewProjectionKeepsAuthorizedScanMetadataButExcludesArchivedAndStaleTargets()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        f.Add("good.txt");
        f.Add("quarantined.txt").FileObject!.Status = FileObjectStatus.Quarantined;
        f.Add("archived.txt").FileObject!.Status = FileObjectStatus.Archived;
        f.Add("pending.txt").ScanStatus = FileScanStatus.Pending;
        f.Add("infected.txt").ScanStatus = FileScanStatus.Infected;
        var stale = f.Add("stale-folder.txt");
        var folder = f.Folder("Deleted"); folder.MarkDeleted(DateTimeOffset.UtcNow, f.ActorId, "Synthetic");
        f.Place(stale, folder.Id);
        await f.Db.SaveChangesAsync();
        var result = (await f.List()).Value!;
        Assert.Equal(4, result.TotalCount);
        Assert.Contains(result.Items, x => x.OriginalFileName == "quarantined.txt" && x.Status == "Quarantined");
        Assert.Contains(result.Items, x => x.OriginalFileName == "pending.txt" && x.ScanStatus == "Pending");
        Assert.Contains(result.Items, x => x.OriginalFileName == "infected.txt" && x.ScanStatus == "Infected");
        Assert.DoesNotContain(result.Items, x => x.OriginalFileName == "archived.txt" || x.OriginalFileName == "stale-folder.txt");
    }

    [Fact]
    public async Task PrivateTaskFileUsesCurrentProjectViewRatherThanWorkspaceSharingAndReportsBlockedContent()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var task = f.Add("private-project.txt", f.Project.Id, f.Task, visible: false);
        task.FileObject!.Status = FileObjectStatus.Quarantined;
        task.ScanStatus = FileScanStatus.Infected;
        f.Add("private-workspace.txt", visible: false);
        await f.Db.SaveChangesAsync();
        var project = (await f.List("project", f.Project.Id)).Value!;
        Assert.Equal(1, project.TotalCount);
        Assert.Equal("Quarantined", Assert.Single(project.Items).AccessState);
        Assert.False(project.Items.Single().CanManageSharing);
        Assert.Null(project.Items.Single().ExternalRecipientCount);
        Assert.Empty((await f.List()).Value!.Items);
        f.ProjectAuthority.Allowed = false;
        Assert.False((await f.List("project", f.Project.Id)).IsSuccess);
    }

    [Fact]
    public async Task ConcreteAuthorizationRechecksPersistedMembershipDespiteTrackedProjectAndWorkspaceRows()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        f.Project.Visibility = ProjectVisibility.Restricted;
        var member = new ProjectMember { ProjectId = f.Project.Id, UserId = f.ActorId, Role = ProjectRole.Viewer };
        f.Db.ProjectMembers.Add(member);
        f.Add("real-policy-private-task.txt", f.Project.Id, f.Task, visible: false);
        await f.Db.SaveChangesAsync();
        var service = f.ConcreteService();
        Task<Result<PagedResponse<FilteredFileListItemResponse>>> Read() => service.ListFilteredFileObjectsAsync(
            f.Workspace.Id, "project", f.Project.Id, null, null, 1, 20, null, null);
        Assert.Single((await Read()).Value!.Items);
        Assert.False((await Read()).Value!.Items.Single().CanDelete);
        await using (var writer = f.NewContext())
        {
            writer.ProjectMembers.Remove(await writer.ProjectMembers.SingleAsync());
            await writer.SaveChangesAsync();
        }
        Assert.Contains(member, f.Db.ProjectMembers.Local); // deliberately stale tracked control
        var denied = await Read();
        Assert.False(denied.IsSuccess); Assert.Equal("FileScopeUnavailable", denied.ErrorDetail!.Code);
        await using (var writer = f.NewContext())
        {
            writer.ProjectMembers.Add(new ProjectMember { ProjectId = f.Project.Id, UserId = f.ActorId, Role = ProjectRole.Viewer });
            await writer.SaveChangesAsync();
        }
        Assert.Single((await Read()).Value!.Items);
        var trackedWorkspaceMember = await f.Db.WorkspaceMembers.SingleAsync();
        await using (var writer = f.NewContext())
        {
            (await writer.WorkspaceMembers.SingleAsync()).Status = MembershipStatus.Suspended;
            await writer.SaveChangesAsync();
        }
        Assert.Equal(MembershipStatus.Active, trackedWorkspaceMember.Status);
        Assert.False((await Read()).IsSuccess);
        await using (var writer = f.NewContext())
        {
            (await writer.WorkspaceMembers.SingleAsync()).Status = MembershipStatus.Active;
            await writer.SaveChangesAsync();
        }
        Assert.Single((await Read()).Value!.Items);
    }

    [Theory]
    [InlineData("createdAt", "desc")]
    [InlineData("updatedAt", "asc")]
    [InlineData("name", "asc")]
    public async Task StableOrderingAndExtremeOffsetAreDeterministic(string sort, string direction)
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var first = f.Add(" beta.txt"); first.FileObject!.UpdatedAt = first.FileObject.CreatedAt.AddDays(1);
        f.Add("Alpha.txt"); f.Add("alpha.txt");
        await f.Db.SaveChangesAsync();
        var full = (await f.List(sort: sort, direction: direction)).Value!;
        var pages = new List<Guid>();
        for (var page = 1; page <= 3; page++)
            pages.Add(Assert.Single((await f.List(page: page, pageSize: 1, sort: sort, direction: direction)).Value!.Items).FileObjectId);
        Assert.Equal(full.Items.Select(x => x.FileObjectId), pages);
        var far = (await f.List(page: int.MaxValue, pageSize: int.MaxValue)).Value!;
        Assert.Empty(far.Items); Assert.Equal(3, far.TotalCount); Assert.Equal(100, far.PageSize);
        var low = (await f.List(page: int.MinValue, pageSize: 0)).Value!;
        Assert.Equal(1, low.Page); Assert.Equal(1, low.PageSize);
    }

    [Theory]
    [InlineData("unknown", false, false, false, null, null)]
    [InlineData("workspace", true, false, false, null, null)]
    [InlineData("project", false, false, false, null, null)]
    [InlineData("workspace", false, true, true, null, null)]
    [InlineData("workspace", false, false, false, "path", null)]
    [InlineData("workspace", false, false, false, null, "sideways")]
    public void InvalidCombinationUsesSafeConstantValidation(string scope, bool project, bool folder, bool root, string? sort, string? direction)
    {
        var result = FileFilterQuery.Parse(Guid.NewGuid(), Guid.NewGuid(), scope,
            project ? Guid.NewGuid() : null, folder ? Guid.NewGuid() : null, root, 1, 20, sort, direction);
        Assert.False(result.IsSuccess); Assert.Equal("ValidationFailed", result.ErrorDetail!.Code);
        Assert.Equal("File filter is invalid.", result.ErrorDetail.Message);
    }

    [Fact]
    public async Task LegacyResponseShapeAndOrderingAreUnchangedWhenNewProjectionIsUnused()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var item = f.Add("legacy.txt"); item.ScanStatus = FileScanStatus.Pending;
        await f.Db.SaveChangesAsync();
        var legacy = (await f.Service.ListFileObjectsAsync(f.Workspace.Id, 1, 20)).Value!;
        Assert.Equal("Pending", Assert.Single(legacy.Items).ScanStatus);
        var json = JsonSerializer.SerializeToElement(legacy.Items.Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("attachmentId", out _));
        Assert.False(json.TryGetProperty("projectId", out _));
        Assert.False(json.TryGetProperty("folderId", out _));
        Assert.Equal(item.Id, json.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("workspace", "name")]
    [InlineData("project", "updatedAt")]
    public void NpgsqlTranslatesAuthorizedCanonicalQueryBeforeOrderingAndPaging(string scope, string sort)
    {
        var tenant = new CurrentTenantService(); tenant.SetTenant(Guid.NewGuid(), "synthetic");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Database=not_connected;Username=not_connected").Options;
        using var db = new AppDbContext(options, tenant);
        var filter = FileFilterQuery.Parse(tenant.TenantId, Guid.NewGuid(), scope,
            scope == "project" ? Guid.NewGuid() : null, null, true, 2, 3, sort, "asc").Value!;
        var query = FileFilterProjectionQuery.Build(db, filter, Guid.NewGuid(), false);
        var sql = FileFilterProjectionQuery.Order(query, filter, true).Skip(3).Take(3).ToQueryString();
        Assert.Contains("NOT EXISTS", sql); Assert.Contains("ORDER BY", sql); Assert.Contains("LIMIT", sql);
        Assert.Contains("TenantId", sql); Assert.Contains("FileObjectId", sql);
        if (sort == "name") Assert.Contains("COLLATE", sql);
        else Assert.Contains("COALESCE", sql);
        // Compilation only: no connection, PostgreSQL execution, plan or runtime PASS claimed.
    }
}

internal sealed class FileFilterFixture : IAsyncDisposable
{
    public CurrentTenantService CurrentTenant { get; } = new();
    public AppDbContext Db { get; }
    public Guid ActorId { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public Workspace Workspace { get; } = new() { Name = "Synthetic files", Slug = "files" };
    public Workspace OtherWorkspace { get; } = new() { Name = "Other", Slug = "other" };
    public Project Project { get; private set; } = null!;
    public Project OtherProject { get; private set; } = null!;
    public TaskItem Task { get; private set; } = null!;
    public TaskItem OtherTask { get; private set; } = null!;
    public WorkspaceAuthorityStub WorkspaceAuthority { get; } = new();
    public ProjectAuthorityStub ProjectAuthority { get; } = new();
    public FileService Service { get; private set; } = null!;
    public ICurrentUser Actor => new ActorContext(ActorId);
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    private readonly string databaseName = $"filter-{Guid.NewGuid():N}";
    private FileFilterFixture() { Db = NewContext(); }
    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options, CurrentTenant);
    public FileService ConcreteService()
    {
        var files = new FileRepository(Db);
        var workspaceRepository = new WorkspaceRepository(Db);
        var workspace = new WorkspaceAuthorizationService(new UserRepository(Db), workspaceRepository);
        var groupRepository = new GroupRepository(Db);
        var group = new GroupAuthorizationService(groupRepository, workspaceRepository, workspace);
        var project = new ProjectAuthorizationService(new ProjectRepository(Db), workspace, group, groupRepository);
        var authorization = new FileAuthorizationService(files, project, null!, null!, workspace, new FileAccessGrantRepository(Db));
        return new FileService(files, null!, null!, authorization, null!, null!, null!, Actor, CurrentTenant,
            null!, null!, null!, null!, null!, workspaceAuthorization: workspace, projectAuthorization: project);
    }
    public static async Task<FileFilterFixture> CreateAsync()
    {
        var f = new FileFilterFixture();
        f.CurrentTenant.SetPlatformScope();
        var tenant = new Tenant { Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic" };
        f.Db.Tenants.Add(tenant);
        f.Db.Users.AddRange(new User { Id = f.ActorId, DisplayName = "Actor", Email = "actor@example.test", NormalizedEmail = "ACTOR@EXAMPLE.TEST" },
            new User { Id = f.OtherId, DisplayName = "Other", Email = "other@example.test", NormalizedEmail = "OTHER@EXAMPLE.TEST" });
        await f.Db.SaveChangesAsync();
        f.CurrentTenant.SetTenant(tenant.Id, tenant.Slug);
        f.Workspace.CreatedByUserId = f.ActorId; f.OtherWorkspace.CreatedByUserId = f.ActorId;
        f.Db.Workspaces.AddRange(f.Workspace, f.OtherWorkspace);
        f.Db.TenantUsers.Add(new TenantUser { UserId = f.ActorId, Status = TenantUserStatus.Active });
        f.Db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = f.Workspace.Id, UserId = f.ActorId, Status = MembershipStatus.Active });
        f.Project = new Project { WorkspaceId = f.Workspace.Id, Name = "Project", Slug = "project", OwnerUserId = f.ActorId, CreatedByUserId = f.ActorId };
        f.OtherProject = new Project { WorkspaceId = f.Workspace.Id, Name = "Other project", Slug = "other-project", OwnerUserId = f.ActorId, CreatedByUserId = f.ActorId };
        f.Db.Projects.AddRange(f.Project, f.OtherProject);
        f.Task = new TaskItem { WorkspaceId = f.Workspace.Id, ProjectId = f.Project.Id, Title = "Task", CreatedByUserId = f.ActorId };
        f.OtherTask = new TaskItem { WorkspaceId = f.Workspace.Id, ProjectId = f.OtherProject.Id, Title = "Other Task", CreatedByUserId = f.ActorId };
        f.Db.TaskItems.AddRange(f.Task, f.OtherTask);
        await f.Db.SaveChangesAsync();
        var files = new FileRepository(f.Db);
        var authorization = new FileAuthorizationService(files, f.ProjectAuthority, null!, null!, f.WorkspaceAuthority, new FileAccessGrantRepository(f.Db));
        f.Service = new FileService(files, null!, null!, authorization, null!, null!, null!, f.Actor, f.CurrentTenant,
            null!, null!, null!, null!, null!, workspaceAuthorization: f.WorkspaceAuthority, projectAuthorization: f.ProjectAuthority);
        return f;
    }
    public Attachment Add(string name, Guid? projectId = null, TaskItem? task = null, AttachmentOwnerType? type = null, bool visible = true)
    {
        var file = new FileObject { WorkspaceId = Workspace.Id, ProjectId = projectId, OriginalFileName = name,
            UploadedByUserId = OtherId, CreatedAt = Timestamp, SharingPolicy = visible ? FileSharingPolicy.Workspace : FileSharingPolicy.Private };
        Db.FileObjects.Add(file);
        return Associate(file, task, type);
    }
    public Attachment Associate(FileObject file, TaskItem? task = null, AttachmentOwnerType? type = null)
    {
        var a = new Attachment { FileObject = file, FileObjectId = file.Id, WorkspaceId = Workspace.Id,
            OwnerType = type ?? (task is null ? AttachmentOwnerType.Workspace : AttachmentOwnerType.TaskItem), OwnerId = task?.Id ?? Workspace.Id,
            OwnerUserId = OtherId, UploadedByUserId = OtherId, CreatedAt = Timestamp, ScanStatus = FileScanStatus.Clean };
        Db.Attachments.Add(a); return a;
    }
    public FileFolder Folder(string name, Guid? parent = null)
    {
        var folder = new FileFolder { WorkspaceId = Workspace.Id, Name = name, ParentFolderId = parent };
        Db.Set<FileFolder>().Add(folder); return folder;
    }
    public void Place(Attachment attachment, Guid? folder) => Db.Set<FileFolderPlacement>().Add(new FileFolderPlacement {
        WorkspaceId = Workspace.Id, FileObjectId = attachment.FileObjectId, FolderId = folder });
    public Task<Result<PagedResponse<FilteredFileListItemResponse>>> List(string? scope = "workspace", Guid? projectId = null,
        Guid? folderId = null, bool? atRoot = null, int page = 1, int pageSize = 20, string? sort = null, string? direction = null) =>
        Service.ListFilteredFileObjectsAsync(Workspace.Id, scope, projectId, folderId, atRoot, page, pageSize, sort, direction);
    public ValueTask DisposeAsync() => Db.DisposeAsync();
    private sealed record ActorContext(Guid UserIdValue) : ICurrentUser
    {
        public Guid? UserId => UserIdValue;
        public Guid? SessionId => null;
        public string? Email => "actor@example.test";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
    internal sealed class WorkspaceAuthorityStub : IWorkspaceAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> CanViewWorkspace(Guid u, Guid w, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(Allowed);
        public Task<bool> CanContributeWorkspace(Guid u, Guid w, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(false);
        public Task<bool> CanManageWorkspace(Guid u, Guid w, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(false);
        public Task<bool> CanCreateWorkspace(Guid u, Guid t, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(false);
    }
    internal sealed class ProjectAuthorityStub : IProjectAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> CanViewProject(Guid u, Guid p, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(Allowed);
        public Task<bool> CanManageProject(Guid u, Guid p, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(false);
        public Task<bool> CanCreateProject(Guid u, Guid w, Guid g, CancellationToken c = default) => System.Threading.Tasks.Task.FromResult(false);
    }
}
