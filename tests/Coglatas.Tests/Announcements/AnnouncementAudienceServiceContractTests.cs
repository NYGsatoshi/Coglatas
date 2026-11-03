using Coglatas.Application.Announcements;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Announcements;

public sealed class AnnouncementAudienceServiceContractTests
{
    [Theory]
    [InlineData("anonymous", "Authentication is required.")]
    [InlineData("missing-user", "Authentication is required.")]
    [InlineData("missing-tenant", "A tenant context is required to resolve announcement audiences.")]
    [InlineData("platform", "A tenant context is required to resolve announcement audiences.")]
    public async Task CurrentActorOperationsRejectInvalidContextWithoutRepositoryAccess(string scenario, string error)
    {
        using var fixture = new Fixture();
        if (scenario == "anonymous") fixture.Actor.IsAuthenticated = false;
        if (scenario == "missing-user") fixture.Actor.UserId = null;
        if (scenario == "missing-tenant") fixture.Tenant.Clear();
        if (scenario == "platform") fixture.Tenant.SetPlatformScope();

        var list = await fixture.Service.ListAsync(fixture.Token);
        var authorize = await fixture.Service.IsAuthorizedAsync(fixture.Workspace.Id, null, null, fixture.Token);

        Assert.False(list.IsSuccess);
        Assert.Null(list.Value);
        Assert.Equal(error, list.Error);
        Assert.False(authorize.IsSuccess);
        Assert.Equal(error, authorize.Error);
        Assert.Empty(fixture.Dependencies.Calls);
    }

