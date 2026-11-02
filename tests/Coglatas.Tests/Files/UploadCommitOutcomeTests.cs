using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Files;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Coglatas.Tests.Files;

/// <summary>Real owned PostgreSQL commits followed by synthetic acknowledgement loss; not a production network reproduction.</summary>
[Collection("PostgreSqlTaskV1")]
public sealed class UploadCommitOutcomeTests(ITestOutputHelper output)
{
    [PostgreSqlFact]
    public Task ArtifactUploadRetainsBytesWhenCommittedMetadataAcknowledgementIsLost() => VerifyAsync("artifact");

    [PostgreSqlFact]
    public Task FileUploadRetainsBytesWhenCommittedMetadataAcknowledgementIsLost() => VerifyAsync("file");

    private Task VerifyAsync(string consumer) => PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
        PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var fixture = await Fixture.CreateAsync(database, consumer, output);
            await fixture.UploadAsync(Fixture.PreviousBytes);
            var previousKey = Assert.Single(fixture.Storage.SavedKeys);
            await fixture.Storage.SeedSentinelAsync(fixture.SentinelKey, Fixture.SentinelBytes);
            fixture.Persistence.LoseNextAcknowledgement = true;

            Exception? observed = null;
            try { await fixture.UploadAsync(Fixture.NewBytes); }
            catch (Exception error) { observed = error; }

