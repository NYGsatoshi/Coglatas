using Coglatas.Application.Events;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Events;

public sealed partial class EventServiceTests
{
    [Fact]
    public async Task ListPaginationExcludesAnExistingForeignWorkspaceBeforePaging()
    {
        var (fixture, visible) = CreateEventPaginationFixture();
        var hidden = Assert.Single(fixture.Events.Items, item => item.WorkspaceId != fixture.Workspace.Id);
        var actor = Assert.Single(fixture.Workspaces.Members, member => member.UserId == fixture.Current.UserIdValue);
        var administrator = Assert.Single(fixture.Workspaces.Members, member => member.UserId == hidden.CreatedByUserId);
        var foreign = new Workspace
        {
            Name = "Existing foreign Workspace", Slug = "existing-foreign-workspace",
            CreatedByUserId = administrator.UserId, Status = WorkspaceStatus.Active
        };
        fixture.Workspaces.Items.Add(foreign.Id, foreign);
        fixture.Workspaces.Members.Add(new WorkspaceMember
        {
            WorkspaceId = foreign.Id, UserId = administrator.UserId, User = administrator.User,
            Role = WorkspaceRole.Admin, Status = MembershipStatus.Active, JoinedAt = fixture.Clock.UtcNow
        });
        hidden.WorkspaceId = foreign.Id;
        Assert.Same(foreign, fixture.Workspaces.Items[foreign.Id]);
        Assert.Equal(SystemRole.User, actor.User!.SystemRole);
        Assert.DoesNotContain(fixture.Workspaces.Members,
            member => member.WorkspaceId == foreign.Id && member.UserId == actor.UserId);

        var result = await fixture.Service.ListAsync(new EventListQuery(Page: 2, PageSize: 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Page);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(visible.Skip(2).Take(2).Select(item => item.Id), result.Value.Items.Select(item => item.Id));
        Assert.DoesNotContain(result.Value.Items, item => item.Id == hidden.Id);
    }
}
