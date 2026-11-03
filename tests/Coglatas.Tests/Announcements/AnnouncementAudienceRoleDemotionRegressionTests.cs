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
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.Announcements;

// Uses the production Audience, Workspace/Group authorizers and EF repositories.
// Independent InMemory contexts establish committed state versus retained tracking;
// these controls do not qualify PostgreSQL, RLS, transaction/lease concurrency,
// hosted workers, notification delivery or invalidation transport.
public sealed class AnnouncementAudienceRoleDemotionRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(SystemRole.Teacher, false)]
    [InlineData(SystemRole.Admin, false)]
    [InlineData(SystemRole.PlatformAdmin, false)]
    [InlineData(SystemRole.Teacher, true)]
    [InlineData(SystemRole.Admin, true)]
    [InlineData(SystemRole.PlatformAdmin, true)]
    public async Task CommittedRoleDemotionRemovesAudienceAuthorityDespiteRetainedTrackedRole(
        SystemRole originalRole, bool groupScope)
    {
        await using var fixture = await Fixture.CreateAsync(originalRole, WorkspaceRole.Member);
        var canManage = await fixture.Authorization.CanManageAsync(groupScope);
        // PlatformAdmin and SystemAdmin share persisted value 5. Its management
        // bypass must be covered separately from Teacher/Admin's visible-scope fallback.
        Assert.Equal(originalRole == SystemRole.SystemAdmin, canManage);
        Assert.True(await fixture.Authorization.CanViewAsync(groupScope));
        var before = await fixture.Authorization.AuthorizeAsync(groupScope);
        Assert.True(before.IsSuccess, before.Error);
        Assert.True(before.Value);

        await fixture.CommitDemotionAsync();
        await AssertRetainedRoleAndCurrentDemotionAsync(fixture, originalRole);
        await AssertFreshScopeDeniesVisibleMemberAsync(fixture, groupScope);

        var after = await fixture.Authorization.AuthorizeAsync(groupScope);

        output.WriteLine($"Original role={originalRole} ({(int)originalRole}); group={groupScope}; " +
            $"original management={canManage}; persisted=NormalUser; retained={fixture.TrackedActor!.SystemRole}; " +
            $"fresh scope denied; same scope authorized={after.Value}");
        Assert.True(after.IsSuccess, after.Error);
        Assert.False(after.Value);
    }

    [Theory]
    [InlineData(SystemRole.NormalUser, WorkspaceRole.Owner, false)]
    [InlineData(SystemRole.Teacher, WorkspaceRole.Member, false)]
    [InlineData(SystemRole.Admin, WorkspaceRole.Member, false)]
    [InlineData(SystemRole.PlatformAdmin, WorkspaceRole.Member, false)]
    [InlineData(SystemRole.NormalUser, WorkspaceRole.Owner, true)]
    [InlineData(SystemRole.Teacher, WorkspaceRole.Member, true)]
    [InlineData(SystemRole.Admin, WorkspaceRole.Member, true)]
    [InlineData(SystemRole.PlatformAdmin, WorkspaceRole.Member, true)]
    public async Task CurrentElevatedMembersAndOrdinaryOwnersRetainTheirExistingAudienceAuthority(
        SystemRole role, WorkspaceRole membershipRole, bool groupScope)
    {
        await using var fixture = await Fixture.CreateAsync(role, membershipRole);

        var authorized = await fixture.Authorization.AuthorizeAsync(groupScope);

        Assert.True(authorized.IsSuccess, authorized.Error);
        Assert.True(authorized.Value);
        Assert.True(await fixture.Authorization.CanViewAsync(groupScope));
        Assert.Equal(role == SystemRole.SystemAdmin || membershipRole == WorkspaceRole.Owner,
            await fixture.Authorization.CanManageAsync(groupScope));
        Assert.Empty(await fixture.Db.Announcements.ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryVisibleMembersWithoutManagementHaveNoAudiencePublicationAuthority(bool groupScope)
    {
        await using var fixture = await Fixture.CreateAsync(SystemRole.NormalUser, WorkspaceRole.Member);

        Assert.True(await fixture.Authorization.CanViewAsync(groupScope));
        Assert.False(await fixture.Authorization.CanManageAsync(groupScope));
        var authorized = await fixture.Authorization.AuthorizeAsync(groupScope);

        Assert.True(authorized.IsSuccess, authorized.Error);
        Assert.False(authorized.Value);
    }

    [Theory]
    [InlineData(SystemRole.Teacher, false)]
    [InlineData(SystemRole.Admin, false)]
    [InlineData(SystemRole.PlatformAdmin, false)]
    [InlineData(SystemRole.Teacher, true)]
    [InlineData(SystemRole.Admin, true)]
    [InlineData(SystemRole.PlatformAdmin, true)]
    public async Task PublicationRechecksRoleDemotedBetweenActualValidationAndDispatchAuthorization(
        SystemRole originalRole, bool groupScope)
    {
        await using var fixture = await Fixture.CreateAsync(originalRole, WorkspaceRole.Member);
        var claim = await fixture.SeedClaimAsync(groupScope);
        var observedAudience = new InterposingAudience(
            fixture.Authorization.Audience, fixture.CommitDemotionAsync);

        await fixture.CreateProcessor(observedAudience).ProcessAsync(
            claim, fixture.Clock.UtcNow, TimeSpan.FromMinutes(5));

        Assert.Equal(1, fixture.DemotionCommits);
        await AssertRetainedRoleAndCurrentDemotionAsync(fixture, originalRole);
        await AssertFreshScopeDeniesVisibleMemberAsync(fixture, groupScope);
        var calls = observedAudience.Calls;
        Assert.Equal(2, calls.Count);
        Guid? expectedGroupId = groupScope ? fixture.GroupId : null;
        Assert.All(calls, call =>
        {
            Assert.Equal(fixture.ActorId, call.ActorId);
            Assert.Equal(fixture.WorkspaceId, call.WorkspaceId);
            Assert.Equal(expectedGroupId, call.GroupId);
            Assert.Null(call.ChannelId);
            Assert.True(call.Success);
        });
        Assert.True(calls[0].Allowed);
        var draft = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        var announcements = await fixture.Db.Announcements.AsNoTracking().ToListAsync();
        var audits = await fixture.Db.AuditLogs.AsNoTracking().ToListAsync();
        output.WriteLine($"Original role={originalRole} ({(int)originalRole}); group={groupScope}; " +
            $"real authorizations={string.Join(',', calls.Select(call => call.Allowed))}; " +
            $"demotion commits={fixture.DemotionCommits}; persisted=NormalUser; draft={draft.Status}; " +
            $"announcements={announcements.Count}; invalidations={fixture.Invalidations.Calls.Count}; " +
            $"audit actions={string.Join(',', audits.Select(audit => audit.Action))}");

        Assert.Empty(announcements);
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.False(calls[1].Allowed);
        Assert.Equal(AnnouncementDraftStatus.Scheduled, draft.Status);
        Assert.Null(draft.PublishedAnnouncementId);
        Assert.Null(draft.PublishedAtUtc);
        Assert.Null(draft.PublicationClaimToken);
        Assert.Equal("AudienceNoLongerAuthorized", draft.LastPublicationFailureCode);
        Assert.Equal("AnnouncementPublicationDeferred", Assert.Single(audits).Action);
    }

    [Theory]
    [InlineData(SystemRole.NormalUser, WorkspaceRole.Owner)]
    [InlineData(SystemRole.Teacher, WorkspaceRole.Member)]
    [InlineData(SystemRole.Admin, WorkspaceRole.Member)]
    [InlineData(SystemRole.PlatformAdmin, WorkspaceRole.Member)]
    public async Task UnchangedAuthorizedRolesStillPublishThroughBothActualAudienceChecks(
        SystemRole role, WorkspaceRole membershipRole)
    {
        await using var fixture = await Fixture.CreateAsync(role, membershipRole);
        var claim = await fixture.SeedClaimAsync(false);
        var observedAudience = new InterposingAudience(fixture.Authorization.Audience);

        await fixture.CreateProcessor(observedAudience).ProcessAsync(
            claim, fixture.Clock.UtcNow, TimeSpan.FromMinutes(5));

        Assert.Equal(0, fixture.DemotionCommits);
        Assert.Equal(2, observedAudience.Calls.Count);
        Assert.All(observedAudience.Calls, call => Assert.True(call.Success && call.Allowed));
        var announcement = await fixture.Db.Announcements.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.ActorId, announcement.AuthorUserId);
        var draft = await fixture.Db.AnnouncementDrafts.AsNoTracking().SingleAsync();
        Assert.Equal(AnnouncementDraftStatus.Published, draft.Status);
        Assert.Equal(announcement.Id, draft.PublishedAnnouncementId);
        var invalidation = Assert.Single(fixture.Invalidations.Calls);
        Assert.Equal(announcement.Id, invalidation.AnnouncementId);
        Assert.Equal(fixture.ActorId, invalidation.ActorId);
        Assert.Equal("created", invalidation.Change);
        Assert.Equal(new[] { fixture.ActorId, fixture.RecipientId }.Order(), invalidation.Audience.Order());
        Assert.Equal("AnnouncementPublished", (await fixture.Db.AuditLogs.AsNoTracking().SingleAsync()).Action);
    }

    private static async Task AssertRetainedRoleAndCurrentDemotionAsync(Fixture fixture, SystemRole originalRole)
    {
        var tracked = Assert.IsType<User>(fixture.TrackedActor);
        Assert.Equal(originalRole, tracked.SystemRole);
        Assert.Equal(UserStatus.Active, tracked.Status);
        Assert.Null(tracked.DeletedAt);
        Assert.Equal(EntityState.Unchanged, fixture.Db.Entry(tracked).State);
        var current = await new TenantRepository(fixture.Db).GetCurrentUserAsync(fixture.ActorId);
        Assert.NotNull(current);
        Assert.NotSame(tracked, current);
        Assert.Equal(SystemRole.NormalUser, current.SystemRole);
        Assert.Equal(UserStatus.Active, current.Status);
        Assert.Null(current.DeletedAt);
        var member = await fixture.Db.WorkspaceMembers.AsNoTracking().SingleAsync(item => item.UserId == fixture.ActorId);
        Assert.Equal(WorkspaceRole.Member, member.Role);
        Assert.Equal(MembershipStatus.Active, member.Status);
        Assert.Equal(TenantUserStatus.Active,
            (await fixture.Db.TenantUsers.AsNoTracking().SingleAsync(item => item.UserId == fixture.ActorId)).Status);
    }

    private static async Task AssertFreshScopeDeniesVisibleMemberAsync(Fixture fixture, bool groupScope)
    {
        await using var fresh = fixture.NewContext();
        var authorization = fixture.CreateAuthorization(fresh);
        Assert.True(await authorization.CanViewAsync(groupScope));
        Assert.False(await authorization.CanManageAsync(groupScope));
        var control = await authorization.AuthorizeAsync(groupScope);
        Assert.True(control.IsSuccess, control.Error);
        Assert.False(control.Value);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly CurrentTenantService _tenant;
        public AppDbContext Db { get; }
        public Guid ActorId { get; }
        public Guid RecipientId { get; }
        public Guid WorkspaceId { get; }
        public Guid GroupId { get; }
        public FixedClock Clock { get; } = new();
        public RecordingInvalidations Invalidations { get; } = new();
        public AuthorizationScope Authorization { get; }
        public User? TrackedActor { get; private set; }
        public int DemotionCommits { get; private set; }

        private Fixture(DbContextOptions<AppDbContext> options, CurrentTenantService tenant, AppDbContext db,
            Guid actorId, Guid recipientId, Guid workspaceId, Guid groupId)
        {
            _options = options;
            _tenant = tenant;
            Db = db;
            ActorId = actorId;
            RecipientId = recipientId;
            WorkspaceId = workspaceId;
            GroupId = groupId;
            Authorization = CreateAuthorization(db);
        }

        public static async Task<Fixture> CreateAsync(SystemRole role, WorkspaceRole membershipRole)
        {
            var tenant = new CurrentTenantService();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"announcement-role-demotion-{Guid.NewGuid():N}").Options;
            var db = new AppDbContext(options, tenant);
            var tenantId = Guid.NewGuid();
            tenant.SetPlatformScope();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "Synthetic role tenant", DisplayName = "Synthetic role tenant",
                Slug = "announcement-role-demotion", Status = TenantStatus.Active
            });
            await db.SaveChangesAsync();
            tenant.SetTenant(tenantId, "announcement-role-demotion");
            var actor = new User
            {
                Email = "actor@example.test", NormalizedEmail = "ACTOR@EXAMPLE.TEST",
                DisplayName = "Synthetic author", Status = UserStatus.Active, SystemRole = role
            };
            var creator = new User
            {
                Email = "creator@example.test", NormalizedEmail = "CREATOR@EXAMPLE.TEST",
                DisplayName = "Synthetic workspace creator", Status = UserStatus.Active, SystemRole = SystemRole.NormalUser
            };
            var workspace = new Workspace
            {
                TenantId = tenantId, Name = "Workspace", Slug = "workspace",
                CreatedByUserId = creator.Id, Status = WorkspaceStatus.Active
            };
            var group = new Group
            {
                TenantId = tenantId, WorkspaceId = workspace.Id, Name = "Group", Slug = "group",
                CreatedByUserId = creator.Id, Status = GroupStatus.Active
            };
            db.Users.AddRange(actor, creator);
            db.Workspaces.Add(workspace);
            db.Groups.Add(group);
            foreach (var user in new[] { actor, creator })
            {
                db.TenantUsers.Add(new TenantUser { TenantId = tenantId, UserId = user.Id, Status = TenantUserStatus.Active });
                db.WorkspaceMembers.Add(new WorkspaceMember
                {
                    TenantId = tenantId, WorkspaceId = workspace.Id, UserId = user.Id,
                    Role = user.Id == actor.Id ? membershipRole : WorkspaceRole.Owner, Status = MembershipStatus.Active
                });
                db.GroupMembers.Add(new GroupMember
                {
                    TenantId = tenantId, GroupId = group.Id, UserId = user.Id,
                    Role = user.Id == actor.Id ? GroupRole.Member : GroupRole.Owner
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(options, tenant, db, actor.Id, creator.Id, workspace.Id, group.Id);
        }

        public AppDbContext NewContext() => new(_options, _tenant);

        public AuthorizationScope CreateAuthorization(AppDbContext db)
        {
            var unused = new AnnouncementContractDependencies();
            var users = new UserRepository(db);
            var workspaces = new WorkspaceRepository(db);
            var groups = new GroupRepository(db);
            var workspaceAuthorization = new WorkspaceAuthorizationService(users, workspaces);
            var groupAuthorization = new GroupAuthorizationService(groups, workspaces, workspaceAuthorization);
            var audience = new AnnouncementAudienceService(
                new AnnouncementRepository(db, Clock, _tenant), workspaces, groups, new ChannelRepository(db),
                users, new TenantRepository(db), workspaceAuthorization, groupAuthorization,
                unused.Create<IChannelAuthorizationService>("unusedChannelAuthorization"),
                unused.Create<IAnnouncementScheduleTimeZoneResolver>("unusedTimeZones"), new WorkerUser(), _tenant);
            return new AuthorizationScope(audience, workspaceAuthorization, groupAuthorization, ActorId, WorkspaceId, GroupId);
        }

        public AnnouncementDraftService CreateProcessor(IAnnouncementAudienceService audiences)
        {
            var unused = new AnnouncementContractDependencies();
            var worker = new WorkerUser();
            return new AnnouncementDraftService(new AnnouncementDraftRepository(Db, _tenant),
                new AnnouncementRepository(Db, Clock, _tenant), audiences,
                unused.Create<IAnnouncementScheduleTimeZoneResolver>("unusedTimeZones"),
                unused.Create<ICreateIdempotencyCoordinator>("unusedIdempotency"), worker, _tenant, Clock,
                new DbAuditLogger(Db, Clock, worker, _tenant), Invalidations, new EfUnitOfWork(Db));
        }

        public async Task CommitDemotionAsync()
        {
            TrackedActor = await Db.Users.SingleAsync(user => user.Id == ActorId);
            await using var writer = NewContext();
            var persistedActor = await writer.Users.SingleAsync(user => user.Id == ActorId);
            persistedActor.SystemRole = SystemRole.NormalUser;
            await writer.SaveChangesAsync();
            DemotionCommits++;
        }

        public async Task<AnnouncementPublicationClaim> SeedClaimAsync(bool groupScope)
        {
            var draft = new AnnouncementDraft
            {
                TenantId = _tenant.TenantId, AuthorUserId = ActorId, WorkspaceId = WorkspaceId,
                GroupId = groupScope ? GroupId : null,
                Title = "Synthetic role-sensitive publication", Body = "Synthetic body",
                Status = AnnouncementDraftStatus.Scheduled, ScheduledForUtc = Clock.UtcNow.AddMinutes(-1),
                PublicationClaimToken = Guid.NewGuid(), PublicationClaimOwner = "synthetic-role-worker",
                PublicationClaimExpiresAtUtc = Clock.UtcNow.AddMinutes(2)
            };
            Db.AnnouncementDrafts.Add(draft);
            await Db.SaveChangesAsync();
            return new AnnouncementPublicationClaim(draft.Id, draft.TenantId, draft.PublicationClaimToken.Value);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed record AuthorizationScope(
        AnnouncementAudienceService Audience, WorkspaceAuthorizationService Workspaces,
        GroupAuthorizationService Groups, Guid ActorId, Guid WorkspaceId, Guid GroupId)
    {
        public Task<Result<bool>> AuthorizeAsync(bool groupScope) =>
            Audience.IsAuthorizedForActorAsync(ActorId, WorkspaceId, groupScope ? GroupId : null, null);
        public Task<bool> CanManageAsync(bool groupScope) => groupScope
            ? Groups.CanManageGroup(ActorId, GroupId) : Workspaces.CanManageWorkspace(ActorId, WorkspaceId);
        public Task<bool> CanViewAsync(bool groupScope) => groupScope
            ? Groups.CanViewGroup(ActorId, GroupId) : Workspaces.CanViewWorkspace(ActorId, WorkspaceId);
    }

    private sealed class InterposingAudience(IAnnouncementAudienceService inner, Func<Task>? afterFirst = null)
        : IAnnouncementAudienceService
    {
        public List<AuthorizationCall> Calls { get; } = [];
        public Task<Result<IReadOnlyList<AnnouncementAudienceOptionResponse>>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);
        public Task<Result<bool>> IsAuthorizedAsync(Guid? workspaceId, Guid? groupId, Guid? channelId,
            CancellationToken cancellationToken = default) => inner.IsAuthorizedAsync(workspaceId, groupId, channelId, cancellationToken);

        public async Task<Result<bool>> IsAuthorizedForActorAsync(Guid actorUserId, Guid? workspaceId, Guid? groupId,
            Guid? channelId, CancellationToken cancellationToken = default)
        {
            var result = await inner.IsAuthorizedForActorAsync(actorUserId, workspaceId, groupId, channelId, cancellationToken);
            Calls.Add(new AuthorizationCall(actorUserId, workspaceId, groupId, channelId, result.IsSuccess, result.Value));
            if (Calls.Count == 1 && afterFirst is not null) await afterFirst();
            return result;
        }
    }

    private sealed record AuthorizationCall(Guid ActorId, Guid? WorkspaceId, Guid? GroupId, Guid? ChannelId, bool Success, bool Allowed);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
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
