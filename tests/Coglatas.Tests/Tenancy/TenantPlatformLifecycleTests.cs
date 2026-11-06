using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
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

    [Theory]
    [InlineData("regular")]
    [InlineData("suspended")]
    [InlineData("archived")]
    [InlineData("deleted")]
    [InlineData("missing")]
    public async Task PlatformTenantOperationsRequireTheCurrentlyPersistedAdministrator(string boundary)
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        switch (boundary)
        {
            case "regular": data.PlatformAdmin.SystemRole = SystemRole.User; break;
            case "suspended": data.PlatformAdmin.Status = UserStatus.Suspended; break;
            case "archived": data.PlatformAdmin.Status = UserStatus.Archived; break;
            case "deleted": data.PlatformAdmin.MarkDeleted(DateTimeOffset.UtcNow); break;
            case "missing": dbContext.Users.Remove(data.PlatformAdmin); break;
        }
        await dbContext.SaveChangesAsync();
        var before = (data.TenantA.Name, data.TenantA.DisplayName, data.TenantA.Status, data.TenantA.DeletedAt);
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, data.PlatformAdmin, new TenancyOptions(), audit);

        var listed = await service.ListPlatformTenantsAsync();
        var found = await service.GetPlatformTenantAsync(data.TenantA.Id);
        var created = await service.CreateTenantAsync(new CreateTenantRequest("New", "new", null, null, null));
        var updated = await service.UpdateTenantAsync(data.TenantA.Id, new UpdateTenantRequest("changed", "changed", null, null));
        var suspended = await service.SuspendTenantAsync(data.TenantA.Id);
        var activated = await service.ActivateTenantAsync(data.TenantA.Id);
        var archived = await service.ArchiveTenantAsync(data.TenantA.Id);

        Assert.False(listed.IsSuccess);
        Assert.False(found.IsSuccess);
        Assert.False(created.IsSuccess);
        Assert.False(updated.IsSuccess);
        Assert.False(suspended.IsSuccess);
        Assert.False(activated.IsSuccess);
        Assert.False(archived.IsSuccess);
        Assert.Equal("PlatformAdmin access is required.", listed.Error);
        Assert.Equal(listed.Error, found.Error);
        Assert.Equal(listed.Error, created.Error);
        Assert.Equal(listed.Error, updated.Error);
        Assert.Equal(listed.Error, suspended.Error);
        Assert.Equal(listed.Error, activated.Error);
        Assert.Equal(listed.Error, archived.Error);
        Assert.Equal(before, (data.TenantA.Name, data.TenantA.DisplayName, data.TenantA.Status, data.TenantA.DeletedAt));
        Assert.Empty(audit.Entries);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Theory]
    [InlineData("", "new", null, "Tenant name and slug are required.")]
    [InlineData("Valid", "  ", null, "Tenant name and slug are required.")]
    [InlineData("Valid", " TENANT-A ", null, "Tenant slug is already in use.")]
    [InlineData("Valid", "new", " USED.EXAMPLE.TEST ", "Tenant primary domain is already in use.")]
    public async Task InvalidTenantCreationDoesNotAddRowsOrAudit(string name, string slug, string? domain, string error)
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        data.TenantB.PrimaryDomain = "used.example.test";
        await dbContext.SaveChangesAsync();
        var before = await dbContext.Tenants.CountAsync();
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, data.PlatformAdmin, new TenancyOptions(), audit);

        var result = await service.CreateTenantAsync(new CreateTenantRequest(name, slug, null, domain, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
        Assert.Equal(before, await dbContext.Tenants.CountAsync());
        Assert.Empty(audit.Entries);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Theory]
    [InlineData(null, null, null, "New Tenant")]
    [InlineData(" Custom display ", " NEW.EXAMPLE.TEST ", " TEAM ", "Custom display")]
    public async Task ValidTenantCreationNormalizesAndAuditsTheSavedTenant(string? display, string? domain, string? plan, string expectedDisplay)
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, data.PlatformAdmin, new TenancyOptions(), audit);

        var result = await service.CreateTenantAsync(new CreateTenantRequest(" New Tenant ", " NEW-TENANT ", display, domain, plan));

        Assert.True(result.IsSuccess, result.Error);
        var saved = await dbContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == result.Value!.Id);
        Assert.Equal("New Tenant", saved.Name);
        Assert.Equal("new-tenant", saved.Slug);
        Assert.Equal(expectedDisplay, saved.DisplayName);
        Assert.Equal(domain?.Trim().ToLowerInvariant(), saved.PrimaryDomain);
        Assert.Equal(plan?.Trim().ToLowerInvariant(), saved.PlanId);
        Assert.Equal(TenantStatus.Active, saved.Status);
        Assert.Null(saved.DeletedAt);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("TenantCreated", entry.Action);
        Assert.Equal(saved.Id, entry.TenantId);
        Assert.Equal(saved.Id, entry.EntityId);
        Assert.Equal(data.PlatformAdmin.Id, entry.ActorUserId);
        Assert.Equal(saved.Slug, entry.Metadata!["slug"]);
    }

    [Theory]
    [InlineData(null, "own.example.test")]
    [InlineData("  ", null)]
    [InlineData(" OWN.EXAMPLE.TEST ", "own.example.test")]
    [InlineData(" NEW.EXAMPLE.TEST ", "new.example.test")]
    public async Task ValidTenantPatchRetainsClearsOrNormalizesTheDomain(string? domain, string? expectedDomain)
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        data.TenantA.PrimaryDomain = "own.example.test";
        await dbContext.SaveChangesAsync();
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, data.PlatformAdmin, new TenancyOptions(), audit);

        var result = await service.UpdateTenantAsync(data.TenantA.Id, new UpdateTenantRequest(" Renamed ", " Display ", domain, " PLAN "));

        Assert.True(result.IsSuccess, result.Error);
        var saved = await dbContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == data.TenantA.Id);
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal("Display", saved.DisplayName);
        Assert.Equal(expectedDomain, saved.PrimaryDomain);
        Assert.Equal("plan", saved.PlanId);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("TenantUpdated", entry.Action);
        Assert.Equal(saved.Id, entry.TenantId);
        Assert.Equal(saved.Id, entry.EntityId);
        Assert.Equal(data.PlatformAdmin.Id, entry.ActorUserId);
    }

    [Fact]
    public async Task StalePlatformPrincipalCannotReplaceCurrentPersistedAuthority()
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        var stalePrincipal = new User
        {
            Id = data.PlatformAdmin.Id,
            Email = data.PlatformAdmin.Email,
            SystemRole = SystemRole.PlatformAdmin,
            Status = UserStatus.Active
        };
        data.PlatformAdmin.SystemRole = SystemRole.User;
        await dbContext.SaveChangesAsync();
        var before = (data.TenantA.Name, data.TenantA.DisplayName, data.TenantA.Status);
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, stalePrincipal, new TenancyOptions(), audit);

        var created = await service.CreateTenantAsync(new CreateTenantRequest("New", "new", null, null, null));
        var updated = await service.UpdateTenantAsync(data.TenantA.Id, new UpdateTenantRequest("Changed", "Changed", null, null));
        var suspended = await service.SuspendTenantAsync(data.TenantA.Id);

        Assert.False(created.IsSuccess);
        Assert.False(updated.IsSuccess);
        Assert.False(suspended.IsSuccess);
        Assert.Equal("PlatformAdmin access is required.", created.Error);
        Assert.Equal(created.Error, updated.Error);
        Assert.Equal(created.Error, suspended.Error);
        Assert.Equal(SystemRole.PlatformAdmin, stalePrincipal.SystemRole);
        Assert.Equal(SystemRole.User, (await dbContext.Users.AsNoTracking().SingleAsync(user => user.Id == stalePrincipal.Id)).SystemRole);
        Assert.Equal(before, (data.TenantA.Name, data.TenantA.DisplayName, data.TenantA.Status));
        Assert.Empty(audit.Entries);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task MissingPlatformTenantRejectsReadsAndChangesWithoutAudit()
    {
        var (dbContext, currentTenant, data) = await CreateSeededContextAsync();
        await using var ownedContext = dbContext;
        currentTenant.SetPlatformScope();
        var audit = new CapturingAuditLogger();
        var service = CreateTenantService(dbContext, currentTenant, data.PlatformAdmin, new TenancyOptions(), audit);
        var missing = Guid.NewGuid();

        var found = await service.GetPlatformTenantAsync(missing);
        var updated = await service.UpdateTenantAsync(missing, new UpdateTenantRequest(null, null, null, null));
        var suspended = await service.SuspendTenantAsync(missing);
        var activated = await service.ActivateTenantAsync(missing);
        var archived = await service.ArchiveTenantAsync(missing);

        Assert.False(found.IsSuccess);
        Assert.False(updated.IsSuccess);
        Assert.False(suspended.IsSuccess);
        Assert.False(activated.IsSuccess);
        Assert.False(archived.IsSuccess);
        Assert.Equal("Tenant not found.", found.Error);
        Assert.Equal(found.Error, updated.Error);
        Assert.Equal(found.Error, suspended.Error);
        Assert.Equal(found.Error, activated.Error);
        Assert.Equal(found.Error, archived.Error);
        Assert.Empty(audit.Entries);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }
}
