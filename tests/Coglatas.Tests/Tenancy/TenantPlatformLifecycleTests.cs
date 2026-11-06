using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Tenancy;

public sealed partial class TenantIsolationSecurityTests
{
    [Theory]
    [Trait("Scope", "TenantPlatformValidationOrder")]
    [InlineData("name")]
    [InlineData("display-name")]
    [InlineData("both")]
    public async Task DuplicatePlatformDomainDoesNotStageOtherTenantChanges(string field)
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        data.TenantB.PrimaryDomain = "used.example.test";
        await dbContext.SaveChangesAsync();
        var before = (data.TenantA.Name, data.TenantA.DisplayName, data.TenantA.PrimaryDomain, data.TenantA.PlanId);
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, data.PlatformAdmin, new TenancyOptions(), audit);

        var result = await service.UpdateTenantAsync(data.TenantA.Id, new UpdateTenantRequest(
            field is "name" or "both" ? "Changed name" : null,
            field is "display-name" or "both" ? "Changed display" : null,
            "  USED.EXAMPLE.TEST  ", "changed-plan"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Tenant primary domain is already in use.", result.Error);
        Assert.Equal(before, (data.TenantA.Name, data.TenantA.DisplayName, data.TenantA.PrimaryDomain, data.TenantA.PlanId));
        Assert.Empty(audit.Entries);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }
}
