using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Files;

/// <summary>Deterministic post-query histories using concrete authorization and independent persisted writers.</summary>
public sealed class FileFilterFreshnessRegressionTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("revoked")]
    [InlineData("regranted")]
    public async Task PrivateGrantChangeAfterCapturedRowsCannotReturnProtectedMetadataOrOldCount(string history)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        var file = fixture.Add("post-query-private-grant-sentinel.txt", visible: false);
        var grant = new FileAccessGrant { WorkspaceId = fixture.Workspace.Id, FileObjectId = file.FileObjectId,
            RecipientUserId = fixture.ActorId, GrantedByUserId = fixture.OtherId,
            RecipientKind = FileAccessGrantRecipientKind.WorkspaceMember };
        fixture.Db.FileAccessGrants.Add(grant);
        await fixture.Db.SaveChangesAsync();
        Assert.NotEqual(fixture.ActorId, file.UploadedByUserId);
        Assert.NotEqual(fixture.ActorId, file.OwnerUserId);
        Assert.False(await new WorkspaceAuthorizationService(new UserRepository(fixture.Db),
            new WorkspaceRepository(fixture.Db)).CanManageWorkspace(fixture.ActorId, fixture.Workspace.Id));
        var captured = new CapturingRepository(new FileRepository(fixture.Db), fixture, async state =>
        {
            if (history == "unchanged") return;
            await using var writer = state.NewContext();
            var persisted = await writer.FileAccessGrants.SingleAsync();
            persisted.RevokedAt = DateTimeOffset.UtcNow;
            await writer.SaveChangesAsync();
            if (history == "regranted")
            {
                persisted.RevokedAt = null;
                await writer.SaveChangesAsync();
            }
        });
        var service = ConcreteService(fixture, captured);

        var result = await service.ListFilteredFileObjectsAsync(fixture.Workspace.Id, "workspace", null, null, null, 1, 20, null, null);

        Assert.Equal(file.Id, Assert.Single(captured.Initial!.Items).Attachment.Id);
        Assert.Equal(1, captured.Initial.TotalCount);
        Assert.Null(grant.RevokedAt); // initial context deliberately retains the old tracked grant
        if (history == "revoked") AssertUnavailableWithoutPayload(result);
        else Assert.Equal(file.Id, Assert.Single(result.Value!.Items).Id);
        // Independent authorized subsequent-request control: current rows/count remain queryable.
        var current = await fixture.ConcreteService().ListFilteredFileObjectsAsync(fixture.Workspace.Id, "workspace", null, null, null, 1, 20, null, null);
        Assert.True(current.IsSuccess);
        Assert.Equal(history == "revoked" ? 0 : 1, current.Value!.TotalCount);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("deleted")]
    [InlineData("restored")]
    public async Task TaskDeletionAfterCapturedProjectRowsCannotSurviveAStillReadableProject(string history)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        fixture.Project.Visibility = ProjectVisibility.Restricted;
        fixture.Db.ProjectMembers.Add(new ProjectMember { ProjectId = fixture.Project.Id, UserId = fixture.ActorId, Role = ProjectRole.Viewer });
        var file = fixture.Add("post-query-task-owner-sentinel.txt", fixture.Project.Id, fixture.Task, visible: false);
        await fixture.Db.SaveChangesAsync();
        var captured = new CapturingRepository(new FileRepository(fixture.Db), fixture, async state =>
        {
            if (history == "unchanged") return;
            await using var writer = state.NewContext();
            var task = await writer.TaskItems.SingleAsync(t => t.Id == state.Task.Id);
            task.MarkDeleted(DateTimeOffset.UtcNow, state.ActorId, "Synthetic post-query lifecycle");
            await writer.SaveChangesAsync();
            if (history == "restored") { task.Restore(); await writer.SaveChangesAsync(); }
        });
        var service = ConcreteService(fixture, captured);

        var result = await service.ListFilteredFileObjectsAsync(fixture.Workspace.Id, "project", fixture.Project.Id, null, null, 1, 20, null, null);

        Assert.Equal(file.Id, Assert.Single(captured.Initial!.Items).Attachment.Id);
        Assert.Equal(1, captured.Initial.TotalCount);
        Assert.Null(fixture.Task.DeletedAt); // old reader's owner graph remains tracked and live
        if (history == "deleted") AssertUnavailableWithoutPayload(result);
        else Assert.Equal(file.Id, Assert.Single(result.Value!.Items).Id);
        var current = await fixture.ConcreteService().ListFilteredFileObjectsAsync(fixture.Workspace.Id, "project", fixture.Project.Id, null, null, 1, 20, null, null);
        Assert.True(current.IsSuccess); // Project membership never changed
        Assert.Equal(history == "deleted" ? 0 : 1, current.Value!.TotalCount);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("demoted")]
    [InlineData("restored")]
    public async Task ManagerDemotionAfterCaptureCannotReuseAStalePrivateFileDiscoveryBypass(string history)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        var member = await fixture.Db.WorkspaceMembers.SingleAsync();
        member.Role = WorkspaceRole.Admin;
        var file = fixture.Add("post-query-manager-private-sentinel.txt", visible: false);
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(fixture.Db.FileAccessGrants.Local);
        var authority = new WorkspaceAuthorizationService(new UserRepository(fixture.Db), new WorkspaceRepository(fixture.Db));
        Assert.True(await authority.CanManageWorkspace(fixture.ActorId, fixture.Workspace.Id)); // warm the tracked reader
        fixture.Workspace.Name = "Reader pending local write";
        fixture.Db.ChangeTracker.DetectChanges();
        var originalMode = fixture.Db.ChangeTracker.QueryTrackingBehavior;
        var readerRepository = new FileRepository(fixture.Db);
        var captured = new CapturingRepository(readerRepository, fixture, async state =>
        {
            if (history == "unchanged") return;
            await using var writer = state.NewContext();
            var current = await writer.WorkspaceMembers.SingleAsync();
            current.Role = WorkspaceRole.Member;
            await writer.SaveChangesAsync();
            if (history == "restored") { current.Role = WorkspaceRole.Admin; await writer.SaveChangesAsync(); }
            if (history == "demoted")
            {
                // Strong control: an unguarded warmed reader really does retain stale authority.
                Assert.True(await authority.CanManageWorkspace(state.ActorId, state.Workspace.Id));
                Assert.False(await readerRepository.ReadCurrentUploadAdmissionAsync(token =>
                    authority.CanManageWorkspace(state.ActorId, state.Workspace.Id, token)));
                Assert.Equal(originalMode, state.Db.ChangeTracker.QueryTrackingBehavior);
            }
        });
        var result = await ConcreteService(fixture, captured).ListFilteredFileObjectsAsync(
            fixture.Workspace.Id, "workspace", null, null, null, 1, 20, null, null);
        Assert.Equal(file.Id, Assert.Single(captured.Initial!.Items).Attachment.Id);
        Assert.Equal(WorkspaceRole.Admin, member.Role); // stale reader does not reflect persisted demotion
        Assert.Equal(originalMode, fixture.Db.ChangeTracker.QueryTrackingBehavior);
        Assert.Same(member, Assert.Single(fixture.Db.WorkspaceMembers.Local));
        Assert.Equal("Reader pending local write", fixture.Workspace.Name);
        Assert.Equal(EntityState.Modified, fixture.Db.Entry(fixture.Workspace).State);
        if (history == "demoted") AssertUnavailableWithoutPayload(result);
        else Assert.Equal(file.Id, Assert.Single(result.Value!.Items).Id);
    }

    [Fact]
    public async Task RevokedOffPageGrantCannotLeaveAnOldTotalBesideAnUnchangedVisiblePage()
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        var visible = fixture.Add("first.txt");
        var privateFile = fixture.Add("second-private-sentinel.txt", visible: false);
        fixture.Db.FileAccessGrants.Add(new FileAccessGrant { WorkspaceId = fixture.Workspace.Id, FileObjectId = privateFile.FileObjectId,
            RecipientUserId = fixture.ActorId, GrantedByUserId = fixture.OtherId, RecipientKind = FileAccessGrantRecipientKind.WorkspaceMember });
        await fixture.Db.SaveChangesAsync();
        var captured = new CapturingRepository(new FileRepository(fixture.Db), fixture, async state =>
        {
            await using var writer = state.NewContext();
            (await writer.FileAccessGrants.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            await writer.SaveChangesAsync();
        });
        var result = await ConcreteService(fixture, captured).ListFilteredFileObjectsAsync(
            fixture.Workspace.Id, "workspace", null, null, null, 1, 1, "name", "asc");
        Assert.Equal(visible.Id, Assert.Single(captured.Initial!.Items).Attachment.Id);
        Assert.Equal(2, captured.Initial.TotalCount);
        AssertUnavailableWithoutPayload(result);
        var current = await fixture.List(sort: "name", direction: "asc", pageSize: 1);
        Assert.Equal(1, current.Value!.TotalCount);
        Assert.Equal(visible.Id, Assert.Single(current.Value.Items).Id);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("project")]
    public async Task ArchivedWorkspaceKeepsCurrentActiveMemberReadWithoutGrantingMutation(string scope)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        fixture.Project.Visibility = ProjectVisibility.Restricted;
        fixture.Db.ProjectMembers.Add(new ProjectMember { ProjectId = fixture.Project.Id, UserId = fixture.ActorId, Role = ProjectRole.Viewer });
        var direct = fixture.Add("archived-workspace-metadata.txt");
        var project = fixture.Add("archived-project-metadata.txt", fixture.Project.Id, fixture.Task, visible: false);
        await fixture.Db.SaveChangesAsync();
        var service = fixture.ConcreteService();
        Task<Result<PagedResponse<FilteredFileListItemResponse>>> Read(FileFilterFixture state) => service.ListFilteredFileObjectsAsync(
            state.Workspace.Id, scope, scope == "project" ? state.Project.Id : null, null, true, 1, 20, null, null);
        Assert.Single((await Read(fixture)).Value!.Items); // active-Workspace positive control
        fixture.Workspace.Status = WorkspaceStatus.Archived;
        await fixture.Db.SaveChangesAsync();
        var legacy = await service.ListFileObjectsAsync(fixture.Workspace.Id, 1, 20);
        Assert.True(legacy.IsSuccess);
        Assert.Equal(direct.Id, Assert.Single(legacy.Value!.Items).Id);
        Assert.False(legacy.Value.Items.Single().CanDelete);

        var archived = await Read(fixture);

        Assert.True(archived.IsSuccess);
        Assert.Equal(scope == "project" ? project.Id : direct.Id, Assert.Single(archived.Value!.Items).Id);
        Assert.False(archived.Value.Items.Single().CanDelete);
        var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(fixture.Db), new WorkspaceRepository(fixture.Db));
        Assert.True(await workspaceAuthorization.CanViewWorkspace(fixture.ActorId, fixture.Workspace.Id));
        Assert.False(await workspaceAuthorization.CanContributeWorkspace(fixture.ActorId, fixture.Workspace.Id));
        (await fixture.Db.WorkspaceMembers.SingleAsync()).Status = MembershipStatus.Suspended;
        await fixture.Db.SaveChangesAsync();
        Assert.False((await Read(fixture)).IsSuccess); // archived history is not a membership bypass
    }

    [Theory]
    [InlineData("membership", "unchanged")]
    [InlineData("membership", "revoked")]
    [InlineData("membership", "restored")]
    [InlineData("management", "unchanged")]
    [InlineData("management", "revoked")]
    [InlineData("management", "restored")]
    public async Task ScopeChangeDuringFinalRepositoryReadCannotReturnAnUploaderPage(string authority, string history)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        var member = await fixture.Db.WorkspaceMembers.SingleAsync();
        member.Role = authority == "management" ? WorkspaceRole.Admin : WorkspaceRole.Member;
        var file = fixture.Add("final-read-uploader-sentinel.txt", visible: false);
        file.UploadedByUserId = fixture.ActorId;
        file.OwnerUserId = fixture.ActorId;
        file.FileObject!.UploadedByUserId = fixture.ActorId;
        await fixture.Db.SaveChangesAsync();
        var captured = new CapturingRepository(new FileRepository(fixture.Db), fixture, async state =>
        {
            if (history == "unchanged") return;
            await using var writer = state.NewContext();
            var persisted = await writer.WorkspaceMembers.SingleAsync();
            if (authority == "membership") persisted.Status = MembershipStatus.Suspended;
            else persisted.Role = WorkspaceRole.Member;
            await writer.SaveChangesAsync();
            if (history == "restored")
            {
                persisted.Status = MembershipStatus.Active;
                if (authority == "management") persisted.Role = WorkspaceRole.Admin;
                await writer.SaveChangesAsync();
            }
        }, captureReadNumber: 2);

        var result = await ConcreteService(fixture, captured).ListFilteredFileObjectsAsync(
            fixture.Workspace.Id, "workspace", null, null, null, 1, 20, null, null);

        Assert.Equal(2, captured.ReadCount);
        Assert.True(captured.HookCompleted);
        Assert.Equal(file.Id, Assert.Single(captured.Initial!.Items).Attachment.Id);
        Assert.Equal(1, captured.Initial.TotalCount);
        Assert.Equal(file.Id, Assert.Single(captured.Last!.Items).Attachment.Id);
        Assert.Equal(1, captured.Last.TotalCount); // uploader page/count alone cannot prove current scope
        Assert.Equal(MembershipStatus.Active, member.Status); // independent writer did not update this graph
        if (history == "revoked") AssertUnavailableWithoutPayload(result);
        else
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(file.Id, Assert.Single(result.Value!.Items).Id);
        }
    }

    private static void AssertUnavailableWithoutPayload(Result<PagedResponse<FilteredFileListItemResponse>> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value); // never return a partially filtered page under its old total
        Assert.Equal("FileScopeUnavailable", result.ErrorDetail!.Code);
        Assert.Equal("Files not found.", result.ErrorDetail.Message);
    }

    private static FileService ConcreteService(FileFilterFixture fixture, IFileRepository repository)
    {
        var workspaceRepository = new WorkspaceRepository(fixture.Db);
        var workspace = new WorkspaceAuthorizationService(new UserRepository(fixture.Db), workspaceRepository);
        var groups = new GroupRepository(fixture.Db);
        var group = new GroupAuthorizationService(groups, workspaceRepository, workspace);
        var project = new ProjectAuthorizationService(new ProjectRepository(fixture.Db), workspace, group, groups);
        var authorization = new FileAuthorizationService(repository, project, null!, null!, workspace, new FileAccessGrantRepository(fixture.Db));
        return new FileService(repository, null!, null!, authorization, null!, null!, null!, fixture.Actor, fixture.CurrentTenant,
            null!, null!, null!, null!, null!, workspaceAuthorization: workspace, projectAuthorization: project);
    }

    private sealed class CapturingRepository(IFileRepository inner, FileFilterFixture fixture, Func<FileFilterFixture, Task> afterCapture, int captureReadNumber = 1) : IFileRepository
    {
        public PagedResponse<FilteredFileRecord>? Initial { get; private set; }
        public PagedResponse<FilteredFileRecord>? Last { get; private set; }
        public int ReadCount { get; private set; }
        public bool HookCompleted { get; private set; }
        public async Task<PagedResponse<FilteredFileRecord>> ListFilteredFileObjectsAsync(FileFilterQuery query, Guid actor, bool manage, CancellationToken ct = default)
        {
            var result = await inner.ListFilteredFileObjectsAsync(query, actor, manage, ct);
            Initial ??= result;
            Last = result;
            ReadCount++;
            if (ReadCount == captureReadNumber) { await afterCapture(fixture); HookCompleted = true; }
            return result;
        }
        public Task<bool> IsFileFilterScopeValidAsync(FileFilterQuery q, CancellationToken ct = default) => inner.IsFileFilterScopeValidAsync(q, ct);
        public Task<bool> ReadCurrentUploadAdmissionAsync(Func<CancellationToken, Task<bool>> admission, CancellationToken ct = default) => inner.ReadCurrentUploadAdmissionAsync(admission, ct);
        public Task<FileObject?> GetFileObjectAsync(Guid id, CancellationToken ct = default) => inner.GetFileObjectAsync(id, ct);
        public Task<PagedResponse<Attachment>> ListWorkspaceFileObjectsAsync(Guid ws, int p, int s, CancellationToken ct = default) => inner.ListWorkspaceFileObjectsAsync(ws, p, s, ct);
        public Task AddFileObjectAsync(FileObject file, CancellationToken ct = default) => inner.AddFileObjectAsync(file, ct);
        public Task<Attachment?> GetAttachmentAsync(Guid id, CancellationToken ct = default) => inner.GetAttachmentAsync(id, ct);
        public Task<Attachment?> GetAttachmentByFileObjectAsync(Guid id, CancellationToken ct = default) => inner.GetAttachmentByFileObjectAsync(id, ct);
        public Task AddAttachmentAsync(Attachment a, CancellationToken ct = default) => inner.AddAttachmentAsync(a, ct);
        public Task<FileOwnerContext?> ResolveOwnerAsync(AttachmentOwnerType t, Guid id, CancellationToken ct = default) => inner.ResolveOwnerAsync(t, id, ct);
    }
}
