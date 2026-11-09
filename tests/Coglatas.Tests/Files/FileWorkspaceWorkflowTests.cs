using System.Text;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Files;

public sealed class FileWorkspaceWorkflowTests
{
    [Fact]
    public async Task WorkspaceUploadPersistsOnlyAfterStorageAcceptsAndAppearsInList()
    {
        var fixture = new Fixture();

        var upload = await fixture.UploadTextAsync("workspace-note.txt", "hello");
        var list = await fixture.Service.ListFileObjectsAsync(fixture.WorkspaceId, 1, 20);

        Assert.True(upload.IsSuccess);
        Assert.True(list.IsSuccess);
        var item = Assert.Single(list.Value!.Items);
        Assert.Equal(upload.Value!.FileObjectId, item.FileObjectId);
        Assert.Equal(fixture.WorkspaceId, item.WorkspaceId);
        Assert.Equal("workspace-note.txt", item.OriginalFileName);
        Assert.Equal("Skipped", item.ScanStatus);
        Assert.Equal("Fixture User", item.UploadedByDisplayName);
        Assert.True(item.CanDelete);
        Assert.Single(fixture.Files.FileObjects);
        Assert.Single(fixture.Files.Attachments);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.Empty(fixture.Logger.Entries);
    }

    [Fact]
    public async Task WorkspaceFileListProjectsServerAuthoritativeDeleteCapability()
    {
        var fixture = new Fixture();
        await fixture.UploadTextAsync("workspace-note.txt", "hello");
        fixture.Authorization.CanDeleteResult = false;

        var list = await fixture.Service.ListFileObjectsAsync(fixture.WorkspaceId, 1, 20);

        Assert.True(list.IsSuccess);
        Assert.False(Assert.Single(list.Value!.Items).CanDelete);
    }

