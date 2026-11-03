using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Tenancy;

// These exercise the actual service, EF repositories, audit logger and transactional
// publisher. InMemory persistence is not PostgreSQL, HTTP or RLS qualification.
public sealed class CapabilityGrantServiceTests
{
    [Theory]
    [InlineData(TenantUserRole.Owner)]
    [InlineData(TenantUserRole.Admin)]
    public async Task GrantRevokeAndRegrantPreserveOneScopedSlotAndRequiredEvidence(TenantUserRole role)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        fixture.ActorMembership.Role = role;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await VerifyLifecycleAsync(fixture);
    }

    internal static async Task VerifyLifecycleAsync(CapabilityGrantFixture fixture)
    {
        var service = fixture.CreateService();
        var first = await service.GrantAsync(fixture.Request with { CapabilityKey = " workspace.create " });
        Assert.True(first.IsSuccess, first.Error);
        var created = Assert.IsType<CapabilityGrantResponse>(first.Value);
        Assert.Equal(fixture.Tenant.Id, created.TenantId);
        Assert.Equal(fixture.Subject.Id, created.SubjectUserId);
        Assert.Equal(fixture.Actor.Id, created.GrantedByUserId);
        Assert.Equal(CapabilityKeys.WorkspaceCreate, created.CapabilityKey);
        Assert.Equal(CapabilityScopeType.Tenant, created.ScopeType);
        Assert.Equal(fixture.Tenant.Id, created.ScopeId);
        Assert.Equal(fixture.Clock.UtcNow, created.GrantedAt);
        Assert.Equal(fixture.Request.ExpiresAt, created.ExpiresAt);
        Assert.Null(created.RevokedAt);
        Assert.Equal(1, created.VersionNo);
        Assert.True(await fixture.HasWorkspaceCreateAsync());
        await fixture.AssertEvidenceAsync(created.Id, "CapabilityGranted", "granted", 1);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        var revoked = await service.RevokeAsync(created.Id);
        Assert.True(revoked.IsSuccess, revoked.Error);
        Assert.Equal(2, revoked.Value!.VersionNo);
        Assert.Equal(fixture.Clock.UtcNow, revoked.Value.RevokedAt);
        Assert.False(await fixture.HasWorkspaceCreateAsync());
        await fixture.AssertEvidenceAsync(created.Id, "CapabilityRevoked", "revoked", 2);
        var secondRevoke = await service.RevokeAsync(created.Id);
        Assert.Equal(revoked.Value, secondRevoke.Value);
        Assert.True(secondRevoke.IsSuccess);
        Assert.Equal(2, fixture.Saves.Calls);
        await fixture.AssertEvidenceAsync(created.Id, "CapabilityRevoked", "revoked", 2);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        var renewed = await service.GrantAsync(fixture.Request with { ExpiresAt = null });
        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.Equal(created.Id, renewed.Value!.Id);
        Assert.Equal(3, renewed.Value.VersionNo);
        Assert.Equal(fixture.Clock.UtcNow, renewed.Value.GrantedAt);
        Assert.Null(renewed.Value.RevokedAt);
        Assert.Null(renewed.Value.ExpiresAt);
        Assert.True(await fixture.HasWorkspaceCreateAsync());
        var list = await service.ListAsync();
        Assert.True(list.IsSuccess, list.Error);
        Assert.Equal(renewed.Value, Assert.Single(list.Value!));
        await fixture.AssertEvidenceAsync(created.Id, "CapabilityGranted", "granted", 3);
        await fixture.AssertForeignGrantUnchangedAsync();
    }

    [Fact]
    public async Task WorkspaceGrantRemainsScopedAndExpiryDoesNotGrantFutureAuthority()
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        var service = fixture.CreateService();
        var result = await service.GrantAsync(fixture.Request with
        {
            CapabilityKey = CapabilityKeys.ProjectCreate,
            ScopeType = CapabilityScopeType.Workspace,
            ScopeId = fixture.Workspace.Id
        });
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(fixture.Workspace.Id, result.Value!.ScopeId);
        Assert.Equal(CapabilityScopeType.Workspace, result.Value.ScopeType);
        var evaluator = fixture.CreateEvaluator();
        Assert.True(await evaluator.HasActiveGrantAsync(fixture.Subject.Id, fixture.Tenant.Id,
            CapabilityKeys.ProjectCreate, CapabilityScopeType.Workspace, fixture.Workspace.Id));
        Assert.False(await evaluator.HasActiveGrantAsync(fixture.Subject.Id, fixture.Tenant.Id,
            CapabilityKeys.ProjectCreate, CapabilityScopeType.Workspace, fixture.ForeignWorkspace.Id));
        await fixture.AssertEvidenceAsync(result.Value.Id, "CapabilityGranted", "granted", 1);
        fixture.Clock.UtcNow = result.Value.ExpiresAt!.Value;
        Assert.False(await evaluator.HasActiveGrantAsync(fixture.Subject.Id, fixture.Tenant.Id,
            CapabilityKeys.ProjectCreate, CapabilityScopeType.Workspace, fixture.Workspace.Id));
        var listed = Assert.Single((await service.ListAsync()).Value!);
        Assert.Equal(result.Value, listed);
    }

    [Theory]
    [InlineData("anonymous", "AuthenticationRequired")]
    [InlineData("missing-user", "AuthenticationRequired")]
    [InlineData("no-tenant", "TenantMembershipRequired")]
    [InlineData("platform", "TenantMembershipRequired")]
    [InlineData("member", "CapabilityDenied")]
    [InlineData("guest", "CapabilityDenied")]
    [InlineData("suspended-membership", "CapabilityDenied")]
    [InlineData("inactive-actor", "CapabilityDenied")]
    [InlineData("deleted-actor", "CapabilityDenied")]
    [InlineData("inactive-tenant", "CapabilityDenied")]
    [InlineData("deleted-tenant", "CapabilityDenied")]
    [InlineData("foreign-manager", "CapabilityDenied")]
    [InlineData("system-admin-member", "CapabilityDenied")]
    public async Task EveryOperationRequiresCurrentTenantManagerAuthority(string denied, string code)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        switch (denied)
        {
            case "anonymous": fixture.CurrentUser.IsAuthenticated = false; break;
            case "missing-user": fixture.CurrentUser.UserId = null; break;
            case "member": fixture.ActorMembership.Role = TenantUserRole.Member; break;
            case "guest": fixture.ActorMembership.Role = TenantUserRole.Guest; break;
            case "suspended-membership": fixture.ActorMembership.Status = TenantUserStatus.Suspended; break;
            case "inactive-actor": fixture.Actor.Status = UserStatus.Suspended; break;
            case "deleted-actor": fixture.Actor.MarkDeleted(fixture.Clock.UtcNow); break;
            case "inactive-tenant": fixture.Tenant.Status = TenantStatus.Suspended; break;
            case "deleted-tenant": fixture.Tenant.MarkDeleted(fixture.Clock.UtcNow); break;
            case "system-admin-member":
                fixture.Actor.SystemRole = SystemRole.PlatformAdmin;
                fixture.ActorMembership.Role = TenantUserRole.Member;
                break;
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        switch (denied)
        {
            case "no-tenant": fixture.CurrentTenant.Clear(); break;
            case "platform": fixture.CurrentTenant.SetPlatformScope(); break;
            case "foreign-manager": fixture.CurrentTenant.SetTenant(fixture.ForeignTenant.Id, fixture.ForeignTenant.Slug); break;
        }
        var service = fixture.CreateService();
        AssertDenied(await service.ListAsync(), code);
        AssertDenied(await service.GrantAsync(fixture.Request), code);
        AssertDenied(await service.RevokeAsync(fixture.ForeignGrant.Id), code);
        Assert.Equal(0, fixture.Saves.Calls);
        await fixture.AssertNoMutationsAsync();
    }

    [Theory]
    [InlineData("empty-key", "body.capabilityKey")]
    [InlineData("null-key", "body.capabilityKey")]
    [InlineData("short-key", "body.capabilityKey")]
    [InlineData("uppercase-key", "body.capabilityKey")]
    [InlineData("double-dot-key", "body.capabilityKey")]
    [InlineData("long-key", "body.capabilityKey")]
    [InlineData("missing-subject", "body.subjectUserId")]
    [InlineData("tenant-null", "body.scopeId")]
    [InlineData("tenant-foreign", "body.scopeId")]
    [InlineData("workspace-null", "body.scopeId")]
    [InlineData("workspace-empty", "body.scopeId")]
    [InlineData("unknown-scope", "body.scopeId")]
    [InlineData("workspace-create-workspace", "body.scopeType")]
    [InlineData("expiry-now", "body.expiresAt")]
    [InlineData("expiry-past", "body.expiresAt")]
    public async Task MalformedGrantCannotStageAuthorityOrMutationEvidence(string invalid, string target)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        var request = fixture.Request;
        request = invalid switch
        {
            "empty-key" => request with { CapabilityKey = " " },
            "null-key" => request with { CapabilityKey = null! },
            "short-key" => request with { CapabilityKey = "a" },
            "uppercase-key" => request with { CapabilityKey = "Workspace.create" },
            "double-dot-key" => request with { CapabilityKey = "workspace..create" },
            "long-key" => request with { CapabilityKey = "a." + new string('b', 119) },
            "missing-subject" => request with { SubjectUserId = Guid.Empty },
            "tenant-null" => request with { ScopeId = null },
            "tenant-foreign" => request with { ScopeId = fixture.ForeignTenant.Id },
            "workspace-null" => request with { ScopeType = CapabilityScopeType.Workspace, ScopeId = null },
            "workspace-empty" => request with { ScopeType = CapabilityScopeType.Workspace, ScopeId = Guid.Empty },
            "unknown-scope" => request with { ScopeType = (CapabilityScopeType)999 },
            "workspace-create-workspace" => request with { ScopeType = CapabilityScopeType.Workspace, ScopeId = fixture.Workspace.Id },
            "expiry-now" => request with { ExpiresAt = fixture.Clock.UtcNow },
            "expiry-past" => request with { ExpiresAt = fixture.Clock.UtcNow.AddTicks(-1) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var result = await fixture.CreateService().GrantAsync(request);
        AssertDenied(result, "ValidationFailed");
        Assert.Equal(target, result.ErrorDetail!.Target);
        Assert.Equal(0, fixture.Saves.Calls);
        await fixture.AssertNoMutationsAsync();
    }

    [Theory]
    [InlineData("missing-membership")]
    [InlineData("suspended-membership")]
    [InlineData("inactive-user")]
    [InlineData("deleted-user")]
    [InlineData("missing-user")]
    [InlineData("foreign-user")]
    public async Task SubjectMustBeAnExistingActiveUserAndMemberOfTheCurrentTenant(string invalid)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        switch (invalid)
        {
            case "missing-membership": fixture.Db.TenantUsers.Remove(fixture.SubjectMembership); break;
            case "missing-user":
                fixture.Db.TenantUsers.Remove(fixture.SubjectMembership);
                fixture.Db.Users.Remove(fixture.Subject);
                break;
            case "suspended-membership": fixture.SubjectMembership.Status = TenantUserStatus.Suspended; break;
            case "inactive-user": fixture.Subject.Status = UserStatus.Suspended; break;
            case "deleted-user": fixture.Subject.MarkDeleted(fixture.Clock.UtcNow); break;
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var result = await fixture.CreateService().GrantAsync(fixture.Request with
        {
            SubjectUserId = invalid == "foreign-user" ? fixture.ForeignSubject.Id : fixture.Subject.Id
        });
        AssertDenied(result, "InvalidSubject");
        Assert.Equal("body.subjectUserId", result.ErrorDetail!.Target);
        await fixture.AssertNoMutationsAsync();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("deleted-at")]
    [InlineData("deleted-status")]
    public async Task WorkspaceScopeCannotReferToUnavailableOrForeignWorkspace(string invalid)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        switch (invalid)
        {
            case "deleted-at": fixture.Workspace.MarkDeleted(fixture.Clock.UtcNow); break;
            case "deleted-status": fixture.Workspace.Status = WorkspaceStatus.Deleted; break;
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var result = await fixture.CreateService().GrantAsync(fixture.Request with
        {
            CapabilityKey = CapabilityKeys.ProjectCreate,
            ScopeType = CapabilityScopeType.Workspace,
            ScopeId = invalid switch
            {
                "missing" => Guid.NewGuid(),
                "foreign" => fixture.ForeignWorkspace.Id,
                _ => fixture.Workspace.Id
            }
        });
        AssertDenied(result, "ValidationFailed");
        Assert.Equal("body.scopeId", result.ErrorDetail!.Target);
        await fixture.AssertNoMutationsAsync();
    }

    [Fact]
    public async Task ForeignAbsentAndEmptyRevokeIdentitiesReturnTheSameSafeError()
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        var service = fixture.CreateService();
        var absent = await service.RevokeAsync(Guid.NewGuid());
        var foreign = await service.RevokeAsync(fixture.ForeignGrant.Id);
        var empty = await service.RevokeAsync(Guid.Empty);
        AssertDenied(absent, "NotFound");
        Assert.Equal(absent.ErrorDetail, foreign.ErrorDetail);
        Assert.Equal(absent.ErrorDetail, empty.ErrorDetail);
        Assert.DoesNotContain(fixture.ForeignGrant.Id.ToString(), foreign.Error!);
        Assert.DoesNotContain(fixture.ForeignTenant.Id.ToString(), foreign.Error!);
        await fixture.AssertNoMutationsAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredInvalidationFailureCannotPublishSuccessOrCommitStagedGrantAudit(bool revoke)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        var grantId = await fixture.SeedOwnGrantAsync(revoke);
        var service = fixture.CreateService(outbox: new RejectingOutbox());
        var result = revoke ? await service.RevokeAsync(grantId) : await service.GrantAsync(fixture.Request);
        AssertDenied(result, "DependencyUnavailable");
        Assert.Equal("Capability update could not be committed.", result.ErrorDetail!.Message);
        Assert.Equal(0, fixture.Saves.Calls);
        await fixture.AssertMutationCountsAsync(revoke ? 1 : 0, 0);
        if (revoke)
        {
            await using var read = fixture.OpenReadContext();
            Assert.Null((await read.Set<CapabilityGrant>().SingleAsync(item => item.Id == grantId)).RevokedAt);
        }
    }

    [Theory]
    [InlineData("audit", false)]
    [InlineData("audit", true)]
    [InlineData("save", false)]
    [InlineData("save", true)]
    public async Task OriginalAuditOrSaveFailureEscapesWithoutCommittingPartialAuthority(string failedStage, bool revoke)
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        var grantId = await fixture.SeedOwnGrantAsync(revoke);
        var expected = new InvalidOperationException("Synthetic capability dependency failure.");
        if (failedStage == "save") fixture.Saves.Failure = expected;
        var service = fixture.CreateService(audit: failedStage == "audit" ? new FailingAudit(expected) : null);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => revoke
            ? service.RevokeAsync(grantId) : service.GrantAsync(fixture.Request));
        Assert.Same(expected, actual);
        Assert.Equal(failedStage == "save" ? 1 : 0, fixture.Saves.Calls);
        await fixture.AssertMutationCountsAsync(revoke ? 1 : 0, 0);
        await fixture.AssertForeignGrantUnchangedAsync();
    }

    [Fact]
    public async Task CancellationBeforeRepositoryReadsCannotCreateGrantAuditOrInvalidation()
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CreateService().GrantAsync(fixture.Request, cancellation.Token));
        Assert.Equal(0, fixture.Saves.Calls);
        await fixture.AssertNoMutationsAsync();
    }

    [Fact]
    public async Task CurrentManagerSnapshotPreservesTrackedMembershipEditsForTheExistingWriter()
    {
        await using var fixture = await CapabilityGrantFixture.CreateAsync();
        var repository = new TenantRepository(fixture.Db);
        var tracked = await repository.GetTenantUserAsync(fixture.Tenant.Id, fixture.Actor.Id);
        Assert.NotNull(tracked);
        tracked.Role = TenantUserRole.Admin;
        Assert.True(await new TenantAuthorizationService(repository).CanManageTenantAsync(fixture.Actor.Id, fixture.Tenant.Id));
        Assert.Same(tracked, Assert.Single(fixture.Db.ChangeTracker.Entries<TenantUser>(),
            entry => entry.Entity.UserId == fixture.Actor.Id).Entity);
        Assert.Equal(TenantUserRole.Admin, tracked.Role);
        await fixture.Db.SaveChangesAsync();
        await using var read = fixture.OpenReadContext();
        Assert.Equal(TenantUserRole.Admin, (await read.TenantUsers.SingleAsync(item =>
            item.TenantId == fixture.Tenant.Id && item.UserId == fixture.Actor.Id)).Role);
    }

    private static void AssertDenied<T>(Result<T> result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(code, result.ErrorDetail!.Code);
    }

    private sealed class RejectingOutbox : ITransactionalOutbox
    {
        public Task<Result<Guid>> EnqueueAsync(DurableEventEnvelope envelope,
            IReadOnlyCollection<RealtimeRoutingTarget> routingTargets, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Guid>.Failure("Synthetic rejected transactional invalidation."));
    }

    private sealed class FailingAudit(Exception failure) : IAuditLogger
    {
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.FromException(failure);
    }
}

