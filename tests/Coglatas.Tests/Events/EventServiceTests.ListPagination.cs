using Coglatas.Application.Events;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Events;

public sealed partial class EventServiceTests
{
    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(1_073_741_825, 4)]
    [InlineData(42_949_674, 100)]
    [InlineData(21_474_838, 100)]
    public async Task ListPaginationCannotWrapAnOutOfRangePageBackIntoVisibleEvents(int page, int pageSize)
    {
        var (fixture, _) = CreateEventPaginationFixture();

        var result = await fixture.Service.ListAsync(new EventListQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsSuccess);
        Assert.Equal(page, result.Value!.Page);
        Assert.Equal(pageSize, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
        Assert.Equal(6, fixture.Events.Items.Count);
        Assert.Equal(10, fixture.Events.Attendances.Count);
    }

    [Theory]
    [InlineData(1, 2, 1, 2, 0, 2)]
    [InlineData(2, 2, 2, 2, 2, 2)]
    [InlineData(3, 2, 3, 2, 4, 1)]
    [InlineData(4, 2, 4, 2, 5, 0)]
    [InlineData(0, 0, 1, 1, 0, 1)]
    [InlineData(-1, 200, 1, 100, 0, 5)]
    public async Task ListPaginationRetainsScopeOrderClampsAndAttendingCounts(
        int page, int pageSize, int expectedPage, int expectedSize, int start, int count)
    {
        var (fixture, visible) = CreateEventPaginationFixture();

        var result = await fixture.Service.ListAsync(new EventListQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPage, result.Value!.Page);
        Assert.Equal(expectedSize, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(visible.Skip(start).Take(count).Select(item => item.Id), result.Value.Items.Select(item => item.Id));
        for (var index = 0; index < count; index++)
        {
            var expected = visible[start + index];
            var actual = result.Value.Items[index];
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.StartsAt, actual.StartsAt);
            Assert.Equal(expected.EndsAt, actual.EndsAt);
            Assert.Equal(start + index, actual.AttendingCount);
        }
        Assert.Equal(6, fixture.Events.Items.Count);
        Assert.Equal(10, fixture.Events.Attendances.Count);
    }

    [Fact]
    public async Task ListPaginationStillRequiresAnAuthenticatedActor()
    {
        var (fixture, _) = CreateEventPaginationFixture();
        fixture.Current.UserIdValue = null;

        var result = await fixture.Service.ListAsync(new EventListQuery(Page: int.MaxValue, PageSize: 100));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Authentication is required.", result.Error);
        Assert.Equal(6, fixture.Events.Items.Count);
        Assert.Equal(10, fixture.Events.Attendances.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListPaginationRetainsAnEmptyVisibleList(bool hiddenEvents)
    {
        var (fixture, _) = CreateEventPaginationFixture(includeEvents: hiddenEvents);
        if (hiddenEvents) fixture.Current.UserIdValue = fixture.AddUser().Id;

        var result = await fixture.Service.ListAsync(new EventListQuery(Page: int.MaxValue, PageSize: 100));

        Assert.True(result.IsSuccess);
        Assert.Equal(int.MaxValue, result.Value!.Page);
        Assert.Equal(100, result.Value.PageSize);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
        Assert.Equal(hiddenEvents ? 6 : 0, fixture.Events.Items.Count);
        Assert.Equal(hiddenEvents ? 10 : 0, fixture.Events.Attendances.Count);
    }

    private static (EventFixture Fixture, ActivityEvent[] Visible) CreateEventPaginationFixture(bool includeEvents = true)
    {
        var fixture = EventFixture.Create();
        var actor = fixture.AddUser();
        var creator = fixture.AddUser();
        fixture.AddWorkspaceMember(actor.Id, WorkspaceRole.Member);
        fixture.AddWorkspaceMember(creator.Id, WorkspaceRole.Admin);
        fixture.Current.UserIdValue = actor.Id;
        var visible = new List<ActivityEvent>();
        for (var index = 0; includeEvents && index < 5; index++)
        {
            var item = fixture.AddEvent(fixture.Workspace.Id, null, null, creator.Id, EventStatus.Published);
            item.StartsAt = fixture.Clock.UtcNow.AddDays(1).AddMinutes(index * 5);
            item.EndsAt = item.StartsAt.AddHours(1);
            visible.Add(item);
            for (var attendee = 0; attendee < index; attendee++)
                fixture.AddAttendance(item.Id, fixture.AddUser().Id, AttendanceStatus.Attending);
            if (index != 1) continue;
            var foreign = fixture.AddEvent(Guid.NewGuid(), null, null, creator.Id, EventStatus.Published);
            foreign.StartsAt = item.StartsAt.AddMinutes(1);
            foreign.EndsAt = foreign.StartsAt.AddHours(1);
        }
        return (fixture, visible.ToArray());
    }
}
