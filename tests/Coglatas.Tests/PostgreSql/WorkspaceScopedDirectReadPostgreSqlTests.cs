using System.Text;
using Coglatas.Application;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Communication;
using Coglatas.Application.Files;
using Coglatas.Application.Messaging;
using Coglatas.Application.Notifications;
using Coglatas.Application.Projects;
using Coglatas.Application.Search;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class WorkspaceScopedDirectReadPostgreSqlTests
{
    [PostgreSqlFact]
    public Task CommittedWorkspaceRevocationOverridesTrackedDirectAndThreadAccess() => WithScenarioAsync(async scenario =>
    {
        var authorization = scenario.Get<IConversationAuthorizationService>();
        var workspaceAuthorization = scenario.Get<IWorkspaceAuthorizationService>();
        Assert.True(await workspaceAuthorization.CanViewWorkspace(scenario.Actor.Id, scenario.Workspace.Id));
        await AssertReadabilityAsync(scenario, expected: true);
        await scenario.RevokeAsync();
        // The same scope still contains the previously loaded active membership.
        // Persisted readability must remain authoritative even when the outer guard is warm.
        Assert.Equal(MembershipStatus.Active, scenario.Db.ChangeTracker.Entries<WorkspaceMember>()
            .Single(entry => entry.Entity.UserId == scenario.Actor.Id).Entity.Status);
        await AssertReadabilityAsync(scenario, expected: false);
        Assert.False(await authorization.CanSendMessage(scenario.Actor.Id, scenario.Direct.Id));
        Assert.False(await authorization.CanCreateThread(scenario.Actor.Id, scenario.Direct.Id));
    });

    [PostgreSqlFact]
    public Task SystemRoleDoesNotReplaceCurrentDirectWorkspaceMembership() => WithScenarioAsync(async scenario =>
    {
        scenario.Actor.SystemRole = SystemRole.PlatformAdmin;
        await scenario.Db.SaveChangesAsync();
        await AssertReadabilityAsync(scenario, expected: true);
        await scenario.RevokeAsync();
        Assert.True(await scenario.Get<IWorkspaceAuthorizationService>().CanViewWorkspace(scenario.Actor.Id, scenario.Workspace.Id));
        await AssertReadabilityAsync(scenario, expected: false);
    });

    [PostgreSqlFact]
    public Task ArchivedWorkspaceHistoryRequiresCurrentMembership() => WithScenarioAsync(async scenario =>
    {
        scenario.Workspace.Status = WorkspaceStatus.Archived;
        await scenario.Db.SaveChangesAsync();
        await AssertReadabilityAsync(scenario, expected: true);
        Assert.True((await scenario.Get<IConversationService>().ListMessagesAsync(scenario.Direct.Id, new())).IsSuccess);
        await scenario.RevokeAsync();
        await AssertReadabilityAsync(scenario, expected: false);
    });

    [PostgreSqlFact]
    public Task DeletedWorkspaceAndMissingMembershipFailClosed() => WithScenarioAsync(async scenario =>
    {
        await AssertReadabilityAsync(scenario, expected: true);
        await scenario.Db.WorkspaceMembers.Where(member => member.UserId == scenario.Actor.Id).ExecuteDeleteAsync();
        await AssertReadabilityAsync(scenario, expected: false);
        scenario.Db.ChangeTracker.Clear();
        scenario.Db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = scenario.Workspace.Id, UserId = scenario.Actor.Id,
            Role = WorkspaceRole.Member, Status = MembershipStatus.Active
        });
        await scenario.Db.SaveChangesAsync();
        await AssertReadabilityAsync(scenario, expected: true);
        await scenario.Db.Workspaces.Where(workspace => workspace.Id == scenario.Workspace.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(workspace => workspace.Status, WorkspaceStatus.Deleted));
        await AssertReadabilityAsync(scenario, expected: false);
    });

    [PostgreSqlFact]
    public Task ProjectBoundDirectKeepsExistingProjectAuthorityWithoutWorkspaceMembership() => WithScenarioAsync(async scenario =>
    {
        scenario.Actor.SystemRole = SystemRole.PlatformAdmin;
        var project = new Project
        {
            WorkspaceId = scenario.Workspace.Id, OwnerUserId = scenario.Actor.Id, CreatedByUserId = scenario.Actor.Id,
            Name = "Read control project", Slug = $"project-{Guid.NewGuid():N}", Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.MembersOnly, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = DateTimeOffset.UtcNow, ActivationVersion = 1
        };
        scenario.Db.Projects.Add(project);
        scenario.Db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = scenario.Actor.Id, Role = ProjectRole.Owner });
        scenario.Direct.ProjectId = project.Id;
        scenario.Thread.ProjectId = project.Id;
        await scenario.Db.SaveChangesAsync();
        await scenario.RevokeAsync();
        Assert.True(await scenario.Get<IProjectAuthorizationService>().CanViewProject(scenario.Actor.Id, project.Id));
        await AssertReadabilityAsync(scenario, expected: true);
        var withoutWorkspaceAuthorizer = new ConversationAuthorizationService(
            scenario.Get<IMessagingRepository>(), scenario.Get<IProjectAuthorizationService>());
        Assert.True(await withoutWorkspaceAuthorizer.CanViewConversation(scenario.Actor.Id, scenario.Direct.Id));
    });

    [PostgreSqlFact]
    public Task WorkspaceScopedDirectFailsClosedWithoutWorkspaceAuthorizer() => WithScenarioAsync(async scenario =>
    {
        Assert.NotNull(scenario.Get<IWorkspaceAuthorizationService>());
        Assert.True(await scenario.Get<IConversationAuthorizationService>().CanViewConversation(scenario.Actor.Id, scenario.Direct.Id));
        var withoutWorkspaceAuthorizer = new ConversationAuthorizationService(
            scenario.Get<IMessagingRepository>(), scenario.Get<IProjectAuthorizationService>());
        Assert.False(await withoutWorkspaceAuthorizer.CanViewConversation(scenario.Actor.Id, scenario.Direct.Id));
        // A legacy Thread uses the authoritative repository ancestry relation.
        Assert.True(await withoutWorkspaceAuthorizer.CanViewConversation(scenario.Actor.Id, scenario.Thread.Id));
        await scenario.RevokeAsync();
        Assert.False(await withoutWorkspaceAuthorizer.CanViewConversation(scenario.Actor.Id, scenario.Thread.Id));
    });

    [PostgreSqlFact]
    public Task RevocationRemovesDerivedProjectionsAndPreviouslyIssuedFileGrantAccess() => WithScenarioAsync(async scenario =>
    {
        var files = scenario.Get<IFileService>();
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("Synthetic Direct attachment"));
        var upload = await files.UploadAsync(new(AttachmentOwnerType.Message, scenario.Message.Id, "control.txt", "text/plain", bytes.Length, bytes));
        Assert.True(upload.IsSuccess, upload.Error);
        var attachment = upload.Value!;
        var grantResult = await files.RequestDownloadGrantAsync(attachment.Id, new("read-control"));
        Assert.True(grantResult.IsSuccess, grantResult.Error);
        var grant = grantResult.Value!;
        var download = await files.DownloadWithGrantAsync(grant.FileDownloadGrantId, grant.Token);
        Assert.True(download.IsSuccess, download.Error);
        await download.Value!.Content.DisposeAsync();
        await AssertDerivedAsync(scenario, expected: true);
        var beforeMessages = await scenario.Db.Messages.CountAsync();
        var beforeFiles = await scenario.Db.Attachments.CountAsync();
        await scenario.RevokeAsync();
        await AssertDerivedAsync(scenario, expected: false);
        Assert.False((await files.GetAsync(attachment.Id)).IsSuccess);
        Assert.False((await files.DownloadAsync(attachment.Id)).IsSuccess);
        Assert.False((await files.RequestDownloadGrantAsync(attachment.Id, new())).IsSuccess);
        Assert.False((await files.DownloadWithGrantAsync(grant.FileDownloadGrantId, grant.Token)).IsSuccess);
        Assert.False((await scenario.Get<IConversationService>().SendMessageAsync(scenario.Direct.Id, new("Denied synthetic body", []))).IsSuccess);
        Assert.Equal(beforeMessages, await scenario.Db.Messages.CountAsync());
        Assert.Equal(beforeFiles, await scenario.Db.Attachments.CountAsync());
        Assert.Single(await scenario.Db.MessageFollowUps.ToListAsync());
        Assert.Single(await scenario.Db.Notifications.ToListAsync());
    });

    private static async Task AssertReadabilityAsync(Scenario scenario, bool expected)
    {
        var ids = new[] { scenario.Direct.Id, scenario.Thread.Id };
        var repository = scenario.Get<IMessagingRepository>();
        var query = repository.QueryReadableConversationIds(scenario.Actor.Id);
        Assert.NotNull(query);
        Assert.Equal(expected ? 2 : 0, await query.Where(id => ids.Contains(id)).CountAsync());
        Assert.Equal(expected ? 2 : 0, (await repository.FilterReadableConversationIdsAsync(scenario.Actor.Id, ids)).Count);
        foreach (var id in ids)
            Assert.Equal(expected, await scenario.Get<IConversationAuthorizationService>().CanViewConversation(scenario.Actor.Id, id));
    }

    private static async Task AssertDerivedAsync(Scenario scenario, bool expected)
    {
        var conversations = scenario.Get<IConversationService>();
        Assert.Equal(expected, (await conversations.GetAsync(scenario.Direct.Id)).IsSuccess);
        Assert.Equal(expected, (await conversations.ListMessagesAsync(scenario.Direct.Id, new())).IsSuccess);
        var inbox = await conversations.ListAsync(new());
        Assert.True(inbox.IsSuccess, inbox.Error);
        Assert.Equal(expected ? 2 : 0, inbox.Value!.TotalCount);
        Assert.Equal(expected ? 2 : 0, inbox.Value.Counts.All);
        Assert.Equal(expected ? 1 : 0, inbox.Value.Counts.Unread);
        var saved = await scenario.Get<IMessageFollowUpService>().ListAsync(new());
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(expected ? 1 : 0, saved.Value!.TotalCount);
        var search = await scenario.Get<ISearchService>().SearchAsync(new(Q: "Synthetic", Type: SearchResultType.Message));
        Assert.True(search.IsSuccess, search.Error);
        Assert.Equal(expected ? 1 : 0, search.Value!.TotalCount);
        var authors = await scenario.Get<ISearchService>().SearchMessageAuthorsAsync(new(SelectedUserId: scenario.Sender.Id));
        Assert.True(authors.IsSuccess, authors.Error);
        Assert.Equal(expected ? 1 : 0, authors.Value!.Items.Count);
        var unread = await scenario.Get<ICommunicationPollingService>().GetUnreadCountsAsync(new());
        Assert.True(unread.IsSuccess, unread.Error);
        Assert.Equal(expected ? 2 : 0, unread.Value!.TotalCount);
        var resolution = await scenario.Get<INotificationTargetResolver>().ResolveAsync(scenario.Tenant.Id, scenario.Actor.Id, scenario.Notification.Id);
        Assert.True(resolution.IsOwned);
        Assert.Equal(expected, resolution.IsAvailable);
        Assert.Equal(expected, resolution.Route is not null);
        var notifications = scenario.Get<INotificationService>();
        Assert.Equal(expected ? 1 : 0, (await notifications.ListAsync(scenario.Actor.Id, 1, 20)).TotalCount);
        Assert.Equal(expected ? 1 : 0, await notifications.GetUnreadCountAsync(scenario.Actor.Id));
    }

    private static Task WithScenarioAsync(Func<Scenario, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            var actor = NewUser("Reader");
            var sender = NewUser("Sender");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database,
                ["FileStorage:Provider"] = "LocalFileSystem",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "coglatas-direct-read", Guid.NewGuid().ToString("N")),
                ["FileStorage:AllowedExtensions:0"] = ".txt", ["FileStorage:AllowedContentTypes:0"] = "text/plain"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication().AddInfrastructure(configuration).AddWebServices(configuration);
            services.AddSingleton<ICurrentUser>(new ReaderIdentity(actor.Id));
            await using var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantScope = scope.ServiceProvider.GetRequiredService<CurrentTenantService>();
            tenantScope.SetPlatformScope();
            var tenant = new Tenant { Name = "Read control", DisplayName = "Read control", Slug = $"read-{Guid.NewGuid():N}" };
            db.Tenants.Add(tenant);
            db.Users.AddRange(actor, sender);
            await db.SaveChangesAsync();
            tenantScope.SetTenant(tenant.Id, tenant.Slug);
            var workspace = new Workspace { Name = "Read control", Slug = $"workspace-{Guid.NewGuid():N}", CreatedByUserId = actor.Id };
            db.Workspaces.Add(workspace);
            foreach (var user in new[] { actor, sender })
            {
                db.TenantUsers.Add(new TenantUser { UserId = user.Id, Status = TenantUserStatus.Active });
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active });
            }
            var direct = new Conversation { WorkspaceId = workspace.Id, Type = ConversationType.DirectMessage, Title = "Read control", CreatedByUserId = actor.Id };
            var thread = new Conversation { WorkspaceId = workspace.Id, Type = ConversationType.Thread, Title = "Legacy thread control", CreatedByUserId = actor.Id, ParentConversationId = direct.Id, RootConversationId = direct.Id };
            db.Conversations.AddRange(direct, thread);
            foreach (var conversation in new[] { direct, thread })
                foreach (var user in new[] { actor, sender })
                    db.ConversationMembers.Add(new ConversationMember { ConversationId = conversation.Id, UserId = user.Id, Role = ConversationMemberRole.Member, CanRead = true, CanPost = true, CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow });
            var message = new Message { WorkspaceId = workspace.Id, ConversationId = direct.Id, AuthorUserId = sender.Id, Body = "Synthetic searchable read control" };
            var notification = new Notification { UserId = actor.Id, NotificationType = NotificationType.Mention, Title = "Read control", Body = "Synthetic notification", RelatedEntityType = "Message", RelatedEntityId = message.Id };
            db.Messages.Add(message);
            db.MessageFollowUps.Add(new MessageFollowUp { UserId = actor.Id, MessageId = message.Id });
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            await test(new(scope.ServiceProvider, db, tenant, workspace, actor, sender, direct, thread, message, notification));
        });

    private static User NewUser(string name)
    {
        var email = $"read-{Guid.NewGuid():N}@example.test";
        return new() { Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = name, Status = UserStatus.Active, PasswordHash = "unused-synthetic-hash" };
    }

    private sealed record Scenario(IServiceProvider Services, AppDbContext Db, Tenant Tenant, Workspace Workspace,
        User Actor, User Sender, Conversation Direct, Conversation Thread, Message Message, Notification Notification)
    {
        public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
        public Task<int> RevokeAsync() => Db.WorkspaceMembers.Where(member => member.WorkspaceId == Workspace.Id && member.UserId == Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Suspended));
    }

    private sealed class ReaderIdentity(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
}