internal sealed class CapabilityGrantFixture : IAsyncDisposable
{
    private readonly DbContextOptions<AppDbContext> _options;
    public AppDbContext Db { get; }
    public CurrentTenantService CurrentTenant { get; }
    public CapabilityTestUser CurrentUser { get; }
    public CapabilityTestClock Clock { get; } = new();
    public Tenant Tenant { get; }
    public Tenant ForeignTenant { get; }
    public User Actor { get; }
    public User Subject { get; }
    public User ForeignSubject { get; }
    public TenantUser ActorMembership { get; }
    public TenantUser SubjectMembership { get; }
    public Workspace Workspace { get; }
    public Workspace ForeignWorkspace { get; }
    public CapabilityGrant ForeignGrant { get; }
    public CapabilitySave Saves { get; }
    public GrantCapabilityRequest Request => new(Subject.Id, CapabilityKeys.WorkspaceCreate, CapabilityScopeType.Tenant, Tenant.Id, Clock.UtcNow.AddHours(1));

    private CapabilityGrantFixture(DbContextOptions<AppDbContext> options)
    {
        _options = options;
        CurrentTenant = new CurrentTenantService();
        CurrentTenant.SetPlatformScope();
        Db = new AppDbContext(options, CurrentTenant);
        Tenant = new Tenant { Name = "Synthetic grant tenant", DisplayName = "Synthetic grant tenant", Slug = "grant-" + Guid.NewGuid().ToString("N") };
        ForeignTenant = new Tenant { Name = "Foreign grant tenant", DisplayName = "Foreign grant tenant", Slug = "foreign-" + Guid.NewGuid().ToString("N") };
        Actor = NewUser("manager");
        Subject = NewUser("subject");
        ForeignSubject = NewUser("foreign");
        CurrentUser = new CapabilityTestUser(Actor.Id);
        ActorMembership = Membership(Tenant, Actor, TenantUserRole.Owner);
        SubjectMembership = Membership(Tenant, Subject, TenantUserRole.Member);
        Workspace = new Workspace { TenantId = Tenant.Id, Name = "Synthetic scope", Slug = "scope", CreatedByUserId = Actor.Id };
        ForeignWorkspace = new Workspace { TenantId = ForeignTenant.Id, Name = "Foreign scope", Slug = "foreign-scope", CreatedByUserId = ForeignSubject.Id };
        ForeignGrant = new CapabilityGrant
        {
            TenantId = ForeignTenant.Id, SubjectUserId = ForeignSubject.Id, GrantedByUserId = ForeignSubject.Id,
            CapabilityKey = CapabilityKeys.WorkspaceCreate, ScopeType = CapabilityScopeType.Tenant, ScopeId = ForeignTenant.Id,
            GrantedAt = Clock.UtcNow, VersionNo = 1
        };
        Saves = new CapabilitySave(new EfUnitOfWork(Db));
    }

