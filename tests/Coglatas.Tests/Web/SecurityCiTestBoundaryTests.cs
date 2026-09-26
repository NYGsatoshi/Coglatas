using Coglatas.Web.Testing;

namespace Coglatas.Tests.Web;

public sealed class SecurityCiTestBoundaryTests
{
    [Theory]
    [InlineData("Test", true)]
    [InlineData("test", true)]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void ExplicitFixtureOptInIsAcceptedOnlyInTest(string environmentName, bool expected)
    {
        Assert.Equal(expected, SecurityCiTestBoundary.IsEnabled(environmentName, requested: true));
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Development")]
    [InlineData("Production")]
    public void MissingOptInAlwaysFailsClosed(string environmentName)
    {
        Assert.False(SecurityCiTestBoundary.IsEnabled(environmentName, requested: false));
    }
}
