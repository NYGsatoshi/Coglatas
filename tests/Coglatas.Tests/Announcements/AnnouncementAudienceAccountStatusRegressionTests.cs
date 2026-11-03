using Coglatas.Application.Announcements;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.Announcements;

// Actual Application services and repositories on InMemory and, in the explicit
// PostgreSQL control, an isolated migrated database. The worker tests
// start with an already-claimed Scheduled draft; they do not qualify PostgreSQL
// leases, transactions, hosted-worker operation, notifications, or transport.
public sealed class AnnouncementAudienceAccountStatusRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("suspended", false)]
    [InlineData("archived", false)]
    [InlineData("deleted", false)]
    [InlineData("suspended", true)]
    [InlineData("archived", true)]
    [InlineData("deleted", true)]
    public async Task ServerOwnedAuthorMustRemainActiveDespiteRetainedWorkspaceOwnership(string change, bool keepTrackedActor)
    {
        await using var fixture = await Fixture.CreateAsync();
        var active = await fixture.AuthorizeAsync(fixture.ActorId);
        Assert.True(active.IsSuccess, active.Error);
        Assert.True(active.Value);
        await fixture.RevokeAccountAsync(change, keepTrackedActor);

        var revoked = await fixture.AuthorizeAsync(fixture.ActorId);

        output.WriteLine($"Change={change}; warmed actor={keepTrackedActor}; authorized={revoked.Value}");
        Assert.True(revoked.IsSuccess, revoked.Error);
        Assert.False(revoked.Value);
    }

    [Fact]
    public async Task MissingServerActorCannotUseAnotherUsersWorkspaceOwnership()
    {
        await using var fixture = await Fixture.CreateAsync();
        var active = await fixture.AuthorizeAsync(fixture.ActorId);
        Assert.True(active.IsSuccess, active.Error);
        Assert.True(active.Value);

        var missing = await fixture.AuthorizeAsync(Guid.NewGuid());

        Assert.True(missing.IsSuccess, missing.Error);
        Assert.False(missing.Value);
        Assert.Equal(2, await fixture.Db.Users.CountAsync());
        Assert.Empty(await fixture.Db.Announcements.ToListAsync());
    }

    [Theory]
    [InlineData("active", false)]
    [InlineData("suspended", false)]
    [InlineData("archived", false)]
    [InlineData("deleted", false)]
    [InlineData("suspended", true)]
    [InlineData("archived", true)]
    [InlineData("deleted", true)]
    public async Task DurableProcessRechecksPersistedAuthorBeforePublicationAndInvalidation(string change, bool keepTrackedActor)
        => await VerifyDurablePublicationAsync(change, keepTrackedActor);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task NativeDurableProcessRejectsCommittedInactiveAuthorDespiteTrackedActiveOwnership()
    {
        foreach (var change in new[] { "active", "suspended", "archived", "deleted" })
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(),
                database => VerifyDurablePublicationAsync(change, true, database));
        }
    }

    private async Task VerifyDurablePublicationAsync(string change, bool keepTrackedActor, string? connectionString = null)
    {
        await using var fixture = await Fixture.CreateAsync(connectionString);
        var active = await fixture.AuthorizeAsync(fixture.ActorId);
        Assert.True(active.IsSuccess, active.Error);
        Assert.True(active.Value);
        var claim = await fixture.SeedClaimAsync();
        if (change != "active") await fixture.RevokeAccountAsync(change, keepTrackedActor);

        await fixture.Processor.ProcessAsync(claim, fixture.Clock.UtcNow, TimeSpan.FromMinutes(5));

        var draft = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        var announcements = await fixture.Db.Announcements.AsNoTracking().ToListAsync();
        var audits = await fixture.Db.AuditLogs.AsNoTracking().ToListAsync();
        output.WriteLine($"Change={change}; warmed actor={keepTrackedActor}; draft={draft.Status}; " +
            $"announcements={announcements.Count}; invalidations={fixture.Invalidations.Calls.Count}; " +
            $"audit actions={string.Join(',', audits.Select(audit => audit.Action))}");
        if (change == "active")
        {
            var published = Assert.Single(announcements);
            Assert.Equal(fixture.ActorId, published.AuthorUserId);
            Assert.Equal(fixture.WorkspaceId, published.WorkspaceId);
            Assert.Equal(AnnouncementDraftStatus.Published, draft.Status);
            Assert.Equal(published.Id, draft.PublishedAnnouncementId);
            Assert.Equal(fixture.ActorId, Assert.Single(audits).ActorUserId);
            Assert.Equal("AnnouncementPublished", audits[0].Action);
            var invalidation = Assert.Single(fixture.Invalidations.Calls);
            Assert.Equal(published.Id, invalidation.AnnouncementId);
            Assert.Equal(fixture.ActorId, invalidation.ActorId);
            Assert.Equal("created", invalidation.Change);
            Assert.Equal(new[] { fixture.ActorId, fixture.RecipientId }.Order(), invalidation.Audience.Order());
        }
        else
        {
            Assert.Empty(announcements);
            Assert.Equal(AnnouncementDraftStatus.Scheduled, draft.Status);
            Assert.Null(draft.PublishedAnnouncementId);
            Assert.Null(draft.PublishedAtUtc);
            Assert.Null(draft.PublicationClaimToken);
            Assert.Equal("DraftValidationFailed", draft.LastPublicationFailureCode);
            Assert.Equal(fixture.ActorId, Assert.Single(audits).ActorUserId);
            Assert.Equal("AnnouncementPublicationDeferred", audits[0].Action);
            Assert.Empty(fixture.Invalidations.Calls);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly CurrentTenantService _tenant;
        private readonly AnnouncementAudienceService _audiences;
        public AppDbContext Db { get; }
        public Guid ActorId { get; }
        public Guid RecipientId { get; }
        public Guid WorkspaceId { get; }
        public FixedClock Clock { get; } = new();
        public RecordingInvalidations Invalidations { get; } = new();
        public AnnouncementDraftService Processor { get; }

        private Fixture(DbContextOptions<AppDbContext> options, CurrentTenantService tenant, AppDbContext db,
            Guid actorId, Guid recipientId, Guid workspaceId)
        {
            _options = options;
            _tenant = tenant;
            Db = db;
            ActorId = actorId;
            RecipientId = recipientId;
            WorkspaceId = workspaceId;
            var unused = new AnnouncementContractDependencies();
            var users = new UserRepository(db);
            var workspaces = new WorkspaceRepository(db);
            var announcements = new AnnouncementRepository(db, Clock, tenant);
            var workerUser = new WorkerUser();
            var timeZones = unused.Create<IAnnouncementScheduleTimeZoneResolver>("unusedTimeZones");
            _audiences = new AnnouncementAudienceService(
                announcements, workspaces, unused.Create<IGroupRepository>("unusedGroups"),
                unused.Create<IChannelRepository>("unusedChannels"), users, new TenantRepository(db),
                new WorkspaceAuthorizationService(users, workspaces),
                unused.Create<IGroupAuthorizationService>("unusedGroupAuthorization"),
                unused.Create<IChannelAuthorizationService>("unusedChannelAuthorization"), timeZones, workerUser, tenant);
            Processor = new AnnouncementDraftService(new AnnouncementDraftRepository(db, tenant), announcements,
                _audiences, timeZones, unused.Create<ICreateIdempotencyCoordinator>("unusedIdempotency"),
                workerUser, tenant, Clock, new DbAuditLogger(db, Clock, workerUser, tenant),
                Invalidations, new EfUnitOfWork(db));
        }

        public static async Task<Fixture> CreateAsync(string? connectionString = null)
        {
            var tenant = new CurrentTenantService();
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            if (connectionString is null) builder.UseInMemoryDatabase($"announcement-account-status-{Guid.NewGuid():N}");
            else builder.UseNpgsql(connectionString);
            var options = builder.Options;
            var db = new AppDbContext(options, tenant);
            var tenantId = Guid.NewGuid();
            tenant.SetPlatformScope();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "Synthetic tenant", DisplayName = "Synthetic tenant",
                Slug = "announcement-account-status", Status = TenantStatus.Active
            });
            await db.SaveChangesAsync();
            tenant.SetTenant(tenantId, "announcement-account-status");
            var owner = new User
            {
                Id = Guid.NewGuid(), Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST",
                DisplayName = "Synthetic owner", Status = UserStatus.Active, SystemRole = SystemRole.NormalUser
            };
            var recipient = new User
            {
                Id = Guid.NewGuid(), Email = "recipient@example.test", NormalizedEmail = "RECIPIENT@EXAMPLE.TEST",
                DisplayName = "Synthetic recipient", Status = UserStatus.Active, SystemRole = SystemRole.NormalUser
            };
            var workspace = new Workspace
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = "Workspace", Slug = "workspace", CreatedByUserId = owner.Id
            };
            db.Users.AddRange(owner, recipient);
            db.Workspaces.Add(workspace);
            foreach (var user in new[] { owner, recipient })
            {
                db.TenantUsers.Add(new TenantUser { TenantId = tenantId, UserId = user.Id, Status = TenantUserStatus.Active });
                db.WorkspaceMembers.Add(new WorkspaceMember
                {
                    TenantId = tenantId, UserId = user.Id, WorkspaceId = workspace.Id,
                    Role = user.Id == owner.Id ? WorkspaceRole.Owner : WorkspaceRole.Member, Status = MembershipStatus.Active
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(options, tenant, db, owner.Id, recipient.Id, workspace.Id);
        }

        public Task<Result<bool>> AuthorizeAsync(Guid actorId) =>
            _audiences.IsAuthorizedForActorAsync(actorId, WorkspaceId, null, null);

        public async Task RevokeAccountAsync(string change, bool keepTrackedActor)
        {
            var tracked = await Db.Users.SingleAsync(user => user.Id == ActorId);
            var trackedMembership = await Db.WorkspaceMembers.Include(member => member.User)
                .SingleAsync(member => member.UserId == ActorId);
            Assert.Same(tracked, trackedMembership.User);
            Assert.Equal(UserStatus.Active, tracked.Status);
            Assert.Null(tracked.DeletedAt);
            await using (var writer = new AppDbContext(_options, _tenant))
            {
                var actor = await writer.Users.SingleAsync(user => user.Id == ActorId);
                if (change == "deleted") actor.MarkDeleted(Clock.UtcNow);
                else actor.Status = change == "suspended" ? UserStatus.Suspended : UserStatus.Archived;
                await writer.SaveChangesAsync();
            }
            var persisted = await Db.Users.AsNoTracking().SingleAsync(user => user.Id == ActorId);
            if (change == "deleted") Assert.NotNull(persisted.DeletedAt);
            else Assert.Equal(change == "suspended" ? UserStatus.Suspended : UserStatus.Archived, persisted.Status);
            Assert.Equal(TenantUserStatus.Active, (await Db.TenantUsers.AsNoTracking().SingleAsync(member => member.UserId == ActorId)).Status);
            Assert.Equal(MembershipStatus.Active, trackedMembership.Status);
            Assert.Equal(WorkspaceRole.Owner, trackedMembership.Role);
            Assert.Equal(UserStatus.Active, tracked.Status);
            Assert.Null(tracked.DeletedAt);
            if (!keepTrackedActor) Db.ChangeTracker.Clear();
        }

        public async Task<AnnouncementPublicationClaim> SeedClaimAsync()
        {
            var draft = new AnnouncementDraft
            {
                Id = Guid.NewGuid(), TenantId = _tenant.TenantId, AuthorUserId = ActorId, WorkspaceId = WorkspaceId,
                Title = "Synthetic scheduled announcement", Body = "Synthetic body", Status = AnnouncementDraftStatus.Scheduled,
                ScheduledForUtc = Clock.UtcNow.AddMinutes(-1), PublicationClaimToken = Guid.NewGuid(),
                PublicationClaimOwner = "synthetic-worker", PublicationClaimExpiresAtUtc = Clock.UtcNow.AddMinutes(2)
            };
            Db.AnnouncementDrafts.Add(draft);
            await Db.SaveChangesAsync();
            return new AnnouncementPublicationClaim(draft.Id, draft.TenantId, draft.PublicationClaimToken.Value);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 16, 0, 0, TimeSpan.Zero);
    }

    private sealed class WorkerUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => false;
    }

    private sealed class RecordingInvalidations : IBusinessInvalidationPublisher
    {
        public List<(Guid AnnouncementId, Guid ActorId, string Change, Guid[] Audience)> Calls { get; } = [];
        public Task AnnouncementChangedAsync(Announcement announcement, Guid actorUserId, string change,
            IEnumerable<Guid> audienceUserIds, CancellationToken cancellationToken = default)
        {
            Calls.Add((announcement.Id, actorUserId, change, audienceUserIds.ToArray()));
            return Task.CompletedTask;
        }
        public Task TaskChangedAsync(TaskItem task, Guid actorUserId, string change, IEnumerable<string>? changedFields = null,
            IEnumerable<Guid>? affectedUserIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ProjectChangedAsync(Project project, Guid actorUserId, string change, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task FileChangedAsync(FileObject fileObject, Attachment attachment, Guid actorUserId, string change,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