    public static async Task<CapabilityGrantFixture> CreateAsync(DbContextOptions<AppDbContext>? options = null)
    {
        var fixture = new CapabilityGrantFixture(options ?? new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("capability-grant-" + Guid.NewGuid().ToString("N")).Options);
        fixture.Db.AddRange(fixture.Tenant, fixture.ForeignTenant, fixture.Actor, fixture.Subject, fixture.ForeignSubject,
            fixture.ActorMembership, fixture.SubjectMembership,
            Membership(fixture.ForeignTenant, fixture.ForeignSubject, TenantUserRole.Owner),
            fixture.Workspace, fixture.ForeignWorkspace, fixture.ForeignGrant);
        await fixture.Db.SaveChangesAsync();
        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        return fixture;
    }

    public CapabilityGrantService CreateService(ITransactionalOutbox? outbox = null, IAuditLogger? audit = null) => new(
        new CapabilityGrantRepository(Db), new TenantRepository(Db), new WorkspaceRepository(Db),
        new TenantAuthorizationService(new TenantRepository(Db)), CurrentTenant, CurrentUser, Clock,
        audit ?? new DbAuditLogger(Db, Clock, CurrentUser, CurrentTenant),
        new AuthorizationStateChangePublisher(outbox ?? new TransactionalOutbox(new OutboxEventRepository(Db), CurrentTenant, Clock), CurrentTenant, Clock),
        Saves);

