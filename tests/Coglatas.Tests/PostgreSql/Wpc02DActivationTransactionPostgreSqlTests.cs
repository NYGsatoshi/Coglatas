using System.Data;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.PostgreSql;

[Trait("Scope", "WPC02D")]
public sealed class Wpc02DActivationTransactionPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ActivationUnitOfWorkUsesSerializableStableSnapshotForAuthorizationInputs()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);

            var platformTenant = new CurrentTenantService();
            platformTenant.SetPlatformScope();
            await using (var platform = new AppDbContext(Options(database), platformTenant))
            {
                var tenant = new Tenant
                {
                    Name = $"WPC02D Tx Tenant {Guid.NewGuid():N}",
                    DisplayName = "WPC02D Tx Tenant",
                    Slug = $"wpc02d-tx-{Guid.NewGuid():N}",
                    Status = TenantStatus.Active
                };
                var owner = NewUser();
                platform.Tenants.Add(tenant);
                platform.Users.Add(owner);
                await platform.SaveChangesAsync();

                var workspace = new Workspace
                {
                    TenantId = tenant.Id,
                    Name = "Authorization Snapshot Before",
                    Slug = $"wpc02d-tx-workspace-{Guid.NewGuid():N}",
                    CreatedByUserId = owner.Id,
                    Status = WorkspaceStatus.Active
                };
                platform.Workspaces.Add(workspace);
                await platform.SaveChangesAsync();

                var currentTenant = new CurrentTenantService();
                currentTenant.SetTenant(tenant.Id, tenant.Slug);
                await using var db = new AppDbContext(Options(database), currentTenant);
                var unitOfWork = new ProjectActivationUnitOfWork(db);

                IsolationLevel observedIsolation = IsolationLevel.Unspecified;
                string? firstRead = null;
                string? secondRead = null;

                var result = await unitOfWork.ExecuteActivationAsync(async token =>
                {
                    var transaction = Assert.IsAssignableFrom<IDbContextTransaction>(db.Database.CurrentTransaction);
                    observedIsolation = transaction.GetDbTransaction().IsolationLevel;

                    firstRead = await db.Workspaces
                        .AsNoTracking()
                        .Where(item => item.Id == workspace.Id)
                        .Select(item => item.Name)
                        .SingleAsync(token);

                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                        database,
                        """
                        UPDATE "workspaces"
                        SET "Name" = @name
                        WHERE "TenantId" = @tenantId AND "Id" = @workspaceId
                        """,
                        ("name", "Authorization Snapshot After"),
                        ("tenantId", tenant.Id),
                        ("workspaceId", workspace.Id));

                    secondRead = await db.Workspaces
                        .AsNoTracking()
                        .Where(item => item.Id == workspace.Id)
                        .Select(item => item.Name)
                        .SingleAsync(token);

                    // Intentionally abort the activation-owned transaction. The
                    // external mutation must remain independent and committed.
                    return Result.Failure(new ApplicationErrorDetail(
                        "TestRollback",
                        "Intentional transaction rollback."));
                });

                Assert.False(result.IsSuccess);
                Assert.Equal("TestRollback", result.ErrorDetail?.Code);
                Assert.Equal(IsolationLevel.Serializable, observedIsolation);
                Assert.Equal("Authorization Snapshot Before", firstRead);
                Assert.Equal(firstRead, secondRead);

                var persistedName = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    database,
                    """
                    SELECT "Name"
                    FROM "workspaces"
                    WHERE "TenantId" = @tenantId AND "Id" = @workspaceId
                    """,
                    ("tenantId", tenant.Id),
                    ("workspaceId", workspace.Id));
                Assert.Equal("Authorization Snapshot After", persistedName);
            }
        });
    }

    private static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new ProjectGovernanceSaveChangesInterceptor())
            .Options;

    private static User NewUser()
    {
        var email = $"wpc02d-tx-{Guid.NewGuid():N}@example.test";
        return new User
        {
            DisplayName = "WPC02D Transaction Owner",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Status = UserStatus.Active,
            SystemRole = SystemRole.NormalUser
        };
    }
}
