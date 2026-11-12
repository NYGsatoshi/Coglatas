using Coglatas.Application.Projects;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectListQueryRegressionTests
{
    [Theory]
    [InlineData(21_474_838, 100)]
    [InlineData(42_949_674, 100)]
    [InlineData(int.MaxValue, 100)]
    [InlineData(int.MaxValue, 2)]
    public async Task TaskQueryPaginationCannotWrapAnOversizedOffset(int page, int pageSize)
        => await AssertTaskQueryPaginationAsync(page, pageSize, 5, 0, null);

    [Theory]
    [InlineData(1, 2, 0, 2)]
    [InlineData(2, 2, 2, 2)]
    [InlineData(3, 2, 4, 1)]
    [InlineData(4, 2, 5, 0)]
    [InlineData(0, 0, 0, 5)]
    [InlineData(1, int.MaxValue, 0, 5)]
    [InlineData(21_474_837, 100, 5, 0)]
    public async Task TaskQueryPaginationPreservesNormalizedScopedRows(
        int page, int pageSize, int start, int count)
        => await AssertTaskQueryPaginationAsync(page, pageSize, start, count, null);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlTaskQueryPaginationPreservesScopedPagesWithoutNegativeOffsets()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                foreach (var page in new[] { 21_474_838, 42_949_674, int.MaxValue })
                    await AssertTaskQueryPaginationAsync(page, 100, 5, 0, database);
                await AssertTaskQueryPaginationAsync(int.MaxValue, 2, 5, 0, database);
                await AssertTaskQueryPaginationAsync(1, 2, 0, 2, database);
                await AssertTaskQueryPaginationAsync(2, 2, 2, 2, database);
                await AssertTaskQueryPaginationAsync(3, 2, 4, 1, database);
                await AssertTaskQueryPaginationAsync(4, 2, 5, 0, database);
                await AssertTaskQueryPaginationAsync(0, 0, 0, 5, database);
                await AssertTaskQueryPaginationAsync(1, int.MaxValue, 0, 5, database);
                await AssertTaskQueryPaginationAsync(21_474_837, 100, 5, 0, database);
            });
    }

    private static async Task AssertTaskQueryPaginationAsync(
        int page, int pageSize, int start, int count, string? postgresConnectionString)
    {
        await using var fixture = await Fixture.CreateAsync(postgresConnectionString: postgresConnectionString);
        var project = fixture.Project("Requested");
        var otherProject = fixture.Project("Other");
        fixture.Context.Projects.AddRange(project, otherProject);
        var expected = Enumerable.Range(0, 5).Select(index => fixture.Task(project, $"Keep {index}", index)).ToArray();
        foreach (var task in expected) task.Priority = TaskPriority.Critical;
        var deleted = fixture.Task(project, "Deleted", -2);
        deleted.Priority = TaskPriority.Critical;
        deleted.MarkDeleted(DateTimeOffset.UtcNow, fixture.User.Id, "Pagination deletion control.");
        var otherParent = fixture.Task(otherProject, "Other Project", -1);
        otherParent.Priority = TaskPriority.Critical;
        var otherPriority = fixture.Task(project, "Other priority", 0);
        otherPriority.Priority = TaskPriority.Low;
        fixture.Context.TaskItems.AddRange(expected);
        fixture.Context.TaskItems.AddRange(deleted, otherParent, otherPriority);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Repository.ListTasksPageAsync(project.Id,
            new TaskListQuery(Priority: TaskPriority.Critical, Page: page, PageSize: pageSize));

        Assert.NotNull(result);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(Math.Max(1, page), result.Page);
        Assert.Equal(pageSize < 1 ? 50 : Math.Min(pageSize, 100), result.PageSize);
        Assert.Equal(expected.Skip(start).Take(count).Select(task => task.Id), result.Items.Select(row => row.Task.Id));
        Assert.All(result.Items, row =>
        {
            Assert.Equal(project.Id, row.Task.ProjectId);
            Assert.Equal(TaskPriority.Critical, row.Task.Priority);
            Assert.Null(row.Task.DeletedAt);
        });
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }
}
