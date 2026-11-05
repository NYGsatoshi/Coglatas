using System.Text.Json;
using Coglatas.Application.Announcements;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Announcements;

public sealed class AnnouncementAttachmentServiceContractTests
{
    [Theory]
    [InlineData("no-tenant")]
    [InlineData("platform")]
    [InlineData("no-actor")]
    [InlineData("global")]
    [InlineData("no-attachment")]
    public async Task InvalidContextDeniesWithoutReadingFeatureOrProtectedMetadata(string scenario)
    {
        using var fixture = new Fixture();
        if (scenario == "no-tenant") fixture.Tenant.Clear();
        if (scenario == "platform") fixture.Tenant.SetPlatformScope();

        var result = await fixture.Service.ResolveAsync(
            scenario == "no-actor" ? Guid.Empty : fixture.ActorId,
            scenario == "global" ? null : fixture.WorkspaceId,
            scenario == "no-attachment" ? Guid.Empty : fixture.Attachment.Id,
            fixture.Token);

        AssertDenied(result);
        Assert.Empty(fixture.Dependencies.Calls);
    }

    [Fact]
    public async Task DisabledFileSharingDeniesBeforeMetadataAndRedactsFeatureError()
    {
        using var fixture = new Fixture();
        fixture.ExpectFeature(Result.Failure("Private tenant entitlement details"));

        AssertDenied(await fixture.ResolveAsync());

        fixture.Dependencies.VerifyComplete();
        Assert.Single(fixture.Dependencies.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("attachment-tenant")]
    [InlineData("attachment-workspace")]
    [InlineData("attachment-deleted")]
    [InlineData("owner-type")]
    [InlineData("owner-type-null")]
    [InlineData("owner-id")]
    [InlineData("owner-id-null")]
    [InlineData("no-file")]
    [InlineData("file-tenant")]
    [InlineData("file-workspace")]
    [InlineData("file-workspace-null")]
    [InlineData("file-project")]
    [InlineData("file-deleted")]
    [InlineData("file-archived")]
    [InlineData("file-status-deleted")]
    [InlineData("file-quarantined")]
    [InlineData("scan-pending")]
    [InlineData("scan-infected")]
    [InlineData("scan-failed")]
    [InlineData("classification-null")]
    [InlineData("classification-unknown")]
    public async Task UnsafeOrMismatchedCanonicalRecordsDenyBeforeOwnerAndDownloadChecks(string scenario)
    {
        using var fixture = new Fixture();
        var attachment = fixture.Attachment;
        var file = attachment.FileObject!;
        switch (scenario)
        {
            case "attachment-tenant": attachment.TenantId = Guid.NewGuid(); break;
            case "attachment-workspace": attachment.WorkspaceId = Guid.NewGuid(); break;
            case "attachment-deleted": attachment.MarkDeleted(DateTimeOffset.UtcNow); break;
            case "owner-type": attachment.OwnerType = AttachmentOwnerType.Message; break;
            case "owner-type-null": attachment.OwnerType = null; break;
            case "owner-id": attachment.OwnerId = Guid.NewGuid(); break;
            case "owner-id-null": attachment.OwnerId = null; break;
            case "no-file": attachment.FileObject = null; break;
            case "file-tenant": file.TenantId = Guid.NewGuid(); break;
            case "file-workspace": file.WorkspaceId = Guid.NewGuid(); break;
            case "file-workspace-null": file.WorkspaceId = null; break;
            case "file-project": file.ProjectId = Guid.NewGuid(); break;
            case "file-deleted":
                file.MarkDeleted(DateTimeOffset.UtcNow, fixture.ActorId, "Fixture deletion");
                file.Status = FileObjectStatus.Active;
                break;
            case "file-archived": file.Status = FileObjectStatus.Archived; break;
            case "file-status-deleted": file.Status = FileObjectStatus.Deleted; break;
            case "file-quarantined": file.Status = FileObjectStatus.Quarantined; break;
            case "scan-pending": attachment.ScanStatus = FileScanStatus.Pending; break;
            case "scan-infected": attachment.ScanStatus = FileScanStatus.Infected; break;
            case "scan-failed": attachment.ScanStatus = FileScanStatus.Failed; break;
            case "classification-null": file.Classification = null; break;
            case "classification-unknown": file.Classification = DataClassification.UnknownSensitive; break;
        }
        fixture.ExpectMetadata(scenario == "missing" ? null : attachment);

        AssertDenied(await fixture.ResolveAsync());

        fixture.Dependencies.VerifyComplete();
        Assert.Equal(2, fixture.Dependencies.Calls.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("workspace")]
    [InlineData("project")]
    public async Task InvalidResolvedOwnerDeniesWithoutConsultingDownloadAuthority(string scenario)
    {
        using var fixture = new Fixture();
        fixture.ExpectMetadata(fixture.Attachment);
        fixture.ExpectOwner(scenario switch
        {
            "missing" => null,
            "workspace" => new FileOwnerContext(Guid.NewGuid()),
            _ => new FileOwnerContext(fixture.WorkspaceId, Guid.NewGuid())
        });

        AssertDenied(await fixture.ResolveAsync());

        fixture.Dependencies.VerifyComplete();
        Assert.Equal(3, fixture.Dependencies.Calls.Count);
    }

    [Theory]
    [InlineData(FileScanStatus.Clean)]
    [InlineData(FileScanStatus.Skipped)]
    public async Task AuthorizedWorkspaceFileReturnsOnlyCanonicalRecipientSafeMetadata(FileScanStatus scanStatus)
    {
        using var fixture = new Fixture();
        fixture.Attachment.ScanStatus = scanStatus;
        fixture.ExpectSuccessfulResolution(true);

        var result = await fixture.ResolveAsync();

        Assert.True(result.IsSuccess, result.Error);
        var response = Assert.IsType<AnnouncementAttachmentResponse>(result.Value);
        var file = fixture.Attachment.FileObject!;
        Assert.Equal(new AnnouncementAttachmentResponse(
            fixture.Attachment.Id, file.Id, fixture.WorkspaceId,
            "canonical-report.pdf", "application/pdf", 7291), response);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.Equal(
            new[] { "AttachmentId", "ContentType", "FileName", "FileObjectId", "SizeBytes", "WorkspaceId" },
            document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        fixture.Dependencies.VerifyComplete();
        Assert.Equal(4, fixture.Dependencies.Calls.Count);
    }

    [Fact]
    public async Task PreviouslyResolvedFileRechecksDownloadPermissionForEachActorAndCall()
    {
        using var fixture = new Fixture();
        fixture.ExpectSuccessfulResolution(true);
        Assert.True((await fixture.ResolveAsync()).IsSuccess);

        fixture.ExpectSuccessfulResolution(false);
        AssertDenied(await fixture.ResolveAsync());

        var recipientId = Guid.NewGuid();
        fixture.ExpectSuccessfulResolution(false, recipientId);
        AssertDenied(await fixture.Service.ResolveAsync(
            recipientId, fixture.WorkspaceId, fixture.Attachment.Id, fixture.Token));

        fixture.Dependencies.VerifyComplete();
        Assert.Equal(3, fixture.Dependencies.Calls.Count(call => call.Method == "authorization.CanDownloadAttachment"));
    }

    [Fact]
    public async Task RepositoryFailurePropagatesWithoutResolvingOwnerOrIssuingMetadata()
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic repository failure");
        fixture.ExpectFeature(Result.Success());
        fixture.Dependencies.Expect("files.GetAttachmentAsync", Task.FromException<Attachment?>(failure),
            [fixture.Attachment.Id, fixture.Token]);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(fixture.ResolveAsync));

        fixture.Dependencies.VerifyComplete();
        Assert.Equal(2, fixture.Dependencies.Calls.Count);
    }

    [Fact]
    public async Task CancellationReachesFeatureGateAndDoesNotReadMetadata()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        fixture.Dependencies.Expect("features.RequireEnabledAsync", Task.FromCanceled<Result>(cancellation.Token),
            [FeatureKeys.FileSharing, cancellation.Token]);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ResolveAsync(
            fixture.ActorId, fixture.WorkspaceId, fixture.Attachment.Id, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        fixture.Dependencies.VerifyComplete();
        Assert.Single(fixture.Dependencies.Calls);
    }

    private static void AssertDenied(Result<AnnouncementAttachmentResponse> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Announcement attachment is unavailable or not authorized.", result.Error);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        public AnnouncementContractDependencies Dependencies { get; } = new();
        public CurrentTenantService Tenant { get; } = new();
        private Guid TenantId { get; } = Guid.NewGuid();
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public Guid ActorId { get; } = Guid.NewGuid();
        public CancellationToken Token => _cancellation.Token;
        public Attachment Attachment { get; }
        public IAnnouncementAttachmentService Service { get; }

        public Fixture()
        {
            Tenant.SetTenant(TenantId, "attachment-contract");
            var file = new FileObject
            {
                Id = Guid.NewGuid(), TenantId = TenantId, WorkspaceId = WorkspaceId,
                OriginalFileName = "canonical-report.pdf", ContentType = "application/pdf", SizeBytes = 7291,
                Classification = DataClassification.Internal, Status = FileObjectStatus.Active,
                StorageKey = "private/canonical-storage-key", HashSha256 = "private-hash"
            };
            Attachment = new Attachment
            {
                Id = Guid.NewGuid(), TenantId = TenantId, WorkspaceId = WorkspaceId,
                OwnerType = AttachmentOwnerType.Workspace, OwnerId = WorkspaceId,
                FileObjectId = file.Id, FileObject = file, ScanStatus = FileScanStatus.Clean,
                FileName = "stale-legacy-name.txt", ContentType = "text/plain", SizeBytes = 1,
                FilePath = "private/local/path", StorageKey = "private/legacy-storage-key"
            };
            Service = new AnnouncementAttachmentService(
                Dependencies.Create<IFileRepository>("files"),
                Dependencies.Create<IFileAuthorizationService>("authorization"),
                Dependencies.Create<IFeatureFlagService>("features"), Tenant);
        }

        public Task<Result<AnnouncementAttachmentResponse>> ResolveAsync() =>
            Service.ResolveAsync(ActorId, WorkspaceId, Attachment.Id, Token);

        public void ExpectFeature(Result result) => Dependencies.Expect(
            "features.RequireEnabledAsync", Task.FromResult(result), [FeatureKeys.FileSharing, Token]);

        public void ExpectMetadata(Attachment? attachment)
        {
            ExpectFeature(Result.Success());
            Dependencies.Expect("files.GetAttachmentAsync", Task.FromResult(attachment), [Attachment.Id, Token]);
        }

        public void ExpectOwner(FileOwnerContext? owner) => Dependencies.Expect(
            "files.ResolveOwnerAsync", Task.FromResult(owner), [AttachmentOwnerType.Workspace, WorkspaceId, Token]);

        public void ExpectSuccessfulResolution(bool canDownload, Guid? actorId = null)
        {
            ExpectMetadata(Attachment);
            ExpectOwner(new FileOwnerContext(WorkspaceId));
            Dependencies.Expect("authorization.CanDownloadAttachment", Task.FromResult(canDownload),
                [actorId ?? ActorId, Attachment, Token]);
        }

        public void Dispose() => _cancellation.Dispose();
    }
}
