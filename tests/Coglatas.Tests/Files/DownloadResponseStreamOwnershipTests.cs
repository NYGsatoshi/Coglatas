using System.Security.Cryptography;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Files;

// Actual services/repositories and owned local bytes; synthetic authority and InMemory are not runtime security qualification.
public sealed class DownloadResponseStreamOwnershipTests
{
    [Theory]
    [InlineData("artifact")]
    [InlineData("file")]
    public async Task DisposalFailureWithoutLoggerPreservesOriginalErrorAndStoredBytes(string consumer)
    {
        await using var fixture = await Fixture.CreateAsync(withLogger: false);
        var expected = new InvalidOperationException("Synthetic primary download failure.");
        fixture.FailAt("audit", expected);
        fixture.Storage.FailDispose = true;

        var attempt = await CaptureDownloadAsync(fixture, consumer);

        Assert.Same(expected, attempt.Error);
        Assert.Null(attempt.Result);
        Assert.Equal(1, Assert.IsType<DisposalFaultStream>(fixture.Storage.ResponseWrapper).Attempts);
        Assert.Equal(0, fixture.ArtifactLogger.Attempts);
        Assert.Equal(0, fixture.FileLogger.Attempts);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.True(await fixture.Storage.ExistsAsync(fixture.StorageKey));
        Assert.Equal(Fixture.Content, await fixture.Storage.ReadStoredBytesAsync(fixture.StorageKey));
    }

    [Theory]
    [InlineData("artifact", "audit", false)]
    [InlineData("artifact", "persistence", false)]
    [InlineData("file", "audit", false)]
    [InlineData("file", "persistence", false)]
    [InlineData("artifact", "audit", true)]
    [InlineData("artifact", "persistence", true)]
    [InlineData("file", "audit", true)]
    [InlineData("file", "persistence", true)]
    public async Task DisposalOrWarningFailurePreservesPrimaryErrorAndStoredBytes(string consumer, string stage, bool warningFails)
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalBytes = await fixture.Storage.ReadStoredBytesAsync(fixture.StorageKey);
        var expected = new InvalidOperationException("Synthetic primary download failure.");
        fixture.FailAt(stage, expected);
        fixture.Storage.FailDispose = true;
        fixture.ArtifactLogger.Fail = warningFails;
        fixture.FileLogger.Fail = warningFails;

        var attempt = await CaptureDownloadAsync(fixture, consumer);

