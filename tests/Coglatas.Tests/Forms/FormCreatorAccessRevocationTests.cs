using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Forms;

// Real repositories/query filters and authorization; EF InMemory is not PostgreSQL/RLS/HTTP acceptance.
public sealed class FormCreatorAccessRevocationTests
{
    [Theory]
    [InlineData("draft-read")]
    [InlineData("update")]
    public async Task CreatorCannotRetainFormAuthorityAfterWorkspaceMembershipIsSuspended(string operation)
    {
        var tenant = new CurrentTenant(Guid.NewGuid());
        var creator = new User { DisplayName = "Synthetic creator", Email = "creator@example.invalid", NormalizedEmail = "CREATOR@EXAMPLE.INVALID", Status = UserStatus.Active };
        var actor = new CurrentUser(creator.Id);
        var clock = new Clock();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, tenant);
        db.Tenants.Add(new Tenant(tenant.TenantId) { Name = "Synthetic", Slug = "synthetic", Status = TenantStatus.Active });
        await db.SaveChangesAsync();
        var workspace = new Workspace { TenantId = tenant.TenantId, Name = "Synthetic workspace", Slug = "workspace", CreatedByUserId = creator.Id };
        var membership = new WorkspaceMember { TenantId = tenant.TenantId, WorkspaceId = workspace.Id, UserId = creator.Id, Role = WorkspaceRole.Admin, Status = MembershipStatus.Active };
        var form = new InternalForm { TenantId = tenant.TenantId, WorkspaceId = workspace.Id, CreatedByUserId = creator.Id, Title = "Original synthetic form", Status = FormStatus.Draft };
        db.Users.Add(creator);
        db.TenantUsers.Add(new TenantUser { TenantId = tenant.TenantId, UserId = creator.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(membership);
        db.InternalForms.Add(form);
        await db.SaveChangesAsync();

        var users = new UserRepository(db);
        var workspaces = new WorkspaceRepository(db);
        var groups = new GroupRepository(db);
        var projects = new ProjectRepository(db);
        var workspaceAuthorization = new WorkspaceAuthorizationService(users, workspaces);
        var groupAuthorization = new GroupAuthorizationService(groups, workspaces, workspaceAuthorization);
        var projectAuthorization = new ProjectAuthorizationService(projects, workspaceAuthorization, groupAuthorization, groups);
        var authorization = new FormAuthorizationService(users, workspaceAuthorization, groupAuthorization, projectAuthorization, groups, projects);
        var service = new FormService(new FormRepository(db), workspaces, groups, projects, authorization,
            actor, clock, new DbAuditLogger(db, clock, actor, tenant), new NoNotifications(), new EfUnitOfWork(db));

        Assert.True(await workspaceAuthorization.CanManageWorkspace(creator.Id, workspace.Id));
        Assert.True((await service.GetAsync(form.Id)).IsSuccess);
        membership.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        // A new request's refreshed authority must also deny; isolate the creator shortcut from tracking staleness.
        db.ChangeTracker.Clear();
        Assert.True(await db.TenantUsers.AnyAsync(member => member.UserId == creator.Id && member.Status == TenantUserStatus.Active));
        Assert.False(await workspaceAuthorization.CanViewWorkspace(creator.Id, workspace.Id));
        Assert.False(await workspaceAuthorization.CanManageWorkspace(creator.Id, workspace.Id));

        if (operation == "draft-read")
        {
            var result = await service.GetAsync(form.Id);
            Assert.False(result.IsSuccess, "A suspended Workspace member must not retain draft read authority solely through CreatedByUserId.");
        }
        else
        {
            var result = await service.UpdateAsync(form.Id, new UpdateFormRequest(Title: "Unauthorized synthetic mutation"));
            var persistedTitle = await db.InternalForms.AsNoTracking().Where(item => item.Id == form.Id).Select(item => item.Title).SingleAsync();
            var auditRows = await db.AuditLogs.CountAsync();
            Assert.False(result.IsSuccess, $"A suspended creator must not update the form. Actual success={result.IsSuccess}; persistedTitle={persistedTitle}; auditRows={auditRows}.");
            Assert.Equal("Original synthetic form", persistedTitle);
            Assert.Equal(0, auditRows);
        }
    }

    private sealed class CurrentTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid TenantId => tenantId;
        public bool IsAvailable => true;
        public string TenantSlug => "synthetic";
        public bool IsPlatformScope => false;
    }

    private sealed class CurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string Email => "creator@example.invalid";
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task NotifyAsync(Guid recipientUserId, string title, string? body, string sourceType, Guid sourceId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The creator authority probe must not send notifications.");
    }
}
