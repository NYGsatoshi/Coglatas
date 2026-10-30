using System.Security.Cryptography;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Artifacts;

public sealed class ArtifactUploadCompensationTests
{
    [Theory]
    [InlineData("audit")]
    [InlineData("notification")]
    [InlineData("persistence")]
    public async Task PostStorageFailureRemovesOnlyNewBytesAndPreservesOriginalException(string stage)
    {
        await using var fixture = await Fixture.CreateAsync();
        var previous = await fixture.UploadAsync();
        Assert.True(previous.IsSuccess);
        var existingKey = fixture.Storage.SavedKeys.Single();
        var existingVersionId = previous.Value!.Id;
        var expected = new InvalidOperationException("Synthetic post-storage failure.");
        fixture.FailAt(stage, expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UploadAsync());

        Assert.Same(expected, actual);
        var failedKey = fixture.Storage.SavedKeys.Last();
        Assert.NotEqual(existingKey, failedKey);
        Assert.Equal(failedKey, Assert.Single(fixture.Storage.DeletedKeys));
        Assert.False(await fixture.Storage.ExistsAsync(failedKey));
        Assert.True(await fixture.Storage.ExistsAsync(existingKey));
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(existingVersionId, (await fixture.Context.Artifacts.SingleAsync()).CurrentVersionId);
        Assert.Equal(existingVersionId, (await fixture.Context.ArtifactVersions.SingleAsync()).Id);
        Assert.Single(await fixture.Context.FileObjects.ToListAsync());
        Assert.Single(await fixture.Context.Attachments.ToListAsync());
    }

