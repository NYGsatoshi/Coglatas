using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
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
                    output.WriteLine("Synthetic current-authority state: " + System.Text.Json.JsonSerializer.Serialize(new
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
    public async Task NativeGrantRevokeRegrantKeepsOneSlotAndScopedAuditAndInvalidation()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await CapabilityGrantFixture.CreateAsync(Options(database));
                await CapabilityGrantServiceTests.VerifyLifecycleAsync(fixture);
            });
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
