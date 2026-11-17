using RecoveryAdmission = Coglatas.SyntheticRecoveryAcceptance.RunAdmission;

namespace Coglatas.Tests;

[Trait("Portability", "CrossPlatform")]
public sealed class SyntheticRecoveryRunAdmissionTests
{
    private static Dictionary<string, string?> MainPush() => new()
    {
        ["COGLATAS_RECOVERY_RUNNER_ENVIRONMENT"] = "github-hosted",
        ["RUNNER_OS"] = "Linux",
        ["GITHUB_EVENT_NAME"] = "push",
        ["GITHUB_REPOSITORY"] = "NYGsatoshi/Coglatas",
        ["GITHUB_REF"] = "refs/heads/main",
        ["GITHUB_SHA"] = "0123456789abcdef0123456789abcdef012345ab"
    };

    [Fact]
    public void ExactMainPushIsAdmittedWithAbsentOrEmptyPrMetadata()
    {
        var environment = MainPush();
        Assert.True(RecoveryAdmission.IsAllowed(environment.GetValueOrDefault));
        environment["COGLATAS_PR_HEAD_SHA"] = "";
        environment["COGLATAS_PR_BASE_SHA"] = "";
        Assert.True(RecoveryAdmission.IsAllowed(environment.GetValueOrDefault));
    }

    [Fact]
    public void ExistingHostedLinuxPullRequestAdmissionIsPreserved()
    {
        var environment = new Dictionary<string, string?>
        {
            ["COGLATAS_RECOVERY_RUNNER_ENVIRONMENT"] = "github-hosted",
            ["RUNNER_OS"] = "Linux",
            ["GITHUB_EVENT_NAME"] = "pull_request"
        };
        Assert.True(RecoveryAdmission.IsAllowed(environment.GetValueOrDefault));
    }

    [Theory]
    [InlineData("GITHUB_EVENT_NAME", "pull_request_target")]
    [InlineData("GITHUB_EVENT_NAME", "workflow_dispatch")]
    [InlineData("GITHUB_EVENT_NAME", "")]
    [InlineData("GITHUB_REPOSITORY", "other/Coglatas")]
    [InlineData("GITHUB_REPOSITORY", null)]
    [InlineData("GITHUB_REF", "refs/heads/other")]
    [InlineData("GITHUB_REF", "refs/pull/2054/merge")]
    [InlineData("GITHUB_REF", null)]
    [InlineData("GITHUB_SHA", null)]
    [InlineData("GITHUB_SHA", "")]
    [InlineData("GITHUB_SHA", "0123456789abcdef0123456789abcdef012345a")]
    [InlineData("GITHUB_SHA", "0123456789abcdef0123456789abcdef012345abc")]
    [InlineData("GITHUB_SHA", "0123456789ABCDEF0123456789abcdef012345ab")]
    [InlineData("GITHUB_SHA", "0123456789abcdef0123456789abcdef012345ag")]
    [InlineData("GITHUB_SHA", "0123456789abcdef0123456789abcdef012345ab\n")]
    [InlineData("COGLATAS_PR_HEAD_SHA", "0123456789abcdef0123456789abcdef012345ab")]
    [InlineData("COGLATAS_PR_BASE_SHA", "0123456789abcdef0123456789abcdef012345ab")]
    [InlineData("COGLATAS_PR_HEAD_SHA", " ")]
    [InlineData("COGLATAS_PR_BASE_SHA", " ")]
    public void InvalidMainPushContextIsRejected(string key, string? value)
    {
        var environment = MainPush();
        environment[key] = value;
        Assert.False(RecoveryAdmission.IsAllowed(environment.GetValueOrDefault));
    }

    [Theory]
    [InlineData("push", "COGLATAS_RECOVERY_RUNNER_ENVIRONMENT", "self-hosted")]
    [InlineData("push", "COGLATAS_RECOVERY_RUNNER_ENVIRONMENT", null)]
    [InlineData("push", "RUNNER_OS", "Windows")]
    [InlineData("push", "RUNNER_OS", null)]
    [InlineData("pull_request", "COGLATAS_RECOVERY_RUNNER_ENVIRONMENT", "self-hosted")]
    [InlineData("pull_request", "COGLATAS_RECOVERY_RUNNER_ENVIRONMENT", null)]
    [InlineData("pull_request", "RUNNER_OS", "Windows")]
    [InlineData("pull_request", "RUNNER_OS", null)]
    public void BothEventsRequireHostedLinux(string eventName, string key, string? value)
    {
        var environment = MainPush();
        environment["GITHUB_EVENT_NAME"] = eventName;
        environment[key] = value;
        Assert.False(RecoveryAdmission.IsAllowed(environment.GetValueOrDefault));
    }
}
