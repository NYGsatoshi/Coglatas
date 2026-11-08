using Coglatas.Application.Groups;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Groups;

public sealed partial class GroupServiceUpdateValidationTests
{
    [Fact]
    public async Task CreatedGroupLifecyclePreservesMembershipHistoryAndCallerTransaction()
        => await AssertCreatedGroupLifecycleAsync(null);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlCreatedGroupLifecyclePreservesMembershipHistoryAndCallerTransaction()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), AssertCreatedGroupLifecycleAsync);
    }

    private static async Task AssertCreatedGroupLifecycleAsync(string? connection)
    {
        await using var fixture = await Fixture.CreateAsync(connection);
        var transaction = fixture.Context.Database.CurrentTransaction;
        var suffix = Guid.NewGuid().ToString("N");
        var participant = new User
        {
            DisplayName = "Participant",
            Email = suffix + "@example.test",
            NormalizedEmail = suffix.ToUpperInvariant() + "@EXAMPLE.TEST",
            PasswordHash = Guid.NewGuid().ToString("N"),
            Status = UserStatus.Active
        };
        fixture.Context.Users.Add(participant);
        fixture.Context.WorkspaceMembers.AddRange(
            new WorkspaceMember
            {
                WorkspaceId = fixture.Workspace.Id, UserId = fixture.User.Id,
                Role = WorkspaceRole.Owner, Status = MembershipStatus.Active
            },
            new WorkspaceMember
            {
                WorkspaceId = fixture.Workspace.Id, UserId = participant.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
        await fixture.Context.SaveChangesAsync();

        var created = await fixture.Service.CreateAsync(fixture.Workspace.Id,
            new CreateGroupRequest(fixture.OldParent.Id, " Lifecycle group ", " Before ", GroupType.Team));
        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Value);
        var groupId = created.Value.Id;
        Assert.NotEqual(fixture.Subject.Id, groupId);
        Assert.Equal(fixture.Workspace.Id, created.Value.WorkspaceId);
        Assert.Equal(fixture.OldParent.Id, created.Value.ParentGroupId);
        Assert.Equal("Lifecycle group", created.Value.Name);
        Assert.Equal("Before", created.Value.Description);
        Assert.Equal(GroupType.Team, created.Value.GroupType);
        Assert.Equal(GroupStatus.Active, created.Value.Status);
        Assert.Equal(fixture.User.Id, created.Value.CreatedByUserId);

        var storedCreated = await fixture.Context.Groups.AsNoTracking().SingleAsync(group => group.Id == groupId);
        Assert.Equal("Lifecycle group", storedCreated.Name);
        Assert.Equal(GroupType.Team, storedCreated.GroupType);
        var creator = await fixture.Context.GroupMembers.AsNoTracking().SingleAsync(member => member.GroupId == groupId);
        Assert.Equal(fixture.User.Id, creator.UserId);
        Assert.Equal(GroupRole.Owner, creator.Role);
        Assert.Equal(new FixedClock().UtcNow, creator.JoinedAt);

        var updated = await fixture.Service.UpdateAsync(groupId,
            new UpdateGroupRequest(fixture.NewParent.Id, " Lifecycle renamed ", " After ",
                GroupType.Club, GroupStatus.Active));
        Assert.True(updated.IsSuccess);
        Assert.NotNull(updated.Value);
        Assert.Equal(GroupType.Club, updated.Value.GroupType);
        Assert.Equal(GroupStatus.Active, updated.Value.Status);
        Assert.Equal(fixture.NewParent.Id, updated.Value.ParentGroupId);
        var storedUpdated = await fixture.Context.Groups.AsNoTracking().SingleAsync(group => group.Id == groupId);
        Assert.Equal("Lifecycle renamed", storedUpdated.Name);
        Assert.Equal(fixture.NewParent.Id, storedUpdated.ParentGroupId);
        Assert.Equal(GroupType.Club, storedUpdated.GroupType);
        Assert.Equal("After", storedUpdated.Description);

        var added = await fixture.Service.AddMemberAsync(groupId,
            new AddGroupMemberRequest(participant.Id, GroupRole.Member));
        Assert.True(added.IsSuccess);
        Assert.NotNull(added.Value);
        Assert.Equal(participant.Id, added.Value.UserId);
        Assert.Equal("Participant", added.Value.DisplayName);
        Assert.Equal(participant.Email, added.Value.Email);
        Assert.Equal(GroupRole.Member, added.Value.Role);
        var storedAdded = await fixture.Context.GroupMembers.AsNoTracking().SingleAsync(row =>
            row.GroupId == groupId && row.UserId == participant.Id);
        Assert.Equal(GroupRole.Member, storedAdded.Role);
        var member = await fixture.Context.GroupMembers.SingleAsync(row =>
            row.GroupId == groupId && row.UserId == participant.Id);
        var historicalJoin = new FixedClock().UtcNow.AddDays(-3);
        member.JoinedAt = historicalJoin;
        await fixture.Context.SaveChangesAsync();

        var promoted = await fixture.Service.UpdateMemberAsync(groupId, participant.Id,
            new UpdateGroupMemberRequest(GroupRole.Admin));
        Assert.True(promoted.IsSuccess);
        Assert.NotNull(promoted.Value);
        Assert.Equal(GroupRole.Admin, promoted.Value.Role);
        Assert.Equal(historicalJoin, promoted.Value.JoinedAt);
        var storedPromoted = await fixture.Context.GroupMembers.AsNoTracking().SingleAsync(row =>
            row.GroupId == groupId && row.UserId == participant.Id);
        Assert.Equal(GroupRole.Admin, storedPromoted.Role);
        Assert.Equal(historicalJoin, storedPromoted.JoinedAt);
        Assert.True((await fixture.Service.RemoveMemberAsync(groupId, participant.Id)).IsSuccess);
        var storedRemoved = await fixture.Context.GroupMembers.AsNoTracking().SingleAsync(row =>
            row.GroupId == groupId && row.UserId == participant.Id);
        Assert.Equal(GroupRole.ReadOnly, storedRemoved.Role);
        Assert.Equal(historicalJoin, storedRemoved.JoinedAt);

        var roster = await fixture.Service.ListMembersAsync(groupId);
        Assert.True(roster.IsSuccess);
        Assert.NotNull(roster.Value);
        Assert.Equal(2, roster.Value.Count);
        var retained = Assert.Single(roster.Value, row => row.UserId == participant.Id);
        Assert.Equal(GroupRole.ReadOnly, retained.Role);
        Assert.Equal(historicalJoin, retained.JoinedAt);
        Assert.Equal(GroupRole.Owner, Assert.Single(roster.Value, row => row.UserId == fixture.User.Id).Role);

        Assert.True((await fixture.Service.ArchiveAsync(groupId)).IsSuccess);
        Assert.Equal(GroupStatus.Archived, (await fixture.Context.Groups.AsNoTracking().SingleAsync(group =>
            group.Id == groupId)).Status);
        var archived = await fixture.Service.GetAsync(groupId);
        Assert.True(archived.IsSuccess);
        Assert.NotNull(archived.Value);
        Assert.Equal(GroupStatus.Archived, archived.Value.Status);
        var active = await fixture.Service.ListByWorkspaceAsync(fixture.Workspace.Id);
        Assert.True(active.IsSuccess);
        Assert.NotNull(active.Value);
        Assert.DoesNotContain(active.Value, row => row.Id == groupId || row.Id == fixture.ForeignParent.Id);
        Assert.Contains(active.Value, row => row.Id == fixture.Subject.Id);

        Assert.True((await fixture.Service.RestoreAsync(groupId)).IsSuccess);
        var restored = await fixture.Service.ListByWorkspaceAsync(fixture.Workspace.Id);
        Assert.True(restored.IsSuccess);
        Assert.NotNull(restored.Value);
        var restoredItem = Assert.Single(restored.Value, row => row.Id == groupId);
        Assert.Equal(GroupStatus.Active, restoredItem.Status);
        Assert.Equal(GroupType.Club, restoredItem.GroupType);
        Assert.Equal("Lifecycle renamed", restoredItem.Name);

        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.Groups.SingleAsync(group => group.Id == groupId);
        Assert.Equal("Lifecycle renamed", stored.Name);
        Assert.Equal("lifecycle-renamed", stored.Slug);
        Assert.Equal("After", stored.Description);
        Assert.Equal(fixture.NewParent.Id, stored.ParentGroupId);
        Assert.Equal(GroupType.Club, stored.GroupType);
        Assert.Equal(GroupStatus.Active, stored.Status);
        Assert.Null(stored.DeletedAt);
        Assert.Equal(2, await fixture.Context.GroupMembers.CountAsync(row => row.GroupId == groupId));
        Assert.Equal(GroupRole.ReadOnly, (await fixture.Context.GroupMembers.SingleAsync(row =>
            row.GroupId == groupId && row.UserId == participant.Id)).Role);
        Assert.Equal(GroupStatus.Active, (await fixture.Context.Groups.SingleAsync(group =>
            group.Id == fixture.ForeignParent.Id)).Status);
        Assert.Equal(7, fixture.Unit.SaveCalls);
        string[] expectedActions =
        [
            "GroupCreated", "GroupUpdated", "GroupMemberAdded", "GroupMemberRoleChanged",
            "GroupMemberRemoved", "GroupArchived", "GroupRestored"
        ];
        Assert.Equal(expectedActions, fixture.Audit.Entries.Select(entry => entry.Action));
        foreach (var entry in fixture.Audit.Entries)
        {
            Assert.Equal(fixture.User.Id, entry.ActorUserId);
            Assert.Equal(groupId, entry.EntityId);
        }
        Assert.Same(transaction, fixture.Context.Database.CurrentTransaction);
        if (connection is not null)
        {
            Assert.NotNull(transaction);
            await using var observer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            Assert.Equal(0, await observer.Groups.CountAsync(group => group.Id == groupId));
        }
    }
}
