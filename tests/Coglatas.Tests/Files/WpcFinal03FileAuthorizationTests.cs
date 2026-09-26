using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Files;

[Trait("Scope", "WPCFinal03")]
public sealed class WpcFinal03FileAuthorizationTests
{
    [Fact]
    public async Task WorkspaceVisibleReaderCannotUploadProjectAttachment()
    {
        var projectId = Guid.NewGuid();
        var projectAuthorization = new ProjectAuthorizationStub
        {
            CanViewResult = true,
            CanContributeResult = false
        };
        var service = CreateService(projectId, projectAuthorization);

        var allowed = await service.CanUploadAttachment(
            Guid.NewGuid(),
            AttachmentOwnerType.TaskItem,
            Guid.NewGuid());

        Assert.False(allowed);
        Assert.Equal(1, projectAuthorization.ContributeCalls);
        Assert.Equal(0, projectAuthorization.ViewCalls);
    }

    [Fact]
    public async Task ExplicitCurrentContributorCanUploadProjectAttachment()
    {
        var projectId = Guid.NewGuid();
        var projectAuthorization = new ProjectAuthorizationStub
        {
            CanContributeResult = true
        };
        var service = CreateService(projectId, projectAuthorization);

        var allowed = await service.CanUploadAttachment(
            Guid.NewGuid(),
            AttachmentOwnerType.TaskItem,
            Guid.NewGuid());

        Assert.True(allowed);
        Assert.Equal(1, projectAuthorization.ContributeCalls);
    }

    [Fact]
    public async Task HistoricalUploaderCannotDeleteAfterCurrentContributionIsRevoked()
    {
        var userId = Guid.NewGuid();
        var projectAuthorization = new ProjectAuthorizationStub
        {
            CanContributeResult = false,
            CanManageResult = true
        };
        var service = CreateService(Guid.NewGuid(), projectAuthorization);
        var attachment = NewAttachment(userId);

        var allowed = await service.CanDeleteAttachment(userId, attachment);

        Assert.False(allowed);
        Assert.Equal(1, projectAuthorization.ContributeCalls);
        Assert.Equal(0, projectAuthorization.ManageCalls);
    }

    [Fact]
    public async Task CurrentUploaderCanDeleteWithoutProjectManagementAuthority()
    {
        var userId = Guid.NewGuid();
        var projectAuthorization = new ProjectAuthorizationStub
        {
            CanContributeResult = true,
            CanManageResult = false
        };
        var service = CreateService(Guid.NewGuid(), projectAuthorization);
        var attachment = NewAttachment(userId);

        var allowed = await service.CanDeleteAttachment(userId, attachment);

        Assert.True(allowed);
        Assert.Equal(1, projectAuthorization.ContributeCalls);
        Assert.Equal(0, projectAuthorization.ManageCalls);
    }

    [Fact]
    public async Task ExplicitCurrentProjectManagerCanModerateAnotherUsersAttachment()
    {
        var projectAuthorization = new ProjectAuthorizationStub
        {
            CanContributeResult = true,
            CanManageResult = true
        };
        var service = CreateService(Guid.NewGuid(), projectAuthorization);
        var attachment = NewAttachment(Guid.NewGuid());

        var allowed = await service.CanDeleteAttachment(Guid.NewGuid(), attachment);

        Assert.True(allowed);
        Assert.Equal(1, projectAuthorization.ContributeCalls);
        Assert.Equal(1, projectAuthorization.ManageCalls);
    }

    [Fact]
    public async Task WorkspaceListDeleteCapabilitiesEvaluateContributionOnceAndPreserveOwnerIdentity()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var workspaceAuthorization = new WorkspaceAuthorizationStub { CanContributeResult = true };
        var service = new FileAuthorizationService(
            new FileRepositoryStub(new FileOwnerContext(workspaceId)),
            null!,
            null!,
            null!,
            workspaceAuthorization);
        var owned = NewWorkspaceAttachment(workspaceId, userId, Guid.NewGuid());
        var ownedByTarget = NewWorkspaceAttachment(workspaceId, Guid.NewGuid(), userId);
        var otherUsers = NewWorkspaceAttachment(workspaceId, Guid.NewGuid(), Guid.NewGuid());
        var wrongOwner = NewWorkspaceAttachment(workspaceId, userId, Guid.NewGuid());
        wrongOwner.OwnerId = Guid.NewGuid();

        var allowedIds = await service.GetDeletableWorkspaceAttachmentIdsAsync(
            userId,
            workspaceId,
            [owned, ownedByTarget, otherUsers, wrongOwner]);