        Assert.Same(expected, attempt.Error);
        Assert.Null(attempt.Result);
        Assert.Equal(1, Assert.IsType<DisposalFaultStream>(fixture.Storage.ResponseWrapper).Attempts);
        Assert.Single(fixture.Storage.Opened);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.True(await fixture.Storage.ExistsAsync(fixture.StorageKey));
        Assert.Equal(Fixture.Content, originalBytes);
        Assert.Equal(originalBytes, await fixture.Storage.ReadStoredBytesAsync(fixture.StorageKey));
        var attempts = consumer == "artifact" ? fixture.ArtifactLogger.Attempts : fixture.FileLogger.Attempts;
        var entries = consumer == "artifact" ? fixture.ArtifactLogger.Entries : fixture.FileLogger.Entries;
        Assert.Equal(1, attempts);
        Assert.Equal(0, consumer == "artifact" ? fixture.FileLogger.Attempts : fixture.ArtifactLogger.Attempts);
        if (warningFails) Assert.Empty(entries);
        else
        {
            var warning = Assert.Single(entries);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal(0, warning.Event.Id);
            Assert.Null(warning.Event.Name);
            Assert.Equal("Download response stream cleanup failed.", warning.Message);
            Assert.Null(warning.Exception);
            var state = Assert.Single(warning.State);
            Assert.Equal("{OriginalFormat}", state.Key);
            Assert.Equal(warning.Message, state.Value);
        }
    }

    [Theory]
    [InlineData("artifact", "audit")]
    [InlineData("artifact", "persistence")]
    [InlineData("file", "audit")]
    [InlineData("file", "persistence")]
    public async Task FailedDownloadDisposesUntransferredStreamAndPreservesPrimaryFailure(string consumer, string stage)
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalBytes = await fixture.Storage.ReadStoredBytesAsync(fixture.StorageKey);
        var originalAuditCalls = fixture.Audit.Calls;
        var originalSaveCalls = fixture.Persistence.Calls;
        var expected = new InvalidOperationException("Synthetic post-open download failure.");
        fixture.FailAt(stage, expected);

        var attempt = await CaptureDownloadAsync(fixture, consumer);

        Assert.Same(expected, attempt.Error);
        Assert.Null(attempt.Result);
        var opened = Assert.Single(fixture.Storage.Opened);
        Assert.Equal(originalAuditCalls + (stage == "audit" || consumer == "artifact" ? 1 : 2), fixture.Audit.Calls);
        Assert.Equal(originalSaveCalls + (stage == "persistence" ? 1 : 0), fixture.Persistence.Calls);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.True(await fixture.Storage.ExistsAsync(fixture.StorageKey));
        Assert.Equal(Fixture.Content, originalBytes);
        Assert.Equal(originalBytes, await fixture.Storage.ReadStoredBytesAsync(fixture.StorageKey));
        Assert.False(opened.CanRead);
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("file")]
    public async Task SuccessfulDownloadTransfersLiveStreamAndPreservesStoredBytes(string consumer)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.DownloadAsync(consumer);

        Assert.True(result.IsSuccess, result.Error);
        var response = Assert.IsType<FileDownloadResponse>(result.Value);
        var opened = Assert.Single(fixture.Storage.Opened);
        Assert.Same(opened, response.Content);
        Assert.True(opened.CanRead);
        Assert.Equal("synthetic.txt", response.FileName);
        Assert.Equal("text/plain", response.ContentType);
        Assert.Equal(Fixture.Content.Length, response.SizeBytes);
        await using (response.Content)
        {
            Assert.Equal(SHA256.HashData(Fixture.Content), await SHA256.HashDataAsync(response.Content));
        }
        Assert.False(opened.CanRead);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.Equal(Fixture.Content, await fixture.Storage.ReadStoredBytesAsync(fixture.StorageKey));
    }

    private static async Task<(Result<FileDownloadResponse>? Result, Exception? Error)> CaptureDownloadAsync(Fixture fixture, string consumer)
    {
        try { return (await fixture.DownloadAsync(consumer), null); }
        catch (Exception error) { return (null, error); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly byte[] Content = [.. "Owned synthetic download bytes."u8];
        private readonly string _root = Path.Combine(Path.GetTempPath(), "coglatas-download-streams", Guid.NewGuid().ToString("N"));
        private readonly Guid _tenantId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly AppDbContext _context;
        private readonly ArtifactService _artifacts;
        private readonly FileService _files;
        private Guid _versionId;
        private FileDownloadGrantResponse _grant = null!;
        public string StorageKey { get; private set; } = null!;
        public TrackingStorage Storage { get; }
        public RecordingAudit Audit { get; } = new();
        public RecordingPersistence Persistence { get; }
        public RecordingDownloadLogger<ArtifactService> ArtifactLogger { get; } = new();
        public RecordingDownloadLogger<FileService> FileLogger { get; } = new();

        private Fixture(bool withLogger)
        {
            var tenant = new CurrentTenant(_tenantId);
            var user = new CurrentUser(_userId);
            _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, tenant);
            Persistence = new RecordingPersistence(_context);
            Storage = new TrackingStorage(new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = _root })));
            var repository = new FileRepository(_context);
            _artifacts = new ArtifactService(new ArtifactRepository(_context), new ProjectRepository(_context), null!,
                repository, Storage, new UploadPolicy(), new FeatureFlags(), new Quotas(), new ArtifactAuthorization(),
                user, tenant, new Clock(), Audit, new Notifications(), Persistence, logger: withLogger ? ArtifactLogger : null);
            _files = new FileService(repository, new FileDownloadGrantRepository(_context), Storage, new FileAuthorization(),
                new UploadPolicy(), new FeatureFlags(), new Quotas(), user, tenant, new Clock(), Audit,
                new Sha256TokenHasher(), null!, Persistence, logger: withLogger ? FileLogger : null);
        }

        public static async Task<Fixture> CreateAsync(bool withLogger = true)
        {
            var fixture = new Fixture(withLogger);
            var workspaceId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var artifactId = Guid.NewGuid();
            fixture._context.Tenants.Add(new Tenant(fixture._tenantId)
            { Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic", Status = TenantStatus.Active });
            await fixture._context.SaveChangesAsync();
            fixture._context.Users.Add(new User { Id = fixture._userId, DisplayName = "Synthetic actor", Email = "download@example.invalid", NormalizedEmail = "DOWNLOAD@EXAMPLE.INVALID", Status = UserStatus.Active });
            fixture._context.Workspaces.Add(new Workspace { Id = workspaceId, TenantId = fixture._tenantId, Name = "Synthetic", Slug = "synthetic", CreatedByUserId = fixture._userId });
            fixture._context.Projects.Add(new Project { Id = projectId, TenantId = fixture._tenantId, WorkspaceId = workspaceId, OwnerUserId = fixture._userId, CreatedByUserId = fixture._userId, Name = "Synthetic", Slug = "synthetic" });
            fixture._context.ProjectMembers.Add(new ProjectMember { TenantId = fixture._tenantId, ProjectId = projectId, UserId = fixture._userId, Role = ProjectRole.Owner });
            fixture._context.Artifacts.Add(new Artifact { Id = artifactId, TenantId = fixture._tenantId, ProjectId = projectId, Name = "Synthetic", CreatedByUserId = fixture._userId });
            await fixture._context.SaveChangesAsync();
            await using var content = new MemoryStream(Content);
            var upload = await fixture._artifacts.UploadVersionAsync(artifactId,
                new UploadArtifactVersionInput("synthetic.txt", "text/plain", Content.Length, content, null));
            Assert.True(upload.IsSuccess, upload.Error);
            fixture._versionId = upload.Value!.Id;
            fixture.StorageKey = Assert.Single(fixture.Storage.SavedKeys);
            var attachment = await fixture._context.Attachments.SingleAsync();
            var grant = await fixture._files.RequestDownloadGrantAsync(attachment.Id, new FileDownloadGrantRequest("Synthetic ownership proof"));
            Assert.True(grant.IsSuccess, grant.Error);
            fixture._grant = grant.Value!;
            return fixture;
        }

        public Task<Result<FileDownloadResponse>> DownloadAsync(string consumer) => consumer switch
        {
            "artifact" => _artifacts.DownloadVersionAsync(_versionId),
            "file" => _files.DownloadWithGrantAsync(_grant.FileDownloadGrantId, _grant.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };

        public void FailAt(string stage, Exception error)
        {
            if (stage == "audit") { Audit.Failure = error; }
            else if (stage == "persistence") { Persistence.Failure = error; }
            else { throw new ArgumentOutOfRangeException(nameof(stage)); }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var stream in Storage.Opened) { await stream.DisposeAsync(); }
            await _context.DisposeAsync();
            if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
        }
    }

    private sealed class TrackingStorage(IFileStorageService inner) : IFileStorageService
    {
        public List<Stream> Opened { get; } = [];
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public bool FailDispose { get; set; }
        public DisposalFaultStream? ResponseWrapper { get; private set; }
        public async Task<Result> SaveAsync(string key, Stream stream, string type, CancellationToken cancellationToken = default)
        {
            var result = await inner.SaveAsync(key, stream, type, cancellationToken);
            if (result.IsSuccess) { SavedKeys.Add(key); }
            return result;
        }
        public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            var stream = await inner.OpenReadAsync(key, cancellationToken);
            Opened.Add(stream);
            if (FailDispose)
            {
                ResponseWrapper = new DisposalFaultStream(stream);
                return ResponseWrapper;
            }
            return stream;
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return inner.DeleteAsync(key, cancellationToken);
        }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);
        public Task<string?> CreateSignedReadUrlAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default) => inner.CreateSignedReadUrlAsync(key, expiresIn, cancellationToken);
        public async Task<byte[]> ReadStoredBytesAsync(string key)
        {
            await using var source = await inner.OpenReadAsync(key);
            await using var bytes = new MemoryStream();
            await source.CopyToAsync(bytes);
            return bytes.ToArray();
        }
    }

    private sealed class DisposalFaultStream(Stream inner) : Stream
    {
        public int Attempts { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync()
        {
            Attempts++;
            return ValueTask.FromException(new IOException("Synthetic private stream disposal detail."));
        }
    }

    private sealed class RecordingDownloadLogger<T> : ILogger<T>
    {
        public bool Fail { get; set; }
        public int Attempts { get; private set; }
        public List<(LogLevel Level, EventId Event, string Message, Exception? Exception, KeyValuePair<string, object?>[] State)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Attempts++;
            if (Fail) throw new IOException("Synthetic warning failure.");
            Entries.Add((logLevel, eventId, formatter(state, exception), exception,
                state is IEnumerable<KeyValuePair<string, object?>> values ? [.. values] : []));
        }
    }

    private sealed class RecordingAudit : IAuditLogger
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is { } error ? Task.FromException(error) : Task.CompletedTask;
        }
    }

    private sealed class RecordingPersistence(AppDbContext context) : IUnitOfWork
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is { } error ? Task.FromException<int>(error) : context.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CurrentTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid TenantId => tenantId;
        public string TenantSlug => "synthetic";
        public bool IsAvailable => true;
        public bool IsPlatformScope => false;
    }
    private sealed class CurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string Email => "download@example.invalid";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero); }
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
    private sealed class Notifications : INotificationService
    {
        public Task NotifyAsync(Guid userId, string title, string? body, string sourceType, Guid sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        public Task<bool> CanDeleteAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlySet<Guid>> GetDeletableWorkspaceAttachmentIdsAsync(Guid userId, Guid workspaceId, IReadOnlyCollection<Attachment> attachments, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<Guid>>(attachments.Select(attachment => attachment.Id).ToHashSet());
    }
}