    [Fact]
    public async Task StorageFailureDoesNotPersistFileMetadata()
    {
        var fixture = new Fixture { Storage = { SaveResult = Result.Failure("storage rejected") } };

        var upload = await fixture.UploadTextAsync("workspace-note.txt", "hello");

        Assert.False(upload.IsSuccess);
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Files.Attachments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CleanupFailureLogsOnlyAFixedWarningAndPreservesOriginalFailure(bool loggingEnabled)
    {
        var fixture = new Fixture(loggingEnabled);
        var expected = new InvalidOperationException("Synthetic admission canary MUST_NOT_BE_LOGGED.");
        fixture.Authorization.SubsequentUploadFailure = expected;
        fixture.Storage.DeleteFailure = new IOException("Synthetic cleanup canary MUST_NOT_BE_LOGGED.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.UploadTextAsync("private-canary.txt", "private content"));

        Assert.Same(expected, actual);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        if (loggingEnabled) { fixture.Logger.AssertOnlyFixedWarning(); }
        else { Assert.Empty(fixture.Logger.Entries); }
    }

    [Fact]
    public async Task CleanupAndLoggerFailureDoNotReplaceUploadDenial()
    {
        var fixture = new Fixture();
        fixture.Storage.AfterSave = () => fixture.Authorization.CanUploadResult = false;
        fixture.Storage.DeleteFailure = new IOException("Synthetic cleanup canary MUST_NOT_BE_LOGGED.");
        fixture.Logger.ThrowOnLog = true;

        var result = await fixture.UploadTextAsync("private-canary.txt", "private content");

        Assert.False(result.IsSuccess);
        Assert.Equal("You are not allowed to upload an attachment for this resource.", result.Error);
        Assert.Single(fixture.Storage.DeletedKeys);
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Logger.Entries);
    }

    [Fact]
    public async Task MetadataFailureCleansUpStoredBytes()
    {
        var fixture = new Fixture { UnitOfWork = { ThrowOnSave = true } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UploadTextAsync("workspace-note.txt", "hello"));

        var key = Assert.Single(fixture.Storage.DeletedKeys);
        Assert.Contains($"/files/{fixture.Files.FileObjects.Single().Id:D}", key);
        Assert.Empty(fixture.Logger.Entries);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("save")]
    public async Task FailedPendingWritesAreDiscardedBeforeCleanupAndCannotEscapeThroughLaterSave(string stage)
    {
        var fixture = new Fixture();
        if (stage == "audit") fixture.Audit.Failure = new InvalidOperationException("Synthetic audit staging failure.");
        else fixture.UnitOfWork.ThrowOnSave = true;
        fixture.Storage.BeforeDelete = () =>
        {
            Assert.Empty(fixture.Files.PendingFileObjects);
            Assert.Empty(fixture.Files.PendingAttachments);
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UploadTextAsync("owned.txt", "owned bytes"));

        Assert.Single(fixture.Storage.DeletedKeys);
        Assert.Single(fixture.Files.FileObjects);
        Assert.Single(fixture.Files.Attachments);
        fixture.UnitOfWork.ThrowOnSave = false;
        await fixture.UnitOfWork.SaveChangesAsync();
        Assert.Empty(fixture.Files.CommittedFileObjects);
        Assert.Empty(fixture.Files.CommittedAttachments);
    }

    [Theory]
    [InlineData("unknown", false)]
    [InlineData("committed", false)]
    [InlineData("discard-refused", false)]
    [InlineData("discard-failed", false)]
    [InlineData("unknown", true)]
    public async Task UnprovenCleanupRetainsBytesAndPreservesTheOriginalFailure(string mode, bool loggerFails)
    {
        var fixture = new Fixture();
        var expected = new IOException("Synthetic private acknowledgement canary MUST_NOT_BE_LOGGED.");
        fixture.UnitOfWork.UploadFailure = expected;
        fixture.UnitOfWork.FailureDisposition = mode switch
        {
            "unknown" => UploadCommitDisposition.Unknown,
            "committed" => UploadCommitDisposition.Committed,
            _ => UploadCommitDisposition.NotCommitted
        };
        fixture.UnitOfWork.DiscardRefused = mode == "discard-refused";
        fixture.UnitOfWork.DiscardFails = mode == "discard-failed";
        fixture.Logger.ThrowOnLog = loggerFails;

        var actual = await Assert.ThrowsAsync<IOException>(() => fixture.UploadTextAsync("private.txt", "private content"));

        Assert.Same(expected, actual);
        Assert.Single(fixture.Storage.SavedKeys);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.Equal(mode.StartsWith("discard-", StringComparison.Ordinal) ? 1 : 0, fixture.UnitOfWork.DiscardCalls);
        if (loggerFails) { Assert.Empty(fixture.Logger.Entries); return; }
        var warning = Assert.Single(fixture.Logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, warning.Level);
        Assert.Equal(53502, warning.Event.Id);
        Assert.Equal("UploadCompensationSkipped", warning.Event.Name);
        Assert.Equal("Stored file retained after an upload failure with a committed or unknown outcome.", warning.Message);
        Assert.Null(warning.Exception);
        var format = Assert.Single(warning.State);
        Assert.Equal("{OriginalFormat}", format.Key);
        Assert.Equal(warning.Message, format.Value);
    }

    [Fact]
    public async Task WorkspaceFileListRequiresCurrentAuthorization()
    {
        var fixture = new Fixture();
        await fixture.UploadTextAsync("workspace-note.txt", "hello");
        fixture.Authorization.CanViewWorkspaceFilesResult = false;

        var list = await fixture.Service.ListFileObjectsAsync(fixture.WorkspaceId, 1, 20);

        Assert.False(list.IsSuccess);
        Assert.Equal("Workspace not found.", list.Error);
    }

    [Fact]
    public async Task WorkspaceUploadRequiresContributeAuthorization()
    {
        var fixture = new Fixture { Authorization = { CanUploadResult = false } };

        var upload = await fixture.UploadTextAsync("workspace-note.txt", "hello");

        Assert.False(upload.IsSuccess);
        Assert.Empty(fixture.Storage.SavedKeys);
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Files.Attachments);
    }

