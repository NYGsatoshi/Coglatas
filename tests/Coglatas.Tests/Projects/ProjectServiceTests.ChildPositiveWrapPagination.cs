namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ChildPaginationCannotWrapToASmallPositiveOffset(int kind)
    {
        var (fixture, expected) = PrepareChildPagination(kind);

        var result = await ReadChildPage(fixture, kind, 42_949_674, 100);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Items);
        Assert.Equal(expected.Length, result.Total);
        Assert.Equal(42_949_674, result.Page);
        Assert.Equal(100, result.PageSize);
        AssertChildPaginationDidNotWrite(fixture);
    }
}
