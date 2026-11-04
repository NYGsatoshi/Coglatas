using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.TenantAdministration;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Tenancy;

// Synthetic EF InMemory service/persistence controls; not PostgreSQL, HTTP or RLS evidence.
public sealed class TenantAdministrationServiceTests
{
    [Theory]
    [InlineData("tenant", "ValidationFailed")]
    [InlineData("anonymous", "AuthenticationRequired")]
    [InlineData("missing-id", "AuthenticationRequired")]
    [InlineData("member", "Forbidden")]
    [InlineData("guest", "Forbidden")]
    [InlineData("inactive-membership", "Forbidden")]
    [InlineData("foreign-member", "Forbidden")]
    public async Task ProtectedTenantReadsAndWritesDenyWithoutChangingSettings(string boundary, string code)
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        switch (boundary)
        {
            case "tenant": fixture.CurrentTenant.SetPlatformScope(); break;
            case "anonymous": fixture.CurrentUser.IsAuthenticated = false; break;
            case "missing-id": fixture.CurrentUser.UserId = null; break;
            case "member": fixture.Membership.Role = TenantUserRole.Member; break;
            case "guest": fixture.Membership.Role = TenantUserRole.Guest; break;
            case "inactive-membership": fixture.Membership.Status = TenantUserStatus.Suspended; break;
            case "foreign-member": fixture.CurrentUser.UserId = Guid.NewGuid(); break;
        }
        await fixture.Db.SaveChangesAsync();
        var service = fixture.Service;
        AssertDenied(await service.GetCurrentTenantOverviewAsync(), code);
        AssertDenied(await service.GetCurrentTenantSettingsAsync(), code);
        AssertDenied(await service.GetCurrentTenantUsageAsync(), code);
        AssertDenied(await service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest), code);
        await using var read = fixture.OpenContext();
        Assert.Equal("Original settings", (await read.TenantSettings.IgnoreQueryFilters().SingleAsync()).DisplayName);
        Assert.Empty(await read.AuditLogs.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(fixture.Files.Requested);
    }

    [Theory]
    [InlineData("display-length")]
    [InlineData("theme-length")]
    [InlineData("storage")]
    [InlineData("users")]
    [InlineData("projects")]
    [InlineData("upload")]
    [InlineData("feature-array")]
    [InlineData("feature-empty")]
    [InlineData("feature-malformed")]
    [InlineData("notification-array")]
    public async Task InvalidSettingsDoNotPersistOrAudit(string invalid)
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var request = invalid switch
        {
            "display-length" => AdministrationFixture.SettingsRequest with { DisplayName = new string('x', 161) },
            "theme-length" => AdministrationFixture.SettingsRequest with { ThemeColor = new string('x', 41) },
            "storage" => AdministrationFixture.SettingsRequest with { StorageQuotaBytes = -1 },
            "users" => AdministrationFixture.SettingsRequest with { UserLimit = -1 },
            "projects" => AdministrationFixture.SettingsRequest with { ProjectLimit = -1 },
            "upload" => AdministrationFixture.SettingsRequest with { FileUploadLimitBytes = -1 },
            "feature-array" => AdministrationFixture.SettingsRequest with { FeatureFlagsJson = "[]" },
            "feature-empty" => AdministrationFixture.SettingsRequest with { FeatureFlagsJson = " " },
            "feature-malformed" => AdministrationFixture.SettingsRequest with { FeatureFlagsJson = "{" },
            "notification-array" => AdministrationFixture.SettingsRequest with { NotificationSettingsJson = "[]" },
            _ => throw new InvalidOperationException("Unclassified settings validation control.")
        };
        var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(request);
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Error!);
        await using var read = fixture.OpenContext();
        Assert.Equal("Original settings", (await read.TenantSettings.SingleAsync()).DisplayName);
        Assert.Empty(await read.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("workspace")]
    [InlineData("group")]
    [InlineData("project")]
    public async Task UnavailableOrScopedLogoIsDeniedWithoutSavingSettings(string kind)
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var logo = Logo(Guid.NewGuid());
        fixture.Files.Result = kind switch
        {
            "missing" => Result<FileObjectResponse>.Failure("Synthetic foreign or absent file."),
            "null" => Result<FileObjectResponse>.Success(null!),
            "inactive" => Result<FileObjectResponse>.Success(logo with { Status = nameof(FileObjectStatus.Deleted) }),
            "deleted" => Result<FileObjectResponse>.Success(logo with { DeletedAt = fixture.Clock.UtcNow }),
            "workspace" => Result<FileObjectResponse>.Success(logo with { WorkspaceId = Guid.NewGuid() }),
            "group" => Result<FileObjectResponse>.Success(logo with { GroupId = Guid.NewGuid() }),
            "project" => Result<FileObjectResponse>.Success(logo with { ProjectId = Guid.NewGuid() }),
            _ => throw new InvalidOperationException("Unclassified logo control.")
        };
        var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest with { LogoFileId = logo.Id });
        Assert.False(result.IsSuccess);
        Assert.Equal("Logo file is not available.", result.Error);
        Assert.Equal(logo.Id, Assert.Single(fixture.Files.Requested));
        await using var read = fixture.OpenContext();
        Assert.Null((await read.TenantSettings.SingleAsync()).LogoFileId);
        Assert.Empty(await read.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task TenantSettingsPatchRetainsOmittedValuesAndAcceptsBoundaryValuesAndTenantLogo()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var logo = Logo(Guid.NewGuid());
        fixture.Files.Result = Result<FileObjectResponse>.Success(logo);
        var first = await fixture.Service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest with
        {
            DisplayName = new string('n', 160), ThemeColor = new string('c', 40), LogoFileId = logo.Id,
            StorageQuotaBytes = 0, UserLimit = 0, ProjectLimit = 0, FileUploadLimitBytes = 0,
            InvitationMode = InvitationMode.AdminOnly
        });
        Assert.True(first.IsSuccess, first.Error);
        var patch = await fixture.Service.UpdateCurrentTenantSettingsAsync(new UpdateTenantSettingsRequest(
            null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.True(patch.IsSuccess, patch.Error);
        Assert.Equal(first.Value, patch.Value);
        Assert.Equal(logo.Id, patch.Value!.LogoFileId);
        Assert.Equal(0, patch.Value.StorageQuotaBytes);
        Assert.Equal("en", patch.Value.DefaultLocale);
        Assert.Equal("UTC", patch.Value.TimeZone);
        await using var read = fixture.OpenContext();
        Assert.Equal(2, await read.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(TenantUserRole.Owner)]
    [InlineData(TenantUserRole.Admin)]
    public async Task ManagersCanReadOverviewUsageFeaturesAndCreateMissingSettings(TenantUserRole role)
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        fixture.Membership.Role = role;
        fixture.Db.TenantSettings.Remove(await fixture.Db.TenantSettings.SingleAsync());
        await fixture.Db.SaveChangesAsync();
        var settings = await fixture.Service.GetCurrentTenantSettingsAsync();
        Assert.True(settings.IsSuccess, settings.Error);
        Assert.Equal(fixture.Tenant.DisplayName, settings.Value!.DisplayName);
        var overview = await fixture.Service.GetCurrentTenantOverviewAsync();
        Assert.True(overview.IsSuccess, overview.Error);
        Assert.Equal(fixture.Tenant.Id, overview.Value!.TenantId);
        Assert.Equal(1, overview.Value.ActiveUserCount);
        Assert.Null(overview.Value.Subscription);
        var usage = await fixture.Service.GetCurrentTenantUsageAsync();
        Assert.True(usage.IsSuccess, usage.Error);
        Assert.Equal(1, usage.Value!.TotalUserCount);
        var features = await fixture.Service.GetCurrentTenantFeaturesAsync();
        Assert.True(features.IsSuccess);
        Assert.Equal(overview.Value.EnabledFeatures, features.Value!.EnabledFeatures);
        await using var read = fixture.OpenContext();
        Assert.Single(await read.TenantSettings.ToListAsync());
        Assert.Empty(await read.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task FeatureDiscoveryPreservesItsExistingTenantOnlyAdmission()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        fixture.Membership.Role = TenantUserRole.Member;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Service.GetCurrentTenantFeaturesAsync()).IsSuccess);
        fixture.CurrentTenant.SetPlatformScope();
        var result = await fixture.Service.GetCurrentTenantFeaturesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal("A tenant context is required.", result.Error);
    }

    [Fact]
    public async Task ExistingPlatformClaimOverrideRetainsCurrentTenantSettingsAccessWithoutManagerMembership()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        fixture.Membership.Role = TenantUserRole.Member;
        await fixture.Db.SaveChangesAsync();
        fixture.CurrentUser.SystemRole = SystemRole.PlatformAdmin;
        fixture.Actor.SystemRole = SystemRole.PlatformAdmin;
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest);
        Assert.True(result.IsSuccess, result.Error);
        Assert.True((await fixture.Service.GetCurrentTenantOverviewAsync()).IsSuccess);
        Assert.True((await fixture.Service.GetCurrentTenantUsageAsync()).IsSuccess);
        await using var read = fixture.OpenContext();
        Assert.Equal("Changed settings", (await read.TenantSettings.SingleAsync()).DisplayName);
        Assert.Single(await read.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task TenantOwnerCannotReadOrMutatePlatformPlansSubscriptionsOrUsage()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var service = fixture.Service;
        Assert.False((await service.GetPlatformOverviewAsync()).IsSuccess);
        Assert.False((await service.GetPlatformUsageAsync()).IsSuccess);
        Assert.False((await service.GetPlatformTenantUsageAsync(fixture.Tenant.Id)).IsSuccess);
        Assert.False((await service.ListPlansAsync()).IsSuccess);
        Assert.False((await service.CreatePlanAsync(PlanRequest)).IsSuccess);
        Assert.False((await service.UpdatePlanAsync(Guid.NewGuid(), PlanRequest)).IsSuccess);
        Assert.False((await service.ArchivePlanAsync(Guid.NewGuid())).IsSuccess);
        Assert.False((await service.GetTenantSubscriptionAsync(fixture.Tenant.Id)).IsSuccess);
        Assert.False((await service.UpdateTenantSubscriptionAsync(fixture.Tenant.Id,
            new UpdateSubscriptionRequest(Guid.NewGuid(), SubscriptionStatus.Active, null, null, null))).IsSuccess);
        Assert.Empty(await fixture.Db.Plans.ToListAsync());
        Assert.Empty(await fixture.Db.Subscriptions.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("users")]
    [InlineData("storage")]
    [InlineData("projects")]
    [InlineData("guests")]
    [InlineData("requests")]
    [InlineData("price")]
    [InlineData("object")]
    [InlineData("malformed")]
    public async Task InvalidPlatformPlanDoesNotCreateOrChangePersistedPlan(string kind)
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        fixture.CurrentUser.SystemRole = SystemRole.PlatformAdmin;
        fixture.Actor.SystemRole = SystemRole.PlatformAdmin;
        await fixture.Db.SaveChangesAsync();
        fixture.CurrentTenant.SetPlatformScope();
        var created = await fixture.Service.CreatePlanAsync(PlanRequest);
        Assert.True(created.IsSuccess, created.Error);
        var request = kind switch
        {
            "name" => PlanRequest with { Name = " " },
            "users" => PlanRequest with { MaxUsers = -1 },
            "storage" => PlanRequest with { MaxStorageBytes = -1 },
            "projects" => PlanRequest with { MaxProjects = -1 },
            "guests" => PlanRequest with { MaxExternalGuests = -1 },
            "requests" => PlanRequest with { MaxApiRequestsPerDay = -1 },
            "price" => PlanRequest with { PriceMonthly = -1 },
            "object" => PlanRequest with { EnabledFeaturesJson = "{}" },
            "malformed" => PlanRequest with { EnabledFeaturesJson = "[" },
            _ => throw new InvalidOperationException("Unclassified plan control.")
        };
        Assert.False((await fixture.Service.CreatePlanAsync(request)).IsSuccess);
        if (kind != "name") Assert.False((await fixture.Service.UpdatePlanAsync(created.Value!.Id, request)).IsSuccess);
        await using var read = fixture.OpenContext();
        Assert.Equal("Synthetic plan", Assert.Single(await read.Plans.ToListAsync()).Name);
    }

    [Fact]
    public async Task PlatformPlanAndSubscriptionLifecyclePersistsAndKeepsTenantUsageSeparated()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        fixture.CurrentUser.SystemRole = SystemRole.PlatformAdmin;
        fixture.Actor.SystemRole = SystemRole.PlatformAdmin;
        await fixture.Db.SaveChangesAsync();
        fixture.CurrentTenant.SetPlatformScope();
        var service = fixture.Service;
        var created = await service.CreatePlanAsync(PlanRequest);
        Assert.True(created.IsSuccess, created.Error);
        var id = created.Value!.Id;
        Assert.Equal("Synthetic plan", Assert.Single((await service.ListPlansAsync()).Value!).Name);
        var updated = await service.UpdatePlanAsync(id, new UpsertPlanRequest(null, null, null, null, null, null, null, null, null, null));
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(created.Value, updated.Value);
        var renamed = await service.UpdatePlanAsync(id, PlanRequest with { Name = " Renamed plan ", Description = " Changed " });
        Assert.Equal("Renamed plan", renamed.Value!.Name);
        Assert.Equal("Changed", renamed.Value.Description);
        Assert.False((await service.UpdatePlanAsync(Guid.NewGuid(), PlanRequest)).IsSuccess);
        Assert.False((await service.ArchivePlanAsync(Guid.NewGuid())).IsSuccess);
        Assert.False((await service.GetTenantSubscriptionAsync(fixture.Tenant.Id)).IsSuccess);
        Assert.False((await service.UpdateTenantSubscriptionAsync(fixture.Tenant.Id,
            new UpdateSubscriptionRequest(Guid.NewGuid(), SubscriptionStatus.Active, null, null, null))).IsSuccess);
        var subscribed = await service.UpdateTenantSubscriptionAsync(fixture.Tenant.Id,
            new UpdateSubscriptionRequest(id, SubscriptionStatus.Active, null, fixture.Clock.UtcNow.AddDays(30), fixture.Clock.UtcNow.AddDays(7)));
        Assert.True(subscribed.IsSuccess, subscribed.Error);
        Assert.Equal(fixture.Clock.UtcNow, subscribed.Value!.StartedAt);
        Assert.Equal("Renamed plan", (await service.GetTenantSubscriptionAsync(fixture.Tenant.Id)).Value!.PlanName);
        var changed = await service.UpdateTenantSubscriptionAsync(fixture.Tenant.Id,
            new UpdateSubscriptionRequest(id, SubscriptionStatus.Active, fixture.Clock.UtcNow.AddDays(1), null, null));
        Assert.Equal(subscribed.Value.Id, changed.Value!.Id);
        Assert.Equal(fixture.Clock.UtcNow.AddDays(1), changed.Value.StartedAt);
        Assert.Equal(1, (await service.GetPlatformOverviewAsync()).Value!.TotalUserCount);
        Assert.Equal(1, (await service.GetPlatformUsageAsync()).Value!.TotalActiveUserCount);
        Assert.Equal(0, (await service.GetPlatformTenantUsageAsync(Guid.NewGuid())).Value!.TotalUserCount);
        Assert.True((await service.ArchivePlanAsync(id)).IsSuccess);
        await using var read = fixture.OpenContext();
        Assert.Equal(PlanStatus.Archived, (await read.Plans.SingleAsync()).Status);
        Assert.Single(await read.Subscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("save")]
    public async Task DependencyFailurePreservesOriginalErrorAndDoesNotPersistSettings(string failure)
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var original = new InvalidOperationException("Synthetic " + failure + " failure.");
        var service = fixture.CreateService(failure == "audit" ? new FailedAudit(original) : null,
            failure == "save" ? new FailedSave(original) : null);
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest)));
        await using var read = fixture.OpenContext();
        Assert.Equal("Original settings", (await read.TenantSettings.SingleAsync()).DisplayName);
        Assert.Empty(await read.AuditLogs.ToListAsync());
    }

    private static UpsertPlanRequest PlanRequest => new(" Synthetic plan ", " Description ", 10, 4096, 5, 2, 100, "[\"Calendar\"]", 12.5m, PlanStatus.Active);
    private static FileObjectResponse Logo(Guid id) => new(id, null, null, null, "logo.png", "image/png", 128,
        nameof(FileObjectStatus.Active), DateTimeOffset.UnixEpoch, null, null);
    private static void AssertDenied<T>(Result<T> result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(code, result.ErrorDetail!.Code);
    }
    private sealed class FailedAudit(Exception error) : IAuditLogger
    {
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.FromException(error);
    }
    private sealed class FailedSave(Exception error) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromException<int>(error);
    }
}