    [Fact]
    public async Task EmptyServerActorIsDeniedWithoutLookingUpResources()
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.IsAuthorizedForActorAsync(
            Guid.Empty, fixture.Workspace.Id, fixture.Group.Id, fixture.Channel.Id, fixture.Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Empty(fixture.Dependencies.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerActorRequiresTenantContextEvenWithoutBrowserAuthentication(bool platformScope)
    {
        using var fixture = new Fixture();
        fixture.Actor.IsAuthenticated = false;
        if (platformScope) fixture.Tenant.SetPlatformScope();
        else fixture.Tenant.Clear();

        var result = await fixture.Service.IsAuthorizedForActorAsync(
            Guid.NewGuid(), fixture.Workspace.Id, null, null, fixture.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("A tenant context is required to resolve announcement audiences.", result.Error);
        Assert.Empty(fixture.Dependencies.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TenantUserStatus.Invited)]
    [InlineData(TenantUserStatus.Suspended)]
    [InlineData(TenantUserStatus.Left)]
    [InlineData(TenantUserStatus.Archived)]
    public async Task MissingOrInactiveTenantMembershipDeniesAllScopesAndDoesNotEnumerateAudiences(TenantUserStatus? status)
    {
        using var fixture = new Fixture();
        fixture.ExpectActor(status: status);
        var list = await fixture.Service.ListAsync(fixture.Token);
        Assert.True(list.IsSuccess, list.Error);
        Assert.Empty(list.Value!);
        foreach (var scope in new[] { "global", "workspace", "group", "channel" })
        {
            fixture.ExpectActor(status: status);
            var result = await fixture.AuthorizeAsync(scope);
            Assert.True(result.IsSuccess, result.Error);
            Assert.False(result.Value);
        }

        fixture.Dependencies.VerifyComplete();
        Assert.Equal(10, fixture.Dependencies.Calls.Count);
    }

    [Fact]
    public async Task TenantMembershipIsResolvedAgainAfterPreviouslyAuthorizedCall()
    {
        using var fixture = new Fixture();
        fixture.ExpectActor();
        fixture.ExpectWorkspace();
        fixture.Expect("workspaceAuthorization.CanManageWorkspace", Task.FromResult(true), fixture.ActorId, fixture.Workspace.Id);
        Assert.True((await fixture.AuthorizeAsync("workspace")).Value);

        fixture.ExpectActor(status: TenantUserStatus.Suspended);
        var revoked = await fixture.AuthorizeAsync("workspace");

        Assert.True(revoked.IsSuccess);
        Assert.False(revoked.Value);
        fixture.Dependencies.VerifyComplete();
        Assert.Equal(2, fixture.Dependencies.Calls.Count(call => call.Method == "tenants.GetTenantUserAsync"));
    }

    [Fact]
    public async Task ActualWorkspaceAuthorizationRechecksActiveMembershipAfterPriorSuccess()
    {
        using var fixture = new Fixture(useActualWorkspaceAuthorization: true);
        fixture.StoredUser.SystemRole = SystemRole.NormalUser;
        foreach (var status in new[] { MembershipStatus.Active, MembershipStatus.Suspended, MembershipStatus.Active })
        {
            fixture.ExpectActor();
            fixture.ExpectWorkspace();
            fixture.Expect("workspaces.GetMemberWithWorkspaceAsync", Task.FromResult<WorkspaceMember?>(new WorkspaceMember
            {
                TenantId = fixture.TenantId, UserId = fixture.ActorId, WorkspaceId = fixture.Workspace.Id,
                Workspace = fixture.Workspace, Role = WorkspaceRole.Owner, Status = status
            }), fixture.Workspace.Id, fixture.ActorId);
            fixture.ExpectUser();
            if (status != MembershipStatus.Active) fixture.ExpectUser();

            var result = await fixture.AuthorizeAsync("workspace");

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(status == MembershipStatus.Active, result.Value);
        }

        fixture.Dependencies.VerifyComplete();
        Assert.Equal(3, fixture.Dependencies.Calls.Count(call => call.Method == "workspaces.GetMemberWithWorkspaceAsync"));
    }

    [Theory]
    [InlineData(SystemRole.NormalUser, false, false)]
    [InlineData(SystemRole.Teacher, false, false)]
    [InlineData(SystemRole.PlatformAdmin, false, true)]
    [InlineData(SystemRole.PlatformAdmin, true, false)]
    public async Task GlobalScopeUsesActiveStoredPlatformRoleInsteadOfCurrentUserRoleClaim(
        SystemRole storedRole, bool deleted, bool allowed)
    {
        using var fixture = new Fixture();
        fixture.Actor.SystemRole = SystemRole.PlatformAdmin;
        fixture.StoredUser.SystemRole = storedRole;
        if (deleted) fixture.StoredUser.MarkDeleted(DateTimeOffset.UtcNow);
        fixture.ExpectActor();

        var result = await fixture.AuthorizeAsync("global");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(allowed, result.Value);
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Archived)]
    public async Task InactiveStoredPlatformUserCannotAuthorizeGlobalAudience(UserStatus status)
    {
        using var fixture = new Fixture();
        fixture.StoredUser.SystemRole = SystemRole.PlatformAdmin;
        fixture.StoredUser.Status = status;
        fixture.ExpectActor();

        var result = await fixture.AuthorizeAsync("global");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value);
        fixture.Dependencies.VerifyComplete();
    }

    [Fact]
    public async Task MissingStoredUserCannotAcquireGlobalAuthorityFromClaim()
    {
        using var fixture = new Fixture();
        fixture.Actor.SystemRole = SystemRole.PlatformAdmin;
        fixture.Expect("users.GetByIdAsync", Task.FromResult<User?>(null), fixture.ActorId);
        fixture.Expect("tenants.GetTenantUserAsync", Task.FromResult<TenantUser?>(new TenantUser
        {
            TenantId = fixture.TenantId, UserId = fixture.ActorId, Status = TenantUserStatus.Active
        }), fixture.TenantId, fixture.ActorId);

        var result = await fixture.AuthorizeAsync("global");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value);
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData("workspace", SystemRole.NormalUser, true, false, true)]
    [InlineData("workspace", SystemRole.NormalUser, false, true, false)]
    [InlineData("workspace", SystemRole.Teacher, false, true, true)]
    [InlineData("workspace", SystemRole.Admin, false, false, false)]
    [InlineData("group", SystemRole.NormalUser, true, false, true)]
    [InlineData("group", SystemRole.NormalUser, false, true, false)]
    [InlineData("group", SystemRole.Teacher, false, true, true)]
    [InlineData("group", SystemRole.Admin, false, false, false)]
    public async Task WorkspaceAndGroupCreationRequireManagementOrElevatedCurrentVisibility(
        string scope, SystemRole role, bool canManage, bool canView, bool allowed)
    {
        using var fixture = new Fixture();
        fixture.StoredUser.SystemRole = role;
        fixture.ExpectActor();
        if (scope == "group") fixture.ExpectGroup();
        fixture.ExpectWorkspace();
        var resourceId = scope == "group" ? fixture.Group.Id : fixture.Workspace.Id;
        var dependency = scope == "group" ? "groupAuthorization" : "workspaceAuthorization";
        var suffix = scope == "group" ? "Group" : "Workspace";
        fixture.Expect($"{dependency}.CanManage{suffix}", Task.FromResult(canManage), fixture.ActorId, resourceId);
        if (!canManage)
        {
            fixture.ExpectUser();
            if (role is SystemRole.Teacher or SystemRole.Admin)
                fixture.Expect($"{dependency}.CanView{suffix}", Task.FromResult(canView), fixture.ActorId, resourceId);
        }

        var result = await fixture.AuthorizeAsync(scope);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(allowed, result.Value);
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData("missing-workspace")]
    [InlineData("workspace-archived")]
    [InlineData("workspace-deleted")]
    [InlineData("missing-group")]
    [InlineData("group-archived")]
    [InlineData("group-deleted")]
    [InlineData("group-parent")]
    [InlineData("missing-channel")]
    [InlineData("channel-archived")]
    [InlineData("channel-deleted")]
    [InlineData("channel-workspace")]
    [InlineData("channel-group")]
    public async Task ChannelAuthorizationRejectsStoredContainmentAndParentLifecycleBeforeGuard(string scenario)
    {
        using var fixture = new Fixture();
        switch (scenario)
        {
            case "workspace-archived": fixture.Workspace.Status = WorkspaceStatus.Archived; break;
            case "workspace-deleted": fixture.Workspace.MarkDeleted(DateTimeOffset.UtcNow); break;
            case "group-archived": fixture.Group.Status = GroupStatus.Archived; break;
            case "group-deleted": fixture.Group.MarkDeleted(DateTimeOffset.UtcNow); break;
            case "group-parent": fixture.Group.WorkspaceId = Guid.NewGuid(); break;
            case "channel-archived": fixture.Channel.Status = ChannelStatus.Archived; break;
            case "channel-deleted": fixture.Channel.MarkDeleted(DateTimeOffset.UtcNow); break;
            case "channel-workspace": fixture.Channel.WorkspaceId = Guid.NewGuid(); break;
            case "channel-group": fixture.Channel.GroupId = Guid.NewGuid(); break;
        }
        fixture.ExpectActor();
        fixture.ExpectChannel(scenario == "missing-channel");
        if (!scenario.StartsWith("channel-", StringComparison.Ordinal) && scenario != "missing-channel")
        {
            fixture.ExpectWorkspace(scenario == "missing-workspace");
            if (!scenario.StartsWith("workspace-", StringComparison.Ordinal) && scenario != "missing-workspace")
                fixture.ExpectGroup(scenario == "missing-group");
        }

        var result = await fixture.AuthorizeAsync("channel");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value);
        fixture.Dependencies.VerifyComplete();
        Assert.DoesNotContain(fixture.Dependencies.Calls, call => call.Method == "channelAuthorization.CanManageChannel");
    }

    [Theory]
    [InlineData("workspace", "missing")]
    [InlineData("workspace", "archived")]
    [InlineData("workspace", "deleted")]
    [InlineData("group", "missing")]
    [InlineData("group", "archived")]
    [InlineData("group", "deleted")]
    [InlineData("group", "parent")]
    [InlineData("group", "workspace-missing")]
    [InlineData("group", "workspace-archived")]
    [InlineData("group", "workspace-deleted")]
    public async Task WorkspaceAndGroupTargetsDenyInvalidStoredParentsBeforeScopeAuthorization(string scope, string scenario)
    {
        using var fixture = new Fixture();
        fixture.ExpectActor();
        if (scope == "workspace" || scenario.StartsWith("workspace-", StringComparison.Ordinal))
        {
            if (scope == "group") fixture.ExpectGroup();
            if (scenario.EndsWith("archived", StringComparison.Ordinal)) fixture.Workspace.Status = WorkspaceStatus.Archived;
            if (scenario.EndsWith("deleted", StringComparison.Ordinal)) fixture.Workspace.MarkDeleted(DateTimeOffset.UtcNow);
            fixture.ExpectWorkspace(scenario.EndsWith("missing", StringComparison.Ordinal));
        }
        else
        {
            if (scenario == "archived") fixture.Group.Status = GroupStatus.Archived;
            if (scenario == "deleted") fixture.Group.MarkDeleted(DateTimeOffset.UtcNow);
            if (scenario == "parent") fixture.Group.WorkspaceId = Guid.NewGuid();
            fixture.ExpectGroup(scenario == "missing");
        }

        var result = await fixture.AuthorizeAsync(scope);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value);
        fixture.Dependencies.VerifyComplete();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IncompleteChildTargetCannotFallBackToGlobalOrParentAuthority(bool workspacePresent, bool groupPresent)
    {
        using var fixture = new Fixture();
        fixture.ExpectActor();

        var result = await fixture.Service.IsAuthorizedAsync(
            workspacePresent ? fixture.Workspace.Id : null,
            groupPresent ? fixture.Group.Id : null,
            fixture.Channel.Id, fixture.Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        fixture.Dependencies.VerifyComplete();
        Assert.Equal(2, fixture.Dependencies.Calls.Count);
    }

    [Fact]
    public async Task GroupWithoutWorkspaceCannotFallBackToGlobalAuthority()
    {
        using var fixture = new Fixture();
        fixture.ExpectActor();

        var result = await fixture.Service.IsAuthorizedAsync(null, fixture.Group.Id, null, fixture.Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        fixture.Dependencies.VerifyComplete();
        Assert.Equal(2, fixture.Dependencies.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerOwnedActorUsesExactPersistedActorAndCurrentChannelManagement(bool canManage)
    {
        using var fixture = new Fixture();
        fixture.Actor.IsAuthenticated = false;
        fixture.Actor.UserId = null;
        var authorId = Guid.NewGuid();
        fixture.StoredUser.Id = authorId;
        fixture.ExpectActor(actorId: authorId);
        fixture.ExpectChannel();
        fixture.ExpectWorkspace();
        fixture.ExpectGroup();
        fixture.Expect("channelAuthorization.CanManageChannel", Task.FromResult(canManage), authorId, fixture.Channel.Id);

        var result = await fixture.Service.IsAuthorizedForActorAsync(
            authorId, fixture.Workspace.Id, fixture.Group.Id, fixture.Channel.Id, fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(canManage, result.Value);
        fixture.Dependencies.VerifyComplete();
    }

    [Fact]
    public async Task AudienceListBuildsOnlyAuthorizedActiveContainedOptionsAndRedactsRecipientIdentities()
    {
        using var fixture = new Fixture();
        var deletedWorkspace = new Workspace();
        deletedWorkspace.MarkDeleted(DateTimeOffset.UtcNow);
        var deletedGroup = new Group { WorkspaceId = fixture.Workspace.Id };
        deletedGroup.MarkDeleted(DateTimeOffset.UtcNow);
        var deletedChannel = new Channel { WorkspaceId = fixture.Workspace.Id, GroupId = fixture.Group.Id };
        deletedChannel.MarkDeleted(DateTimeOffset.UtcNow);
        fixture.ExpectActor();
        fixture.Expect("workspaces.ListForUserAsync", Task.FromResult<IReadOnlyList<Workspace>>(
            [new Workspace { Status = WorkspaceStatus.Archived }, deletedWorkspace, fixture.Workspace]),
            fixture.ActorId, false);
        fixture.Expect("workspaceAuthorization.CanManageWorkspace", Task.FromResult(true), fixture.ActorId, fixture.Workspace.Id);
        fixture.ExpectOption(2);
        fixture.Expect("groups.ListByWorkspaceAsync", Task.FromResult<IReadOnlyList<Group>>(
            [new Group { WorkspaceId = fixture.Workspace.Id, Status = GroupStatus.Archived },
             deletedGroup,
             new Group { WorkspaceId = Guid.NewGuid() }, fixture.Group]), fixture.Workspace.Id);
        fixture.Expect("groupAuthorization.CanManageGroup", Task.FromResult(false), fixture.ActorId, fixture.Group.Id);
        fixture.ExpectUser();
        fixture.Expect("groupAuthorization.CanViewGroup", Task.FromResult(true), fixture.ActorId, fixture.Group.Id);
        fixture.ExpectOption(1);
        var deniedChannel = new Channel { Id = Guid.NewGuid(), WorkspaceId = fixture.Workspace.Id, GroupId = fixture.Group.Id };
        fixture.Expect("channels.ListByGroupAsync", Task.FromResult<IReadOnlyList<Channel>>(
            [new Channel { WorkspaceId = fixture.Workspace.Id, GroupId = fixture.Group.Id, Status = ChannelStatus.Archived },
             deletedChannel,
             new Channel { WorkspaceId = Guid.NewGuid(), GroupId = fixture.Group.Id },
             new Channel { WorkspaceId = fixture.Workspace.Id, GroupId = Guid.NewGuid() }, deniedChannel, fixture.Channel]), fixture.Group.Id);
        fixture.Expect("channelAuthorization.CanManageChannel", Task.FromResult(false), fixture.ActorId, deniedChannel.Id);
        fixture.Expect("channelAuthorization.CanManageChannel", Task.FromResult(true), fixture.ActorId, fixture.Channel.Id);
        fixture.ExpectOption(3);

        var result = await fixture.Service.ListAsync(fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[]
        {
            new AnnouncementAudienceOptionResponse($"workspace:{fixture.Workspace.Id:D}", "workspace", fixture.Workspace.Id, null, null, "Workspace", 2, "UTC"),
            new AnnouncementAudienceOptionResponse($"group:{fixture.Group.Id:D}", "group", fixture.Workspace.Id, fixture.Group.Id, null, "Workspace / Group", 1, "UTC"),
            new AnnouncementAudienceOptionResponse($"channel:{fixture.Channel.Id:D}", "channel", fixture.Workspace.Id, fixture.Group.Id, fixture.Channel.Id, "Workspace / Group / #news", 3, "UTC")
        }, result.Value);
        var targets = fixture.Dependencies.Calls.Where(call => call.Method == "announcements.ListTargetUsersAsync")
            .Select(call => Assert.IsType<Announcement>(call.Arguments[0])).ToArray();
        Assert.Equal(3, targets.Length);
        Assert.All(targets, target => Assert.Equal(fixture.TenantId, target.TenantId));
        Assert.Equal(new (Guid, Guid?, Guid?)[]
            {
                (fixture.Workspace.Id, null, null), (fixture.Workspace.Id, fixture.Group.Id, null),
                (fixture.Workspace.Id, fixture.Group.Id, fixture.Channel.Id)
            },
            targets.Select(target => (target.WorkspaceId!.Value, target.GroupId, target.ChannelId)).ToArray());
        Assert.All(fixture.Dependencies.Calls.Where(call => call.Method == "announcements.ListTargetUsersAsync"),
            call => Assert.Equal(fixture.Token, call.Arguments[1]));
        fixture.Dependencies.VerifyComplete();
    }

    [Fact]
    public async Task UnauthorizedWorkspaceAndGroupOptionsDoNotHideSeparatelyAuthorizedChannel()
    {
        using var fixture = new Fixture();
        fixture.StoredUser.SystemRole = SystemRole.NormalUser;
        fixture.ExpectActor();
        fixture.Expect("workspaces.ListForUserAsync", Task.FromResult<IReadOnlyList<Workspace>>([fixture.Workspace]), fixture.ActorId, false);
        fixture.Expect("workspaceAuthorization.CanManageWorkspace", Task.FromResult(false), fixture.ActorId, fixture.Workspace.Id);
        fixture.ExpectUser();
        fixture.Expect("groups.ListByWorkspaceAsync", Task.FromResult<IReadOnlyList<Group>>([fixture.Group]), fixture.Workspace.Id);
        fixture.Expect("groupAuthorization.CanManageGroup", Task.FromResult(false), fixture.ActorId, fixture.Group.Id);
        fixture.ExpectUser();
        fixture.Expect("channels.ListByGroupAsync", Task.FromResult<IReadOnlyList<Channel>>([fixture.Channel]), fixture.Group.Id);
        fixture.Expect("channelAuthorization.CanManageChannel", Task.FromResult(true), fixture.ActorId, fixture.Channel.Id);
        fixture.ExpectOption(1);

        var result = await fixture.Service.ListAsync(fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(fixture.Channel.Id, Assert.Single(result.Value!).ChannelId);
        fixture.Dependencies.VerifyComplete();
    }

    [Fact]
    public async Task PlatformAdminListIncludesTenantGlobalOptionAndRequestsAllWorkspaces()
    {
        using var fixture = new Fixture();
        fixture.StoredUser.SystemRole = SystemRole.PlatformAdmin;
        fixture.ExpectActor();
        fixture.ExpectOption(2, global: true);
        fixture.Expect("workspaces.ListForUserAsync", Task.FromResult<IReadOnlyList<Workspace>>([]), fixture.ActorId, true);

        var result = await fixture.Service.ListAsync(fixture.Token);

        Assert.True(result.IsSuccess, result.Error);
        var option = Assert.Single(result.Value!);
        Assert.Equal("global", option.Key);
        Assert.Equal("global", option.ScopeType);
        Assert.Null(option.WorkspaceId);
        Assert.Null(option.GroupId);
        Assert.Null(option.ChannelId);
        Assert.Equal(2, option.EstimatedRecipientCount);
        var call = Assert.Single(fixture.Dependencies.Calls, call => call.Method == "announcements.ListTargetUsersAsync");
        var prototype = Assert.IsType<Announcement>(call.Arguments[0]);
        Assert.Equal(fixture.TenantId, prototype.TenantId);
        Assert.Null(prototype.WorkspaceId);
        Assert.Null(prototype.GroupId);
        Assert.Null(prototype.ChannelId);
        Assert.Equal(fixture.Token, call.Arguments[1]);
        fixture.Dependencies.VerifyComplete();
    }

    [Fact]
    public async Task DependencyFailureIsPreservedWithoutEnumeratingProtectedOptions()
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic user lookup failure");
        fixture.Expect("users.GetByIdAsync", Task.FromException<User?>(failure), fixture.ActorId);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ListAsync(fixture.Token)));

        fixture.Dependencies.VerifyComplete();
        Assert.Single(fixture.Dependencies.Calls);
    }

    [Fact]
    public async Task CancelledLookupPreservesTokenAndStopsAudienceEnumeration()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Dependencies.Expect("users.GetByIdAsync", Task.FromCanceled<User?>(cancellation.Token), [fixture.ActorId, cancellation.Token]);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ListAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        fixture.Dependencies.VerifyComplete();
        Assert.Single(fixture.Dependencies.Calls);
    }

    private sealed class Actor(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; set; } = userId;
        public Guid? SessionId => null;
        public string Email => "actor@example.test";
        public SystemRole? SystemRole { get; set; } = Coglatas.Domain.Enums.SystemRole.NormalUser;
        public bool IsAuthenticated { get; set; } = true;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        public CancellationToken Token => _cancellation.Token;
        public AnnouncementContractDependencies Dependencies { get; } = new();
        public CurrentTenantService Tenant { get; } = new();
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid ActorId { get; } = Guid.NewGuid();
        public Actor Actor { get; }
        public User StoredUser { get; }
        public Workspace Workspace { get; }
        public Group Group { get; }
        public Channel Channel { get; }
        public AnnouncementAudienceService Service { get; }

        public Fixture(bool useActualWorkspaceAuthorization = false)
        {
            Tenant.SetTenant(TenantId, "audience-contract");
            Actor = new Actor(ActorId);
            StoredUser = new User { Id = ActorId, Status = UserStatus.Active, SystemRole = SystemRole.Teacher };
            Workspace = new Workspace { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Workspace" };
            Group = new Group { Id = Guid.NewGuid(), TenantId = TenantId, WorkspaceId = Workspace.Id, Name = "Group" };
            Channel = new Channel { Id = Guid.NewGuid(), TenantId = TenantId, WorkspaceId = Workspace.Id, GroupId = Group.Id, Name = "news" };
            var users = Dependencies.Create<IUserRepository>("users");
            var workspaces = Dependencies.Create<IWorkspaceRepository>("workspaces");
            Service = new AnnouncementAudienceService(
                Dependencies.Create<IAnnouncementRepository>("announcements"), workspaces,
                Dependencies.Create<IGroupRepository>("groups"), Dependencies.Create<IChannelRepository>("channels"),
                users, Dependencies.Create<ITenantRepository>("tenants"),
                useActualWorkspaceAuthorization ? new WorkspaceAuthorizationService(users, workspaces)
                    : Dependencies.Create<IWorkspaceAuthorizationService>("workspaceAuthorization"),
                Dependencies.Create<IGroupAuthorizationService>("groupAuthorization"),
                Dependencies.Create<IChannelAuthorizationService>("channelAuthorization"),
                Dependencies.Create<IAnnouncementScheduleTimeZoneResolver>("timeZones"), Actor, Tenant);
        }

        public Task<Result<bool>> AuthorizeAsync(string scope) => Service.IsAuthorizedAsync(
            scope == "global" ? null : Workspace.Id,
            scope is "group" or "channel" ? Group.Id : null,
            scope == "channel" ? Channel.Id : null, Token);

        public void Expect(string method, object result, params object?[] arguments) =>
            Dependencies.Expect(method, result, [.. arguments, Token]);

        public void ExpectUser(Guid? actorId = null) => Expect("users.GetByIdAsync", Task.FromResult<User?>(StoredUser), actorId ?? ActorId);

        public void ExpectActor(TenantUserStatus? status = TenantUserStatus.Active, Guid? actorId = null)
        {
            ExpectUser(actorId);
            if (StoredUser is { SystemRole: SystemRole.PlatformAdmin, Status: UserStatus.Active, DeletedAt: null }) return;
            Expect("tenants.GetTenantUserAsync", Task.FromResult(status.HasValue
                ? new TenantUser { TenantId = TenantId, UserId = actorId ?? ActorId, Status = status.Value } : null),
                TenantId, actorId ?? ActorId);
        }

        public void ExpectWorkspace(bool missing = false) => Expect("workspaces.GetByIdAsync", Task.FromResult(missing ? null : Workspace), Workspace.Id);
        public void ExpectGroup(bool missing = false) => Expect("groups.GetByIdAsync", Task.FromResult(missing ? null : Group), Group.Id);
        public void ExpectChannel(bool missing = false) => Expect("channels.GetByIdAsync", Task.FromResult(missing ? null : Channel), Channel.Id);

        public void ExpectOption(int count, bool global = false)
        {
            Dependencies.Expect("announcements.ListTargetUsersAsync", Task.FromResult<IReadOnlyList<AnnouncementTargetUser>>(
                Enumerable.Range(0, count).Select(index => new AnnouncementTargetUser(Guid.NewGuid(), $"Recipient {index}", $"recipient{index}@example.test", false)).ToArray()));
            Expect("timeZones.ResolveAsync", Task.FromResult(TimeZoneInfo.Utc), TenantId, global ? null : Workspace.Id);
        }

        public void Dispose() => _cancellation.Dispose();
    }
}
