using System.Reflection;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Artifacts;

// Service/authorizer contracts with strict dependencies, not database or transport qualification.
public sealed class ArtifactLifecycleContractTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("get")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("versions")]
    [InlineData("delete-version")]
    public async Task DeniedProjectAuthorityPreservesDomainAndProducesNoEffects(string operation)
    {
        using var fixture = new Fixture { Allowed = false };
        var before = fixture.CaptureGraph();

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal(operation switch
        {
            "list" => "Project not found.",
            "create" => "You are not allowed to create artifacts for this project.",
            "delete-version" => "Artifact version not found.",
            _ => "Artifact not found."
        }, result.Error);
        Assert.Single(fixture.ProjectChecks);
        Assert.Equal("Original", fixture.Artifact.Name);
        Assert.Equal(ArtifactStatus.Draft, fixture.Artifact.Status);
        Assert.Equal(fixture.Version.Id, fixture.Artifact.CurrentVersionId);
        Assert.Null(fixture.Artifact.DeletedAt);
        Assert.Null(fixture.Version.DeletedAt);
        fixture.AssertNoEffects(before);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("delete-version")]
    public async Task ReadableProjectWithoutManagementAuthorityCannotMutateArtifacts(string operation)
    {
        using var fixture = new Fixture { CanManage = false };
        var before = fixture.CaptureGraph();
        var read = await fixture.Service.GetAsync(fixture.Artifact.Id, fixture.Token);
        Assert.True(read.IsSuccess, read.Error);
        Assert.Equal(fixture.Artifact.Id, read.Value!.Id);

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { "CanViewProject", "CanManageProject" }, fixture.ProjectChecks);
        fixture.AssertNoEffects(before);
    }

    [Theory]
    [InlineData("list", false)]
    [InlineData("create", false)]
    [InlineData("get", false)]
    [InlineData("update", false)]
    [InlineData("delete", false)]
    [InlineData("versions", false)]
    [InlineData("delete-version", false)]
    [InlineData("list", true)]
    [InlineData("create", true)]
    [InlineData("get", true)]
    [InlineData("update", true)]
    [InlineData("delete", true)]
    [InlineData("versions", true)]
    [InlineData("delete-version", true)]
    public async Task MissingIdentityNeverConsultsProjectAuthorityOrWrites(string operation, bool missingId)
    {
        using var fixture = new Fixture();
        fixture.Actor.Authenticated = missingId;
        if (missingId) fixture.Actor.Id = null;
        var before = fixture.CaptureGraph();

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Empty(fixture.ProjectChecks);
        fixture.AssertNoEffects(before);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("versions")]
    [InlineData("delete-version")]
    public async Task DeletedParentCannotBeReadOrMutatedThroughArtifactRoutes(string operation)
    {
        using var fixture = new Fixture();
        fixture.Artifact.MarkDeleted(Fixture.Now.AddDays(-1));
        var before = fixture.CaptureGraph();

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Empty(fixture.ProjectChecks);
        Assert.Null(fixture.Version.DeletedAt);
        Assert.Equal(fixture.Version.Id, fixture.Artifact.CurrentVersionId);
        fixture.AssertNoEffects(before);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("versions")]
    public async Task MissingArtifactPreservesNotFoundAndShortCircuitsProjectAuthority(string operation)
    {
        using var fixture = new Fixture();
        fixture.Repository.Items.Clear();
        var before = fixture.CaptureGraph();

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("Artifact not found.", result.Error);
        Assert.Empty(fixture.ProjectChecks);
        fixture.AssertNoEffects(before);
    }

    [Fact]
    public async Task ProjectListFiltersDeletedArtifactsAndPreservesAuthorizedMetadata()
    {
        using var fixture = new Fixture();
        var deleted = new Artifact { ProjectId = fixture.ProjectId, Name = "Deleted" };
        deleted.MarkDeleted(Fixture.Now);
        fixture.Repository.Items.Add(deleted);

        var result = await fixture.Service.ListAsync(fixture.ProjectId, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        var item = Assert.Single(result.Value!);
        Assert.Equal(fixture.Artifact.Id, item.Id);
        Assert.Equal(fixture.ProjectId, item.ProjectId);
        Assert.Equal("Original", item.Title);
        Assert.Equal("Original description", item.Description);
        Assert.Equal(ArtifactType.Document, item.ArtifactType);
        Assert.Equal(ArtifactStatus.Draft, item.Status);
        Assert.Equal(fixture.Version.Id, item.CurrentVersionId);
        Assert.Equal(fixture.Actor.Id, item.CreatedByUserId);
        Assert.Equal(Fixture.Now.AddDays(-2), item.CreatedAt);
        Assert.Equal(Fixture.Now.AddDays(-1), item.UpdatedAt);
        Assert.Equal("CanViewProject", Assert.Single(fixture.ProjectChecks));
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionReadsPreferFileObjectMetadataAndRetainLegacyAttachmentFallback(bool detail)
    {
        using var fixture = new Fixture();
        fixture.Version.FileObject = new FileObject
        {
            OriginalFileName = "authoritative.txt", ContentType = "text/plain", SizeBytes = 37
        };
        var legacy = fixture.AddVersion(2);
        legacy.Attachment = new Attachment { FileName = "legacy.pdf", ContentType = "application/pdf", SizeBytes = 41 };
        var deleted = fixture.AddVersion(3);
        deleted.MarkDeleted(Fixture.Now);

        IReadOnlyList<ArtifactVersionResponse> versions;
        if (detail)
        {
            var result = await fixture.Service.GetAsync(fixture.Artifact.Id, fixture.Token);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(fixture.Artifact.Id, result.Value!.Id);
            Assert.Equal(fixture.ProjectId, result.Value.ProjectId);
            Assert.Equal("Original", result.Value.Title);
            Assert.Equal(fixture.Version.Id, result.Value.CurrentVersionId);
            versions = result.Value.Versions;
        }
        else
        {
            var result = await fixture.Service.ListVersionsAsync(fixture.Artifact.Id, fixture.Token);
            Assert.True(result.IsSuccess, result.Error);
            versions = result.Value!;
        }

        Assert.Equal(2, versions.Count);
        var modern = Assert.Single(versions, item => item.Id == fixture.Version.Id);
        Assert.Equal("authoritative.txt", modern.OriginalFileName);
        Assert.Equal("text/plain", modern.ContentType);
        Assert.Equal(37, modern.FileSize);
        Assert.Equal(fixture.Artifact.Id, modern.ArtifactId);
        Assert.Equal(fixture.Actor.Id, modern.UploadedByUserId);
        Assert.Equal("Version notes", modern.ChangeNote);
        Assert.Equal(Fixture.Now.AddDays(-1), modern.CreatedAt);
        Assert.Null(modern.DeletedAt);
        var old = Assert.Single(versions, item => item.Id == legacy.Id);
        Assert.Equal("legacy.pdf", old.OriginalFileName);
        Assert.Equal("application/pdf", old.ContentType);
        Assert.Equal(41, old.FileSize);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ArtifactStatus.Submitted)]
    public async Task CreateTrimsFieldsAndNotifiesOnlyProjectManagementRoles(ArtifactStatus? status)
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(fixture.ProjectId,
            new CreateArtifactRequest("  New artifact  ", "  New description  ", ArtifactType.Code, status), fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        var added = Assert.Single(fixture.Repository.Added);
        Assert.Equal("New artifact", added.Name);
        Assert.Equal("New description", added.Description);
        Assert.Equal(ArtifactType.Code, added.ArtifactType);
        Assert.Equal(status ?? ArtifactStatus.Draft, added.Status);
        Assert.Equal(fixture.ProjectId, added.ProjectId);
        Assert.Equal(fixture.Actor.Id, added.CreatedByUserId);
        Assert.Equal(added.Id, result.Value!.Id);
        Assert.Empty(result.Value.Versions);
        fixture.AssertMutation("ArtifactCreated", "Artifact", added.Id);
        fixture.AssertNotifications("New artifact uploaded", added.Id, "New artifact");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task BlankCreateTitleDoesNotStageAnyMutation(string title)
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(fixture.ProjectId,
            new CreateArtifactRequest(title, null, ArtifactType.Document, null), fixture.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Artifact title is required.", result.Error);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData(ArtifactStatus.Draft, null)]
    [InlineData(ArtifactStatus.Submitted, "Artifact submitted for review")]
    [InlineData(ArtifactStatus.Reviewed, "Artifact reviewed")]
    [InlineData(ArtifactStatus.Approved, "Artifact approved")]
    [InlineData(ArtifactStatus.Archived, null)]
    public async Task UpdatePreservesStatusNotificationAndAuditContracts(ArtifactStatus status, string? notification)
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.UpdateAsync(fixture.Artifact.Id,
            new UpdateArtifactRequest("  Updated  ", "  Changed description  ", ArtifactType.Code, status), fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Updated", result.Value!.Title);
        Assert.Equal("Changed description", result.Value.Description);
        Assert.Equal(ArtifactType.Code, result.Value.ArtifactType);
        Assert.Equal(status, result.Value.Status);
        Assert.Equal(fixture.Version.Id, Assert.Single(result.Value.Versions).Id);
        fixture.AssertMutation("ArtifactUpdated", "Artifact", fixture.Artifact.Id);
        if (notification is null) Assert.Empty(fixture.Notifications);
        else fixture.AssertNotifications(notification, fixture.Artifact.Id, "Updated");
    }

    [Fact]
    public async Task NullUpdateFieldsRetainExistingValuesWithoutDuplicateStatusNotifications()
    {
        using var fixture = new Fixture();
        fixture.Artifact.Status = ArtifactStatus.Approved;

        var result = await fixture.Service.UpdateAsync(fixture.Artifact.Id,
            new UpdateArtifactRequest(null, null, null, null), fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Original", result.Value!.Title);
        Assert.Equal("Original description", result.Value.Description);
        Assert.Equal(ArtifactType.Document, result.Value.ArtifactType);
        Assert.Equal(ArtifactStatus.Approved, result.Value.Status);
        Assert.Empty(fixture.Notifications);
        fixture.AssertMutation("ArtifactUpdated", "Artifact", fixture.Artifact.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task BlankUpdateTitlePreservesExistingArtifactWithoutEffects(string title)
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.UpdateAsync(fixture.Artifact.Id,
            new UpdateArtifactRequest(title, "Changed", ArtifactType.Code, ArtifactStatus.Approved), fixture.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Artifact title is required.", result.Error);
        Assert.Equal("Original", fixture.Artifact.Name);
        Assert.Equal("Original description", fixture.Artifact.Description);
        Assert.Equal(ArtifactType.Document, fixture.Artifact.ArtifactType);
        Assert.Equal(ArtifactStatus.Draft, fixture.Artifact.Status);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task DeleteSoftArchivesMetadataWithoutDeletingStoredBytesOrVersions()
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.DeleteAsync(fixture.Artifact.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ArtifactStatus.Archived, fixture.Artifact.Status);
        Assert.Equal(Fixture.Now, fixture.Artifact.DeletedAt);
        Assert.Null(fixture.Version.DeletedAt);
        Assert.Equal(fixture.Version.Id, fixture.Artifact.CurrentVersionId);
        Assert.Empty(fixture.Notifications);
        fixture.AssertMutation("ArtifactArchived", "Artifact", fixture.Artifact.Id);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task VersionDeletionSelectsLatestLiveFallbackOnlyWhenDeletingCurrent(bool current, bool fallback)
    {
        using var fixture = new Fixture();
        var deleted = fixture.AddVersion(99);
        deleted.MarkDeleted(Fixture.Now.AddDays(-1));
        ArtifactVersion? latest = null;
        if (fallback)
        {
            latest = fixture.AddVersion(7);
            fixture.AddVersion(3);
        }
        var previous = current ? fixture.Version.Id : latest!.Id;
        fixture.Artifact.CurrentVersionId = previous;

        var result = await fixture.Service.DeleteVersionAsync(fixture.Version.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Fixture.Now, fixture.Version.DeletedAt);
        Assert.Equal(current ? latest?.Id : previous, fixture.Artifact.CurrentVersionId);
        Assert.Equal(fallback ? 4 : 2, fixture.Repository.Versions.Count);
        Assert.Empty(fixture.Notifications);
        Assert.Equal(current ? 1 : 0, fixture.Repository.VersionListCalls);
        fixture.AssertMutation("ArtifactVersionDeleted", "ArtifactVersion", fixture.Version.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingVersionOrParentFailsWithoutAuthorityOrMutation(bool missingParent)
    {
        using var fixture = new Fixture();
        if (missingParent) fixture.Version.Artifact = null;
        else fixture.Repository.Versions.Clear();
        var before = fixture.CaptureGraph();

        var result = await fixture.Service.DeleteVersionAsync(fixture.Version.Id, fixture.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Artifact version not found.", result.Error);
        Assert.Empty(fixture.ProjectChecks);
        fixture.AssertNoEffects(before);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        private readonly CancellationTokenSource _cancellation = new();
        public CancellationToken Token => _cancellation.Token;
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Actor Actor { get; } = new();
        public Repository Repository { get; }
        public Artifact Artifact { get; }
        public ArtifactVersion Version { get; }
        public bool Allowed { get; init; } = true;
        public bool CanManage { get; init; } = true;
        public List<string> ProjectChecks { get; } = [];
        private List<AuditLogEntry> Audits { get; } = [];
        public List<(Guid Recipient, string Title, string? Body, string Type, Guid Id)> Notifications { get; } = [];
        private List<ProjectMember> Members { get; } = [];
        private List<(string Dependency, string Method, object?[] Arguments)> Calls { get; } = [];
        private int SaveCalls { get; set; }
        public ArtifactService Service { get; }

        public Fixture()
        {
            Repository = new Repository(Token);
            Artifact = new Artifact
            {
                ProjectId = ProjectId, Name = "Original", Description = "Original description",
                ArtifactType = ArtifactType.Document, CreatedByUserId = Actor.Id!.Value,
                CreatedAt = Now.AddDays(-2), UpdatedAt = Now.AddDays(-1)
            };
            Repository.Items.Add(Artifact);
            Version = AddVersion(1);
            Version.Notes = "Version notes";
            Version.CreatedAt = Now.AddDays(-1);
            Artifact.CurrentVersionId = Version.Id;
            foreach (var role in Enum.GetValues<ProjectRole>())
                Members.Add(new ProjectMember { ProjectId = ProjectId, UserId = Guid.NewGuid(), Role = role });
            var projectAuthorization = Strict<IProjectAuthorizationService>((method, args) =>
            {
                Calls.Add(("authority", method, args));
                Assert.Contains(method, new[] { "CanViewProject", "CanManageProject" });
                Assert.Equal(Actor.Id, args[0]);
                Assert.Equal(ProjectId, args[1]);
                Assert.Equal(Token, args[2]);
                ProjectChecks.Add(method);
                return Task.FromResult(Allowed && (method == "CanViewProject" || CanManage));
            });
            var projects = Strict<IProjectRepository>((method, args) =>
            {
                Calls.Add(("projects", method, args));
                Assert.Equal("ListMembersAsync", method);
                Assert.Equal(ProjectId, args[0]);
                Assert.Equal(Token, args[1]);
                return Task.FromResult<IReadOnlyList<ProjectMember>>(Members);
            });
            var audit = Strict<IAuditLogger>((method, args) =>
            {
                Calls.Add(("audit", method, args));
                Assert.Equal("LogAsync", method);
                Assert.Equal(Token, args[1]);
                Audits.Add(Assert.IsType<AuditLogEntry>(args[0]));
                return Task.CompletedTask;
            });
            var notification = Strict<INotificationService>((method, args) =>
            {
                Calls.Add(("notification", method, args));
                Assert.Equal("NotifyAsync", method);
                Assert.Equal(Token, args[5]);
                Notifications.Add(((Guid)args[0]!, (string)args[1]!, (string?)args[2], (string)args[3]!, (Guid)args[4]!));
                return Task.CompletedTask;
            });
            var persistence = Strict<IUnitOfWork>((method, args) =>
            {
                Calls.Add(("persistence", method, args));
                Assert.Equal("SaveChangesAsync", method);
                Assert.Equal(Token, args[0]);
                SaveCalls++;
                return Task.FromResult(1);
            });
            Service = new ArtifactService(Repository, projects, projectAuthorization,
                Unused<IFileRepository>(), Unused<IFileStorageService>(), Unused<IFileUploadPolicy>(),
                Unused<IFeatureFlagService>(), Unused<IQuotaService>(),
                new ArtifactAuthorizationService(Repository, projectAuthorization), Actor,
                Unused<ICurrentTenant>(), new Clock(), audit, notification, persistence);
        }

        public ArtifactVersion AddVersion(int number)
        {
            var version = new ArtifactVersion
            {
                ArtifactId = Artifact.Id, Artifact = Artifact, VersionNumber = number,
                CreatedByUserId = Actor.Id!.Value,
                Attachment = new Attachment { FileName = "attachment.txt", ContentType = "text/plain", SizeBytes = 13 }
            };
            Repository.Versions.Add(version);
            return version;
        }

        public async Task<Result> InvokeAsync(string operation) => operation switch
        {
            "list" => Status(await Service.ListAsync(ProjectId, Token)),
            "create" => Status(await Service.CreateAsync(ProjectId, new CreateArtifactRequest("New", null, ArtifactType.Code, null), Token)),
            "get" => Status(await Service.GetAsync(Artifact.Id, Token)),
            "update" => Status(await Service.UpdateAsync(Artifact.Id, new UpdateArtifactRequest("Changed", null, null, ArtifactStatus.Approved), Token)),
            "delete" => await Service.DeleteAsync(Artifact.Id, Token),
            "versions" => Status(await Service.ListVersionsAsync(Artifact.Id, Token)),
            "delete-version" => await Service.DeleteVersionAsync(Version.Id, Token),
            _ => throw new InvalidOperationException("Unknown operation.")
        };

        private static Result Status<T>(Result<T> result)
        {
            if (!result.IsSuccess) Assert.Null(result.Value);
            return new Result(result.IsSuccess, result.Error, result.ErrorDetail);
        }

        public (string Name, string? Description, ArtifactType Type, ArtifactStatus Status,
            Guid? CurrentVersion, DateTimeOffset? Deleted, Guid Creator, Guid VersionCreator,
            int VersionNumber, string? Notes, DateTimeOffset? VersionDeleted) CaptureGraph() =>
            (Artifact.Name, Artifact.Description, Artifact.ArtifactType, Artifact.Status,
                Artifact.CurrentVersionId, Artifact.DeletedAt, Artifact.CreatedByUserId, Version.CreatedByUserId,
                Version.VersionNumber, Version.Notes, Version.DeletedAt);

        public void AssertNoEffects(object? expectedGraph = null)
        {
            if (expectedGraph is not null) Assert.Equal(expectedGraph, CaptureGraph());
            Assert.Empty(Repository.Added);
            Assert.Empty(Audits);
            Assert.Empty(Notifications);
            Assert.Equal(0, SaveCalls);
        }

        public void AssertMutation(string action, string type, Guid id)
        {
            Assert.Equal("CanManageProject", Assert.Single(ProjectChecks));
            var entry = Assert.Single(Audits);
            Assert.Equal(new AuditLogEntry(Actor.Id, action, type, id), entry);
            Assert.Equal(1, SaveCalls);
            var effects = Calls.Where(call => call.Dependency is "audit" or "notification" or "persistence").ToArray();
            Assert.Equal("audit", effects[0].Dependency);
            Assert.Equal("persistence", effects[^1].Dependency);
            Assert.Equal("SaveChangesAsync", effects[^1].Method);
            Assert.Equal(Token, Assert.Single(effects[^1].Arguments));
        }

        public void AssertNotifications(string title, Guid id, string body)
        {
            var expected = Members.Where(member => member.Role is ProjectRole.Owner or ProjectRole.Manager or ProjectRole.Reviewer)
                .Select(member => (Recipient: member.UserId, Title: title, Body: (string?)body, Type: "Artifact", Id: id))
                .OrderBy(item => item.Recipient).ToArray();
            Assert.Equal(expected, Notifications.OrderBy(item => item.Recipient).ToArray());
        }

        public void Dispose() => _cancellation.Dispose();
    }

    private sealed class Actor : ICurrentUser
    {
        public Guid? Id { get; set; } = Guid.NewGuid();
        public bool Authenticated { get; set; } = true;
        public Guid? UserId => Id;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => Authenticated;
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Fixture.Now; }

    private sealed class Repository(CancellationToken expectedToken) : IArtifactRepository
    {
        public List<Artifact> Items { get; } = [];
        public List<Artifact> Added { get; } = [];
        public List<ArtifactVersion> Versions { get; } = [];
        public int VersionListCalls { get; private set; }
        public Task<IReadOnlyList<Artifact>> ListByProjectAsync(Guid id, CancellationToken token = default)
        {
            Assert.Equal(expectedToken, token);
            return Task.FromResult<IReadOnlyList<Artifact>>(Items.Where(item => item.ProjectId == id).ToArray());
        }
        public Task<Artifact?> GetArtifactAsync(Guid id, CancellationToken token = default)
        {
            Assert.Equal(expectedToken, token);
            return Task.FromResult(Items.SingleOrDefault(item => item.Id == id)).WaitAsync(expectedToken);
        }
        public Task<ArtifactVersion?> GetVersionAsync(Guid id, CancellationToken token = default)
        {
            Assert.Equal(expectedToken, token);
            return Task.FromResult(Versions.SingleOrDefault(item => item.Id == id));
        }
        public Task<IReadOnlyList<ArtifactVersion>> ListVersionsAsync(Guid id, CancellationToken token = default)
        {
            Assert.Equal(expectedToken, token);
            VersionListCalls++;
            return Task.FromResult<IReadOnlyList<ArtifactVersion>>(Versions.Where(item => item.ArtifactId == id).ToArray());
        }
        public Task AddArtifactAsync(Artifact artifact, CancellationToken token = default)
        {
            Assert.Equal(expectedToken, token);
            Added.Add(artifact);
            return Task.CompletedTask;
        }
        public Task<int> GetNextVersionNumberAsync(Guid id, CancellationToken token = default) =>
            throw new InvalidOperationException("Unexpected upload allocation.");
        public Task AddVersionAsync(ArtifactVersion version, CancellationToken token = default) =>
            throw new InvalidOperationException("Unexpected upload mutation.");
    }

    private static T Unused<T>() where T : class => Strict<T>((method, _) =>
        throw new InvalidOperationException($"Unexpected dependency: {typeof(T).Name}.{method}."));

    private static T Strict<T>(Func<string, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, StrictDependency>();
        ((StrictDependency)(object)proxy).Handler = handler;
        return proxy;
    }

    public class StrictDependency : DispatchProxy
    {
        public Func<string, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!.Name, args!);
    }
}
