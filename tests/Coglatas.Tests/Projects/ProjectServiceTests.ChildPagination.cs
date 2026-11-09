using Coglatas.Application.Common;
using Coglatas.Application.Projects;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(0, 42949673, 100)]
    [InlineData(0, 21474837, 100)]
    [InlineData(0, int.MaxValue, 100)]
    [InlineData(0, int.MaxValue, 2)]
    [InlineData(1, 42949673, 100)]
    [InlineData(1, 21474837, 100)]
    [InlineData(1, int.MaxValue, 100)]
    [InlineData(1, int.MaxValue, 2)]
    [InlineData(2, 42949673, 100)]
    [InlineData(2, 21474837, 100)]
    [InlineData(2, int.MaxValue, 100)]
    [InlineData(2, int.MaxValue, 2)]
    public async Task ChildPaginationOversizedPageRemainsEmpty(int kind, int page, int pageSize)
    {
        var (fixture, expected) = PrepareChildPagination(kind);

        var result = await ReadChildPage(fixture, kind, page, pageSize);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Items);
        Assert.Equal(expected.Length, result.Total);
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        AssertChildPaginationDidNotWrite(fixture);
    }

    [Theory]
    [InlineData(0, 1, 2, 1, 2, 0, 2)]
    [InlineData(0, 2, 2, 2, 2, 2, 2)]
    [InlineData(0, 3, 2, 3, 2, 4, 1)]
    [InlineData(0, 4, 2, 4, 2, 5, 0)]
    [InlineData(0, 0, 0, 1, 50, 0, 5)]
    [InlineData(0, 1, int.MaxValue, 1, 100, 0, 5)]
    [InlineData(1, 1, 2, 1, 2, 0, 2)]
    [InlineData(1, 2, 2, 2, 2, 2, 2)]
    [InlineData(1, 3, 2, 3, 2, 4, 1)]
    [InlineData(1, 4, 2, 4, 2, 5, 0)]
    [InlineData(1, 0, 0, 1, 50, 0, 5)]
    [InlineData(1, 1, int.MaxValue, 1, 100, 0, 5)]
    [InlineData(2, 1, 2, 1, 2, 0, 2)]
    [InlineData(2, 2, 2, 2, 2, 2, 2)]
    [InlineData(2, 3, 2, 3, 2, 4, 1)]
    [InlineData(2, 4, 2, 4, 2, 5, 0)]
    [InlineData(2, 0, 0, 1, 50, 0, 5)]
    [InlineData(2, 1, int.MaxValue, 1, 100, 0, 5)]
    public async Task ChildPaginationPreservesFilteredRowsAndMetadata(
        int kind, int page, int pageSize, int expectedPage, int expectedSize, int start, int count)
    {
        var (fixture, expected) = PrepareChildPagination(kind);

        var result = await ReadChildPage(fixture, kind, page, pageSize);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(expected.Skip(start).Take(count), result.Items);
        Assert.Equal(expected.Length, result.Total);
        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedSize, result.PageSize);
        AssertChildPaginationDidNotWrite(fixture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ChildPaginationDoesNotBypassParentDenial(int kind)
    {
        var (fixture, _) = PrepareChildPagination(kind);
        fixture.Current.UserIdValue = fixture.AddUser(addWorkspaceMember: false).Id;

        var result = await ReadChildPage(fixture, kind, int.MaxValue, 100);

        Assert.False(result.Succeeded);
        Assert.Equal(kind == 1 ? "Comment target not found." : "Project not found.", result.Error);
        AssertChildPaginationDidNotWrite(fixture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ChildPaginationKeepsAnEmptySearchResultEmpty(int kind)
    {
        var (fixture, _) = PrepareChildPagination(kind);

        var result = await ReadChildPage(fixture, kind, int.MaxValue, 100, "absent-search-value");

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(int.MaxValue, result.Page);
        Assert.Equal(100, result.PageSize);
        AssertChildPaginationDidNotWrite(fixture);
    }

    private static (ProjectFixture Fixture, Guid[] Expected) PrepareChildPagination(int kind)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Owner);
        fixture.EnsureProjectOperational();
        var expected = new List<Guid>();
        for (var index = 0; index < 8; index++)
        {
            var text = index == 3 ? "unmatched" : $"keep {index}";
            var foreign = index == 2;
            var deleted = index == 5;
            SoftDeletableEntity entity;
            if (kind == 0)
            {
                var milestone = fixture.AddMilestone(text, index);
                if (foreign) milestone.ProjectId = Guid.NewGuid();
                entity = milestone;
            }
            else if (kind == 1)
            {
                var comment = new Comment
                {
                    WorkspaceId = fixture.Workspace.Id,
                    AuthorUserId = actor.Id,
                    TargetType = CommentTargetType.Project,
                    TargetId = foreign ? Guid.NewGuid() : fixture.Project.Id,
                    Body = text
                };
                fixture.Projects.Comments.Add(comment);
                entity = comment;
            }
            else
            {
                var task = fixture.AddTask(text);
                if (foreign) task.ProjectId = Guid.NewGuid();
                entity = task;
            }

            if (deleted) entity.MarkDeleted(fixture.Clock.UtcNow, actor.Id, "Pagination control.");
            if (!foreign && !deleted && index != 3) expected.Add(entity.Id);
        }

        return (fixture, expected.ToArray());
    }

    private static async Task<(bool Succeeded, string? Error, Guid[] Items, int Page, int PageSize, int Total)> ReadChildPage(
        ProjectFixture fixture, int kind, int page, int pageSize, string search = "keep")
    {
        return kind switch
        {
            0 => ChildPageIds(await fixture.Service.ListMilestonesAsync(fixture.Project.Id,
                new ProjectChildListQuery(search, page, pageSize)), item => item.Id),
            1 => ChildPageIds(await fixture.Service.ListCommentsAsync(CommentTargetType.Project, fixture.Project.Id,
                new ProjectChildListQuery(search, page, pageSize)), item => item.Id),
            _ => ChildPageIds(await fixture.Service.ListTasksAsync(fixture.Project.Id,
                new TaskListQuery(Search: search, Page: page, PageSize: pageSize)), item => item.Id)
        };
    }

    private static (bool Succeeded, string? Error, Guid[] Items, int Page, int PageSize, int Total) ChildPageIds<T>(
        Result<PagedResponse<T>> result, Func<T, Guid> id)
    {
        return result.IsSuccess
            ? (true, null, result.Value!.Items.Select(id).ToArray(), result.Value.Page, result.Value.PageSize, result.Value.TotalCount)
            : (false, result.Error, [], 0, 0, 0);
    }

    private static void AssertChildPaginationDidNotWrite(ProjectFixture fixture)
    {
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
    }
}