            var committedKey = fixture.Storage.SavedKeys.Last();
            var witness = Assert.IsType<DurableUpload>(fixture.CommitWitness);
            var afterFailure = await fixture.ReadDurableUploadAsync(committedKey);
            var committedBytesExist = await fixture.Storage.ExistsAsync(committedKey);
            var previousHash = await fixture.Storage.HashAsync(previousKey);
            var sentinelHash = await fixture.Storage.HashAsync(fixture.SentinelKey);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                scope = "OWNED_POSTGRESQL_REAL_COMMIT_SYNTHETIC_ACKNOWLEDGEMENT_LOSS",
                consumer, fixture.Persistence.CommitVerifiedBeforeFault,
                originalExceptionPreserved = ReferenceEquals(fixture.Persistence.Failure, observed),
                witness, afterFailure, committedBytesExist,
                deletedKeys = fixture.Storage.DeletedKeys.ToArray(),
                previousBytesPreserved = previousHash.SequenceEqual(SHA256.HashData(Fixture.PreviousBytes)),
                sentinelBytesPreserved = sentinelHash.SequenceEqual(SHA256.HashData(Fixture.SentinelBytes))
            }));

            Assert.Same(fixture.Persistence.Failure, observed);
            Assert.True(fixture.Persistence.CommitVerifiedBeforeFault);
            Assert.NotEqual(previousKey, committedKey);
            Assert.Equal(witness, afterFailure);
            Assert.Equal(2, afterFailure.FileRows);
            Assert.Equal(2, afterFailure.AttachmentRows);
            Assert.Equal(consumer == "artifact" ? 2 : 0, afterFailure.VersionRows);
            if (consumer == "artifact") Assert.Equal(afterFailure.VersionId, afterFailure.CurrentVersionId);
            Assert.Equal(SHA256.HashData(Fixture.PreviousBytes), previousHash);
            Assert.Equal(SHA256.HashData(Fixture.SentinelBytes), sentinelHash);
            Assert.DoesNotContain(previousKey, fixture.Storage.DeletedKeys);
            Assert.DoesNotContain(fixture.SentinelKey, fixture.Storage.DeletedKeys);
            Assert.All(fixture.Storage.DeletedKeys, key => Assert.Equal(committedKey, key));
            Assert.True(committedBytesExist, "A durably referenced upload lost its stored object after synthetic acknowledgement loss.");
            Assert.Equal(SHA256.HashData(Fixture.NewBytes), await fixture.Storage.HashAsync(committedKey));
            Assert.Empty(fixture.Storage.DeletedKeys);
        });

    private sealed record DurableUpload(Guid FileId, Guid AttachmentId, Guid? VersionId, Guid? CurrentVersionId,
        string StorageKey, int FileRows, int AttachmentRows, int VersionRows);

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly byte[] PreviousBytes = "Previously committed owned upload."u8.ToArray();
        public static readonly byte[] NewBytes = "New durably committed owned upload."u8.ToArray();
        public static readonly byte[] SentinelBytes = "Unrelated owned tenant-namespace sentinel."u8.ToArray();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "coglatas-upload-commit-outcome", Guid.NewGuid().ToString("N"));
        private readonly Guid _tenantId = Guid.NewGuid();
        private readonly Guid _actorId = Guid.NewGuid();
        private readonly Guid _workspaceId = Guid.NewGuid();
        private readonly Guid _artifactId = Guid.NewGuid();
        private readonly string _consumer;
        private readonly ITestOutputHelper _output;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly AppDbContext _context;
        private readonly ArtifactService _artifacts;
        private readonly FileService _files;
        public Storage Storage { get; }
        public LostAcknowledgementPersistence Persistence { get; }
        public DurableUpload? CommitWitness { get; private set; }
        public string SentinelKey { get; } = $"tenants/{Guid.NewGuid():D}/files/{Guid.NewGuid():D}";

        private Fixture(string database, string consumer, ITestOutputHelper testOutput)
        {
            _consumer = consumer;
            _output = testOutput;
            _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
            _context = Context();
            Storage = new Storage(new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = _directory })));
            Persistence = new LostAcknowledgementPersistence(new EfUnitOfWork(_context), VerifyCommittedAsync);
            var user = new Actor(_actorId);
            var tenant = new TenantScope(_tenantId);
            var repository = new FileRepository(_context);
            var projectRepository = new ProjectRepository(_context);
            var workspaceRepository = new WorkspaceRepository(_context);
            var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(_context), workspaceRepository);
            var groups = new GroupRepository(_context);
            var groupAuthorization = new GroupAuthorizationService(groups, workspaceRepository, workspaceAuthorization);
            var projectAuthorization = new ProjectAuthorizationService(projectRepository, workspaceAuthorization, groupAuthorization, groups);
            _artifacts = new ArtifactService(new ArtifactRepository(_context), projectRepository, projectAuthorization,
                repository, Storage, new UploadPolicy(), new FeatureFlags(), new Quotas(), new ArtifactAuthorization(),
                user, tenant, new Clock(), new Audit(), new Notifications(), Persistence);
            _files = new FileService(repository, new FileDownloadGrantRepository(_context), Storage, new FileAuthorization(),
                new UploadPolicy(), new FeatureFlags(), new Quotas(), user, tenant, new Clock(), new Audit(),
                new Sha256TokenHasher(), new Invalidations(), Persistence);
        }

        public static async Task<Fixture> CreateAsync(string database, string consumer, ITestOutputHelper testOutput)
        {
            var fixture = new Fixture(database, consumer, testOutput);
            try
            {
                var projectId = Guid.NewGuid();
                fixture._context.Tenants.Add(new Tenant(fixture._tenantId)
                {
                    Name = "Owned synthetic Tenant", DisplayName = "Owned synthetic Tenant", Slug = "owned-synthetic", Status = TenantStatus.Active
                });
                await fixture._context.SaveChangesAsync();
                fixture._context.Users.Add(new User { Id = fixture._actorId, DisplayName = "Synthetic actor", Email = "commit@example.invalid", NormalizedEmail = "COMMIT@EXAMPLE.INVALID", Status = UserStatus.Active });
                fixture._context.Workspaces.Add(new Workspace { Id = fixture._workspaceId, TenantId = fixture._tenantId, Name = "Owned synthetic Workspace", Slug = "owned-synthetic", CreatedByUserId = fixture._actorId });
                fixture._context.WorkspaceMembers.Add(new WorkspaceMember { TenantId = fixture._tenantId, WorkspaceId = fixture._workspaceId, UserId = fixture._actorId, Role = WorkspaceRole.Owner, Status = MembershipStatus.Active });
                fixture._context.Projects.Add(new Project { Id = projectId, TenantId = fixture._tenantId, WorkspaceId = fixture._workspaceId, OwnerUserId = fixture._actorId, CreatedByUserId = fixture._actorId, Name = "Owned synthetic Project", Slug = "owned-synthetic" });
                fixture._context.ProjectMembers.Add(new ProjectMember { TenantId = fixture._tenantId, ProjectId = projectId, UserId = fixture._actorId, Role = ProjectRole.Owner });
                fixture._context.Artifacts.Add(new Artifact { Id = fixture._artifactId, TenantId = fixture._tenantId, ProjectId = projectId, Name = "Owned synthetic Artifact", CreatedByUserId = fixture._actorId });
                await fixture._context.SaveChangesAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        private AppDbContext Context() => new(_options, new TenantScope(_tenantId));

        public async Task UploadAsync(byte[] bytes)
        {
            await using var content = new MemoryStream(bytes);
            if (_consumer == "artifact")
            {
                var result = await _artifacts.UploadVersionAsync(_artifactId, new UploadArtifactVersionInput("owned.txt", "text/plain", bytes.Length, content, null));
                Assert.True(result.IsSuccess, result.Error);
                Assert.IsType<ArtifactVersionResponse>(result.Value);
            }
            else
            {
                var result = await _files.UploadAsync(new AttachmentUploadInput(AttachmentOwnerType.Workspace, _workspaceId, "owned.txt", "text/plain", bytes.Length, content));
                Assert.True(result.IsSuccess, result.Error);
                Assert.IsType<AttachmentResponse>(result.Value);
            }
        }

        private async Task VerifyCommittedAsync()
        {
            Assert.Null(_context.Database.CurrentTransaction);
            CommitWitness = await ReadDurableUploadAsync(Storage.SavedKeys.Last());
        }

        public async Task<DurableUpload> ReadDurableUploadAsync(string key)
        {
            await using var verification = Context();
            Assert.NotSame(_context, verification);
            Assert.NotEqual(_context.ContextId, verification.ContextId);
            Assert.True(verification.Database.IsNpgsql());
            var file = await verification.FileObjects.AsNoTracking().SingleAsync(item => item.StorageKey == key);
            var attachment = await verification.Attachments.AsNoTracking().SingleAsync(item => item.FileObjectId == file.Id);
            var version = await verification.ArtifactVersions.AsNoTracking().SingleOrDefaultAsync(item => item.FileObjectId == file.Id);
            var artifact = await verification.Artifacts.AsNoTracking().SingleAsync(item => item.Id == _artifactId);
            Assert.Equal(file.TenantId, attachment.TenantId);
            Assert.Equal(file.StorageKey, attachment.StorageKey);
            if (version is not null)
            {
                Assert.Equal(version.Id, attachment.OwnerId);
                Assert.Equal(file.Id, version.FileObjectId);
                Assert.Equal(file.TenantId, version.TenantId);
            }
            return new DurableUpload(file.Id, attachment.Id, version?.Id, artifact.CurrentVersionId, file.StorageKey,
                await verification.FileObjects.CountAsync(), await verification.Attachments.CountAsync(), await verification.ArtifactVersions.CountAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            var intendedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "coglatas-upload-commit-outcome"));
            var owned = Path.GetFullPath(_directory);
            Assert.Equal(intendedParent, Path.GetDirectoryName(owned));
            if (Directory.Exists(owned)) Directory.Delete(owned, recursive: true);
            _output.WriteLine(JsonSerializer.Serialize(new { scope = "OWNED_FIXTURE_TEARDOWN", directoryRemoved = !Directory.Exists(owned) }));
        }
    }

    private sealed class LostAcknowledgementPersistence(IUnitOfWork inner, Func<Task> verifyCommitted) : IUnitOfWork
    {
        public bool LoseNextAcknowledgement { get; set; }
        public bool CommitVerifiedBeforeFault { get; private set; }
        public IOException Failure { get; } = new("Synthetic lost commit acknowledgement.");
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var loseAcknowledgement = LoseNextAcknowledgement;
            LoseNextAcknowledgement = false;
            var count = await inner.SaveChangesAsync(cancellationToken);
            if (!loseAcknowledgement) return count;
            await verifyCommitted();
            CommitVerifiedBeforeFault = true;
            throw Failure;
        }
    }

    private sealed class Storage(IFileStorageService inner) : IFileStorageService
    {
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public async Task<Result> SaveAsync(string key, Stream stream, string type, CancellationToken cancellationToken = default)
        {
            var result = await inner.SaveAsync(key, stream, type, cancellationToken);
            if (result.IsSuccess) SavedKeys.Add(key);
            return result;
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);
        public Task<string?> CreateSignedReadUrlAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default) => inner.CreateSignedReadUrlAsync(key, expiresIn, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return inner.DeleteAsync(key, cancellationToken);
        }
        public async Task SeedSentinelAsync(string key, byte[] bytes)
        {
            await using var stream = new MemoryStream(bytes);
            Assert.True((await inner.SaveAsync(key, stream, "text/plain")).IsSuccess);
        }
        public async Task<byte[]> HashAsync(string key)
        {
            await using var stream = await inner.OpenReadAsync(key);
            return await SHA256.HashDataAsync(stream);
        }
    }

    private sealed class TenantScope(Guid id) : ICurrentTenant
    {
        public Guid TenantId => id;
        public string TenantSlug => "owned-synthetic";
        public bool IsAvailable => true;
        public bool IsPlatformScope => false;
    }
    private sealed class Actor(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid? SessionId => null;
        public string? Email => "commit@example.invalid";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero); }
    private sealed class Audit : IAuditLogger
    {
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Notifications : INotificationService
    {
        public Task NotifyAsync(Guid userId, string title, string? body, string sourceType, Guid sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Invalidations : IBusinessInvalidationPublisher
    {
        public Task TaskChangedAsync(TaskItem task, Guid actorUserId, string change, IEnumerable<string>? changedFields = null, IEnumerable<Guid>? affectedUserIds = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ProjectChangedAsync(Project project, Guid actorUserId, string change, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AnnouncementChangedAsync(Announcement announcement, Guid actorUserId, string change, IEnumerable<Guid> audienceUserIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FileChangedAsync(FileObject fileObject, Attachment attachment, Guid actorUserId, string change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class UploadPolicy : IFileUploadPolicy
    {
        public long MaxFileSizeBytes => 1024;
        public IReadOnlyCollection<string> AllowedExtensions => [".txt"];
        public IReadOnlyCollection<string> AllowedContentTypes => ["text/plain"];
    }
    private sealed class FeatureFlags : IFeatureFlagService
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<Result> RequireEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([FeatureKeys.FileSharing]);
    }
    private sealed class Quotas : IQuotaService
    {
        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(new TenantUsageSnapshot(tenantId, 0, 0, 0, 0, 0, 0, 0));
        public Task<Result> CanCreateUserAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CanCreateProjectAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CanUploadFileAsync(Guid tenantId, long bytes, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CanInviteGuestAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task RecordApiRequestAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class ArtifactAuthorization : IArtifactAuthorizationService
    {
        public Task<bool> CanViewArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanUploadArtifact(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanUpdateArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanDownloadArtifactVersion(Guid userId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class FileAuthorization : IFileAuthorizationService
    {
        public Task<bool> CanUploadAttachment(Guid userId, AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanViewWorkspaceFiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanViewAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanDownloadAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlySet<Guid>> GetDeletableWorkspaceAttachmentIdsAsync(Guid userId, Guid workspaceId, IReadOnlyCollection<Attachment> attachments, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        public Task<bool> CanDeleteAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
