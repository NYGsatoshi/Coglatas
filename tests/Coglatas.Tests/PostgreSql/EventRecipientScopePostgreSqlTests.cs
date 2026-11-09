using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class EventRecipientScopePostgreSqlTests
{
    [PostgreSqlFact]
    public Task GroupAndProjectRecipientQueriesPreserveTenantMembershipScopeWithoutTracking() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), AssertRecipientQueriesAsync);

    /// <summary>Checks native recipient queries with independently persisted scope and Tenant controls.</summary>
    /// <param name="connectionString">The isolated migrated synthetic PostgreSQL database supplied by the harness.</param>
    private static async Task AssertRecipientQueriesAsync(string connectionString)
    {
        await using var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
        var at = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var localTenant = new Tenant(Guid.NewGuid())
        {
            Name = "Recipient local tenant", Slug = "recipient-local", Status = TenantStatus.Active
        };
        var foreignTenant = new Tenant(Guid.NewGuid())
        {
            Name = "Recipient foreign tenant", Slug = "recipient-foreign", Status = TenantStatus.Active
        };
        var users = Enumerable.Range(0, 5).Select(index => new User
        {
            DisplayName = $"Recipient {index}", Email = $"recipient-{index}@example.invalid",
            NormalizedEmail = $"RECIPIENT-{index}@EXAMPLE.INVALID", Status = UserStatus.Active
        }).ToArray();
        var workspaces = new[] { localTenant, foreignTenant }.Select((tenant, index) => new Workspace
        {
            TenantId = tenant.Id, Name = $"Recipient workspace {index}", Slug = $"recipient-{index}",
            CreatedByUserId = users[0].Id, Status = WorkspaceStatus.Active
        }).ToArray();
        var groups = Enumerable.Range(0, 3).Select(index => new Group
        {
            TenantId = index == 2 ? foreignTenant.Id : localTenant.Id,
            WorkspaceId = workspaces[index == 2 ? 1 : 0].Id,
            Name = $"Recipient group {index}", Slug = $"recipient-group-{index}", CreatedByUserId = users[0].Id
        }).ToArray();
        var projects = Enumerable.Range(0, 3).Select(index => new Project
        {
            TenantId = index == 2 ? foreignTenant.Id : localTenant.Id,
            WorkspaceId = workspaces[index == 2 ? 1 : 0].Id,
            Name = $"Recipient project {index}", Slug = $"recipient-project-{index}",
            CreatedByUserId = users[0].Id, OwnerUserId = users[0].Id, Status = ProjectStatus.Active
        }).ToArray();
        var groupMembers = new[]
        {
            new GroupMember { TenantId = localTenant.Id, GroupId = groups[0].Id, UserId = users[0].Id, Role = GroupRole.Admin, JoinedAt = at },
            new GroupMember { TenantId = localTenant.Id, GroupId = groups[0].Id, UserId = users[1].Id, Role = GroupRole.ReadOnly, JoinedAt = at },
            new GroupMember { TenantId = localTenant.Id, GroupId = groups[1].Id, UserId = users[2].Id, JoinedAt = at },
            new GroupMember { TenantId = foreignTenant.Id, GroupId = groups[2].Id, UserId = users[3].Id, JoinedAt = at },
            new GroupMember { TenantId = foreignTenant.Id, GroupId = groups[0].Id, UserId = users[4].Id, JoinedAt = at }
        };
        var projectMembers = new[]
        {
            new ProjectMember { TenantId = localTenant.Id, ProjectId = projects[0].Id, UserId = users[0].Id, Role = ProjectRole.Owner, JoinedAt = at },
            new ProjectMember { TenantId = localTenant.Id, ProjectId = projects[0].Id, UserId = users[1].Id, Role = ProjectRole.Viewer, JoinedAt = at },
            new ProjectMember { TenantId = localTenant.Id, ProjectId = projects[1].Id, UserId = users[2].Id, JoinedAt = at },
            new ProjectMember { TenantId = foreignTenant.Id, ProjectId = projects[2].Id, UserId = users[3].Id, JoinedAt = at },
            new ProjectMember { TenantId = foreignTenant.Id, ProjectId = projects[0].Id, UserId = users[4].Id, JoinedAt = at }
        };
        var groupEvents = groups.Select((group, index) => new ActivityEvent
        {
            TenantId = group.TenantId, GroupId = group.Id, CreatedByUserId = users[0].Id,
            Title = $"Group event {index}", StartsAt = at, EndsAt = at.AddHours(1), Status = EventStatus.Published
        }).ToArray();
        var projectEvents = projects.Select((project, index) => new ActivityEvent
        {
            TenantId = project.TenantId, ProjectId = project.Id, CreatedByUserId = users[0].Id,
            Title = $"Project event {index}", StartsAt = at, EndsAt = at.AddHours(1), Status = EventStatus.Published
        }).ToArray();
        seed.Tenants.AddRange(localTenant, foreignTenant);
        seed.Users.AddRange(users);
        seed.Workspaces.AddRange(workspaces);
        seed.Groups.AddRange(groups);
        seed.Projects.AddRange(projects);
        seed.GroupMembers.AddRange(groupMembers);
        seed.ProjectMembers.AddRange(projectMembers);
        seed.ActivityEvents.AddRange(groupEvents.Concat(projectEvents));
        await seed.SaveChangesAsync();

        var groupRows = await seed.GroupMembers.IgnoreQueryFilters().AsNoTracking().OrderBy(member => member.Id)
            .Select(member => new { member.Id, member.TenantId, member.GroupId, member.UserId, member.Role, member.JoinedAt })
            .ToListAsync();
        var projectRows = await seed.ProjectMembers.IgnoreQueryFilters().AsNoTracking().OrderBy(member => member.Id)
            .Select(member => new { member.Id, member.TenantId, member.ProjectId, member.UserId, member.Role, member.JoinedAt })
            .ToListAsync();
        Assert.Equal(5, groupRows.Count);
        Assert.Equal(5, projectRows.Count);
        Assert.Contains(groupRows, member => member.Id == groupMembers[4].Id && member.TenantId == foreignTenant.Id && member.GroupId == groups[0].Id);
        Assert.Contains(projectRows, member => member.Id == projectMembers[4].Id && member.TenantId == foreignTenant.Id && member.ProjectId == projects[0].Id);

        var tenantScope = new CurrentTenantService();
        tenantScope.SetTenant(localTenant.Id, localTenant.Slug);
        await using var scoped = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenantScope);
        var repository = new EventRepository(scoped);
        Guid[] primaryRecipients = [users[0].Id, users[1].Id];
        foreach (var events in new[] { groupEvents, projectEvents })
        {
            var primary = await repository.ListScopeRecipientUserIdsAsync(events[0]);
            Assert.Equal(primaryRecipients.Order(), primary.Order());
            Assert.Equal(users[2].Id, Assert.Single(await repository.ListScopeRecipientUserIdsAsync(events[1])));
            Assert.Empty(await repository.ListScopeRecipientUserIdsAsync(events[2]));
        }

        var finalGroupRows = await seed.GroupMembers.IgnoreQueryFilters().AsNoTracking().OrderBy(member => member.Id)
            .Select(member => new { member.Id, member.TenantId, member.GroupId, member.UserId, member.Role, member.JoinedAt })
            .ToListAsync();
        var finalProjectRows = await seed.ProjectMembers.IgnoreQueryFilters().AsNoTracking().OrderBy(member => member.Id)
            .Select(member => new { member.Id, member.TenantId, member.ProjectId, member.UserId, member.Role, member.JoinedAt })
            .ToListAsync();
        Assert.Equal(groupRows, finalGroupRows);
        Assert.Equal(projectRows, finalProjectRows);
        Assert.Empty(scoped.ChangeTracker.Entries());
        Assert.All(seed.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }
}
