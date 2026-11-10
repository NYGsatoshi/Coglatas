using System.Collections.Concurrent;
using System.Security.Cryptography;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Files;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Coglatas.Tests.Files;
using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.Artifacts;

[Collection("PostgreSqlTaskV1")]
public sealed class ArtifactUploadCompensationTests
{
    [PostgreSqlFact]
    public Task SuccessfulCallerOwnedUploadRetainsBytesAfterCommit() => VerifySuccessfulCallerOwnedUploadAsync(commit: true);

    [PostgreSqlFact]
    public Task SuccessfulCallerOwnedUploadRetainsBytesAfterRollback() => VerifySuccessfulCallerOwnedUploadAsync(commit: false);

    private static Task VerifySuccessfulCallerOwnedUploadAsync(bool commit) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var token = deadline.Token;
                await using var fixture = await Fixture.CreateAsync(connectionString: database, actualPersistence: true, persistAudit: true);
                var original = await fixture.UploadAsync(token);
                Assert.True(original.IsSuccess, original.Error);
                var originalVersion = original.Value!;
                var originalKey = Assert.Single(fixture.Storage.SavedKeys);
                var workspace = await fixture.Context.Workspaces.SingleAsync(token);
                var originalWorkspaceName = workspace.Name;
                const string callerWorkspaceName = "Caller transaction edit";

                await using var caller = await fixture.Context.Database.BeginTransactionAsync(token);
                workspace.Name = callerWorkspaceName;
                await fixture.Context.SaveChangesAsync(token);

                fixture.UploadContent = "Different synthetic caller-owned Artifact bytes."u8.ToArray();
                var uploaded = await fixture.UploadAsync(token);
                Assert.True(uploaded.IsSuccess, uploaded.Error);
                var newVersion = uploaded.Value!;
                Assert.Equal(originalVersion.VersionNumber + 1, newVersion.VersionNumber);
                var newKey = fixture.Storage.SavedKeys.Last();
                Assert.Equal(2, fixture.Storage.SavedKeys.Count);
                Assert.NotEqual(originalKey, newKey);
                Assert.Same(caller, fixture.Context.Database.CurrentTransaction);
                Assert.Same(workspace, fixture.Context.Workspaces.Local.Single());
                Assert.Equal(callerWorkspaceName, workspace.Name);

                var stagedVersion = await fixture.Context.ArtifactVersions.AsNoTracking()
                    .SingleAsync(version => version.Id == newVersion.Id, token);
                Assert.Equal(newVersion.Id, (await fixture.Context.Artifacts.AsNoTracking().SingleAsync(token)).CurrentVersionId);
                Assert.True(await fixture.Context.AuditLogs.AnyAsync(log => log.EntityId == newVersion.Id, token));
                var fileId = Assert.IsType<Guid>(stagedVersion.FileObjectId);
                var attachmentId = stagedVersion.AttachmentId;
                await AssertExactStoredBytesAsync(fixture.Storage, newKey, fixture.UploadContent, token);
                Assert.Empty(fixture.Storage.DeletedKeys);

                var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
                var tenant = new CurrentTenant(workspace.TenantId);
                await using (var observer = new AppDbContext(options, tenant))
                {
                    await AssertVisibilityAsync(observer, false);
                }

                if (commit) await caller.CommitAsync(token);
                else await caller.RollbackAsync(token);

                await using var verifier = new AppDbContext(options, tenant);
                await AssertVisibilityAsync(verifier, commit);
                var retainedOriginal = await verifier.ArtifactVersions.AsNoTracking()
                    .Include(version => version.FileObject)
                    .SingleAsync(version => version.Id == originalVersion.Id, token);
                Assert.Equal(originalKey, retainedOriginal.FileObject!.StorageKey);
                Assert.True(await verifier.AuditLogs.AnyAsync(log => log.EntityId == originalVersion.Id &&
                    log.Action == "ArtifactVersionUploaded", token));
                await AssertExactStoredBytesAsync(fixture.Storage, originalKey, Fixture.Content, token);
                await AssertExactStoredBytesAsync(fixture.Storage, newKey, fixture.UploadContent, token);
                Assert.Empty(fixture.Storage.DeletedKeys);

