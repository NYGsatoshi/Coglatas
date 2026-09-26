using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class MessageNotificationPreferencePostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PreferenceStoreUsesTheAdditiveDefaultAndFailsClosedAcrossTenantOrInactiveMembership()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgreSqlTestEnvironment.RequireConnectionString())
            .Options;
        var currentTenant = new CurrentTenantService();
        var runId = Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 8, 23, 5, 45, 0, TimeSpan.Zero);

        await using var dbContext = new AppDbContext(options, currentTenant);
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());

        var tenantA = new Tenant
        {
            Name = $"Message Preference A {runId}",
            DisplayName = "Message Preference A",
            Slug = $"message-pref-a-{runId}"
        };
        var tenantB = new Tenant
        {
            Name = $"Message Preference B {runId}",
            DisplayName = "Message Preference B",
            Slug = $"message-pref-b-{runId}"
        };
        var userA = NewUser($"message-pref-a-{runId}@example.test");
        var userB = NewUser($"message-pref-b-{runId}@example.test");

        currentTenant.SetPlatformScope();
        dbContext.Tenants.AddRange(tenantA, tenantB);
        dbContext.Users.AddRange(userA, userB);
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenantA.Id, tenantA.Slug);
        dbContext.TenantUsers.Add(new TenantUser
        {
            UserId = userA.Id,
            Status = TenantUserStatus.Active,
            JoinedAt = now
        });
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenantB.Id, tenantB.Slug);
        dbContext.TenantUsers.Add(new TenantUser
        {
            UserId = userB.Id,
            Status = TenantUserStatus.Active,
            JoinedAt = now
        });
        await dbContext.SaveChangesAsync();

        var store = new MessageNotificationPreferenceStore(dbContext);
        Assert.True(await store.GetEnabledAsync(tenantA.Id, userA.Id));
        Assert.True(await store.SetEnabledAsync(tenantA.Id, userA.Id, false, now.AddMinutes(1)));
        Assert.False(await store.GetEnabledAsync(tenantA.Id, userA.Id));
        Assert.Null(await store.GetEnabledAsync(tenantB.Id, userA.Id));
        Assert.False(await store.SetEnabledAsync(tenantB.Id, userA.Id, false, now.AddMinutes(2)));

        currentTenant.SetTenant(tenantA.Id, tenantA.Slug);
        var membership = await dbContext.TenantUsers.SingleAsync(item => item.UserId == userA.Id);
        membership.Status = TenantUserStatus.Suspended;
        await dbContext.SaveChangesAsync();

        Assert.Null(await store.GetEnabledAsync(tenantA.Id, userA.Id));
        Assert.False(await store.SetEnabledAsync(tenantA.Id, userA.Id, true, now.AddMinutes(3)));
    }

    private static User NewUser(string email) => new()
    {
        DisplayName = "Message Preference User",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        Status = UserStatus.Active
    };
}
