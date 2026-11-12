using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

public sealed partial class FormServiceTests
{
    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(1_073_741_825, 4)]
    [InlineData(42_949_674, 100)]
    [InlineData(21_474_838, 100)]
    public async Task ListPaginationCannotWrapAnOutOfRangePageBackIntoVisibleItems(int page, int pageSize)
    {
        var (fixture, _) = CreateListPaginationFixture();

        var result = await fixture.Service.ListAsync(new FormListQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsSuccess);
        Assert.Equal(page, result.Value!.Page);
        Assert.Equal(pageSize, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(1, 2, 1, 2, 0, 2)]
    [InlineData(2, 2, 2, 2, 2, 2)]
    [InlineData(3, 2, 3, 2, 4, 1)]
    [InlineData(4, 2, 4, 2, 5, 0)]
    [InlineData(0, 0, 1, 1, 0, 1)]
    [InlineData(-1, 200, 1, 100, 0, 5)]
    public async Task ListPaginationRetainsNormalPagesClampsAndAuthorization(
        int page, int pageSize, int expectedPage, int expectedPageSize, int expectedStart, int expectedCount)
    {
        var (fixture, visible) = CreateListPaginationFixture();

        var result = await fixture.Service.ListAsync(new FormListQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPage, result.Value!.Page);
        Assert.Equal(expectedPageSize, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(visible.Skip(expectedStart).Take(expectedCount).Select(form => form.Id),
            result.Value.Items.Select(form => form.Id));
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Fact]
    public async Task ListPaginationStillRequiresAnAuthenticatedActor()
    {
        var (fixture, _) = CreateListPaginationFixture();
        fixture.Current.UserIdValue = null;

        var result = await fixture.Service.ListAsync(new FormListQuery(Page: int.MaxValue, PageSize: 100));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Authentication is required.", result.Error);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    private static (FormFixture Fixture, InternalForm[] Visible) CreateListPaginationFixture()
    {
        var fixture = FormFixture.Create();
        var member = fixture.AddUser();
        fixture.AddWorkspaceMember(member.Id, WorkspaceRole.Member);
        fixture.Current.UserIdValue = member.Id;
        var visible = Enumerable.Range(0, 5).Select(_ => fixture.AddForm(FormStatus.Open)).ToArray();
        var foreign = fixture.AddForm(FormStatus.Open);
        foreign.WorkspaceId = Guid.NewGuid();
        foreign.Workspace = null;
        return (fixture, visible);
    }
}