    [Fact]
    public async Task StorageRevocationRechecksAdmissionBeforeMetadataAndCompensates()
    {
        var fixture = new Fixture();
        fixture.Storage.AfterSave = () => fixture.Authorization.CanUploadResult = false;

        var upload = await fixture.UploadTextAsync("workspace-note.txt", "hello");

        Assert.False(upload.IsSuccess);
        Assert.Equal("You are not allowed to upload an attachment for this resource.", upload.Error);
        Assert.Equal(2, fixture.Authorization.UploadCalls);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Files.Attachments);
    }

    [Fact]
    public async Task CurrentAdmissionExceptionCleansStoredBytesAndPreservesOriginalFailure()
    {
        var fixture = new Fixture();
        var expected = new InvalidOperationException("Current admission unavailable.");
        fixture.Authorization.SubsequentUploadFailure = expected;
        fixture.Storage.DeleteFailure = new IOException("Compensation unavailable.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.UploadTextAsync("workspace-note.txt", "hello"));

        Assert.Same(expected, actual);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Files.Attachments);
    }

    [Fact]
    public async Task CurrentAdmissionCancellationCleansStoredBytesWithUncancelledToken()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Storage.AfterSave = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.UploadTextAsync("workspace-note.txt", "hello", cancellation.Token));

        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.False(fixture.Storage.DeleteTokenCanBeCancelled);
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Files.Attachments);
    }

    [Fact]
    public async Task UploadRejectsOwnerScopeChangeDuringStorageBeforePersistingOriginalScope()
    {
        var fixture = new Fixture();
        fixture.Storage.AfterSave = () => fixture.Files.CurrentOwnerWorkspaceId = Guid.NewGuid();

        var upload = await fixture.UploadTextAsync("workspace-note.txt", "hello");

        Assert.False(upload.IsSuccess);
        Assert.Equal("You are not allowed to upload an attachment for this resource.", upload.Error);
        Assert.Equal(2, fixture.Authorization.UploadCalls);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.Empty(fixture.Files.FileObjects);
        Assert.Empty(fixture.Files.Attachments);
    }

    private sealed class Fixture
    {
        public Fixture(bool loggingEnabled = true)
        {
            UnitOfWork = new FakeUnitOfWork(Files);
            Service = new FileService(
                Files,
                Grants,
                Storage,
                Authorization,
                new FakeUploadPolicy(),
                new FakeFeatureFlags(),
                new FakeQuotaService(),
                new FakeCurrentUser(UserId),
                new FakeCurrentTenant(TenantId),
                new FakeClock(),
                Audit,
                new FakeTokenHasher(),
                new NoopInvalidations(),
                UnitOfWork,
                logger: loggingEnabled ? Logger : null);
        }

        private Guid TenantId { get; } = Guid.NewGuid();
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        private Guid UserId { get; } = Guid.NewGuid();
        public FakeFileRepository Files { get; } = new();
        private FakeFileDownloadGrantRepository Grants { get; } = new();
        public FakeStorage Storage { get; } = new();
        public FakeAuthorization Authorization { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeAuditLogger Audit { get; } = new();
        public UploadCleanupLogger<FileService> Logger { get; } = new();
        public FileService Service { get; }

        public Task<Result<AttachmentResponse>> UploadTextAsync(string fileName, string content, CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return Service.UploadAsync(new AttachmentUploadInput(
                AttachmentOwnerType.Workspace,
                WorkspaceId,
                fileName,
                "text/plain",
                bytes.Length,
                new MemoryStream(bytes)), cancellationToken);
        }
    }

    private sealed class NoopInvalidations : IBusinessInvalidationPublisher
    {
        public Task TaskChangedAsync(TaskItem task, Guid actorUserId, string change, IEnumerable<string>? changedFields = null, IEnumerable<Guid>? affectedUserIds = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ProjectChangedAsync(Project project, Guid actorUserId, string change, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AnnouncementChangedAsync(Announcement announcement, Guid actorUserId, string change, IEnumerable<Guid> audienceUserIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FileChangedAsync(FileObject fileObject, Attachment attachment, Guid actorUserId, string change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeFileRepository : IFileRepository
    {
        // Retain observed Add history for the existing assertions, separately from pending/committed writes.
        public List<FileObject> FileObjects { get; } = [];
        public List<Attachment> Attachments { get; } = [];
        public List<FileObject> PendingFileObjects { get; } = [];
        public List<Attachment> PendingAttachments { get; } = [];
        public List<FileObject> CommittedFileObjects { get; } = [];
        public List<Attachment> CommittedAttachments { get; } = [];
        public Guid? CurrentOwnerWorkspaceId { get; set; }

        public Task<FileObject?> GetFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FileObjects.FirstOrDefault(file => file.Id == fileObjectId));

        public Task<PagedResponse<Attachment>> ListWorkspaceFileObjectsAsync(
            Guid workspaceId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var items = Attachments
                .Where(attachment =>
                    attachment.WorkspaceId == workspaceId &&
                    attachment.OwnerType == AttachmentOwnerType.Workspace &&
                    attachment.OwnerId == workspaceId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult(new PagedResponse<Attachment>(items, page, pageSize, items.Count));
        }

        public Task AddFileObjectAsync(FileObject fileObject, CancellationToken cancellationToken = default)
        {
            fileObject.UploadedByUser = new User { DisplayName = "Fixture User" };
            FileObjects.Add(fileObject);
            PendingFileObjects.Add(fileObject);
            return Task.CompletedTask;
        }

        public Task<Attachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Attachments.FirstOrDefault(attachment => attachment.Id == attachmentId));

        public Task<Attachment?> GetAttachmentByFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Attachments.FirstOrDefault(attachment => attachment.FileObjectId == fileObjectId));

        public Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
        {
            attachment.FileObject = FileObjects.First(file => file.Id == attachment.FileObjectId);
            Attachments.Add(attachment);
            PendingAttachments.Add(attachment);
            return Task.CompletedTask;
        }

        public Task<FileOwnerContext?> ResolveOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ownerType == AttachmentOwnerType.Workspace ? new FileOwnerContext(CurrentOwnerWorkspaceId ?? ownerId) : null);
    }

    private sealed class FakeFileDownloadGrantRepository : IFileDownloadGrantRepository
    {
        public Task<FileDownloadGrant?> GetAsync(Guid fileDownloadGrantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileDownloadGrant?>(null);

        public Task AddAsync(FileDownloadGrant grant, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAuthorization : IFileAuthorizationService
    {
        public bool CanUploadResult { get; set; } = true;
        public bool CanViewWorkspaceFilesResult { get; set; } = true;
        public bool CanDeleteResult { get; set; } = true;
        public int UploadCalls { get; private set; }
        public Exception? SubsequentUploadFailure { get; set; }

        public Task<bool> CanUploadAttachment(Guid userId, AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UploadCalls++;
            if (UploadCalls > 1 && SubsequentUploadFailure is { } failure)
            {
                return Task.FromException<bool>(failure);
            }

            return Task.FromResult(CanUploadResult);
        }

        public Task<bool> CanViewWorkspaceFiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CanViewWorkspaceFilesResult);

        public Task<bool> CanViewAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CanDownloadAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlySet<Guid>> GetDeletableWorkspaceAttachmentIdsAsync(
            Guid userId,
            Guid workspaceId,
            IReadOnlyCollection<Attachment> attachments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(CanDeleteResult
                ? attachments.Select(attachment => attachment.Id).ToHashSet()
                : new HashSet<Guid>());

        public Task<bool> CanDeleteAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) =>
            Task.FromResult(CanDeleteResult);
    }

    private sealed class FakeStorage : IFileStorageService
    {
        public Result SaveResult { get; set; } = Result.Success();
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public Action? AfterSave { get; set; }
        public Action? BeforeDelete { get; set; }
        public Exception? DeleteFailure { get; set; }
        public bool DeleteTokenCanBeCancelled { get; private set; }

        public Task<Result> SaveAsync(string storageKey, Stream stream, string contentType, CancellationToken cancellationToken = default)
        {
            if (SaveResult.IsSuccess)
            {
                SavedKeys.Add(storageKey);
                AfterSave?.Invoke();
            }

            return Task.FromResult(SaveResult);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream("file content"u8.ToArray()));

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            BeforeDelete?.Invoke();
            DeletedKeys.Add(storageKey);
            DeleteTokenCanBeCancelled = cancellationToken.CanBeCanceled;
            if (DeleteFailure is { } failure)
            {
                return Task.FromException(failure);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedKeys.Contains(storageKey));

        public Task<string?> CreateSignedReadUrlAsync(string storageKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeUploadPolicy : IFileUploadPolicy
    {
        public long MaxFileSizeBytes => 1024;
        public IReadOnlyCollection<string> AllowedExtensions => [".txt"];
        public IReadOnlyCollection<string> AllowedContentTypes => ["text/plain"];
    }

    private sealed class FakeFeatureFlags : IFeatureFlagService
    {
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<Result> RequireEnabledAsync(string featureKey, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([FeatureKeys.FileSharing]);
    }

    private sealed class FakeQuotaService : IQuotaService
    {
        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(new TenantUsageSnapshot(tenantId, 0, 0, 0, 0, 0, 0, 0));
        public Task<Result> CanCreateUserAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CanCreateProjectAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CanUploadFileAsync(Guid tenantId, long fileSizeBytes, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CanInviteGuestAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task RecordApiRequestAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record FakeCurrentUser(Guid UserIdValue) : ICurrentUser
    {
        public Guid? UserId => UserIdValue;
        public Guid? SessionId => Guid.NewGuid();
        public string Email => "fixture@example.test";
        public SystemRole? SystemRole => global::Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }

    private sealed record FakeCurrentTenant(Guid TenantIdValue) : ICurrentTenant
    {
        public Guid TenantId => TenantIdValue;
        public bool IsAvailable => true;
        public string TenantSlug => "tenant-a";
        public bool IsPlatformScope => false;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public Exception? Failure { get; set; }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) =>
            Failure is { } error ? Task.FromException(error) : Task.CompletedTask;
    }

    private sealed class FakeTokenHasher : ITokenHasher
    {
        public string HashToken(string token) => $"hashed:{token}";
    }

    private sealed class FakeUnitOfWork(FakeFileRepository files) : IUnitOfWork
    {
        public bool ThrowOnSave { get; set; }
        public Exception? UploadFailure { get; set; }
        public UploadCommitDisposition FailureDisposition { get; set; }
        public bool DiscardRefused { get; set; }
        public bool DiscardFails { get; set; }
        public int DiscardCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("metadata write failed");
            }

            files.CommittedFileObjects.AddRange(files.PendingFileObjects);
            files.CommittedAttachments.AddRange(files.PendingAttachments);
            files.PendingFileObjects.Clear();
            files.PendingAttachments.Clear();
            return Task.FromResult(1);
        }

        public async Task<int> SaveUploadChangesAsync(UploadSaveOutcome outcome, CancellationToken cancellationToken = default)
        {
            if (UploadFailure is { } failure)
            {
                outcome.Disposition = FailureDisposition;
                throw failure;
            }
            // This fake throws before committing or moving any pending rows.
            outcome.Disposition = UploadCommitDisposition.NotCommitted;
            var count = await SaveChangesAsync(cancellationToken);
            outcome.Disposition = UploadCommitDisposition.Committed;
            return count;
        }

        public bool TryDiscardFailedUploadChanges()
        {
            DiscardCalls++;
            if (DiscardFails) throw new IOException("Synthetic private discard canary MUST_NOT_BE_LOGGED.");
            if (DiscardRefused) return false;
            files.PendingFileObjects.Clear();
            files.PendingAttachments.Clear();
            return true;
        }
    }
}
