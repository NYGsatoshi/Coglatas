using Coglatas.Application.Common;

namespace Coglatas.Tests;

[Trait("Portability", "CrossPlatform")]
public sealed class SkeletonTests
{
    [Fact]
    public void ResultSuccessCreatesSuccessfulResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }
}
