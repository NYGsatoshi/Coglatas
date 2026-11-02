using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Audit;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Audit;

/// <summary>Actual service and tenant query filters with synthetic packages; not PostgreSQL RLS or worker qualification.</summary>
public sealed class AuditPackageExportServiceTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DownloadRequiresAuthenticatedActorBeforeAuthorizationOrStorage(bool authenticated, bool hasUser)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.User.Authenticated = authenticated;
        fixture.User.Id = hasUser ? fixture.User.Id : null;

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "AuthenticationRequired");
        Assert.Empty(fixture.Authorization.Calls);
        fixture.AssertNoDeliveryEffects();
    }

    [Fact]
    public async Task DownloadRequiresSeparateExportCapabilityBeforeProjectionOrStorage()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authorization.Allowed = false;

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "CapabilityDenied");
        Assert.Equal((CapabilityKeys.AuditExport, "audit.package-export.download"), Assert.Single(fixture.Authorization.Calls));
        Assert.Empty(fixture.Claims.Requests);
        fixture.AssertNoDeliveryEffects();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("requester")]
    [InlineData("export-type")]
    [InlineData("missing-file")]
    [InlineData("unknown-job")]
    public async Task DownloadConcealsUnownedOrUnavailableJobsBeforeReadingPackage(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestedId = fixture.Job.Id;
        switch (boundary)
        {
            case "tenant": fixture.Tenant.SetTenant(Guid.NewGuid(), "other-synthetic-tenant"); break;
            case "requester": fixture.User.Id = Guid.NewGuid(); break;
            case "export-type": fixture.Job.ExportType = TenantExportType.Metadata; break;
            case "missing-file": fixture.Job.FileObject = null; fixture.Job.FileObjectId = null; break;
            case "unknown-job": requestedId = Guid.NewGuid(); break;
        }
        if (boundary is "export-type" or "missing-file") await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.DownloadAsync(requestedId);

        AssertFailure(result, "ExportJobNotFound");
        Assert.Empty(fixture.Claims.Requests);
        fixture.AssertNoDeliveryEffects();
    }

    [Theory]
    [InlineData(ExportJobStatus.Queued)]
    [InlineData(ExportJobStatus.Running)]
    [InlineData(ExportJobStatus.Failed)]
    public async Task DownloadNeverDeliversIncompleteJob(ExportJobStatus status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Job.Status = status;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "ExportJobNotReady");
        Assert.Empty(fixture.Claims.Requests);
        fixture.AssertNoDeliveryEffects();
    }

    [Fact]
    public async Task DownloadRechecksCurrentSourceAuthorizationBeforeOpeningStoredBytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Claims.Allowed = false;

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "AuthorizationChanged");
        Assert.Equal(fixture.Projection.ArtifactVersionId, Assert.Single(fixture.Claims.Requests));
        fixture.AssertNoDeliveryEffects();
    }

    [Fact]
    public async Task DownloadRejectsMalformedStorageBindingBeforeSourceOrStorageAccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.File.StorageKey = "not-an-audit-package-key";
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "ExportJobCorrupt");
        Assert.Empty(fixture.Claims.Requests);
        fixture.AssertNoDeliveryEffects();
    }

    [Fact]
    public async Task DownloadRejectsMissingStorageBytesWithoutDeliveryAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Storage.DeleteAsync(fixture.File.StorageKey);

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "AuthorizationChanged");
        Assert.Equal(1, fixture.Storage.ExistsCalls);
        Assert.Empty(fixture.Storage.Reads);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.Calls);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
    }

    [Theory]
    [InlineData("hash", "AuthorizationChanged")]
    [InlineData("zip", "ExportPackageUnavailable")]
    [InlineData("claim-entry", "AuthorizationChanged")]
    [InlineData("metadata-entry", "AuthorizationChanged")]
    [InlineData("claim-json", "ExportPackageUnavailable")]
    [InlineData("metadata-json", "ExportPackageUnavailable")]
    public async Task DownloadRejectsCorruptOrIncompletePackagesWithoutDeliveryAudit(string damage, string errorCode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var bytes = damage == "zip" ? Encoding.UTF8.GetBytes("not a zip archive") :
            Package(fixture.Projection, false, omit: damage, corrupt: damage);
        await fixture.ReplacePackageAsync(bytes);
        if (damage == "hash")
        {
            fixture.File.HashSha256 = new string('0', 64);
            await fixture.Context.SaveChangesAsync();
        }

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, errorCode);
        Assert.Equal(bytes, fixture.Storage.Bytes);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.Calls);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
        Assert.All(fixture.Storage.Reads, stream => Assert.False(stream.CanRead));
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("version")]
    [InlineData("claim")]
    [InlineData("evidence")]
    [InlineData("sensitive")]
    public async Task DownloadRejectsPackageOutsideCurrentAuthorizedProjection(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var claim = Assert.Single(fixture.Projection.Claims);
        var projection = boundary switch
        {
            "artifact" => fixture.Projection with { ArtifactId = Guid.NewGuid() },
            "version" => fixture.Projection with { ArtifactVersionId = Guid.NewGuid() },
            "claim" => fixture.Projection with { Claims = [claim with { ClaimId = Guid.NewGuid() }] },
            "evidence" => fixture.Projection with { Claims = [claim with { Evidence = [Assert.Single(claim.Evidence) with { EvidenceId = Guid.NewGuid() }] }] },
            _ => fixture.Projection
        };
        var bytes = Package(projection, boundary == "sensitive");
        await fixture.ReplacePackageAsync(bytes);

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        AssertFailure(result, "AuthorizationChanged");
        Assert.Equal(bytes, fixture.Storage.Bytes);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.Calls);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
        Assert.All(fixture.Storage.Reads, stream => Assert.False(stream.CanRead));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadPreservesExactAuthorizedPackageBytesAndRecordsScopedAudit(bool sensitive)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authorization.Sensitive = sensitive;
        var expected = Package(fixture.Projection, sensitive);
        await fixture.ReplacePackageAsync(expected);

        var result = await fixture.Service.DownloadAsync(fixture.Job.Id);

        Assert.True(result.IsSuccess, result.Error ?? result.ErrorDetail?.Message);
        var download = Assert.IsType<AuditPackageExportDownloadResponse>(result.Value);
        await using var content = download.Content;
        using var received = new MemoryStream();
        await content.CopyToAsync(received);
        Assert.Equal(expected, received.ToArray());
        Assert.Equal(expected, fixture.Storage.Bytes);
        Assert.Equal(fixture.File.OriginalFileName, download.FileName);
        Assert.Equal("application/zip", download.ContentType);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("AuditPackageExportDownloaded", audit.Action);
        Assert.Equal(fixture.Tenant.TenantId, audit.TenantId);
        Assert.Equal(fixture.User.UserId, audit.ActorUserId);
        Assert.Equal(fixture.Job.Id, audit.EntityId);
        Assert.Equal(1, fixture.UnitOfWork.Calls);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
        Assert.Equal(fixture.Projection.ArtifactVersionId, Assert.Single(fixture.Claims.Requests));
    }

    [Fact]
    public async Task RetryPreservesFailedStateAndOriginalBytesWhenCleanupFails()
    {
        await using var fixture = await Fixture.CreateAsync(ExportJobStatus.Failed);
        var before = fixture.Snapshot();
        var originalBytes = fixture.Storage.Bytes.ToArray();
        fixture.Storage.FailDelete = true;

        var result = await fixture.Service.RetryAsync(fixture.Job.Id);

        AssertFailure(result, "ExportPackageCleanupFailed");
        Assert.Equal(before, fixture.Snapshot());
        Assert.Equal(originalBytes, fixture.Storage.Bytes);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.Calls);
    }

    [Fact]
    public async Task RetryRequiresExportCapabilityBeforeDeletingFailedPackage()
    {
        await using var fixture = await Fixture.CreateAsync(ExportJobStatus.Failed);
        fixture.Authorization.Allowed = false;
        var before = fixture.Snapshot();

        var result = await fixture.Service.RetryAsync(fixture.Job.Id);

        AssertFailure(result, "CapabilityDenied");
        Assert.Equal(before, fixture.Snapshot());
        Assert.Empty(fixture.Claims.Requests);
        fixture.AssertNoDeliveryEffects();
    }

    [Theory]
    [InlineData(ExportJobStatus.Queued)]
    [InlineData(ExportJobStatus.Running)]
    [InlineData(ExportJobStatus.Completed)]
    public async Task RetryPreservesSuccessfulOrInProgressPackage(ExportJobStatus status)
    {
        await using var fixture = await Fixture.CreateAsync(status);
        var before = fixture.Snapshot();
        var originalBytes = fixture.Storage.Bytes.ToArray();

        var result = await fixture.Service.RetryAsync(fixture.Job.Id);

        AssertFailure(result, "ExportRetryNotAllowed");
        Assert.Equal(before, fixture.Snapshot());
        Assert.Equal(originalBytes, fixture.Storage.Bytes);
        Assert.Empty(fixture.Claims.Requests);
        fixture.AssertNoDeliveryEffects();
    }

    [Fact]
    public async Task RetryRechecksSourceAuthorityBeforeDeletingPreviousPackage()
    {
        await using var fixture = await Fixture.CreateAsync(ExportJobStatus.Failed);
        fixture.Claims.Allowed = false;
        var before = fixture.Snapshot();

        var result = await fixture.Service.RetryAsync(fixture.Job.Id);

        AssertFailure(result, "AuthorizationChanged");
        Assert.Equal(before, fixture.Snapshot());
        fixture.AssertNoDeliveryEffects();
        Assert.Equal((CapabilityKeys.AuditExport, "audit.package-export.retry"), Assert.Single(fixture.Authorization.Calls));
    }

    [Fact]
    public async Task RetryClearsFailedPackageOnlyAfterAuthorizationAndQueuesPersistedJob()
    {
        await using var fixture = await Fixture.CreateAsync(ExportJobStatus.Failed);

        var result = await fixture.Service.RetryAsync(fixture.Job.Id);

        Assert.True(result.IsSuccess, result.Error ?? result.ErrorDetail?.Message);
        var queued = Assert.IsType<AuditPackageExportJobResponse>(result.Value);
        Assert.Equal("Queued", queued.State);
        Assert.Equal(10, queued.ProgressPercent);
        Assert.Equal(fixture.Projection.ArtifactVersionId, queued.ArtifactVersionId);
        Assert.Equal((ExportJobStatus.Queued, (string?)null, (DateTimeOffset?)null, 0L, (string?)null), fixture.Snapshot());
        Assert.False(fixture.Storage.Present);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Equal("AuditPackageExportRetried", Assert.Single(fixture.Audit.Entries).Action);
        Assert.Equal(1, fixture.UnitOfWork.Calls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(ExportJobStatus.Queued, (await fixture.Context.ExportJobs.SingleAsync()).Status);
    }

    private static void AssertFailure<T>(Result<T> result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(code, result.ErrorDetail?.Code);
    }

    private static byte[] Package(AuditClaimsEvidenceResponse projection, bool sensitive, string? omit = null, string? corrupt = null)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (omit != "claim-entry") Write("claim-evidence.json", corrupt == "claim-json" ? "{" : JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            if (omit != "metadata-entry") Write("run-metadata.json", corrupt == "metadata-json" ? "{" : JsonSerializer.Serialize(new { sensitiveMetadataIncluded = sensitive }));
            Write("audit-report.json", "{\"synthetic\":true}");
            Write("source-manifest.json", "[]");
            Write("risk-decisions.json", "[]");
            void Write(string name, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }
        return bytes.ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public CurrentTenantService Tenant { get; } = new();
        public Actor User { get; }
        public Authorization Authorization { get; } = new();
        public Claims Claims { get; }
        public Storage Storage { get; }
        public Audit Audit { get; } = new();
        public UnitOfWork UnitOfWork { get; }
        public AppDbContext Context { get; }
        public AuditPackageExportService Service { get; }
        public AuditClaimsEvidenceResponse Projection { get; }
        public ExportJob Job { get; }
        public FileObject File { get; }

        private Fixture(ExportJobStatus status)
        {
            var actorId = Guid.NewGuid();
            User = new Actor(actorId);
            Tenant.SetTenant(Guid.NewGuid(), "audit-package-synthetic");
            Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, Tenant);
            Projection = new AuditClaimsEvidenceResponse(Guid.NewGuid(), Guid.NewGuid(), 2, "Synthetic report",
                [new AuditClaimEvidenceResponse(Guid.NewGuid(), 1, "Synthetic claim", true, "Supported", "Reviewed",
                    [new AuditEvidenceResponse(Guid.NewGuid(), 1, "WebSnapshot", "https://example.invalid/source", "Synthetic source", "Synthetic passage", null, null, "source-1")])]);
            Job = new ExportJob
            {
                TenantId = Tenant.TenantId, RequestedByUserId = actorId,
                ExportType = TenantExportType.AuditPackage, Status = status,
                ErrorMessage = status == ExportJobStatus.Failed ? "SyntheticFailure" : null,
                CompletedAt = DateTimeOffset.Parse("2026-11-01T00:00:00Z")
            };
            File = new FileObject
            {
                TenantId = Tenant.TenantId, UploadedByUserId = actorId,
                OriginalFileName = "synthetic-audit.zip", ContentType = "application/zip",
                StorageKey = AuditPackageStorageKey.Create(Tenant.TenantId, Job.Id, Projection.ArtifactVersionId)
            };
            Job.FileObject = File;
            Job.FileObjectId = File.Id;
            Storage = new Storage(File.StorageKey, Package(Projection, false));
            Claims = new Claims(Projection);
            UnitOfWork = new UnitOfWork(Context);
            Service = new AuditPackageExportService(Context, new ArtifactRepository(Context), Claims, Authorization, User, Storage, Audit, UnitOfWork);
        }

        public static async Task<Fixture> CreateAsync(ExportJobStatus status = ExportJobStatus.Completed)
        {
            var fixture = new Fixture(status);
            try
            {
                fixture.Context.Tenants.Add(new Tenant(fixture.Tenant.TenantId)
                {
                    Name = "Synthetic audit tenant", DisplayName = "Synthetic audit tenant",
                    Slug = "audit-package-synthetic", Status = TenantStatus.Active
                });
                await fixture.Context.SaveChangesAsync();
                fixture.Context.FileObjects.Add(fixture.File);
                fixture.Context.ExportJobs.Add(fixture.Job);
                await fixture.ReplacePackageAsync(fixture.Storage.Bytes);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task ReplacePackageAsync(byte[] bytes)
        {
            Storage.Bytes = bytes;
            File.SizeBytes = bytes.LongLength;
            File.HashSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await Context.SaveChangesAsync();
        }

        public (ExportJobStatus, string?, DateTimeOffset?, long, string?) Snapshot() =>
            (Job.Status, Job.ErrorMessage, Job.CompletedAt, File.SizeBytes, File.HashSha256);

        public void AssertNoDeliveryEffects()
        {
            Assert.Equal(0, Storage.ExistsCalls);
            Assert.Empty(Storage.Reads);
            Assert.Equal(0, Storage.DeleteCalls);
            Assert.Empty(Audit.Entries);
            Assert.Equal(0, UnitOfWork.Calls);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var stream in Storage.Reads) await stream.DisposeAsync();
            await Context.DisposeAsync();
        }
    }

    private sealed class Actor(Guid actorId) : ICurrentUser
    {
        public Guid? Id { get; set; } = actorId;
        public bool Authenticated { get; set; } = true;
        public Guid? UserId => Id;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => Authenticated;
    }

    private sealed class Authorization : IAuditAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public bool Sensitive { get; set; }
        public List<(string Capability, string Operation)> Calls { get; } = [];
        public Task<AuditCapabilityResponse> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditCapabilityResponse(true, false, false, Allowed, Sensitive));
        public Task<Result> AuthorizeAsync(string capabilityKey, string operation, CancellationToken cancellationToken = default)
        {
            Calls.Add((capabilityKey, operation));
            return Task.FromResult(Allowed ? Result.Success() : Result.Failure(new ApplicationErrorDetail("CapabilityDenied", "Synthetic export denial.")));
        }
    }

    private sealed class Claims(AuditClaimsEvidenceResponse projection) : IAuditClaimsEvidenceService
    {
        public bool Allowed { get; set; } = true;
        public List<Guid> Requests { get; } = [];
        public Task<Result<AuditClaimsEvidenceResponse>> GetAsync(Guid artifactVersionId, CancellationToken cancellationToken = default)
        {
            Requests.Add(artifactVersionId);
            Assert.Equal(projection.ArtifactVersionId, artifactVersionId);
            return Task.FromResult(Allowed ? Result<AuditClaimsEvidenceResponse>.Success(projection) :
                Result<AuditClaimsEvidenceResponse>.Failure(new ApplicationErrorDetail("ArtifactNotFound", "Synthetic source denial.")));
        }
    }

    private sealed class Storage(string key, byte[] bytes) : IFileStorageService
    {
        public byte[] Bytes { get; set; } = bytes;
        public bool Present { get; private set; } = true;
        public bool FailDelete { get; set; }
        public int ExistsCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public List<MemoryStream> Reads { get; } = [];
        public Task<Result> SaveAsync(string storageKey, Stream stream, string contentType, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Download/retry must not create storage bytes.");
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Assert.Equal(key, storageKey);
            Assert.True(Present);
            var stream = new MemoryStream(Bytes, writable: false);
            Reads.Add(stream);
            return Task.FromResult<Stream>(stream);
        }
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Assert.Equal(key, storageKey);
            DeleteCalls++;
            if (FailDelete) throw new IOException("Synthetic cleanup failure.");
            Present = false;
            return Task.CompletedTask;
        }
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Assert.Equal(key, storageKey);
            ExistsCalls++;
            return Task.FromResult(Present);
        }
        public Task<string?> CreateSignedReadUrlAsync(string storageKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Audit delivery must return an authorized stream.");
    }

    private sealed class Audit : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
    {
        public int Calls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return context.SaveChangesAsync(cancellationToken);
        }
    }
}
