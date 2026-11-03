using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.Projects;

// Real Application authorization, repositories, audit and UnitOfWork over
// InMemory. Publisher records represent staging calls, never delivery. These
// tests do not establish PostgreSQL constraints, locks or Outbox atomicity.
internal sealed class ProjectServicePersistenceContext : IAsyncDisposable
{
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly CancellationTokenSource _cancellation = new();
    public AppDbContext Db { get; }
    public CurrentTenantService Tenant { get; }
    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public Guid TargetId { get; }
    public Guid WorkspaceId { get; }
    public Guid ProjectId { get; }
    public Guid? GroupId { get; }
    public TestActor Actor { get; }
    public FixedClock Clock { get; } = new();
    public SaveFault Fault { get; }
    public RecordingProjectInvalidations Invalidations { get; } = new();
    public RecordingAuthorizationChanges AuthorizationChanges { get; } = new();
    public ProjectAuthorizationService Authorization { get; }
    public ProjectService Service { get; }
    public CancellationToken Token => _cancellation.Token;

    private ProjectServicePersistenceContext(DbContextOptions<AppDbContext> options, AppDbContext db,
        CurrentTenantService tenant, SaveFault fault, Guid tenantId, Guid actorId, Guid targetId,
        Guid workspaceId, Guid projectId, Guid? groupId)
    {
        _options = options;
        Db = db;
        Tenant = tenant;
        Fault = fault;
        TenantId = tenantId;
        ActorId = actorId;
        TargetId = targetId;
        WorkspaceId = workspaceId;
        ProjectId = projectId;
        GroupId = groupId;
        Actor = new TestActor(actorId);
        var projects = new ProjectRepository(db);
        var workspaces = new WorkspaceRepository(db);
        var groups = new GroupRepository(db);
        var users = new UserRepository(db);
        var workspaceAuthorization = new WorkspaceAuthorizationService(users, workspaces);
        var groupAuthorization = new GroupAuthorizationService(groups, workspaces, workspaceAuthorization);
        Authorization = new ProjectAuthorizationService(projects, workspaceAuthorization, groupAuthorization, groups);
        var unitOfWork = new EfUnitOfWork(db);
        Service = new ProjectService(projects, workspaces, groups, users,
            Authorization, Authorization, Authorization, Actor, Clock,
            new DbAuditLogger(db, Clock, Actor, tenant), Invalidations, AuthorizationChanges, unitOfWork, unitOfWork);
    }

