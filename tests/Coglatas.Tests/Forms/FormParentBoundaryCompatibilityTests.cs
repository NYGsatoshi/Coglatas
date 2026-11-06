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

// Real repositories and authorizers with EF InMemory; PostgreSQL/HTTP evidence is separate.
public sealed partial class FormParentBoundaryCompatibilityTests
{
    [Theory]
    [InlineData("workspace")]
    [InlineData("group")]
    [InlineData("project")]
    public async Task ReadOnlyCreatorRetainsDraftAndReportsButCannotModifyFormOrQuestions(string scope)
    {
        await using var fixture = await Fixture.CreateAsync(scope);
        Assert.True((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Healthy update"))).IsSuccess);
        fixture.Membership.Role = WorkspaceRole.ReadOnly;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.True((await fixture.Service.GetAsync(fixture.Form.Id)).IsSuccess);
        Assert.True((await fixture.Service.ListResponsesAsync(fixture.Form.Id)).IsSuccess);
        Assert.True((await fixture.Service.GetSummaryAsync(fixture.Form.Id)).IsSuccess);
        Assert.True((await fixture.Service.ListUnansweredUsersAsync(fixture.Form.Id)).IsSuccess);
        var before = await fixture.StateAsync();
        Assert.False((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Denied title"))).IsSuccess);
        Assert.False((await fixture.Service.CloseAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.DeleteAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.AddQuestionAsync(fixture.Form.Id, new CreateFormQuestionRequest("Denied question", FormQuestionType.ShortText))).IsSuccess);
        Assert.False((await fixture.Service.UpdateQuestionAsync(fixture.Form.Id, fixture.Question.Id, new UpdateFormQuestionRequest(QuestionText: "Denied question edit"))).IsSuccess);
        Assert.False((await fixture.Service.DeleteQuestionAsync(fixture.Form.Id, fixture.Question.Id)).IsSuccess);
        Assert.Equal(before, await fixture.StateAsync());
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("group")]
    [InlineData("project")]
    public async Task SuspendedParentDeniesCreatorDraftResponsesSummaryAndUnansweredUsers(string scope)
    {
        await using var fixture = await Fixture.CreateAsync(scope);
        await fixture.SeedResponseAsync();
        var initial = await fixture.Service.ListResponsesAsync(fixture.Form.Id);
        Assert.True(initial.IsSuccess);
        Assert.Equal("Synthetic private answer", initial.Value!.Items.Single().Answers.Single().AnswerText);
        fixture.Membership.Status = MembershipStatus.Suspended;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.False(await fixture.WorkspaceAuthorization.CanViewWorkspace(fixture.Actor.Id, fixture.Workspace.Id));
        var before = await fixture.StateAsync();
        Assert.False((await fixture.Service.GetAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.ListQuestionsAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.ListResponsesAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.GetSummaryAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.ListUnansweredUsersAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Denied mutation"))).IsSuccess);
        Assert.Equal(before, await fixture.StateAsync());
    }

    [Fact]
    public async Task ArchivedProjectExplicitManagerRetainsHistoricalFormAndReportAccess()
    {
        await using var fixture = await Fixture.CreateAsync("project", creator: false, projectRole: ProjectRole.Manager);
        await fixture.SeedResponseAsync();
        fixture.Project.Status = ProjectStatus.Archived;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.False(await fixture.ProjectAuthorization.CanViewProject(fixture.Actor.Id, fixture.Project.Id));
        Assert.True(await fixture.ProjectAuthorization.CanManageProject(fixture.Actor.Id, fixture.Project.Id));
        Assert.True((await fixture.Service.GetAsync(fixture.Form.Id)).IsSuccess);
        var responses = await fixture.Service.ListResponsesAsync(fixture.Form.Id);
        Assert.True(responses.IsSuccess);
        Assert.Equal("Synthetic private answer", responses.Value!.Items.Single().Answers.Single().AnswerText);
        Assert.True((await fixture.Service.GetSummaryAsync(fixture.Form.Id)).IsSuccess);
        Assert.True((await fixture.Service.ListUnansweredUsersAsync(fixture.Form.Id)).IsSuccess);
        // Preserve the existing operation validator rather than inventing a new lifecycle rule.
        var edit = await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Archived edit"));
        Assert.False(edit.IsSuccess);
        Assert.Equal("Project not found.", edit.Error);
    }

    [Fact]
    public async Task RestrictedProjectCreatorCannotUseParentWorkspaceViewAfterProjectMembershipRemoval()
    {
        await using var fixture = await Fixture.CreateAsync("project");
        Assert.True((await fixture.Service.GetAsync(fixture.Form.Id)).IsSuccess);
        fixture.Db.ProjectMembers.Remove(await fixture.Db.ProjectMembers.SingleAsync());
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.True(await fixture.WorkspaceAuthorization.CanViewWorkspace(fixture.Actor.Id, fixture.Workspace.Id));
        Assert.False(await fixture.ProjectAuthorization.CanViewProject(fixture.Actor.Id, fixture.Project.Id));
        var before = await fixture.StateAsync();
        Assert.False((await fixture.Service.GetAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.ListResponsesAsync(fixture.Form.Id)).IsSuccess);
        Assert.False((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Denied restricted edit"))).IsSuccess);
        Assert.Equal(before, await fixture.StateAsync());
    }

    [Theory]
    [InlineData(SystemRole.Teacher)]
    [InlineData(SystemRole.Admin)]
    [InlineData(SystemRole.SystemAdmin)]
    public async Task ElevatedStaffRetainExistingVisibleScopeCreateAndManagement(SystemRole role)
    {
        await using var fixture = await Fixture.CreateAsync("workspace", creator: false);
        fixture.Actor.SystemRole = role;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.True((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Allowed staff edit"))).IsSuccess);
        var created = await fixture.Service.CreateAsync(new CreateFormRequest(fixture.Workspace.Id, null, null, "Allowed staff form", null));
        Assert.True(created.IsSuccess);
        Assert.Equal(2, await fixture.Db.InternalForms.CountAsync());
    }

    [Fact]
    public async Task GroupAdministratorRetainsReadOnlyDestinationGrantButLosesItWithParentRead()
    {
        await using var fixture = await Fixture.CreateAsync("workspace");
        var destination = new Workspace { TenantId = fixture.TenantId, Name = "Destination", Slug = "destination", CreatedByUserId = fixture.Actor.Id };
        var member = new WorkspaceMember { TenantId = fixture.TenantId, WorkspaceId = destination.Id, UserId = fixture.Actor.Id, Role = WorkspaceRole.ReadOnly, Status = MembershipStatus.Active };
        var group = new Group { TenantId = fixture.TenantId, WorkspaceId = destination.Id, Name = "Destination group", Slug = "destination-group", CreatedByUserId = fixture.Actor.Id };
        fixture.Db.Workspaces.Add(destination);
        fixture.Db.WorkspaceMembers.Add(member);
        fixture.Db.Groups.Add(group);
        fixture.Db.GroupMembers.Add(new GroupMember { TenantId = fixture.TenantId, GroupId = group.Id, UserId = fixture.Actor.Id, Role = GroupRole.Admin });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.True(await fixture.GroupAuthorization.CanManageGroup(fixture.Actor.Id, group.Id));
        Assert.False(await fixture.WorkspaceAuthorization.CanContributeWorkspace(fixture.Actor.Id, destination.Id));
        // Baseline native controls establish this independent management grant;
        // ReadOnly ownership alone remains denied by the separate controls.
        Assert.True((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(GroupId: group.Id))).IsSuccess);
        Assert.True((await fixture.Service.CreateAsync(new CreateFormRequest(null, group.Id, null, "Allowed destination form", null))).IsSuccess);
        Assert.Equal(2, await fixture.Db.InternalForms.CountAsync());
        Assert.Equal(group.Id, (await fixture.Db.InternalForms.AsNoTracking().SingleAsync(row => row.Id == fixture.Form.Id)).GroupId);
        var stored = await fixture.Db.WorkspaceMembers.SingleAsync(row => row.Id == member.Id);
        stored.Status = MembershipStatus.Suspended;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.True(await fixture.GroupAuthorization.CanManageGroup(fixture.Actor.Id, group.Id));
        Assert.False(await fixture.WorkspaceAuthorization.CanViewWorkspace(fixture.Actor.Id, destination.Id));
        var before = await fixture.StateAsync();
        Assert.False((await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Denied after suspension"))).IsSuccess);
        Assert.False((await fixture.Service.CreateAsync(new CreateFormRequest(null, group.Id, null, "Denied destination form", null))).IsSuccess);
        Assert.Equal(before, await fixture.StateAsync());
        Assert.Equal(2, await fixture.Db.InternalForms.CountAsync());
    }

    [Fact]
    public async Task GroupParentResolutionUsesStoredIdRatherThanAnAccessibleNavigationCopy()
    {
        await using var fixture = await Fixture.CreateAsync("group");
        fixture.Membership.Status = MembershipStatus.Suspended;
        var accessible = new Workspace { TenantId = fixture.TenantId, Name = "Accessible decoy", Slug = "accessible-decoy", CreatedByUserId = fixture.Actor.Id };
        fixture.Db.Workspaces.Add(accessible);
        fixture.Db.WorkspaceMembers.Add(new WorkspaceMember { TenantId = fixture.TenantId, WorkspaceId = accessible.Id, UserId = fixture.Actor.Id, Role = WorkspaceRole.Admin, Status = MembershipStatus.Active });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var copied = new InternalForm { TenantId = fixture.TenantId, GroupId = fixture.Group.Id, CreatedByUserId = fixture.Actor.Id, Group = new Group { WorkspaceId = accessible.Id } };
        Assert.True(await fixture.WorkspaceAuthorization.CanViewWorkspace(fixture.Actor.Id, accessible.Id));
        Assert.False(await fixture.Authorization.CanManageForm(fixture.Actor.Id, copied));
        Assert.False(await fixture.Authorization.CanModifyForm(fixture.Actor.Id, copied));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorCannotAuthorizeACopiedResourceWithMissingOrAmbiguousScope(bool ambiguous)
    {
        await using var fixture = await Fixture.CreateAsync("workspace");
        var copied = new InternalForm
        {
            TenantId = fixture.TenantId, CreatedByUserId = fixture.Actor.Id,
            WorkspaceId = ambiguous ? fixture.Workspace.Id : null,
            GroupId = ambiguous ? fixture.Group.Id : null
        };
        Assert.False(await fixture.Authorization.CanManageForm(fixture.Actor.Id, copied));
        Assert.False(await fixture.Authorization.CanModifyForm(fixture.Actor.Id, copied));
        Assert.False(await fixture.Authorization.CanCreateForm(fixture.Actor.Id, copied.WorkspaceId, copied.GroupId, copied.ProjectId));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, Guid tenantId, DbContextOptions<AppDbContext> options)
        {
            Db = db;
            Options = options;
            TenantId = tenantId;
            var users = new UserRepository(db);
            var workspaces = new WorkspaceRepository(db);
            var groups = new GroupRepository(db);
            var projects = new ProjectRepository(db);
            WorkspaceAuthorization = new WorkspaceAuthorizationService(users, workspaces);
            GroupAuthorization = new GroupAuthorizationService(groups, workspaces, WorkspaceAuthorization);
            ProjectAuthorization = new ProjectAuthorizationService(projects, WorkspaceAuthorization, GroupAuthorization, groups);
            Authorization = new FormAuthorizationService(users, WorkspaceAuthorization, GroupAuthorization, ProjectAuthorization, groups, projects, new FormRepository(db));
            var actor = new CurrentUser(Actor.Id);
            var clock = new Clock();
            Service = new FormService(new FormRepository(db), workspaces, groups, projects, Authorization, actor, clock,
                new DbAuditLogger(db, clock, actor, new CurrentTenant(tenantId)), new NoNotifications(), new EfUnitOfWork(db));
        }

        public AppDbContext Db { get; }
        public DbContextOptions<AppDbContext> Options { get; }
        public Guid TenantId { get; }
        public User Actor { get; } = new() { DisplayName = "Synthetic actor", Email = "actor@example.invalid", NormalizedEmail = "ACTOR@EXAMPLE.INVALID", Status = UserStatus.Active };
        public User Other { get; } = new() { DisplayName = "Synthetic respondent", Email = "respondent@example.invalid", NormalizedEmail = "RESPONDENT@EXAMPLE.INVALID", Status = UserStatus.Active };
        public Workspace Workspace { get; private set; } = null!;
        public WorkspaceMember Membership { get; private set; } = null!;
        public Group Group { get; private set; } = null!;
        public Project Project { get; private set; } = null!;
        public InternalForm Form { get; private set; } = null!;
        public FormQuestion Question { get; private set; } = null!;
        public WorkspaceAuthorizationService WorkspaceAuthorization { get; }
        public GroupAuthorizationService GroupAuthorization { get; }
        public ProjectAuthorizationService ProjectAuthorization { get; }
        public IFormAuthorizationService Authorization { get; }
        public FormService Service { get; }

        public static async Task<Fixture> CreateAsync(string scope, bool creator = true, ProjectRole projectRole = ProjectRole.Contributor,
            string? connectionString = null)
        {
            var tenantId = Guid.NewGuid();
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            if (connectionString is null) builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            else builder.UseNpgsql(connectionString);
            var fixture = new Fixture(new AppDbContext(builder.Options, new CurrentTenant(tenantId)), tenantId, builder.Options);
            var db = fixture.Db;
            db.Tenants.Add(new Tenant(tenantId) { Name = "Synthetic", Slug = "synthetic", Status = TenantStatus.Active });
            await db.SaveChangesAsync();
            db.Users.AddRange(fixture.Actor, fixture.Other);
            db.TenantUsers.AddRange(new[] { fixture.Actor, fixture.Other }.Select(user => new TenantUser { TenantId = tenantId, UserId = user.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active }));
            fixture.Workspace = new Workspace { TenantId = tenantId, Name = "Synthetic workspace", Slug = "workspace", CreatedByUserId = fixture.Actor.Id };
            fixture.Membership = new WorkspaceMember { TenantId = tenantId, WorkspaceId = fixture.Workspace.Id, UserId = fixture.Actor.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active };
            fixture.Group = new Group { TenantId = tenantId, WorkspaceId = fixture.Workspace.Id, Name = "Synthetic group", Slug = "group", CreatedByUserId = fixture.Actor.Id };
            fixture.Project = new Project { TenantId = tenantId, WorkspaceId = fixture.Workspace.Id, Name = "Synthetic project", Slug = "project", CreatedByUserId = fixture.Actor.Id, OwnerUserId = fixture.Actor.Id, Status = ProjectStatus.Active, Visibility = ProjectVisibility.Restricted, ActivationState = ProjectActivationState.Activated };
            if (connectionString is not null)
            {
                fixture.Project.ActivatedAtUtc = new Clock().UtcNow;
                fixture.Project.ActivationVersion = 1;
            }
            db.Workspaces.Add(fixture.Workspace);
            db.WorkspaceMembers.Add(fixture.Membership);
            db.Groups.Add(fixture.Group);
            db.GroupMembers.Add(new GroupMember { TenantId = tenantId, GroupId = fixture.Group.Id, UserId = fixture.Actor.Id, Role = GroupRole.Member });
            db.Projects.Add(fixture.Project);
            db.ProjectMembers.Add(new ProjectMember { TenantId = tenantId, ProjectId = fixture.Project.Id, UserId = fixture.Actor.Id, Role = projectRole });
            fixture.Form = new InternalForm { TenantId = tenantId, CreatedByUserId = creator ? fixture.Actor.Id : fixture.Other.Id, Title = "Original synthetic form", Status = FormStatus.Draft, WorkspaceId = scope == "workspace" ? fixture.Workspace.Id : null, GroupId = scope == "group" ? fixture.Group.Id : null, ProjectId = scope == "project" ? fixture.Project.Id : null };
            fixture.Question = new FormQuestion { TenantId = tenantId, FormId = fixture.Form.Id, QuestionText = "Synthetic question", QuestionType = FormQuestionType.ShortText };
            db.InternalForms.Add(fixture.Form);
            db.FormQuestions.Add(fixture.Question);
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task SeedResponseAsync()
        {
            var response = new FormResponse { TenantId = TenantId, FormId = Form.Id, RespondentUserId = Other.Id, SubmittedAt = DateTimeOffset.UtcNow };
            Db.FormResponses.Add(response);
            Db.FormAnswers.Add(new FormAnswer { TenantId = TenantId, FormResponseId = response.Id, FormQuestionId = Question.Id, AnswerText = "Synthetic private answer" });
            await Db.SaveChangesAsync();
        }

        public async Task<string> StateAsync()
        {
            var form = await Db.InternalForms.AsNoTracking().SingleAsync(row => row.Id == Form.Id);
            var question = await Db.FormQuestions.AsNoTracking().SingleAsync(row => row.Id == Question.Id);
            return $"{form.Title}|{form.Status}|{form.DeletedAt}|{form.WorkspaceId}|{form.GroupId}|{form.ProjectId}|{question.QuestionText}|{await Db.FormQuestions.CountAsync()}|{await Db.AuditLogs.CountAsync()}|{await Db.Notifications.CountAsync()}|{await Db.OutboxEvents.CountAsync()}";
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
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
        public string Email => "actor@example.invalid";
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
            throw new InvalidOperationException("These bounded draft and denial controls must not send notifications.");
    }
}
