using Coglatas.Application;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

public sealed class ConversationAdmissionPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationPersistsForActiveMembersOfTheSelectedWorkspace() =>
        WithScenarioAsync("active", expectedSuccess: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsARecipientFromAnotherWorkspace() =>
        WithScenarioAsync("other-workspace", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsARecipientFromAnotherTenant() =>
        WithScenarioAsync("other-tenant", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsASuspendedAccount() =>
        WithScenarioAsync("suspended-account", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsADeletedAccount() =>
        WithScenarioAsync("deleted-account", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsASuspendedWorkspaceMember() =>
        WithScenarioAsync("suspended-workspace-member", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsASuspendedTenantMember() =>
        WithScenarioAsync("suspended-tenant-member", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsAnActorOutsideTheSelectedWorkspace() =>
        WithScenarioAsync("other-workspace-actor", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericCreationKeepsTheUnsupportedGroupTypeDisabled() =>
        WithScenarioAsync("group", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelAdmissionPersistsForAnActiveWorkspaceMember() =>
        WithScenarioAsync("active", expectedSuccess: true, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelAdmissionRejectsARecipientFromAnotherTenant() =>
        WithScenarioAsync("other-tenant", expectedSuccess: false, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelAdmissionRejectsARecipientFromAnotherWorkspace() =>
        WithScenarioAsync("other-workspace", expectedSuccess: false, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelAdmissionRejectsASuspendedAccount() =>
        WithScenarioAsync("suspended-account", expectedSuccess: false, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelAdmissionRejectsASuspendedWorkspaceMember() =>
        WithScenarioAsync("suspended-workspace-member", expectedSuccess: false, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelAdmissionRejectsASuspendedTenantMember() =>
        WithScenarioAsync("suspended-tenant-member", expectedSuccess: false, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GenericDirectCreationRejectsCommittedRevocationDespiteTrackedMembership() =>
        WithScenarioAsync("tracked-revocation", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ProjectConversationCreationPreservesAuthorizedProjectParticipants() =>
        WithScenarioAsync("project-active", expectedSuccess: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ProjectConversationCreationRejectsAnUnauthorizedProjectParticipant() =>
        WithScenarioAsync("project-nonmember", expectedSuccess: false);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ProjectConversationAdmissionPreservesExistingProjectAuthorityWithoutWorkspaceMembership() =>
        WithScenarioAsync("project-external", expectedSuccess: true, addMember: true);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ProjectConversationAdmissionRejectsAnUnauthorizedProjectParticipant() =>
        WithScenarioAsync("project-nonmember", expectedSuccess: false, addMember: true);

    private static async Task WithScenarioAsync(string scenario, bool expectedSuccess, bool addMember = false)
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var actor = NewUser("actor");
                var recipient = NewUser("recipient");
                if (scenario == "project-external") recipient.SystemRole = SystemRole.PlatformAdmin;
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = database,
                        ["FileStorage:Provider"] = "LocalFileSystem",
                        ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "coglatas-pilot-admission", Guid.NewGuid().ToString("N"))
                    }).Build();
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddApplication().AddInfrastructure(configuration).AddWebServices(configuration);
                services.AddSingleton<ICurrentUser>(new Actor(actor.Id));
                await using var provider = services.BuildServiceProvider(validateScopes: true);
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantService>();
                currentTenant.SetPlatformScope();
                var tenant = new Tenant { Name = "Admission Tenant", DisplayName = "Admission Tenant", Slug = $"admission-{Guid.NewGuid():N}" };
                var foreignTenant = new Tenant { Name = "Foreign Tenant", DisplayName = "Foreign Tenant", Slug = $"foreign-{Guid.NewGuid():N}" };
                db.Tenants.AddRange(tenant, foreignTenant);
                db.Users.AddRange(actor, recipient);
                await db.SaveChangesAsync();

                currentTenant.SetTenant(tenant.Id, tenant.Slug);
                var workspace = new Workspace { Name = "Admission Workspace", Slug = $"workspace-{Guid.NewGuid():N}", CreatedByUserId = actor.Id };
                var otherWorkspace = new Workspace { Name = "Other Workspace", Slug = $"other-{Guid.NewGuid():N}", CreatedByUserId = actor.Id };
                db.Workspaces.AddRange(workspace, otherWorkspace);
                db.TenantUsers.Add(new TenantUser { UserId = actor.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
                if (scenario != "other-tenant")
                    db.TenantUsers.Add(new TenantUser
                    {
                        UserId = recipient.Id,
                        Role = TenantUserRole.Member,
                        Status = scenario == "suspended-tenant-member" ? TenantUserStatus.Suspended : TenantUserStatus.Active
                    });
                db.WorkspaceMembers.Add(new WorkspaceMember
                {
                    WorkspaceId = scenario == "other-workspace-actor" ? otherWorkspace.Id : workspace.Id,
                    UserId = actor.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active
                });
                if (scenario is not ("other-tenant" or "project-external"))
                    db.WorkspaceMembers.Add(new WorkspaceMember
                    {
                        WorkspaceId = scenario == "other-workspace" ? otherWorkspace.Id : workspace.Id,
                        UserId = recipient.Id, Role = WorkspaceRole.Member,
                        Status = scenario == "suspended-workspace-member" ? MembershipStatus.Suspended : MembershipStatus.Active
                    });
                recipient.Status = scenario == "suspended-account" ? UserStatus.Suspended : UserStatus.Active;
                if (scenario == "deleted-account") recipient.MarkDeleted(DateTimeOffset.UtcNow);
                Project? project = null;
                if (scenario.StartsWith("project-", StringComparison.Ordinal))
                {
                    project = new Project
                    {
                        WorkspaceId = workspace.Id, OwnerUserId = actor.Id, CreatedByUserId = actor.Id,
                        Name = "Admission project", Slug = $"admission-project-{Guid.NewGuid():N}",
                        Status = ProjectStatus.Active, Visibility = ProjectVisibility.MembersOnly,
                        ActivationState = ProjectActivationState.Activated,
                        ActivatedAtUtc = DateTimeOffset.UtcNow, ActivationVersion = 1
                    };
                    db.Projects.Add(project);
                    db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = actor.Id, Role = ProjectRole.Owner });
                    if (scenario != "project-nonmember")
                        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = recipient.Id, Role = ProjectRole.Contributor });
                }
                await db.SaveChangesAsync();
                if (scenario == "other-tenant")
                {
                    currentTenant.SetTenant(foreignTenant.Id, foreignTenant.Slug);
                    db.TenantUsers.Add(new TenantUser { UserId = recipient.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
                    await db.SaveChangesAsync();
                    currentTenant.SetTenant(tenant.Id, tenant.Slug);
                }
                db.ChangeTracker.Clear();

                if (scenario == "tracked-revocation")
                {
                    _ = await db.WorkspaceMembers.SingleAsync(member => member.UserId == recipient.Id);
                    await db.WorkspaceMembers.Where(member => member.UserId == recipient.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Suspended));
                }

                var service = scope.ServiceProvider.GetRequiredService<IConversationService>();
                if (addMember)
                {
                    var channel = new Conversation
                    {
                        WorkspaceId = workspace.Id, ProjectId = project?.Id,
                        Type = project is null ? ConversationType.WorkspaceChannel : ConversationType.ProjectChannel,
                        Title = "Admission channel", CreatedByUserId = actor.Id
                    };
                    db.Conversations.Add(channel);
                    db.ConversationMembers.Add(new ConversationMember
                    {
                        ConversationId = channel.Id, UserId = actor.Id,
                        Role = ConversationMemberRole.Admin, CanRead = true, CanPost = true,
                        CanManageMembers = true, CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                    var addition = await service.AddMemberAsync(channel.Id, new AddConversationMemberRequest(recipient.Id));
                    Assert.Equal(expectedSuccess, addition.IsSuccess);
                    db.ChangeTracker.Clear();
                    Assert.Equal(1, await db.Conversations.CountAsync());
                    Assert.Equal(expectedSuccess ? 2 : 1, await db.ConversationMembers.CountAsync());
                    Assert.Equal(expectedSuccess ? 1 : 0, await db.AuditLogs.CountAsync(log => log.Action == "ConversationMemberAdded"));
                    return;
                }

                var result = await service.CreateAsync(new CreateConversationRequest(
                    scenario == "group" ? ConversationType.Group : project is null ? ConversationType.DirectMessage : ConversationType.ProjectChannel,
                    null, [recipient.Id], workspace.Id, project?.Id));

                Assert.Equal(expectedSuccess, result.IsSuccess);
                db.ChangeTracker.Clear();
                Assert.Equal(expectedSuccess ? 1 : 0, await db.Conversations.CountAsync());
                Assert.Equal(expectedSuccess ? 2 : 0, await db.ConversationMembers.CountAsync());
                Assert.Equal(expectedSuccess ? 1 : 0, await db.AuditLogs.CountAsync(log => log.Action == "ConversationCreated"));
                if (expectedSuccess)
                {
                    var persisted = await db.Conversations.AsNoTracking().SingleAsync();
                    Assert.Equal(workspace.Id, persisted.WorkspaceId);
                    Assert.Equal(project is null ? ConversationType.DirectMessage : ConversationType.ProjectChannel, persisted.Type);
                    Assert.Equal(project?.Id, persisted.ProjectId);
                    Assert.Equal(result.Value!.Id, persisted.Id);
                }
            });
    }

    private static User NewUser(string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.test";
        return new User
        {
            DisplayName = $"Admission {label}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Status = UserStatus.Active
        };
    }

    private sealed class Actor(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
}
