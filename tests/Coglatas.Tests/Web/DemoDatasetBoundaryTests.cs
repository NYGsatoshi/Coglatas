using Coglatas.Web.Testing;

namespace Coglatas.Tests.Web;

public sealed class DemoDatasetBoundaryTests
{
    [Theory]
    [InlineData("Test", true)]
    [InlineData("test", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void DemoDatasetIsRestrictedToAnExplicitTestEnvironmentOptIn(
        string environmentName,
        bool expected)
    {
        Assert.Equal(expected, DemoDatasetBoundary.IsEnabled(environmentName, requested: true));
    }

    [Fact]
    public void DemoDatasetRemainsDisabledWithoutExplicitOptIn()
    {
        Assert.False(DemoDatasetBoundary.IsEnabled("Test", requested: false));
    }
}