    [Fact]
    public async Task CancellationAfterStorageUsesUncancelledCompensationToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Storage.AfterSave = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.UploadAsync(cancellation.Token));

        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.False(fixture.Storage.DeleteTokenCanBeCancelled);
        Assert.False(await fixture.Storage.ExistsAsync(fixture.Storage.SavedKeys.Single()));
        Assert.Empty(await fixture.Context.ArtifactVersions.ToListAsync());
    }

    [Fact]
    public async Task CleanupFailureDoesNotReplaceOriginalPersistenceFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expected = new InvalidOperationException("Synthetic persistence failure.");
        fixture.FailAt("persistence", expected);
        fixture.Storage.DeleteFailure = new IOException("Synthetic cleanup failure.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UploadAsync());

        Assert.Same(expected, actual);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.True(await fixture.Storage.ExistsAsync(fixture.Storage.SavedKeys.Single()));
    }

    [Fact]
    public async Task SuccessfulUploadRetainsExactBytesAndPersistsMatchingMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.UploadAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(fixture.Storage.DeletedKeys);
        var key = Assert.Single(fixture.Storage.SavedKeys);
        await using var stored = await fixture.Storage.OpenReadAsync(key);
        Assert.Equal(SHA256.HashData(Fixture.Content), await SHA256.HashDataAsync(stored));
        fixture.Context.ChangeTracker.Clear();
        var version = await fixture.Context.ArtifactVersions.SingleAsync();
        var file = await fixture.Context.FileObjects.SingleAsync();
        var attachment = await fixture.Context.Attachments.SingleAsync();
        Assert.Equal(result.Value!.Id, version.Id);
        Assert.Equal(version.Id, (await fixture.Context.Artifacts.SingleAsync()).CurrentVersionId);
        Assert.Equal(file.Id, version.FileObjectId);
        Assert.Equal(file.Id, attachment.FileObjectId);
        Assert.Equal(key, file.StorageKey);
        Assert.Equal(FileScanStatus.Skipped, attachment.ScanStatus);
    }

    [Fact]
    public async Task StorageRejectionAndClosedIntakeDoNotStageMetadataOrDeleteBytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Storage.SaveResult = Result.Failure("Synthetic storage rejection.");
        Assert.False((await fixture.UploadAsync()).IsSuccess);
        fixture.Features.Enabled = false;
        fixture.Storage.SaveResult = Result.Success();
        Assert.False((await fixture.UploadAsync()).IsSuccess);
        Assert.Empty(fixture.Storage.SavedKeys);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.Empty(await fixture.Context.ArtifactVersions.ToListAsync());
        Assert.Empty(await fixture.Context.FileObjects.ToListAsync());
        Assert.Empty(await fixture.Context.Attachments.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly byte[] Content = "Harmless synthetic Artifact bytes."u8.ToArray();
        private readonly string root = Path.Combine(Path.GetTempPath(), "coglatas-artifact-compensation", Guid.NewGuid().ToString("N"));
        private readonly Guid artifactId = Guid.NewGuid();
        private readonly RecordingAudit audit = new();
        private readonly RecordingNotifications notifications = new();
        private readonly Persistence persistence;
        private readonly ArtifactService service;
        public AppDbContext Context { get; }
        public Storage Storage { get; }
        public FeatureFlags Features { get; } = new();

        private Fixture(Guid tenantId, Guid userId)
        {
            Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, new CurrentTenant(tenantId));
            Storage = new Storage(new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = root })));
            persistence = new Persistence(Context);
            service = new ArtifactService(new ArtifactRepository(Context), new ProjectRepository(Context), null!,
                new FileRepository(Context), Storage, new UploadPolicy(), Features, new Quotas(),
                new ArtifactAuthorization(), new CurrentUser(userId), new CurrentTenant(tenantId), new Clock(),
                audit, notifications, persistence);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var fixture = new Fixture(tenantId, userId);
            var projectId = Guid.NewGuid();
            fixture.Context.Tenants.Add(new Tenant(tenantId) { Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic", Status = TenantStatus.Active });
            await fixture.Context.SaveChangesAsync();
            fixture.Context.Users.Add(new User { Id = userId, DisplayName = "Synthetic actor", Email = "synthetic@example.invalid", NormalizedEmail = "SYNTHETIC@EXAMPLE.INVALID", Status = UserStatus.Active });
            fixture.Context.Projects.Add(new Project { Id = projectId, TenantId = tenantId, WorkspaceId = Guid.NewGuid(), OwnerUserId = userId, CreatedByUserId = userId, Name = "Synthetic", Slug = "synthetic" });
            fixture.Context.ProjectMembers.Add(new ProjectMember { TenantId = tenantId, ProjectId = projectId, UserId = userId, Role = ProjectRole.Owner });
            fixture.Context.Artifacts.Add(new Artifact { Id = fixture.artifactId, TenantId = tenantId, ProjectId = projectId, Name = "Synthetic", CreatedByUserId = userId });
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }

        public async Task<Result<ArtifactVersionResponse>> UploadAsync(CancellationToken cancellationToken = default)
        {
            await using var content = new MemoryStream(Content);
            return await service.UploadVersionAsync(artifactId, new UploadArtifactVersionInput("synthetic.txt", "text/plain", Content.Length, content, null), cancellationToken);
        }

        public void FailAt(string stage, Exception error)
        {
            if (stage == "audit") { audit.Failure = error; }
            else if (stage == "notification") { notifications.Failure = error; }
            else if (stage == "persistence") { persistence.Failure = error; }
            else { throw new ArgumentOutOfRangeException(nameof(stage)); }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        }
    }

    private sealed class Storage(IFileStorageService inner) : IFileStorageService
    {
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public Result SaveResult { get; set; } = Result.Success();
        public Action? AfterSave { get; set; }
        public Exception? DeleteFailure { get; set; }
        public bool DeleteTokenCanBeCancelled { get; private set; }
        public async Task<Result> SaveAsync(string key, Stream stream, string contentType, CancellationToken cancellationToken = default)
        {
            if (!SaveResult.IsSuccess) { return SaveResult; }
            var result = await inner.SaveAsync(key, stream, contentType, cancellationToken);
            if (result.IsSuccess) { SavedKeys.Add(key); AfterSave?.Invoke(); }
            return result;
        }
        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            DeleteTokenCanBeCancelled = cancellationToken.CanBeCanceled;
            if (DeleteFailure is { } error) { throw error; }
            await inner.DeleteAsync(key, cancellationToken);
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);
        public Task<string?> CreateSignedReadUrlAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default) => inner.CreateSignedReadUrlAsync(key, expiresIn, cancellationToken);
    }
    private sealed class Persistence(AppDbContext context) : IUnitOfWork
    {
        public Exception? Failure { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Failure is { } error
            ? Task.FromException<int>(error) : context.SaveChangesAsync(cancellationToken);
    }
    private sealed class RecordingAudit : IAuditLogger
    {
        public Exception? Failure { get; set; }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Failure is { } error ? Task.FromException(error) : Task.CompletedTask;
    }
    private sealed class RecordingNotifications : INotificationService
    {
        public Exception? Failure { get; set; }
        public Task NotifyAsync(Guid userId, string title, string? body, string sourceType, Guid sourceId, CancellationToken cancellationToken = default) => Failure is { } error ? Task.FromException(error) : Task.CompletedTask;
    }
    private sealed class FeatureFlags : IFeatureFlagService
    {
        public bool Enabled { get; set; } = true;
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Enabled);
        public Task<Result> RequireEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Enabled ? Result.Success() : Result.Failure("Intake closed."));
        public Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Enabled ? [FeatureKeys.FileSharing] : []);
    }
    private sealed class UploadPolicy : IFileUploadPolicy
    {
        public long MaxFileSizeBytes => 1024;
        public IReadOnlyCollection<string> AllowedExtensions => [".txt"];
        public IReadOnlyCollection<string> AllowedContentTypes => ["text/plain"];
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
        public string Email => "synthetic@example.invalid";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero); }
    private sealed class ArtifactAuthorization : IArtifactAuthorizationService
    {
        public Task<bool> CanViewArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanUploadArtifact(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanUpdateArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanDownloadArtifactVersion(Guid userId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}