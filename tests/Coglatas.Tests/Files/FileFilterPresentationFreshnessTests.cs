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

public sealed class FileFilterPresentationFreshnessTests
{
    [Theory]
    [InlineData("return")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task FreshProjectionBoundaryRestoresTrackingAndPreservesPendingWrites(string completion)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        var member = await fixture.Db.WorkspaceMembers.SingleAsync();
        member.Role = WorkspaceRole.Admin;
        await fixture.Db.SaveChangesAsync();
        fixture.Workspace.Name = "Pending write survives projection";
        fixture.Db.ChangeTracker.DetectChanges();
        var originalMode = fixture.Db.ChangeTracker.QueryTrackingBehavior;
        var repository = new FileRepository(fixture.Db);
        using var cancellation = new CancellationTokenSource();
        async Task<string> Read(CancellationToken token)
        {
            Assert.Equal(cancellation.Token, token);
            Assert.Equal(QueryTrackingBehavior.NoTracking, fixture.Db.ChangeTracker.QueryTrackingBehavior);
            var persisted = await fixture.Db.Workspaces.SingleAsync(w => w.Id == fixture.Workspace.Id, token);
            Assert.NotSame(fixture.Workspace, persisted);
            Assert.NotEqual(fixture.Workspace.Name, persisted.Name);
            if (completion == "throw") throw new InvalidOperationException("Synthetic projection failure.");
            if (completion == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return persisted.Name;
        }
        if (completion == "throw")
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReadCurrentFileFilterProjectionAsync(Read, cancellation.Token));
        else if (completion == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ReadCurrentFileFilterProjectionAsync(Read, cancellation.Token));
        else
            Assert.NotEqual(fixture.Workspace.Name, await repository.ReadCurrentFileFilterProjectionAsync(Read, cancellation.Token));
        Assert.Equal(originalMode, fixture.Db.ChangeTracker.QueryTrackingBehavior);
        Assert.Same(member, Assert.Single(fixture.Db.WorkspaceMembers.Local));
        Assert.Equal("Pending write survives projection", fixture.Workspace.Name);
        Assert.Equal(EntityState.Modified, fixture.Db.Entry(fixture.Workspace).State);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("demoted")]
    [InlineData("restored")]
    public async Task AlreadyDemotedUploaderCannotReceiveStaleSharingManagementOrExternalCount(string history)
    {
        await using var fixture = await FileFilterFixture.CreateAsync();
        var member = await fixture.Db.WorkspaceMembers.SingleAsync();
        member.Role = WorkspaceRole.Admin;
        var file = fixture.Add("uploader-visible-sharing-sentinel.txt", visible: false);
        file.UploadedByUserId = fixture.ActorId;
        file.OwnerUserId = fixture.ActorId;
        file.FileObject!.UploadedByUserId = fixture.ActorId;
        fixture.Db.TenantUsers.Add(new TenantUser { UserId = fixture.OtherId, Status = TenantUserStatus.Active });
        fixture.Db.ProjectMembers.Add(new ProjectMember { ProjectId = fixture.Project.Id, UserId = fixture.OtherId, Role = ProjectRole.Viewer });
        fixture.Db.FileAccessGrants.Add(new FileAccessGrant { WorkspaceId = fixture.Workspace.Id, FileObjectId = file.FileObjectId,
            RecipientUserId = fixture.OtherId, GrantedByUserId = fixture.ActorId, RecipientKind = FileAccessGrantRecipientKind.ExternalProjectMember });
        await fixture.Db.SaveChangesAsync();
        var files = new FileRepository(fixture.Db);
        var workspaceRepository = new WorkspaceRepository(fixture.Db);
        var workspace = new WorkspaceAuthorizationService(new UserRepository(fixture.Db), workspaceRepository);
        var groups = new GroupRepository(fixture.Db);
        var group = new GroupAuthorizationService(groups, workspaceRepository, workspace);
        var project = new ProjectAuthorizationService(new ProjectRepository(fixture.Db), workspace, group, groups);
        var grants = new FileAccessGrantRepository(fixture.Db);
        var fileAuthorization = new FileAuthorizationService(files, project, null!, null!, workspace, grants);
        var sharing = new FileSharingService(grants, fileAuthorization, workspace, fixture.Actor, fixture.CurrentTenant, null!, null!, null!, null!);
        var service = new FileService(files, null!, null!, fileAuthorization, null!, null!, null!, fixture.Actor, fixture.CurrentTenant,
            null!, null!, null!, null!, null!, sharing: sharing, workspaceAuthorization: workspace, projectAuthorization: project);
        Assert.Equal(1, (await grants.GetEffectiveSummariesAsync([file.FileObjectId]))[file.FileObjectId].ExternalRecipientCount);
        Assert.True(await workspace.CanManageWorkspace(fixture.ActorId, fixture.Workspace.Id)); // warmed tracked Admin
        fixture.Workspace.Name = "Reader pending presentation write";
        fixture.Db.ChangeTracker.DetectChanges();
        var originalMode = fixture.Db.ChangeTracker.QueryTrackingBehavior;
        if (history != "unchanged")
        {
            await using var writer = fixture.NewContext();
            var current = await writer.WorkspaceMembers.SingleAsync();
            current.Role = WorkspaceRole.Member;
            await writer.SaveChangesAsync(); // before this request's first manager snapshot
            if (history == "restored") { current.Role = WorkspaceRole.Admin; await writer.SaveChangesAsync(); }
        }
        Assert.True(await workspace.CanManageWorkspace(fixture.ActorId, fixture.Workspace.Id)); // stale unguarded positive
        Assert.Equal(history != "demoted", await files.ReadCurrentUploadAdmissionAsync(token =>
            workspace.CanManageWorkspace(fixture.ActorId, fixture.Workspace.Id, token)));

        var result = await service.ListFilteredFileObjectsAsync(fixture.Workspace.Id, "workspace", null, null, null, 1, 20, null, null);

        Assert.True(result.IsSuccess); // uploader retains read access; initial/final current manager agree
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(file.Id, item.Id);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(history == "demoted" ? (false, (int?)null) : (true, (int?)1),
            (item.CanManageSharing, item.ExternalRecipientCount));
        Assert.Equal(originalMode, fixture.Db.ChangeTracker.QueryTrackingBehavior);
        Assert.Same(member, Assert.Single(fixture.Db.WorkspaceMembers.Local));
        Assert.Equal(WorkspaceRole.Admin, member.Role);
        Assert.Equal("Reader pending presentation write", fixture.Workspace.Name);
        Assert.Equal(EntityState.Modified, fixture.Db.Entry(fixture.Workspace).State);
    }
}
