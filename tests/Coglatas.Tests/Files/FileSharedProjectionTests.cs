using Coglatas.Application.Common;
using Coglatas.Application.Files;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Files;

public sealed class FileSharedProjectionTests
{
    [Fact]
    public async Task DirectionsUseRecipientAndGrantorWithoutManagerOrUploaderInference()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var incoming = await f.AddFileAsync("incoming.txt", uploader: f.SecondRecipient.Id);
        await f.GrantAsync(incoming, f.Owner.Id, f.SecondRecipient.Id);
        var outgoing = await f.AddFileAsync("outgoing.txt");
        await f.GrantAsync(outgoing, f.Recipient.Id);
        var unrelated = await f.AddFileAsync("unrelated.txt", policy: FileSharingPolicy.Workspace);
        await f.GrantAsync(unrelated, f.Recipient.Id, f.SecondRecipient.Id);
        await f.SetRoleAsync(f.Owner.Id, WorkspaceRole.Owner);

        var withMe = await f.Service().ListAsync(f.Workspace.Id, "withMe");
        var byMe = await f.Service().ListAsync(f.Workspace.Id, "byMe");

        Assert.Equal(incoming.FileObjectId, Assert.Single(withMe.Value!.Items).FileObjectId);
        Assert.Equal(outgoing.FileObjectId, Assert.Single(byMe.Value!.Items).FileObjectId);
        Assert.Equal(1, withMe.Value.TotalCount);
        Assert.Equal(1, byMe.Value.TotalCount);
    }

    [Fact]
    public async Task OneFileMayLegitimatelyAppearInBothDirections()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("dual.txt");
        await f.GrantAsync(file, f.Owner.Id);
        await f.GrantAsync(file, f.Recipient.Id);
        foreach (var direction in new[] { "withMe", "byMe" })
        {
            var result = await f.Service().ListAsync(f.Workspace.Id, direction);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(file.FileObjectId, Assert.Single(result.Value!.Items).FileObjectId);
            Assert.Equal(1, result.Value.TotalCount);
        }
    }

    [Fact]
    public async Task GrantorWithoutCurrentSourceAccessIsExcludedBeforeCanonicalCountAndPaging()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var visible = await f.AddFileAsync("visible.txt");
        await f.GrantAsync(visible, f.Recipient.Id);
        var hidden = await f.AddFileAsync("hidden.txt", uploader: f.SecondRecipient.Id);
        await f.GrantAsync(hidden, f.Recipient.Id);
        Assert.False(await f.FileAuthorization.CanViewAttachment(f.Owner.Id, hidden));

        var result = await f.Service().ListAsync(f.Workspace.Id, "byMe", pageSize: 1);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(visible.FileObjectId, Assert.Single(result.Value.Items).FileObjectId);
    }

    [Fact]
    public async Task CanonicalAttachmentIsChosenAfterPerAttachmentDiscoveryAndDeduplication()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var hiddenFirst = await f.AddFileAsync("same-file.txt", uploader: f.SecondRecipient.Id);
        await f.GrantAsync(hiddenFirst, f.Recipient.Id);
        var visible = new Attachment { FileObjectId = hiddenFirst.FileObjectId, WorkspaceId = f.Workspace.Id,
            FileObject = hiddenFirst.FileObject, OwnerType = AttachmentOwnerType.Workspace, OwnerId = f.Workspace.Id,
            OwnerUserId = f.Owner.Id, UploadedByUserId = f.Owner.Id, FileName = "same-file.txt",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(1) };
        f.Db.Attachments.Add(visible);
        await f.Db.SaveChangesAsync();

        var result = await f.Service().ListAsync(f.Workspace.Id, "byMe");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(visible.Id, Assert.Single(result.Value.Items).Id);
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("membership")]
    [InlineData("tenant-membership")]
    [InlineData("account")]
    [InlineData("file")]
    public async Task StaleOrRevokedRecordsDisappearFromRowsAndCounts(string mutation)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("revoke.txt");
        var grant = await f.GrantAsync(file, f.Recipient.Id);
        Assert.Single((await f.Service().ListAsync(f.Workspace.Id, "byMe")).Value!.Items);
        switch (mutation)
        {
            case "grant": grant.RevokedAt = DateTimeOffset.UtcNow; break;
            case "membership": (await f.Db.WorkspaceMembers.SingleAsync(m => m.UserId == f.Recipient.Id)).Status = MembershipStatus.Suspended; break;
            case "tenant-membership": (await f.Db.TenantUsers.SingleAsync(m => m.UserId == f.Recipient.Id)).Status = TenantUserStatus.Suspended; break;
            case "account": f.Recipient.Status = UserStatus.Suspended; break;
            case "file": file.FileObject!.MarkDeleted(DateTimeOffset.UtcNow, f.Owner.Id, "Synthetic deletion"); break;
        }
        await f.Db.SaveChangesAsync();

        var result = await f.Service().ListAsync(f.Workspace.Id, "byMe");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task CurrentExternalEligibilityAndExistingAggregateRedactionAreRetained()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var project = new Project { WorkspaceId = f.Workspace.Id, Name = "Synthetic external", Status = ProjectStatus.Active, CreatedByUserId = f.Owner.Id, OwnerUserId = f.Owner.Id };
        f.Db.Projects.Add(project);
        f.Db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = f.External.Id });
        await f.Db.SaveChangesAsync();
        var file = await f.AddFileAsync("external.txt");
        await f.GrantAsync(file, f.External.Id, kind: FileAccessGrantRecipientKind.ExternalProjectMember);
        f.Actor.UserId = f.External.Id;
        Assert.True(await f.FileAuthorization.CanViewAttachment(f.External.Id, file));
        Assert.Equal("SharedFileScopeUnavailable", (await f.Service().ListAsync(f.Workspace.Id, "withMe")).ErrorDetail!.Code);
        f.Actor.UserId = f.Owner.Id;
        var viewer = await f.Service().ListAsync(f.Workspace.Id, "byMe");
        var item = Assert.Single(viewer.Value!.Items);
        Assert.Equal("External", item.AccessState);
        Assert.Null(item.ExternalRecipientCount);
        Assert.False(item.CanManageSharing);
        await f.SetRoleAsync(f.Owner.Id, WorkspaceRole.Owner);
        var manager = Assert.Single((await f.Service().ListAsync(f.Workspace.Id, "byMe")).Value!.Items);
        Assert.Equal(1, manager.ExternalRecipientCount);
        project.Status = ProjectStatus.Archived;
        await f.Db.SaveChangesAsync();
        var revoked = await f.Service().ListAsync(f.Workspace.Id, "byMe");
        Assert.Empty(revoked.Value!.Items);
        Assert.Equal(0, revoked.Value.TotalCount);
    }

    [Fact]
    public async Task LastEffectiveDirectionalGrantOrdersDistinctFilesWithStableIdTieBreakAndWideOffset()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var low = await f.AddFileAsync("low.txt", fixedId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = await f.AddFileAsync("high.txt", fixedId: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var newest = await f.AddFileAsync("newest.txt", fixedId: Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var date = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        await f.GrantAsync(low, f.Recipient.Id, at: date);
        await f.GrantAsync(high, f.Recipient.Id, at: date);
        await f.GrantAsync(newest, f.Recipient.Id, at: date.AddDays(-1));
        await f.GrantAsync(newest, f.SecondRecipient.Id, at: date.AddDays(1));
        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await f.Service().ListAsync(f.Workspace.Id, "byMe", page, 1);
            Assert.Equal(3, result.Value!.TotalCount);
            ids.Add(Assert.Single(result.Value.Items).FileObjectId);
        }
        Assert.Equal([newest.FileObjectId, high.FileObjectId, low.FileObjectId], ids);
        var beyond = await f.Service().ListAsync(f.Workspace.Id, "byMe", int.MaxValue, int.MaxValue);
        Assert.Empty(beyond.Value!.Items);
        Assert.Equal(3, beyond.Value.TotalCount);
        Assert.Equal(100, beyond.Value.PageSize);
    }

    [Fact]
    public async Task AnotherWorkspaceTenantAndNonWorkspaceAttachmentCannotBeDiscovered()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("not-workspace.txt");
        await f.GrantAsync(file, f.Recipient.Id);
        file.OwnerType = AttachmentOwnerType.TaskItem;
        file.OwnerId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Service().ListAsync(f.Workspace.Id, "byMe")).Value!.Items);
        var denied = await f.Service().ListAsync(f.OtherWorkspace.Id, "byMe");
        Assert.False(denied.IsSuccess);
        Assert.Equal("SharedFileScopeUnavailable", denied.ErrorDetail!.Code);
        f.TenantContext.SetTenant(f.OtherTenant.Id, f.OtherTenant.Slug);
        denied = await f.Service().ListAsync(f.Workspace.Id, "byMe");
        Assert.False(denied.IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("BYME")]
    public async Task MissingOrUnsupportedDirectionIsRejectedWithoutInventingAView(string? direction)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var result = await f.Service().ListAsync(f.Workspace.Id, direction);
        Assert.False(result.IsSuccess);
        Assert.Equal("ValidationFailed", result.ErrorDetail!.Code);
    }

    [Fact]
    public async Task AuthenticatedTenantActorAndWorkspaceAdmissionAreRequiredEvenForEmptyLists()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        f.Actor.IsAuthenticated = false;
        Assert.Equal("AuthenticationRequired", (await f.Service().ListAsync(f.Workspace.Id, "withMe")).ErrorDetail!.Code);
        f.Actor.IsAuthenticated = true;
        f.TenantContext.SetPlatformScope();
        Assert.Equal("SharedFileScopeUnavailable", (await f.Service().ListAsync(f.Workspace.Id, "withMe")).ErrorDetail!.Code);
    }

    [Fact]
    public async Task AuthorityLossWhileRepositoryIsRunningDoesNotReturnRetainedRowsOrCounts()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("late.txt");
        await f.GrantAsync(file, f.Recipient.Id);
        var actor = f.Actor;
        var unrelatedId = f.Unrelated.Id;
        var repository = new AfterReadRepository(f.Repository, () => actor.UserId = unrelatedId);
        var result = await f.Service(repository: repository).ListAsync(f.Workspace.Id, "byMe");
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("SharedFileScopeUnavailable", result.ErrorDetail!.Code);
    }

    [Fact]
    public void NpgsqlTranslatesDirectionalCanonicalizationAndPagingWithoutExecutingDatabase()
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(Guid.NewGuid(), "sql-translation-only");
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only").Options, tenant);
        var scope = new FileSharedProjectionScope(tenant.TenantId, Guid.NewGuid(), Guid.NewGuid(), FileSharedDirection.ByMe, false);
        var sql = FileSharedProjectionQuery.Ordered(db, scope).Skip(5).Take(5).ToQueryString();
        Assert.Contains("GrantedByUserId", sql, StringComparison.Ordinal);
        Assert.Contains("RecipientUserId", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("RevokedAt", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryDiscoveryMatchesExistingFileAuthorizationForEachDirectWorkspaceAttachment()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        foreach (var policy in new[] { FileSharingPolicy.Private, FileSharingPolicy.Workspace })
        {
            foreach (var owned in new[] { false, true })
            {
                var attachment = await f.AddFileAsync($"parity-{policy}-{owned}.txt",
                    uploader: owned ? f.Owner.Id : f.SecondRecipient.Id, policy: policy);
                await f.GrantAsync(attachment, f.Recipient.Id);
            }
        }
        foreach (var role in new[] { WorkspaceRole.Member, WorkspaceRole.Owner })
        {
            await f.SetRoleAsync(f.Owner.Id, role);
            var expected = new List<Guid>();
            foreach (var attachment in await f.Db.Attachments.Include(a => a.FileObject).ToListAsync())
            {
                if (await f.FileAuthorization.CanViewAttachment(f.Owner.Id, attachment)) expected.Add(attachment.FileObjectId);
            }
            var actual = await f.Service().ListAsync(f.Workspace.Id, "byMe");
            Assert.True(actual.IsSuccess, actual.Error);
            Assert.Equal(expected.Order(), actual.Value!.Items.Select(i => i.FileObjectId).Order());
            Assert.Equal(expected.Count, actual.Value.TotalCount);
        }
    }

    [Fact]
    public async Task ActorChangeDuringSharingProjectionCannotReturnRetainedPage()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("late-presentation.txt");
        await f.GrantAsync(file, f.Recipient.Id);
        var actor = f.Actor;
        var unrelatedId = f.Unrelated.Id;
        var sharing = new BoundaryPresentation(() => actor.UserId = unrelatedId);
        var result = await f.Service(sharing: sharing).ListAsync(f.Workspace.Id, "byMe");
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("SharedFileScopeUnavailable", result.ErrorDetail!.Code);
    }

    [Theory]
    [InlineData("membership", false)]
    [InlineData("membership", true)]
    [InlineData("manager", false)]
    [InlineData("manager", true)]
    public async Task SameActorAuthorityIsRecheckedAfterSharingPresentation(string boundary, bool revoke)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("same-actor-presentation.txt");
        await f.GrantAsync(file, f.Recipient.Id);
        if (boundary == "manager") await f.SetRoleAsync(f.Owner.Id, WorkspaceRole.Owner);
        var actor = f.Actor.UserId;
        var member = await f.Db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == f.Workspace.Id && m.UserId == actor);
        Func<int> saveChanges = f.Db.SaveChanges;
        var sharing = new BoundaryPresentation(() =>
        {
            if (!revoke) return;
            if (boundary == "membership") member.Status = MembershipStatus.Suspended;
            else member.Role = WorkspaceRole.Member;
            saveChanges();
        }, boundary == "manager");

        var result = await f.Service(sharing: sharing).ListAsync(f.Workspace.Id, "byMe");

        Assert.Equal(actor, f.Actor.UserId);
        if (revoke)
        {
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal("SharedFileScopeUnavailable", result.ErrorDetail!.Code);
        }
        else
        {
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(1, result.Value!.TotalCount);
            var item = Assert.Single(result.Value.Items);
            Assert.Equal(file.FileObjectId, item.FileObjectId);
            Assert.Equal(boundary == "manager", item.CanManageSharing);
            Assert.Equal(boundary == "manager" ? 1 : null, item.ExternalRecipientCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectionalGrantIsRecheckedAfterReadEvenWhenUploaderRetainsFileAccess(bool revoke)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("same-actor-direction.txt");
        var grant = await f.GrantAsync(file, f.Recipient.Id);
        Func<int> saveChanges = f.Db.SaveChanges;
        var repository = new AfterReadRepository(f.Repository, () =>
        {
            if (!revoke) return;
            grant.RevokedAt = DateTimeOffset.UtcNow;
            saveChanges();
        });

        var result = await f.Service(repository: repository).ListAsync(f.Workspace.Id, "byMe");
        Assert.True(await f.FileAuthorization.CanViewAttachment(f.Owner.Id, file));
        var fresh = await f.Repository.ListAsync(new(f.Tenant.Id, f.Owner.Id, f.Workspace.Id, FileSharedDirection.ByMe, false), 1, 20);
        Assert.Equal(revoke ? 0 : 1, fresh.TotalCount);
        if (revoke)
        {
            Assert.Empty(fresh.Items);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal("SharedFileScopeUnavailable", result.ErrorDetail!.Code);
        }
        else
        {
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal(file.FileObjectId, Assert.Single(result.Value.Items).FileObjectId);
        }
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("demoted")]
    [InlineData("restored")]
    public async Task IndependentCommittedManagerChangeCannotReuseReadersTrackedOwner(string change)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        await f.SetRoleAsync(f.Owner.Id, WorkspaceRole.Owner);
        var file = await f.AddFileAsync("manager-only-private.txt", uploader: f.SecondRecipient.Id, owner: f.SecondRecipient.Id);
        await f.GrantAsync(file, f.Recipient.Id);
        Assert.NotEqual(f.Owner.Id, file.OwnerUserId);
        Assert.NotEqual(f.Owner.Id, file.UploadedByUserId);
        Assert.False(await f.Grants.HasEffectiveGrantAsync(file.FileObjectId, f.Owner.Id));
        Assert.True(await f.FileAuthorization.CanViewAttachment(f.Owner.Id, file));
        var cachedOwner = await f.Db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == f.Workspace.Id && m.UserId == f.Owner.Id);
        Assert.Equal(WorkspaceRole.Owner, cachedOwner.Role);
        await using var writer = f.IndependentContext();
        var workspaceId = f.Workspace.Id;
        var ownerId = f.Owner.Id;
        var writerMembers = writer.WorkspaceMembers;
        Func<int> saveWriter = writer.SaveChanges;
        var sharing = new BoundaryPresentation(() =>
        {
            if (change == "unchanged") return;
            var member = writerMembers.Single(m => m.WorkspaceId == workspaceId && m.UserId == ownerId);
            member.Role = WorkspaceRole.Member;
            saveWriter();
            if (change == "restored")
            {
                member.Role = WorkspaceRole.Owner;
                saveWriter();
            }
        }, canManage: true);

        var result = await f.Service(sharing: sharing).ListAsync(f.Workspace.Id, "byMe");

        Assert.Equal(WorkspaceRole.Owner, cachedOwner.Role);
        Assert.Same(cachedOwner, f.Db.ChangeTracker.Entries<WorkspaceMember>().Single(e => e.Entity.UserId == f.Owner.Id).Entity);
        var storedRole = await writer.WorkspaceMembers.AsNoTracking().Where(m => m.WorkspaceId == workspaceId && m.UserId == ownerId).Select(m => m.Role).SingleAsync();
        Assert.Equal(change == "demoted" ? WorkspaceRole.Member : WorkspaceRole.Owner, storedRole);
        Assert.Equal(QueryTrackingBehavior.TrackAll, f.Db.ChangeTracker.QueryTrackingBehavior);
        if (change == "demoted")
        {
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal("SharedFileScopeUnavailable", result.ErrorDetail!.Code);
        }
        else
        {
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal(file.FileObjectId, Assert.Single(result.Value.Items).FileObjectId);
        }
    }

    [Theory]
    [InlineData(QueryTrackingBehavior.TrackAll, "success")]
    [InlineData(QueryTrackingBehavior.TrackAll, "failure")]
    [InlineData(QueryTrackingBehavior.TrackAll, "canceled")]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, "success")]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, "failure")]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, "canceled")]
    public async Task CurrentAuthorizationReadPreservesPendingTrackedStateAndRestoresPriorMode(QueryTrackingBehavior mode, string outcome)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        await f.SetRoleAsync(f.Owner.Id, WorkspaceRole.Owner);
        var member = await f.Db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == f.Workspace.Id && m.UserId == f.Owner.Id);
        member.Role = WorkspaceRole.Member; // Pending, deliberately not committed.
        f.Db.ChangeTracker.DetectChanges();
        var entry = f.Db.Entry(member);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(WorkspaceRole.Owner, entry.Property(m => m.Role).OriginalValue);
        f.Db.ChangeTracker.QueryTrackingBehavior = mode;
        using var cancellation = new CancellationTokenSource();
        var tracker = f.Db.ChangeTracker;
        var authorization = f.WorkspaceAuthorization;
        var ownerId = f.Owner.Id;
        var workspaceId = f.Workspace.Id;
        Action cancel = cancellation.Cancel;
        async Task<bool> Read(CancellationToken token)
        {
            Assert.Equal(QueryTrackingBehavior.NoTracking, tracker.QueryTrackingBehavior);
            Assert.True(await authorization.CanManageWorkspace(ownerId, workspaceId, token));
            if (outcome == "failure") throw new InvalidOperationException("Synthetic read failure.");
            if (outcome == "canceled") { cancel(); token.ThrowIfCancellationRequested(); }
            return true;
        }
        if (outcome == "failure")
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Repository.ReadCurrentAuthorizationAsync(Read, cancellation.Token));
        else if (outcome == "canceled")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Repository.ReadCurrentAuthorizationAsync(Read, cancellation.Token));
        else
            Assert.True(await f.Repository.ReadCurrentAuthorizationAsync(Read, cancellation.Token));

        Assert.Equal(mode, f.Db.ChangeTracker.QueryTrackingBehavior);
        Assert.Same(member, f.Db.ChangeTracker.Entries<WorkspaceMember>().Single(e => e.Entity.UserId == f.Owner.Id).Entity);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(WorkspaceRole.Member, member.Role);
        Assert.Equal(WorkspaceRole.Owner, entry.Property(m => m.Role).OriginalValue);
    }

    private sealed class BoundaryPresentation(Action boundary, bool canManage = false) : IFileSharingService
    {
        public Task<IReadOnlyDictionary<Guid, FileSharingPresentation>> GetListPresentationsAsync(Guid workspaceId,
            Guid actorUserId, IReadOnlyCollection<FileObject> files, CancellationToken cancellationToken = default)
        {
            var result = files.ToDictionary(file => file.Id, file => new FileSharingPresentation("Private", canManage ? 1 : null, canManage, file.SharingVersion));
            boundary();
            return Task.FromResult<IReadOnlyDictionary<Guid, FileSharingPresentation>>(result);
        }
        public Task<Result<FileSharingResponse>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<FileSharingResponse>> UpdatePolicyAsync(Guid id, FileSharingPolicyUpdateRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<FileSharingResponse>> GrantAsync(Guid id, FileShareGrantCreateRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<FileSharingResponse>> RevokeAsync(Guid id, Guid grantId, long version, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class AfterReadRepository(IFileSharedProjectionRepository inner, Action after) : IFileSharedProjectionRepository
    {
        public Task<T> ReadCurrentAuthorizationAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken = default) =>
            inner.ReadCurrentAuthorizationAsync(read, cancellationToken);

        public async Task<PagedResponse<Attachment>> ListAsync(FileSharedProjectionScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var result = await inner.ListAsync(scope, page, pageSize, cancellationToken);
            after();
            return result;
        }
    }
}