    public CapabilityGrantEvaluator CreateEvaluator() => new(new CapabilityGrantRepository(Db), new TenantRepository(Db),
        new WorkspaceRepository(Db), CurrentTenant, Clock);

    public Task<bool> HasWorkspaceCreateAsync() => CreateEvaluator().HasActiveGrantAsync(Subject.Id, Tenant.Id,
        CapabilityKeys.WorkspaceCreate, CapabilityScopeType.Tenant, Tenant.Id);

    public AppDbContext OpenReadContext()
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        return new AppDbContext(_options, tenant);
    }

    public async Task<Guid> SeedOwnGrantAsync(bool required)
    {
        if (!required) return Guid.Empty;
        var grant = new CapabilityGrant
        {
            TenantId = Tenant.Id, SubjectUserId = Subject.Id, GrantedByUserId = Actor.Id,
            CapabilityKey = CapabilityKeys.WorkspaceCreate, ScopeType = CapabilityScopeType.Tenant, ScopeId = Tenant.Id,
            GrantedAt = Clock.UtcNow, VersionNo = 1
        };
        Db.Set<CapabilityGrant>().Add(grant);
        await Db.SaveChangesAsync();
        return grant.Id;
    }

    public async Task WarmAuthenticatedRequestAsync()
    {
        var session = new Session
        {
            UserId = Actor.Id, SessionKeyHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = Clock.UtcNow.AddHours(1), LastSeenAt = Clock.UtcNow.AddHours(-1)
        };
        Db.Sessions.Add(session);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var sessions = new UserSessionService(new SessionRepository(Db), new TenantRepository(Db),
            new DbAuditLogger(Db, Clock, CurrentUser, CurrentTenant), Clock, new EfUnitOfWork(Db));
        var validated = await sessions.ValidateSessionAsync(Actor.Id, session.Id, Tenant.Id, true);
        Assert.True(validated.IsValid);
        await using var read = OpenReadContext();
        Assert.Equal(Clock.UtcNow, (await read.Sessions.SingleAsync(item => item.Id == session.Id)).LastSeenAt);
    }

    public async Task AssertEvidenceAsync(Guid grantId, string action, string change, int count, int? version = null)
    {
        await using var read = OpenReadContext();
        var grant = Assert.Single(await read.Set<CapabilityGrant>().Where(item => item.TenantId == Tenant.Id).ToListAsync());
        Assert.Equal(grantId, grant.Id);
        Assert.Equal(version ?? count, grant.VersionNo);
        var logs = await read.AuditLogs.Where(item => item.TenantId == Tenant.Id).ToListAsync();
        Assert.Equal(count, logs.Count);
        var log = Assert.Single(logs, item => item.Action == action && item.CreatedAt == Clock.UtcNow);
        Assert.Equal(Actor.Id, log.ActorUserId);
        Assert.Equal(grantId, log.EntityId);
        Assert.Equal("CapabilityGrant", log.EntityType);
        using var metadata = JsonDocument.Parse(log.MetadataJson!);
        Assert.Equal(Subject.Id, metadata.RootElement.GetProperty("subjectUserId").GetGuid());
        Assert.Equal(grant.CapabilityKey, metadata.RootElement.GetProperty("capabilityKey").GetString());
        Assert.Equal(grant.ScopeType.ToString(), metadata.RootElement.GetProperty("scopeType").GetString());
        Assert.Equal(grant.ScopeId, metadata.RootElement.GetProperty("scopeId").GetGuid());
        var events = await read.OutboxEvents.Where(item => item.TenantId == Tenant.Id).ToListAsync();
        Assert.Equal(count, events.Count);
        var notification = Assert.Single(events, item => item.OccurredAt == Clock.UtcNow);
        Assert.Equal("Security.AuthorizationStateChanged.v1", notification.EventType);
        Assert.Equal(Subject.Id, notification.AggregateId);
        using var envelope = JsonDocument.Parse(notification.PayloadJson);
        var payload = envelope.RootElement.GetProperty("payload");
        Assert.Equal(5, payload.EnumerateObject().Count());
        Assert.Equal(Subject.Id, payload.GetProperty("affectedUserId").GetGuid());
        Assert.Equal("capability", payload.GetProperty("scopeType").GetString());
        Assert.Equal(grantId, payload.GetProperty("scopeId").GetGuid());
        Assert.Equal(change, payload.GetProperty("change").GetString());
        Assert.Equal(Clock.UtcNow.UtcTicks, payload.GetProperty("authorizationVersion").GetInt64());
        using var routing = JsonDocument.Parse(notification.RoutingJson);
        var route = Assert.Single(routing.RootElement.EnumerateArray());
        Assert.Equal((int)RealtimeSubscriptionType.User, route.GetProperty("subscriptionType").GetInt32());
        Assert.Equal(Subject.Id, route.GetProperty("resourceId").GetGuid());
    }

    public async Task AssertMutationCountsAsync(int grants, int evidence)
    {
        await using var read = OpenReadContext();
        Assert.Equal(grants, await read.Set<CapabilityGrant>().CountAsync(item => item.TenantId == Tenant.Id));
        Assert.Equal(evidence, await read.AuditLogs.CountAsync(item => item.TenantId == Tenant.Id));
        Assert.Equal(evidence, await read.OutboxEvents.CountAsync(item => item.TenantId == Tenant.Id));
    }

    public async Task AssertForeignGrantUnchangedAsync()
    {
        await using var read = OpenReadContext();
        var grant = await read.Set<CapabilityGrant>().SingleAsync(item => item.Id == ForeignGrant.Id);
        Assert.Equal(ForeignTenant.Id, grant.TenantId);
        Assert.Equal(ForeignSubject.Id, grant.SubjectUserId);
        Assert.Equal(1, grant.VersionNo);
        Assert.Null(grant.RevokedAt);
    }

    public async Task AssertNoMutationsAsync()
    {
        await AssertMutationCountsAsync(0, 0);
        await AssertForeignGrantUnchangedAsync();
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    private static User NewUser(string name) => new()
    {
        DisplayName = "Synthetic " + name, Email = name + "@example.test", NormalizedEmail = name.ToUpperInvariant() + "@EXAMPLE.TEST"
    };

    private static TenantUser Membership(Tenant tenant, User user, TenantUserRole role) => new()
    {
        TenantId = tenant.Id, UserId = user.Id, Role = role, Status = TenantUserStatus.Active,
        JoinedAt = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)
    };
}

internal sealed class CapabilityTestUser(Guid userId) : ICurrentUser
{
    public Guid? UserId { get; set; } = userId;
    public Guid? SessionId => null;
    public string? Email => null;
    public SystemRole? SystemRole => null;
    public bool IsAuthenticated { get; set; } = true;
}

internal sealed class CapabilityTestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class CapabilitySave(IUnitOfWork inner) : IUnitOfWork
{
    public int Calls { get; private set; }
    public Exception? Failure { get; set; }
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Failure is null ? inner.SaveChangesAsync(cancellationToken) : Task.FromException<int>(Failure);
    }
}
