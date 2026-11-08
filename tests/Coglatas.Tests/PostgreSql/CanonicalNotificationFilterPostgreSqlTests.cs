using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class CanonicalNotificationFilterPostgreSqlTests
{
    [PostgreSqlFact]
    public Task BulkTaskNotificationFilteringUsesCurrentAuthorityAndPreservesStoredState() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), AssertCurrentFilteringAsync);

    /// <summary>Checks actual SQL selection and committed revocation in an isolated synthetic database.</summary>
    /// <param name="connectionString">The migrated temporary PostgreSQL connection owned by the test harness.</param>
    private static async Task AssertCurrentFilteringAsync(string connectionString)
    {
        await using var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
        var now = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var local = new Tenant(Guid.NewGuid()) { Name = "Notification local", Slug = "notification-local", Status = TenantStatus.Active };
        var foreign = new Tenant(Guid.NewGuid()) { Name = "Notification foreign", Slug = "notification-foreign", Status = TenantStatus.Active };
        var users = Enumerable.Range(0, 3).Select(index => new User
        {
            DisplayName = $"Notification user {index}", Email = $"notification-{index}@example.invalid",
            NormalizedEmail = $"NOTIFICATION-{index}@EXAMPLE.INVALID", Status = UserStatus.Active
        }).ToArray();
        var membership = new TenantUser
        {
            TenantId = local.Id, UserId = users[1].Id, Status = TenantUserStatus.Active, JoinedAt = now
        };
        var workspaces = new[] { local, foreign }.Select((tenant, index) => new Workspace
        {
            TenantId = tenant.Id, Name = $"Notification workspace {index}", Slug = $"notification-workspace-{index}",
            Status = WorkspaceStatus.Active, CreatedByUserId = users[0].Id
        }).ToArray();
        var projects = workspaces.Select((workspace, index) => new Project
        {
            TenantId = workspace.TenantId, WorkspaceId = workspace.Id, Name = $"Notification project {index}",
            Slug = $"notification-project-{index}", OwnerUserId = users[0].Id, CreatedByUserId = users[0].Id,
            Status = ProjectStatus.Active, Visibility = ProjectVisibility.MembersOnly,
            ActivationState = ProjectActivationState.Activated, ActivatedAtUtc = now, ActivationVersion = 1, VersionNo = 1
        }).ToArray();
        var tasks = new[] { projects[0], projects[0], projects[1] }.Select((project, index) => new TaskItem
        {
            TenantId = project.TenantId, WorkspaceId = project.WorkspaceId, ProjectId = project.Id,
            Title = $"Notification task {index}", CreatedByUserId = users[0].Id, SortKey = (index + 1) * 1024,
            DeletedAt = index == 1 ? now : null
        }).ToArray();
        Notification Notice(Guid tenant, Guid user, string kind, Guid? target) => new()
        {
            TenantId = tenant, UserId = user, RelatedEntityType = kind, RelatedEntityId = target,
            Title = "Synthetic notification", Body = "Synthetic notification body", StateVersion = 7, CreatedAt = now
        };
        var notifications = new[]
        {
            Notice(local.Id, users[1].Id, "TaskItem", tasks[0].Id),
            Notice(local.Id, users[1].Id, "Task", tasks[0].Id),
            Notice(local.Id, users[1].Id, "Task", null),
            Notice(local.Id, users[1].Id, "Task", Guid.NewGuid()),
            Notice(local.Id, users[1].Id, "Task", tasks[1].Id),
            Notice(local.Id, users[1].Id, "Task", tasks[2].Id),
            Notice(local.Id, users[1].Id, "Task", tasks[0].Id),
            Notice(local.Id, users[2].Id, "Task", tasks[0].Id),
            Notice(foreign.Id, users[1].Id, "Task", tasks[2].Id),
            Notice(local.Id, users[1].Id, "UnknownSyntheticTarget", tasks[0].Id)
        };
        notifications[6].DeletedAt = now;
        seed.Tenants.AddRange(local, foreign);
        seed.Users.AddRange(users);
        seed.TenantUsers.Add(membership);
        seed.Workspaces.AddRange(workspaces);
        seed.WorkspaceMembers.Add(new WorkspaceMember
        {
            TenantId = local.Id, WorkspaceId = workspaces[0].Id, UserId = users[1].Id,
            Role = WorkspaceRole.Member, Status = MembershipStatus.Active, JoinedAt = now
        });
        seed.Projects.AddRange(projects);
        seed.ProjectMembers.Add(new ProjectMember
        {
            TenantId = local.Id, ProjectId = projects[0].Id, UserId = users[1].Id, Role = ProjectRole.Viewer, JoinedAt = now
        });
        seed.TaskItems.AddRange(tasks);
        seed.Notifications.AddRange(notifications);
        await seed.SaveChangesAsync();

        var persisted = seed.Notifications.IgnoreQueryFilters().AsNoTracking().OrderBy(item => item.Id).Select(item => new
        {
            item.Id, item.TenantId, item.UserId, item.Title, item.Body, item.NotificationType, item.LogicalKey,
            item.RelatedEntityType, item.RelatedEntityId, item.StateVersion, item.CreatedAt, item.IsRead, item.ReadAt, item.DeletedAt
        });
        var before = await persisted.ToArrayAsync();
        Assert.Equal(10, before.Length);
        var tenant = new CurrentTenantService();
        tenant.SetTenant(local.Id, local.Slug);
        await using var scoped = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenant);
        var resolver = new CanonicalCurrentAuthorizationTargetResolver(scoped, tenant, new CurrentAuthorizationTargetResolver(scoped, tenant));
        var requested = notifications.Select(item => item.Id).Concat([notifications[0].Id, Guid.Empty, Guid.NewGuid()]).ToArray();
        var expected = new[] { notifications[0].Id, notifications[1].Id }.Order().ToArray();
        Assert.Equal(expected, (await resolver.FilterAvailableNotificationIdsAsync(local.Id, users[1].Id, requested)).Order());
        Assert.Empty(await resolver.FilterAvailableNotificationIdsAsync(foreign.Id, users[1].Id, requested));
        Assert.Empty(await resolver.FilterAvailableNotificationIdsAsync(local.Id, Guid.Empty, requested));
        Assert.Empty(await resolver.FilterAvailableNotificationIdsAsync(local.Id, users[1].Id, []));
        Assert.Empty(scoped.ChangeTracker.Entries());
        Assert.Equal(before, await persisted.ToArrayAsync());

        var trackedMembership = await scoped.TenantUsers.SingleAsync(item => item.Id == membership.Id);
        var trackedUser = await scoped.Users.SingleAsync(item => item.Id == users[1].Id);
        membership.Status = TenantUserStatus.Suspended;
        await seed.SaveChangesAsync();
        Assert.Equal(TenantUserStatus.Suspended, await seed.TenantUsers.IgnoreQueryFilters().AsNoTracking().Where(item => item.Id == membership.Id).Select(item => item.Status).SingleAsync());
        Assert.Equal(TenantUserStatus.Active, trackedMembership.Status);
        Assert.Empty(await resolver.FilterAvailableNotificationIdsAsync(local.Id, users[1].Id, requested));
        Assert.Equal(before, await persisted.ToArrayAsync());

        membership.Status = TenantUserStatus.Active;
        users[1].Status = UserStatus.Suspended;
        await seed.SaveChangesAsync();
        Assert.Equal(UserStatus.Suspended, await seed.Users.IgnoreQueryFilters().AsNoTracking().Where(item => item.Id == users[1].Id).Select(item => item.Status).SingleAsync());
        Assert.Equal(UserStatus.Active, trackedUser.Status);
        Assert.Empty(await resolver.FilterAvailableNotificationIdsAsync(local.Id, users[1].Id, requested));
        Assert.Equal(before, await persisted.ToArrayAsync());

        users[1].Status = UserStatus.Active;
        await seed.SaveChangesAsync();
        Assert.Equal(expected, (await resolver.FilterAvailableNotificationIdsAsync(local.Id, users[1].Id, requested)).Order());
        Assert.Equal(before, await persisted.ToArrayAsync());
        var trackedNotification = await scoped.Notifications.SingleAsync(item => item.Id == notifications[0].Id);
        notifications[0].DeletedAt = now;
        await seed.SaveChangesAsync();
        var afterFixtureDelete = await persisted.ToArrayAsync();
        Assert.NotNull(afterFixtureDelete.Single(item => item.Id == notifications[0].Id).DeletedAt);
        Assert.Null(trackedNotification.DeletedAt);
        Assert.Equal(notifications[1].Id, Assert.Single(await resolver.FilterAvailableNotificationIdsAsync(local.Id, users[1].Id, requested)));
        Assert.Equal(afterFixtureDelete, await persisted.ToArrayAsync());
        Assert.Equal(3, scoped.ChangeTracker.Entries().Count());
        Assert.All(scoped.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }
}
