using Coglatas.Web.Testing;

namespace Coglatas.Tests.Web;

public sealed class PerformanceCiTestBoundaryTests
{
    [Theory]
    [InlineData("Test", true)]
    [InlineData("test", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void PerformanceFixtureIsRestrictedToAnExplicitTestEnvironmentOptIn(
        string environmentName,
        bool expected)
    {
        Assert.Equal(expected, PerformanceCiTestBoundary.IsEnabled(environmentName, requested: true));
    }

    [Fact]
    public void PerformanceFixtureRemainsDisabledWithoutExplicitOptIn()
    {
        Assert.False(PerformanceCiTestBoundary.IsEnabled("Test", requested: false));
    }
}