        Assert.Equal(1, workspaceAuthorization.ContributeCalls);
        Assert.True(allowedIds.SetEquals([owned.Id, ownedByTarget.Id]));
    }

    [Fact]
    public async Task WorkspaceListDeleteCapabilitiesFailClosedWithoutCurrentContribution()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var workspaceAuthorization = new WorkspaceAuthorizationStub { CanContributeResult = false };
        var service = new FileAuthorizationService(
            new FileRepositoryStub(new FileOwnerContext(workspaceId)),
            null!,
            null!,
            null!,
            workspaceAuthorization);

        var allowedIds = await service.GetDeletableWorkspaceAttachmentIdsAsync(
            userId,
            workspaceId,
            [NewWorkspaceAttachment(workspaceId, userId, userId)]);

        Assert.Empty(allowedIds);
        Assert.Equal(1, workspaceAuthorization.ContributeCalls);
    }

    [Fact]
    public async Task DirectWorkspacePrivateFileDeniesUnauthorisedReadAndAllowsOnlyAnEffectiveServerGrant()
    {
        var workspaceId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var workspaceAuthorization = new WorkspaceAuthorizationStub { CanViewResult = false };
        var grants = new FileAccessGrantRepositoryStub { HasEffectiveGrantResult = false };
        var service = new FileAuthorizationService(
            new FileRepositoryStub(new FileOwnerContext(workspaceId)),
            null!,
            null!,
            null!,
            workspaceAuthorization,
            grants);
        var attachment = NewWorkspaceAttachment(workspaceId, Guid.NewGuid(), Guid.NewGuid());
        attachment.FileObject = new FileObject
        {
            Id = attachment.FileObjectId,
            TenantId = attachment.TenantId,
            WorkspaceId = workspaceId,
            UploadedByUserId = attachment.UploadedByUserId,
            SharingPolicy = FileSharingPolicy.Private,
        };

        Assert.False(await service.CanViewAttachment(recipientUserId, attachment));
        Assert.False(await service.CanDownloadAttachment(recipientUserId, attachment));

        grants.HasEffectiveGrantResult = true;

        Assert.True(await service.CanViewAttachment(recipientUserId, attachment));
        Assert.True(await service.CanDownloadAttachment(recipientUserId, attachment));
        Assert.True(grants.HasEffectiveGrantCalls >= 4);
        Assert.Equal(2, workspaceAuthorization.ViewCalls);
    }

    private static FileAuthorizationService CreateService(
        Guid projectId,
        IProjectAuthorizationService projectAuthorization) =>
        new(
            new FileRepositoryStub(new FileOwnerContext(Guid.NewGuid(), projectId)),
            projectAuthorization,
            null!,
            null!,
            null!);

    private static Attachment NewAttachment(Guid uploadedByUserId) => new()
    {
        TenantId = Guid.NewGuid(),
        FileObjectId = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        OwnerType = AttachmentOwnerType.TaskItem,
        OwnerId = Guid.NewGuid(),
        OwnerUserId = Guid.NewGuid(),
        UploadedByUserId = uploadedByUserId,
        FileName = "evidence.txt",
        StoredFileName = "stored.txt",
        FilePath = "internal/evidence.txt",
        ContentType = "text/plain",
        Extension = ".txt",
        SizeBytes = 8,
        StorageProvider = "test",
        StorageKey = "internal/evidence.txt",
        ScanStatus = FileScanStatus.Clean
    };

    private static Attachment NewWorkspaceAttachment(Guid workspaceId, Guid uploadedByUserId, Guid ownerUserId) => new()
    {
        TenantId = Guid.NewGuid(),
        FileObjectId = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        OwnerType = AttachmentOwnerType.Workspace,
        OwnerId = workspaceId,
        OwnerUserId = ownerUserId,
        UploadedByUserId = uploadedByUserId,
        FileName = "workspace-file.txt",
        StoredFileName = "stored.txt",
        FilePath = "internal/workspace-file.txt",
        ContentType = "text/plain",
        Extension = ".txt",
        SizeBytes = 8,
        StorageProvider = "test",
        StorageKey = "internal/workspace-file.txt",
        ScanStatus = FileScanStatus.Clean
    };

    private sealed class ProjectAuthorizationStub : IProjectAuthorizationService
    {
        public bool CanViewResult { get; init; }
        public bool CanManageResult { get; init; }
        public bool CanContributeResult { get; init; }
        public int ViewCalls { get; private set; }
        public int ManageCalls { get; private set; }
        public int ContributeCalls { get; private set; }

        public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            ViewCalls++;
            return Task.FromResult(CanViewResult);
        }

        public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            ManageCalls++;
            return Task.FromResult(CanManageResult);
        }

        public Task<bool> CanContributeProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            ContributeCalls++;
            return Task.FromResult(CanContributeResult);
        }

        public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FileRepositoryStub(FileOwnerContext owner) : IFileRepository
    {
        public Task<FileOwnerContext?> ResolveOwnerAsync(
            AttachmentOwnerType ownerType,
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FileOwnerContext?>(owner);

        public Task<FileObject?> GetFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResponse<Attachment>> ListWorkspaceFileObjectsAsync(
            Guid workspaceId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddFileObjectAsync(FileObject fileObject, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Attachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Attachment?> GetAttachmentByFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class WorkspaceAuthorizationStub : IWorkspaceAuthorizationService
    {
        public bool CanContributeResult { get; init; }
        public bool CanViewResult { get; init; }
        public int ContributeCalls { get; private set; }
        public int ViewCalls { get; private set; }

        public Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
        {
            ViewCalls++;
            return Task.FromResult(CanViewResult);
        }

        public Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
        {
            ContributeCalls++;
            return Task.FromResult(CanContributeResult);
        }

        public Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FileAccessGrantRepositoryStub : IFileAccessGrantRepository
    {
        public bool HasEffectiveGrantResult { get; set; }
        public int HasEffectiveGrantCalls { get; private set; }

        public Task<bool> HasEffectiveGrantAsync(Guid fileObjectId, Guid userId, CancellationToken cancellationToken = default)
        {
            HasEffectiveGrantCalls++;
            return Task.FromResult(HasEffectiveGrantResult);
        }

        public Task<Attachment?> GetWorkspaceAttachmentAsync(Guid fileObjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, FileAccessGrantSummary>> GetEffectiveSummariesAsync(IReadOnlyCollection<Guid> fileObjectIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<FileAccessGrantRecipient>> ListEffectiveRecipientsAsync(Guid fileObjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<FileAccessGrantCandidate>> ListEligibleRecipientsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileAccessGrantCandidate?> FindEligibleRecipientAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileAccessGrant?> GetActiveGrantAsync(Guid fileObjectId, Guid grantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileAccessGrant?> GetActiveGrantForRecipientAsync(Guid fileObjectId, Guid recipientUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task AddAsync(FileAccessGrant grant, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