    public static async Task<ProjectServicePersistenceContext> CreateAsync(bool grouped = false)
    {
        var tenant = new CurrentTenantService();
        var fault = new SaveFault();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"project-service-persistence-{Guid.NewGuid():N}")
            .AddInterceptors(new ProjectGovernanceSaveChangesInterceptor(), fault).Options;
        var db = new AppDbContext(options, tenant);
        var ownerTenant = new Tenant { Name = "Synthetic tenant", DisplayName = "Synthetic tenant", Slug = "project-service" };
        tenant.SetPlatformScope();
        db.Tenants.Add(ownerTenant);
        await db.SaveChangesAsync();
        tenant.SetTenant(ownerTenant.Id, "project-service");
        var actor = new User { DisplayName = "Owner", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var target = new User { DisplayName = "Target", Email = "target@example.test", NormalizedEmail = "TARGET@EXAMPLE.TEST" };
        var workspace = new Workspace { TenantId = ownerTenant.Id, Name = "Workspace", Slug = "workspace", CreatedByUserId = actor.Id };
        var group = grouped ? new Group { TenantId = ownerTenant.Id, WorkspaceId = workspace.Id, Name = "Group", Slug = "group", CreatedByUserId = actor.Id } : null;
        var project = new Project
        {
            TenantId = ownerTenant.Id, WorkspaceId = workspace.Id, GroupId = group?.Id, Name = "Project", Slug = "project",
            OwnerUserId = actor.Id, CreatedByUserId = actor.Id, Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.MembersOnly, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = new DateTimeOffset(2026, 11, 3, 16, 0, 0, TimeSpan.Zero), ActivationVersion = 1, VersionNo = 1
        };
        db.AddRange(actor, target, workspace, project);
        foreach (var user in new[] { actor, target })
        {
            db.TenantUsers.Add(new TenantUser { TenantId = ownerTenant.Id, UserId = user.Id, Status = TenantUserStatus.Active });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                TenantId = ownerTenant.Id, WorkspaceId = workspace.Id, UserId = user.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
        }
        if (group is not null)
        {
            db.Groups.Add(group);
            db.GroupMembers.Add(new GroupMember { TenantId = ownerTenant.Id, GroupId = group.Id, UserId = actor.Id, Role = GroupRole.Member });
        }
        db.ProjectMembers.Add(new ProjectMember
        {
            TenantId = ownerTenant.Id, ProjectId = project.Id, UserId = actor.Id,
            Role = ProjectRole.Owner, JoinedAt = project.ActivatedAtUtc.Value
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new ProjectServicePersistenceContext(options, db, tenant, fault,
            ownerTenant.Id, actor.Id, target.Id, workspace.Id, project.Id, group?.Id);
    }

    public async Task AddTargetMemberAsync(ProjectRole role = ProjectRole.Contributor)
    {
        Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = ProjectId, UserId = TargetId, Role = role, JoinedAt = Clock.UtcNow
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async Task<Result> MutateMembershipAsync(string operation, Guid? projectId = null, Guid? userId = null)
    {
        var selectedProject = projectId ?? ProjectId;
        var selectedUser = userId ?? TargetId;
        switch (operation)
        {
            case "add":
                var added = await Service.AddMemberAsync(selectedProject, new AddProjectMemberRequest(selectedUser, ProjectRole.Reviewer), Token);
                return new Result(added.IsSuccess, added.Error, added.ErrorDetail);
            case "update":
                var updated = await Service.UpdateMemberAsync(selectedProject, selectedUser, new UpdateProjectMemberRequest(ProjectRole.Reviewer), Token);
                return new Result(updated.IsSuccess, updated.Error, updated.ErrorDetail);
            case "remove":
                return await Service.RemoveMemberAsync(selectedProject, selectedUser, Token);
            default: throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown membership operation.");
        }
    }

    public async Task<string> ReadPersistedMembershipsAsync()
    {
        var platform = new CurrentTenantService();
        platform.SetPlatformScope();
        await using var inspection = new AppDbContext(_options, platform);
        var rows = await inspection.ProjectMembers.AsNoTracking()
            .OrderBy(member => member.TenantId).ThenBy(member => member.ProjectId).ThenBy(member => member.UserId).ToListAsync();
        return string.Join(';', rows.Select(member => $"{member.TenantId:D}/{member.ProjectId:D}/{member.UserId:D}/{member.Role}/{member.JoinedAt:O}"));
    }

    public async Task<Guid> SeedForeignProjectAsync()
    {
        Tenant.SetPlatformScope();
        var foreign = new Tenant { Name = "Foreign tenant", DisplayName = "Foreign tenant", Slug = "foreign-project" };
        var workspace = new Workspace { TenantId = foreign.Id, Name = "Foreign workspace", Slug = "foreign", CreatedByUserId = ActorId };
        var project = new Project
        {
            TenantId = foreign.Id, WorkspaceId = workspace.Id, Name = "Foreign project", Slug = "foreign",
            OwnerUserId = ActorId, CreatedByUserId = ActorId, Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.MembersOnly, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = Clock.UtcNow, ActivationVersion = 1, VersionNo = 1
        };
        Db.AddRange(foreign, workspace, project);
        foreach (var userId in new[] { ActorId, TargetId })
        {
            Db.TenantUsers.Add(new TenantUser { TenantId = foreign.Id, UserId = userId, Status = TenantUserStatus.Active });
            Db.WorkspaceMembers.Add(new WorkspaceMember { TenantId = foreign.Id, WorkspaceId = workspace.Id, UserId = userId, Status = MembershipStatus.Active, Role = WorkspaceRole.Member });
            Db.ProjectMembers.Add(new ProjectMember { TenantId = foreign.Id, ProjectId = project.Id, UserId = userId, Role = userId == ActorId ? ProjectRole.Owner : ProjectRole.Contributor, JoinedAt = Clock.UtcNow });
        }
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        Tenant.SetTenant(TenantId, "project-service");
        return project.Id;
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Dispose();
        await Db.DisposeAsync();
    }

    internal sealed class TestActor(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; set; } = userId;
        public Guid? SessionId => null;
        public string Email => "owner@example.test";
        public SystemRole? SystemRole => Domain.Enums.SystemRole.NormalUser;
        public bool IsAuthenticated { get; set; } = true;
    }

    internal sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 16, 0, 0, TimeSpan.Zero);
    }

    internal sealed class SaveFault : SaveChangesInterceptor
    {
        public bool FailNextSave { get; set; }
        public List<CancellationToken> FailedTokens { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!FailNextSave) return base.SavingChangesAsync(eventData, result, cancellationToken);
            FailNextSave = false;
            FailedTokens.Add(cancellationToken);
            throw new DbUpdateConcurrencyException("Synthetic Project membership concurrency failure.");
        }
    }

    internal sealed class RecordingProjectInvalidations : IBusinessInvalidationPublisher
    {
        public List<ProjectChange> Calls { get; } = [];
        public Task ProjectChangedAsync(Project project, Guid actorUserId, string change, CancellationToken cancellationToken = default)
        {
            Calls.Add(new ProjectChange(project.Id, project.TenantId, actorUserId, change, cancellationToken));
            return Task.CompletedTask;
        }
        public Task TaskChangedAsync(TaskItem task, Guid actorUserId, string change, IEnumerable<string>? changedFields = null,
            IEnumerable<Guid>? affectedUserIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AnnouncementChangedAsync(Announcement announcement, Guid actorUserId, string change,
            IEnumerable<Guid> audienceUserIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task FileChangedAsync(FileObject fileObject, Attachment attachment, Guid actorUserId, string change,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed record ProjectChange(Guid ProjectId, Guid TenantId, Guid ActorId, string Change, CancellationToken Token);

    internal sealed class RecordingAuthorizationChanges : IAuthorizationStateChangePublisher
    {
        public List<AuthorizationChange> Calls { get; } = [];
        public Task PublishAsync(Guid tenantId, Guid affectedUserId, string scopeType, Guid? scopeId, string change,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new AuthorizationChange(tenantId, affectedUserId, scopeType, scopeId, change, cancellationToken));
            return Task.CompletedTask;
        }
    }

    internal sealed record AuthorizationChange(Guid TenantId, Guid UserId, string ScopeType, Guid? ScopeId, string Change, CancellationToken Token);
}
