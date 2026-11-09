using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

// Actual PostgreSQL service/persistence controls; no application-role RLS or HTTP claim.
public sealed class CapabilityGrantServicePostgreSqlTests(ITestOutputHelper output)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CommittedManagerRevocationAfterTrackedReadDeniesCapabilityMutation()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                var service = fixture.CreateService();
                Assert.True((await service.ListAsync()).IsSuccess);
                await using (var revoker = fixture.OpenReadContext())
                {
                    var membership = await revoker.TenantUsers.SingleAsync(item =>
                        item.TenantId == fixture.Tenant.Id && item.UserId == fixture.Actor.Id);
                    membership.Status = TenantUserStatus.Suspended;
                    await revoker.SaveChangesAsync();
                }

                var result = await service.GrantAsync(fixture.Request);
                await using (var persisted = fixture.OpenReadContext())
                {
                    var membership = await persisted.TenantUsers.SingleAsync(item =>
                        item.TenantId == fixture.Tenant.Id && item.UserId == fixture.Actor.Id);
                    output.WriteLine("Synthetic current-authority state: " + JsonSerializer.Serialize(new
                    {
                        persistedMembership = membership.Status.ToString(),
                        trackedMembership = fixture.ActorMembership.Status.ToString(),
                        result.IsSuccess,
                        savedGrants = await persisted.Set<CapabilityGrant>().CountAsync(item => item.TenantId == fixture.Tenant.Id),
                        savedAudit = await persisted.AuditLogs.CountAsync(item => item.TenantId == fixture.Tenant.Id),
                        savedInvalidations = await persisted.OutboxEvents.CountAsync(item => item.TenantId == fixture.Tenant.Id)
                    }));
                    Assert.Equal(TenantUserStatus.Suspended, membership.Status);
                }
                Assert.False(result.IsSuccess);
                Assert.Equal("CapabilityDenied", result.ErrorDetail!.Code);
                await fixture.AssertNoMutationsAsync();
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CookieValidatedActorLifecycleChangesDenyEveryCapabilityManagementOperation()
    {
        foreach (var change in new[] { "member", "guest", "membership", "actor-status", "actor-deleted", "tenant-status", "tenant-deleted" })
        {
            output.WriteLine("Synthetic actor lifecycle control: " + change);
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
                {
                    await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                    await fixture.WarmAuthenticatedRequestAsync();
                    await using (var revoker = fixture.OpenReadContext())
                    {
                        var membership = await revoker.TenantUsers.Include(item => item.User).Include(item => item.Tenant)
                            .SingleAsync(item => item.TenantId == fixture.Tenant.Id && item.UserId == fixture.Actor.Id);
                        switch (change)
                        {
                            case "member": membership.Role = TenantUserRole.Member; break;
                            case "guest": membership.Role = TenantUserRole.Guest; break;
                            case "membership": membership.Status = TenantUserStatus.Suspended; break;
                            case "actor-status": membership.User!.Status = UserStatus.Suspended; break;
                            case "actor-deleted": membership.User!.MarkDeleted(fixture.Clock.UtcNow); break;
                            case "tenant-status": membership.Tenant!.Status = TenantStatus.Suspended; break;
                            case "tenant-deleted": membership.Tenant!.MarkDeleted(fixture.Clock.UtcNow); break;
                        }
                        await revoker.SaveChangesAsync();
                    }

                    var service = fixture.CreateService();
                    var list = await service.ListAsync();
                    var grant = await service.GrantAsync(fixture.Request);
                    var revoke = await service.RevokeAsync(fixture.ForeignGrant.Id);
                    Assert.False(list.IsSuccess);
                    Assert.False(grant.IsSuccess);
                    Assert.False(revoke.IsSuccess);
                    Assert.Equal("CapabilityDenied", list.ErrorDetail!.Code);
                    Assert.Equal(list.ErrorDetail, grant.ErrorDetail);
                    Assert.Equal(list.ErrorDetail, revoke.ErrorDetail);
                    Assert.Equal(0, fixture.Saves.Calls);
                    await fixture.AssertNoMutationsAsync();
                });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ActiveDelegatedWorkspaceCreateRemainsAvailableAfterOwnerDemotion()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                fixture.Db.Set<CapabilityGrant>().Add(new CapabilityGrant
                {
                    TenantId = fixture.Tenant.Id, SubjectUserId = fixture.Actor.Id, GrantedByUserId = fixture.Actor.Id,
                    CapabilityKey = CapabilityKeys.WorkspaceCreate, ScopeType = CapabilityScopeType.Tenant,
                    ScopeId = fixture.Tenant.Id, GrantedAt = fixture.Clock.UtcNow, ExpiresAt = fixture.Clock.UtcNow.AddHours(1)
                });
                await fixture.Db.SaveChangesAsync();
                await fixture.WarmAuthenticatedRequestAsync();
                await using (var revoker = fixture.OpenReadContext())
                {
                    var membership = await revoker.TenantUsers.SingleAsync(item =>
                        item.TenantId == fixture.Tenant.Id && item.UserId == fixture.Actor.Id);
                    membership.Role = TenantUserRole.Member;
                    await revoker.SaveChangesAsync();
                }

                var managers = new TenantAuthorizationService(new TenantRepository(fixture.Db));
                Assert.False(await managers.CanManageTenantAsync(fixture.Actor.Id, fixture.Tenant.Id));
                var workspaces = new WorkspaceAuthorizationService(new UserRepository(fixture.Db),
                    new WorkspaceRepository(fixture.Db), managers, fixture.CreateEvaluator());
                Assert.True(await workspaces.CanCreateWorkspace(fixture.Actor.Id, fixture.Tenant.Id));
                Assert.False((await fixture.CreateService().GrantAsync(fixture.Request)).IsSuccess);
                await fixture.AssertMutationCountsAsync(1, 0);
                await fixture.AssertForeignGrantUnchangedAsync();
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task NativeGrantRevokeRegrantKeepsOneSlotAndScopedAuditAndInvalidation()
    {
        foreach (var role in new[] { TenantUserRole.Owner, TenantUserRole.Admin })
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
                {
                    await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                    fixture.ActorMembership.Role = role;
                    await fixture.Db.SaveChangesAsync();
                    fixture.Db.ChangeTracker.Clear();
                    await CapabilityGrantServiceTests.VerifyLifecycleAsync(fixture);
                });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CallerRollbackAtomicallyRemovesGrantAuditAndAuthorizationInvalidation()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
                var result = await fixture.CreateService().GrantAsync(fixture.Request);
                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(1, await fixture.Db.Set<CapabilityGrant>().CountAsync());
                Assert.Equal(1, await fixture.Db.AuditLogs.CountAsync());
                Assert.Equal(1, await fixture.Db.OutboxEvents.CountAsync());
                await fixture.AssertMutationCountsAsync(0, 0);
                await transaction.RollbackAsync();
                await fixture.AssertNoMutationsAsync();
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task StaleConcurrentRevokeCannotCommitDuplicateAuditOrInvalidation()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                var grantId = await fixture.SeedOwnGrantAsync(true);
                fixture.Db.ChangeTracker.Clear();
                var original = await fixture.Db.Set<CapabilityGrant>().SingleAsync();
                Assert.Null(original.RevokedAt);

                var currentTenant = new CurrentTenantService();
                currentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
                await using (var competing = new AppDbContext(Options(database), currentTenant))
                {
                    var tenants = new TenantRepository(competing);
                    var winner = new CapabilityGrantService(new CapabilityGrantRepository(competing), tenants,
                        new WorkspaceRepository(competing), new TenantAuthorizationService(tenants), currentTenant,
                        fixture.CurrentUser, fixture.Clock,
                        new DbAuditLogger(competing, fixture.Clock, fixture.CurrentUser, currentTenant),
                        new AuthorizationStateChangePublisher(new TransactionalOutbox(new OutboxEventRepository(competing),
                            currentTenant, fixture.Clock), currentTenant, fixture.Clock), new EfUnitOfWork(competing));
                    var result = await winner.RevokeAsync(grantId);
                    Assert.True(result.IsSuccess, result.Error);
                    Assert.Equal(2, result.Value!.VersionNo);
                }

                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.CreateService().RevokeAsync(grantId));
                await fixture.AssertMutationCountsAsync(1, 1);
                await fixture.AssertEvidenceAsync(grantId, "CapabilityRevoked", "revoked", 1, 2);
                await fixture.AssertForeignGrantUnchangedAsync();
            });
    }

    private static DbContextOptions<AppDbContext> Options(string database) =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
}