                async Task AssertVisibilityAsync(AppDbContext observer, bool expectedVisible)
                {
                    var artifact = await observer.Artifacts.AsNoTracking().SingleAsync(token);
                    Assert.Equal(expectedVisible ? newVersion.Id : originalVersion.Id, artifact.CurrentVersionId);
                    Assert.Equal(expectedVisible ? callerWorkspaceName : originalWorkspaceName,
                        (await observer.Workspaces.AsNoTracking().SingleAsync(token)).Name);
                    var version = await observer.ArtifactVersions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == newVersion.Id, token);
                    var file = await observer.FileObjects.AsNoTracking().SingleOrDefaultAsync(row => row.Id == fileId, token);
                    var attachment = await observer.Attachments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == attachmentId, token);
                    var audit = await observer.AuditLogs.AsNoTracking().SingleOrDefaultAsync(row => row.EntityId == newVersion.Id, token);
                    var nativeVersion = await new FileRepository(observer).GetFileVersionAsync(tenant.TenantId, fileId, fileId, token);
                    if (!expectedVisible)
                    {
                        Assert.Null(version);
                        Assert.Null(file);
                        Assert.Null(attachment);
                        Assert.Null(audit);
                        Assert.Null(nativeVersion);
                        return;
                    }

                    Assert.NotNull(version);
                    Assert.NotNull(file);
                    Assert.NotNull(attachment);
                    Assert.NotNull(audit);
                    Assert.NotNull(nativeVersion);
                    Assert.Equal(artifact.Id, version.ArtifactId);
                    Assert.Equal(newVersion.VersionNumber, version.VersionNumber);
                    Assert.Equal(file.Id, version.FileObjectId);
                    Assert.Equal(attachment.Id, version.AttachmentId);
                    Assert.Equal(file.Id, attachment.FileObjectId);
                    Assert.Equal(AttachmentOwnerType.ArtifactVersion, attachment.OwnerType);
                    Assert.Equal(version.Id, attachment.OwnerId);
                    Assert.Equal(newKey, file.StorageKey);
                    Assert.Equal(newKey, attachment.StorageKey);
                    Assert.Equal(newKey, nativeVersion.StorageKey);
                    Assert.Equal(newVersion.FileSize, file.SizeBytes);
                    Assert.Equal(newVersion.FileSize, attachment.SizeBytes);
                    Assert.Equal(newVersion.FileSize, nativeVersion.SizeBytes);
                    Assert.Equal(file.Id, nativeVersion.FileObjectId);
                    Assert.Equal(1, nativeVersion.VersionNumber);
                    Assert.Equal("ArtifactVersionUploaded", audit.Action);
                    Assert.Equal("ArtifactVersion", audit.EntityType);
                    Assert.Equal(newVersion.UploadedByUserId, audit.ActorUserId);
                }
            });

    private static async Task AssertExactStoredBytesAsync(Storage storage, string key, byte[] expected, CancellationToken cancellationToken)
    {
        await using var stored = await storage.OpenReadAsync(key, cancellationToken);
        using var bytes = new MemoryStream();
        await stored.CopyToAsync(bytes, cancellationToken);
        Assert.Equal(expected, bytes.ToArray());
    }

    [PostgreSqlFact]
    public Task CallerSavepointFailureRestoresTheArtifactGraphWithoutResurrectingTheFailedVersion() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var fault = new UploadSaveAcknowledgementFailure();
                await using var fixture = await Fixture.CreateAsync(connectionString: database, interceptor: fault, actualPersistence: true);
                var previous = await fixture.UploadAsync(CancellationToken.None);
                Assert.True(previous.IsSuccess, previous.Error);
                var artifact = await fixture.Context.Artifacts.SingleAsync(CancellationToken.None);
                var originalVersion = Assert.Single(artifact.Versions);
                var previousKey = Assert.Single(fixture.Storage.SavedKeys);
                await using var caller = await fixture.Context.Database.BeginTransactionAsync(CancellationToken.None);
                fault.Enabled = true;

                var error = await Assert.ThrowsAsync<IOException>(() => fixture.UploadAsync(CancellationToken.None));

                Assert.Same(fault.Failure, error);
                var failedKey = fixture.Storage.SavedKeys.Last();
                Assert.NotEqual(previousKey, failedKey);
                Assert.Equal(failedKey, Assert.Single(fixture.Storage.DeletedKeys));
                Assert.False(await fixture.Storage.ExistsAsync(failedKey, CancellationToken.None));
                Assert.True(await fixture.Storage.ExistsAsync(previousKey, CancellationToken.None));
                Assert.Equal(previous.Value!.Id, artifact.CurrentVersionId);
                Assert.Same(originalVersion, artifact.CurrentVersion);
                Assert.Same(originalVersion, Assert.Single(artifact.Versions));
                Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(originalVersion).State);
                Assert.Same(caller, fixture.Context.Database.CurrentTransaction);
                fault.Enabled = false;
                Assert.Equal(0, await fixture.Context.SaveChangesAsync(CancellationToken.None));
                await caller.CommitAsync(CancellationToken.None);

                await using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                    new CurrentTenant(artifact.TenantId));
                Assert.Equal(previous.Value.Id, (await verification.Artifacts.SingleAsync(CancellationToken.None)).CurrentVersionId);
                Assert.Equal(previous.Value.Id, (await verification.ArtifactVersions.SingleAsync(CancellationToken.None)).Id);
                Assert.Equal(previousKey, (await verification.FileObjects.SingleAsync(CancellationToken.None)).StorageKey);
                Assert.Single(await verification.Attachments.ToListAsync(CancellationToken.None));
            });

    private sealed class UploadSaveAcknowledgementFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public IOException Failure { get; } = new("Synthetic Artifact save acknowledgement failure inside a caller transaction.");
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => Enabled ? throw Failure : ValueTask.FromResult(result);
    }

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
        Assert.False(await fixture.Storage.ExistsAsync(fixture.Storage.SavedKeys.Single(), CancellationToken.None));
        Assert.Empty(await fixture.Context.ArtifactVersions.ToListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CleanupFailureDoesNotReplaceOriginalPersistenceFailure(bool loggingEnabled)
    {
        await using var fixture = await Fixture.CreateAsync(loggingEnabled);
        var expected = new InvalidOperationException("Synthetic persistence failure.");
        fixture.FailAt("persistence", expected);
        fixture.Storage.DeleteFailure = new IOException("Synthetic cleanup failure.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UploadAsync());

        Assert.Same(expected, actual);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.True(await fixture.Storage.ExistsAsync(fixture.Storage.SavedKeys.Single()));
        if (loggingEnabled) { fixture.Logger.AssertOnlyFixedWarning(); }
        else { Assert.Empty(fixture.Logger.Entries); }
    }

    [Fact]
    public async Task CleanupAndLoggerFailureStillPreserveOriginalFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expected = new InvalidOperationException("Synthetic metadata canary MUST_NOT_BE_LOGGED.");
        fixture.FailAt("persistence", expected);
        fixture.Storage.DeleteFailure = new IOException("Synthetic cleanup canary MUST_NOT_BE_LOGGED.");
        fixture.Logger.ThrowOnLog = true;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UploadAsync());

        Assert.Same(expected, actual);
        Assert.Equal(Assert.Single(fixture.Storage.SavedKeys), Assert.Single(fixture.Storage.DeletedKeys));
        Assert.Empty(fixture.Logger.Entries);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task NativeVersionConflictRollsBackMetadataAndPreservesCleanupFailureEvidence()
    {
        bool[] cleanupOutcomes = [false, true];
        foreach (var cleanupFails in cleanupOutcomes)
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
                {
                    await using var fixture = await Fixture.CreateAsync(connectionString: database);
                    var previous = await fixture.UploadAsync();
                    Assert.True(previous.IsSuccess, previous.Error);
                    var existingKey = Assert.Single(fixture.Storage.SavedKeys);
                    var existingVersion = await fixture.Context.ArtifactVersions.SingleAsync();
                    fixture.Context.ArtifactVersions.Add(new ArtifactVersion
                    {
                        ArtifactId = existingVersion.ArtifactId,
                        VersionNumber = existingVersion.VersionNumber,
                        AttachmentId = existingVersion.AttachmentId,
                        FileObjectId = existingVersion.FileObjectId,
                        CreatedByUserId = existingVersion.CreatedByUserId
                    });
                    if (cleanupFails) { fixture.Storage.DeleteFailure = new IOException("Synthetic private cleanup canary."); }

                    var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.UploadAsync());

                    Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
                    var failedKey = fixture.Storage.SavedKeys.Last();
                    Assert.NotEqual(existingKey, failedKey);
                    Assert.Equal(failedKey, Assert.Single(fixture.Storage.DeletedKeys));
                    Assert.Equal(cleanupFails, await fixture.Storage.ExistsAsync(failedKey));
                    Assert.True(await fixture.Storage.ExistsAsync(existingKey));
                    fixture.Context.ChangeTracker.Clear();
                    Assert.Equal(existingVersion.Id, (await fixture.Context.ArtifactVersions.SingleAsync()).Id);
                    Assert.Equal(existingVersion.Id, (await fixture.Context.Artifacts.SingleAsync()).CurrentVersionId);
                    Assert.Single(await fixture.Context.FileObjects.ToListAsync());
                    Assert.Single(await fixture.Context.Attachments.ToListAsync());
                    if (cleanupFails) { fixture.Logger.AssertOnlyFixedWarning(); }
                    else { Assert.Empty(fixture.Logger.Entries); }
                });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ConcurrentNativeUploadsRetainWinnerAndCompensateOnlyTheConflictingUpload()
    {
        foreach (var failure in new[] { "none", "cleanup", "cleanup-and-logger" })
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(),
                database => VerifyConcurrentUploadsAsync(database, failure));
        }
    }

    private static async Task VerifyConcurrentUploadsAsync(string database, string failure)
    {
        await using var seed = await Fixture.CreateAsync(connectionString: database);
        var previous = await seed.UploadAsync();
        Assert.True(previous.IsSuccess, previous.Error);
        var previousKey = Assert.Single(seed.Storage.SavedKeys);
        var barrier = new AllocationBarrier();
        await using var first = await seed.CreateCompetitorAsync(database, barrier);
        await using var second = await seed.CreateCompetitorAsync(database, barrier);
        Assert.NotSame(first.Context, second.Context);
        first.UploadContent = "First concurrent synthetic Artifact bytes."u8.ToArray();
        second.UploadContent = "Second concurrent synthetic Artifact bytes."u8.ToArray();
        foreach (var competitor in new[] { first, second })
        {
            if (failure != "none") { competitor.Storage.DeleteFailure = new IOException("Synthetic private cleanup failure."); }
            competitor.Logger.ThrowOnLog = failure == "cleanup-and-logger";
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstAttempt = CaptureUploadAsync(first, deadline.Token);
        var secondAttempt = CaptureUploadAsync(second, deadline.Token);
        var attempts = await Task.WhenAll(firstAttempt, secondAttempt);
        var errors = attempts.Select(attempt => attempt.Error).ToArray();

        Assert.Equal(new[] { 2, 2 }, barrier.ObservedNumbers.ToArray());
        Assert.Single(errors, error => error is null);
        var error = Assert.IsType<DbUpdateException>(Assert.Single(errors, error => error is not null));
        var native = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, native.SqlState);
        Assert.Equal("IX_artifact_versions_TenantId_ArtifactId_VersionNumber", native.ConstraintName);
        var winner = errors[0] is null ? first : second;
        var loser = errors[0] is null ? second : first;
        var result = errors[0] is null ? attempts[0].Result : attempts[1].Result;
        Assert.NotNull(result);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Same(loser.DatabaseFailure, error);
        var winnerKey = Assert.Single(winner.Storage.SavedKeys);
        var loserKey = Assert.Single(loser.Storage.SavedKeys);
        Assert.NotEqual(previousKey, winnerKey);
        Assert.NotEqual(previousKey, loserKey);
        Assert.NotEqual(winnerKey, loserKey);
        Assert.Empty(winner.Storage.DeletedKeys);
        Assert.Equal(loserKey, Assert.Single(loser.Storage.DeletedKeys));
        Assert.False(loser.Storage.DeleteTokenCanBeCancelled);
        Assert.Equal(failure != "none", await loser.Storage.ExistsAsync(loserKey, CancellationToken.None));
        await AssertStoredBytesAsync(seed.Storage, previousKey, Fixture.Content);
        await AssertStoredBytesAsync(winner.Storage, winnerKey, winner.UploadContent);
        if (failure == "cleanup") { loser.Logger.AssertOnlyFixedWarning(); }
        else { Assert.Empty(loser.Logger.Entries); }
        Assert.Empty(winner.Logger.Entries);

        seed.Context.ChangeTracker.Clear();
        var versions = await seed.Context.ArtifactVersions.OrderBy(version => version.VersionNumber).ToListAsync(CancellationToken.None);
        Assert.Equal([1, 2], versions.Select(version => version.VersionNumber));
        Assert.Equal(previous.Value!.Id, versions[0].Id);
        Assert.Equal(result.Value!.Id, versions[1].Id);
        Assert.Equal(result.Value.Id, (await seed.Context.Artifacts.SingleAsync(CancellationToken.None)).CurrentVersionId);
        var files = await seed.Context.FileObjects.ToListAsync(CancellationToken.None);
        var attachments = await seed.Context.Attachments.ToListAsync(CancellationToken.None);
        Assert.Equal(2, files.Count);
        Assert.Equal(2, attachments.Count);
        Assert.DoesNotContain(files, file => file.StorageKey == loserKey);
        Assert.DoesNotContain(attachments, attachment => attachment.StorageKey == loserKey);
        Assert.Equal(previousKey, Assert.Single(files, file => file.Id == versions[0].FileObjectId).StorageKey);
        Assert.Equal(winnerKey, Assert.Single(files, file => file.Id == versions[1].FileObjectId).StorageKey);
        foreach (var version in versions)
        {
            var file = Assert.Single(files, file => file.Id == version.FileObjectId);
            var attachment = Assert.Single(attachments, attachment => attachment.Id == version.AttachmentId);
            Assert.Equal(file.Id, attachment.FileObjectId);
            Assert.Equal(version.Id, attachment.OwnerId);
            Assert.Equal(AttachmentOwnerType.ArtifactVersion, attachment.OwnerType);
            Assert.Equal(file.StorageKey, attachment.StorageKey);
            Assert.Equal(file.TenantId, version.TenantId);
            Assert.Equal(file.TenantId, attachment.TenantId);
            Assert.Equal(file.SizeBytes, attachment.SizeBytes);
        }
    }

    private static async Task AssertStoredBytesAsync(Storage storage, string key, byte[] expected)
    {
        await using var content = await storage.OpenReadAsync(key);
        Assert.Equal(SHA256.HashData(expected), await SHA256.HashDataAsync(content));
    }

    private static async Task<(Result<ArtifactVersionResponse>? Result, Exception? Error)> CaptureUploadAsync(
        Fixture fixture, CancellationToken cancellationToken)
    {
        try { return (await fixture.UploadAsync(cancellationToken), null); }
        catch (Exception error) { return (null, error); }
    }

    [Fact]
    public async Task SuccessfulUploadRetainsExactBytesAndPersistsMatchingMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.UploadAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(fixture.Storage.DeletedKeys);
        Assert.Empty(fixture.Logger.Entries);
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
        private readonly string _root;
        private readonly Guid _artifactId;
        private readonly bool _ownsRoot;
        private readonly RecordingAudit _audit = new();
        private readonly RecordingNotifications _notifications = new();
        private readonly Persistence _persistence;
        private readonly ArtifactService _service;
        public AppDbContext Context { get; }
        public Storage Storage { get; }
        public FeatureFlags Features { get; } = new();
        public UploadCleanupLogger<ArtifactService> Logger { get; } = new();
        public byte[] UploadContent { get; set; } = Content;
        public DbUpdateException? DatabaseFailure => _persistence.DatabaseFailure;

        private Fixture(Guid tenantId, Guid userId, bool loggingEnabled, string? connectionString,
            string? sharedRoot = null, Guid? artifactId = null, AllocationBarrier? barrier = null,
            IInterceptor? interceptor = null, bool actualPersistence = false, bool persistAudit = false)
        {
            _root = sharedRoot ?? Path.Combine(Path.GetTempPath(), "coglatas-artifact-compensation", Guid.NewGuid().ToString("N"));
            _artifactId = artifactId ?? Guid.NewGuid();
            _ownsRoot = sharedRoot is null;
            var options = new DbContextOptionsBuilder<AppDbContext>();
            if (connectionString is null) { options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")); }
            else { options.UseNpgsql(connectionString); }
            if (interceptor is not null) options.AddInterceptors(interceptor);
            Context = new AppDbContext(options.Options, new CurrentTenant(tenantId));
            Storage = new Storage(new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = _root })));
            _persistence = new Persistence(Context);
            IArtifactRepository repository = new ArtifactRepository(Context);
            if (barrier is not null) { repository = new SynchronizedArtifactRepository(repository, barrier); }
            var currentUser = new CurrentUser(userId);
            var currentTenant = new CurrentTenant(tenantId);
            var clock = new Clock();
            IAuditLogger audit = persistAudit ? new DbAuditLogger(Context, clock, currentUser, currentTenant) : _audit;
            _service = new ArtifactService(repository, new ProjectRepository(Context), null!,
                new FileRepository(Context), Storage, new UploadPolicy(), Features, new Quotas(),
                new ArtifactAuthorization(), currentUser, currentTenant, clock,
                audit, _notifications, actualPersistence ? new EfUnitOfWork(Context) : _persistence, loggingEnabled ? Logger : null);
        }

        public static async Task<Fixture> CreateAsync(bool loggingEnabled = true, string? connectionString = null,
            IInterceptor? interceptor = null, bool actualPersistence = false, bool persistAudit = false)
        {
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var fixture = new Fixture(tenantId, userId, loggingEnabled, connectionString, interceptor: interceptor,
                actualPersistence: actualPersistence, persistAudit: persistAudit);
            var projectId = Guid.NewGuid();
            var workspaceId = Guid.NewGuid();
            fixture.Context.Tenants.Add(new Tenant(tenantId) { Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic", Status = TenantStatus.Active });
            await fixture.Context.SaveChangesAsync();
            fixture.Context.Users.Add(new User { Id = userId, DisplayName = "Synthetic actor", Email = "synthetic@example.invalid", NormalizedEmail = "SYNTHETIC@EXAMPLE.INVALID", Status = UserStatus.Active });
            fixture.Context.Workspaces.Add(new Workspace { Id = workspaceId, TenantId = tenantId, Name = "Synthetic", Slug = "synthetic", CreatedByUserId = userId });
            fixture.Context.Projects.Add(new Project { Id = projectId, TenantId = tenantId, WorkspaceId = workspaceId, OwnerUserId = userId, CreatedByUserId = userId, Name = "Synthetic", Slug = "synthetic" });
            fixture.Context.ProjectMembers.Add(new ProjectMember { TenantId = tenantId, ProjectId = projectId, UserId = userId, Role = ProjectRole.Owner });
            fixture.Context.Artifacts.Add(new Artifact { Id = fixture._artifactId, TenantId = tenantId, ProjectId = projectId, Name = "Synthetic", CreatedByUserId = userId });
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }

        public async Task<Result<ArtifactVersionResponse>> UploadAsync(CancellationToken cancellationToken = default)
        {
            await using var content = new MemoryStream(UploadContent);
            return await _service.UploadVersionAsync(_artifactId, new UploadArtifactVersionInput("synthetic.txt", "text/plain", UploadContent.Length, content, null), cancellationToken);
        }

        public async Task<Fixture> CreateCompetitorAsync(string database, AllocationBarrier barrier)
        {
            var artifact = await Context.Artifacts.SingleAsync();
            return new Fixture(artifact.TenantId, artifact.CreatedByUserId, true, database, _root, _artifactId, barrier);
        }

        public void FailAt(string stage, Exception error)
        {
            if (stage == "audit") { _audit.Failure = error; }
            else if (stage == "notification") { _notifications.Failure = error; }
            else if (stage == "persistence") { _persistence.Failure = error; }
            else { throw new ArgumentOutOfRangeException(nameof(stage)); }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            if (_ownsRoot && Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
        }
    }

    private sealed class AllocationBarrier
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public ConcurrentQueue<int> ObservedNumbers { get; } = new();

        public async Task<int> AfterReadAsync(int number, CancellationToken cancellationToken)
        {
            ObservedNumbers.Enqueue(number);
            if (Interlocked.Increment(ref _arrivals) == 2) { _bothRead.SetResult(); }
            await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return number;
        }
    }

    private sealed class SynchronizedArtifactRepository(IArtifactRepository inner, AllocationBarrier barrier) : IArtifactRepository
    {
        public Task<IReadOnlyList<Artifact>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.ListByProjectAsync(projectId, cancellationToken);
        public Task<Artifact?> GetArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default) => inner.GetArtifactAsync(artifactId, cancellationToken);
        public Task<ArtifactVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default) => inner.GetVersionAsync(versionId, cancellationToken);
        public Task<IReadOnlyList<ArtifactVersion>> ListVersionsAsync(Guid artifactId, CancellationToken cancellationToken = default) => inner.ListVersionsAsync(artifactId, cancellationToken);
        public async Task<int> GetNextVersionNumberAsync(Guid artifactId, CancellationToken cancellationToken = default) =>
            await barrier.AfterReadAsync(await inner.GetNextVersionNumberAsync(artifactId, cancellationToken), cancellationToken);
        public Task AddArtifactAsync(Artifact artifact, CancellationToken cancellationToken = default) => inner.AddArtifactAsync(artifact, cancellationToken);
        public Task AddVersionAsync(ArtifactVersion version, CancellationToken cancellationToken = default) => inner.AddVersionAsync(version, cancellationToken);
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
        public DbUpdateException? DatabaseFailure { get; private set; }
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (Failure is { } error) { throw error; }
            try { return await context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException databaseError) { DatabaseFailure = databaseError; throw; }
        }

        public bool TryDiscardFailedUploadChanges()
        {
            context.ChangeTracker.Clear();
            return true;
        }

        public async Task<int> SaveUploadChangesAsync(UploadSaveOutcome outcome, CancellationToken cancellationToken = default)
        {
            if (Failure is { } error)
            {
                outcome.Disposition = UploadCommitDisposition.NotCommitted;
                throw error;
            }
            try { return await new EfUnitOfWork(context).SaveUploadChangesAsync(outcome, cancellationToken); }
            catch (DbUpdateException databaseError) { DatabaseFailure = databaseError; throw; }
        }
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
