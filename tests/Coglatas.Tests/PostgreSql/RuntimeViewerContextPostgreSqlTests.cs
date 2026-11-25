using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class RuntimeViewerContextPostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentViewerRechecksPersistedAuthorityWithoutFlushingPendingWrites() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
            {
                await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var tenant = new Tenant { Slug = "projectide-viewer", Name = "Viewer", DisplayName = "Viewer" };
                var otherTenant = new Tenant { Slug = "projectide-other", Name = "Other", DisplayName = "Other" };
                var user = new User { Email = "viewer@example.invalid", NormalizedEmail = "VIEWER@EXAMPLE.INVALID" };
                var membership = new TenantUser
                {
                    TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active, JoinedAt = Now
                };
                var session = new Session { UserId = user.Id, SessionKeyHash = "synthetic-viewer-hash", ExpiresAt = Now.AddHours(1) };
                writer.AddRange(tenant, otherTenant, user, membership, session);
                await writer.SaveChangesAsync();

                await using var request = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var sessions = new SessionRepository(request);
                var tenants = new TenantRepository(request);
                var trackedSession = Assert.IsType<Session>(await sessions.GetByIdWithUserAsync(session.Id));
                var trackedMembership = Assert.IsType<TenantUser>(await tenants.GetTenantUserAsync(tenant.Id, user.Id));
                trackedSession.LastSeenAt = Now;
                var currentTenant = new CurrentTenantService();
                currentTenant.SetTenant(tenant.Id, tenant.Slug);
                var currentUser = new Viewer(user.Id, session.Id);
                var reader = new ProjectIdeViewerContextReader(currentUser, currentTenant, sessions, tenants, new Clock());
                var viewer = Assert.IsType<ProjectIdeViewerContext>(reader.Capture());
                Assert.True(await reader.IsCurrentAsync(viewer));

                currentUser.SessionId = Guid.NewGuid();
                Assert.False(await reader.IsCurrentAsync(viewer));
                currentUser.SessionId = session.Id;
                currentTenant.SetTenant(otherTenant.Id, otherTenant.Slug);
                Assert.False(await reader.IsCurrentAsync(viewer));
                Assert.False(await reader.IsCurrentAsync(Assert.IsType<ProjectIdeViewerContext>(reader.Capture())));
                currentTenant.SetPlatformScope();
                Assert.Null(reader.Capture());
                currentTenant.SetTenant(tenant.Id, tenant.Slug);

                var changes = new (Action Change, Action Restore)[]
                {
                    (() => session.RevokedAt = Now, () => session.RevokedAt = null),
                    (() => session.ExpiresAt = Now, () => session.ExpiresAt = Now.AddHours(1)),
                    (() => user.Status = UserStatus.Suspended, () => user.Status = UserStatus.Active),
                    (() => membership.Status = TenantUserStatus.Suspended, () => membership.Status = TenantUserStatus.Active),
                    (() => tenant.Status = TenantStatus.Suspended, () => tenant.Status = TenantStatus.Active)
                };
                foreach (var (change, restore) in changes)
                {
                    change();
                    await writer.SaveChangesAsync();
                    Assert.False(await reader.IsCurrentAsync(viewer));
                    restore();
                    await writer.SaveChangesAsync();
                    Assert.True(await reader.IsCurrentAsync(viewer));
                }

                Assert.Null(trackedSession.RevokedAt);
                Assert.Equal(TenantUserStatus.Active, trackedMembership.Status);
                Assert.Equal(EntityState.Modified, request.Entry(trackedSession).State);
                Assert.Null(await writer.Sessions.AsNoTracking().Select(row => row.LastSeenAt).SingleAsync());
                Assert.Empty(await writer.AuditLogs.ToListAsync());
            });

    private sealed class Viewer(Guid userId, Guid sessionId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId { get; set; } = sessionId;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
