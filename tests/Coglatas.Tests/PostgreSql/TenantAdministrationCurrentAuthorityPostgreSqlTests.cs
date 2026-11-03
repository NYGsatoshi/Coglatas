using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Files;
using Coglatas.Application.TenantAdministration;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

// Actual PostgreSQL service controls; no HTTP or application-role RLS qualification.
public sealed class TenantAdministrationCurrentAuthorityPostgreSqlTests(ITestOutputHelper output)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CommittedManagerDemotionAfterTrackedReadDeniesSettingsChange()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await AdministrationFixture.CreateAsync(database);
                Assert.True((await fixture.Service.GetCurrentTenantSettingsAsync()).IsSuccess);
                await using (var revoker = fixture.OpenContext())
                {
                    var membership = await revoker.TenantUsers.SingleAsync();
                    membership.Role = TenantUserRole.Member;
                    await revoker.SaveChangesAsync();
                }

                var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(
                    AdministrationFixture.SettingsRequest with { DisplayName = "Denied update" });
                await using var read = fixture.OpenContext();
                var stored = await read.TenantSettings.AsNoTracking().SingleAsync();
                output.WriteLine("Synthetic current-authority state: " + JsonSerializer.Serialize(new
                {
                    persistedRole = (await read.TenantUsers.AsNoTracking().SingleAsync()).Role.ToString(),
                    trackedRole = fixture.Membership.Role.ToString(), result.IsSuccess, stored.DisplayName,
                    auditCount = await read.AuditLogs.CountAsync()
                }));
                Assert.False(result.IsSuccess);
                Assert.Equal("Forbidden", result.ErrorDetail!.Code);
                Assert.Equal("Original settings", stored.DisplayName);
                Assert.Empty(await read.AuditLogs.ToListAsync());
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ActiveOwnerSettingsChangePersistsWithScopedAudit()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await AdministrationFixture.CreateAsync(database);
                var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest);
                Assert.True(result.IsSuccess, result.Error);
                await using var read = fixture.OpenContext();
                Assert.Equal("Changed settings", (await read.TenantSettings.SingleAsync()).DisplayName);
                var audit = Assert.Single(await read.AuditLogs.ToListAsync());
                Assert.Equal(fixture.Tenant.Id, audit.TenantId);
                Assert.Equal(fixture.Actor.Id, audit.ActorUserId);
                Assert.Equal("TenantSettingsChanged", audit.Action);
            });
    }
}

internal sealed class AdministrationFixture : IAsyncDisposable
{
    private readonly DbContextOptions<AppDbContext> _options;
    public CurrentTenantService CurrentTenant { get; } = new();
    public AppDbContext Db { get; }
    public Tenant Tenant { get; } = new() { Name = "Synthetic administration", DisplayName = "Synthetic administration", Slug = "admin-" + Guid.NewGuid().ToString("N") };
    public User Actor { get; } = new() { DisplayName = "Synthetic manager", Email = Guid.NewGuid().ToString("N") + "@example.invalid", PasswordHash = "synthetic-unused", Status = UserStatus.Active };
    public TenantUser Membership { get; }
    public TenantAdministrationService Service { get; }
    public static UpdateTenantSettingsRequest SettingsRequest => new(" Changed settings ", null, " #123456 ", " en ", " UTC ", null, 4096, 12, 4, 1024, "{}", "{}");

    private AdministrationFixture(string database)
    {
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
        CurrentTenant.SetPlatformScope();
        Db = new AppDbContext(_options, CurrentTenant);
        Membership = new TenantUser { TenantId = Tenant.Id, Tenant = Tenant, UserId = Actor.Id, User = Actor, Role = TenantUserRole.Owner, Status = TenantUserStatus.Active };
        var user = new AdministrationUser(Actor.Id);
        var clock = new AdministrationClock();
        var plans = new TenantPlanRepository(Db);
        Service = new TenantAdministrationService(plans, new TenantRepository(Db), new FeatureFlagService(plans, CurrentTenant),
            new QuotaService(plans), CurrentTenant, user, clock, new DbAuditLogger(Db, clock, user, CurrentTenant),
            new EfUnitOfWork(Db), new UnusedAdministrationFiles());
    }

    public static async Task<AdministrationFixture> CreateAsync(string database)
    {
        var fixture = new AdministrationFixture(database);
        fixture.Db.AddRange(fixture.Tenant, fixture.Actor, fixture.Membership,
            new TenantSettings { TenantId = fixture.Tenant.Id, DisplayName = "Original settings" });
        await fixture.Db.SaveChangesAsync();
        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        return fixture;
    }

    public AppDbContext OpenContext() => new(_options, CurrentTenant);
    public ValueTask DisposeAsync() => Db.DisposeAsync();

    private sealed class AdministrationUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid? SessionId => null;
        public string Email => "synthetic-manager@example.invalid";
        public SystemRole? SystemRole => Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }

    private sealed class AdministrationClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class UnusedAdministrationFiles : IFileObjectService
    {
        public Task<Result<FileObjectResponse>> GetFileObjectAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No logo lookup is expected.");
        public Task<Result<AttachmentResponse>> UploadAsync(AttachmentUploadInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<PagedResponse<FileListItemResponse>>> ListFileObjectsAsync(Guid workspaceId, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileDownloadGrantResponse>> RequestFileObjectDownloadGrantAsync(Guid id, FileDownloadGrantRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileDownloadResponse>> DownloadFileObjectWithGrantAsync(Guid id, string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileDownloadResponse>> DownloadFileObjectAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteFileObjectAsync(Guid id, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
