using System.Text.Json;
using Coglatas.Application.Auth;
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

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CookieWarmedManagerChangesDenyEveryProtectedReadAndWriteWithoutEffects()
    {
        foreach (var change in new[] { "member", "guest", "membership", "actor-status", "actor-deleted", "tenant-status", "tenant-deleted" })
        {
            output.WriteLine("Synthetic persisted manager control: " + change);
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
                {
                    await using var fixture = await AdministrationFixture.CreateAsync(database);
                    await fixture.WarmAuthenticatedRequestAsync();
                    await using (var revoker = fixture.OpenContext())
                    {
                        var membership = await revoker.TenantUsers.Include(item => item.User).Include(item => item.Tenant).SingleAsync();
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
                    var service = fixture.Service;
                    AssertDenied(await service.GetCurrentTenantSettingsAsync());
                    AssertDenied(await service.GetCurrentTenantOverviewAsync());
                    AssertDenied(await service.GetCurrentTenantUsageAsync());
                    AssertDenied(await service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest));
                    await using var read = fixture.OpenContext();
                    Assert.Equal("Original settings", (await read.TenantSettings.SingleAsync()).DisplayName);
                    Assert.Empty(await read.AuditLogs.ToListAsync());
                    Assert.Empty(fixture.Files.Requested);
                });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CallerRollbackRemovesAdminSettingsChangeAndAuditTogether()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await AdministrationFixture.CreateAsync(database);
                fixture.Membership.Role = TenantUserRole.Admin;
                await fixture.Db.SaveChangesAsync();
                await fixture.WarmAuthenticatedRequestAsync();
                await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
                var changed = await fixture.Service.UpdateCurrentTenantSettingsAsync(AdministrationFixture.SettingsRequest);
                Assert.True(changed.IsSuccess, changed.Error);
                Assert.Single(await fixture.Db.AuditLogs.ToListAsync());
                await using (var beforeRollback = fixture.OpenContext())
                {
                    Assert.Equal("Original settings", (await beforeRollback.TenantSettings.SingleAsync()).DisplayName);
                    Assert.Empty(await beforeRollback.AuditLogs.ToListAsync());
                }
                await transaction.RollbackAsync();
                await using var afterRollback = fixture.OpenContext();
                Assert.Equal("Original settings", (await afterRollback.TenantSettings.SingleAsync()).DisplayName);
                Assert.Empty(await afterRollback.AuditLogs.ToListAsync());
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task RevokedManagerCannotCreateMissingSettingsThroughRead()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await AdministrationFixture.CreateAsync(database);
                fixture.Db.TenantSettings.Remove(await fixture.Db.TenantSettings.SingleAsync());
                await fixture.Db.SaveChangesAsync();
                await fixture.WarmAuthenticatedRequestAsync();
                await using (var revoker = fixture.OpenContext())
                {
                    (await revoker.TenantUsers.SingleAsync()).Role = TenantUserRole.Member;
                    await revoker.SaveChangesAsync();
                }
                AssertDenied(await fixture.Service.GetCurrentTenantSettingsAsync());
                await using var read = fixture.OpenContext();
                Assert.Empty(await read.TenantSettings.ToListAsync());
                Assert.Empty(await read.AuditLogs.ToListAsync());
            });
    }

    private static void AssertDenied<T>(Result<T> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Forbidden", result.ErrorDetail!.Code);
        Assert.Equal("TenantAdmin access is required.", result.Error);
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
    public AdministrationUser CurrentUser { get; }
    public AdministrationClock Clock { get; } = new();
    public AdministrationFiles Files { get; } = new();
    public TenantAdministrationService Service => CreateService();
    public static UpdateTenantSettingsRequest SettingsRequest => new(" Changed settings ", null, " #123456 ", " en ", " UTC ", null, 4096, 12, 4, 1024, "{}", "{}");

    private AdministrationFixture(string? database)
    {
        _options = database is null
            ? new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("tenant-admin-" + Guid.NewGuid().ToString("N")).Options
            : new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
        CurrentTenant.SetPlatformScope();
        Db = new AppDbContext(_options, CurrentTenant);
        Membership = new TenantUser { TenantId = Tenant.Id, Tenant = Tenant, UserId = Actor.Id, User = Actor, Role = TenantUserRole.Owner, Status = TenantUserStatus.Active };
        CurrentUser = new AdministrationUser(Actor.Id);
    }

    public TenantAdministrationService CreateService(IAuditLogger? audit = null, IUnitOfWork? saves = null)
    {
        var plans = new TenantPlanRepository(Db);
        return new TenantAdministrationService(plans, new TenantRepository(Db), new FeatureFlagService(plans, CurrentTenant),
            new QuotaService(plans), CurrentTenant, CurrentUser, Clock, audit ?? new DbAuditLogger(Db, Clock, CurrentUser, CurrentTenant),
            saves ?? new EfUnitOfWork(Db), Files);
    }

    public static async Task<AdministrationFixture> CreateAsync(string? database = null)
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

    public async Task WarmAuthenticatedRequestAsync()
    {
        var session = new Session { UserId = Actor.Id, SessionKeyHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = Clock.UtcNow.AddHours(1), LastSeenAt = Clock.UtcNow.AddHours(-1) };
        Db.Sessions.Add(session);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var sessions = new UserSessionService(new SessionRepository(Db), new TenantRepository(Db),
            new DbAuditLogger(Db, Clock, CurrentUser, CurrentTenant), Clock, new EfUnitOfWork(Db));
        Assert.True((await sessions.ValidateSessionAsync(Actor.Id, session.Id, Tenant.Id, true)).IsValid);
        await using var read = OpenContext();
        Assert.Equal(Clock.UtcNow, (await read.Sessions.SingleAsync()).LastSeenAt);
    }

    public sealed class AdministrationUser(Guid id) : ICurrentUser
    {
        public Guid? UserId { get; set; } = id;
        public Guid? SessionId => null;
        public string Email => "synthetic-manager@example.invalid";
        public SystemRole? SystemRole { get; set; } = Domain.Enums.SystemRole.User;
        public bool IsAuthenticated { get; set; } = true;
    }

    public sealed class AdministrationClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    }

    // Only the logo-admission boundary is doubled; storage/HTTP/RLS are not qualified here.
    public sealed class AdministrationFiles : IFileObjectService
    {
        public Result<FileObjectResponse> Result { get; set; } = Result<FileObjectResponse>.Failure("Synthetic unavailable logo.");
        public List<Guid> Requested { get; } = [];
        public Task<Result<FileObjectResponse>> GetFileObjectAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Requested.Add(id);
            return Task.FromResult(Result);
        }
        public Task<Result<AttachmentResponse>> UploadAsync(AttachmentUploadInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<PagedResponse<FileListItemResponse>>> ListFileObjectsAsync(Guid workspaceId, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileDownloadGrantResponse>> RequestFileObjectDownloadGrantAsync(Guid id, FileDownloadGrantRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileDownloadResponse>> DownloadFileObjectWithGrantAsync(Guid id, string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileDownloadResponse>> DownloadFileObjectAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteFileObjectAsync(Guid id, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
