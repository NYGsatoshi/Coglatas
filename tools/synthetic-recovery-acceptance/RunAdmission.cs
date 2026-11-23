namespace Coglatas.SyntheticRecoveryAcceptance;

internal static class RunAdmission
{
    internal static bool IsAllowed(Func<string, string?> environment)
    {
        if (environment("COGLATAS_RECOVERY_RUNNER_ENVIRONMENT") != "github-hosted"
            || environment("RUNNER_OS") != "Linux")
            return false;

        if (environment("GITHUB_EVENT_NAME") == "pull_request")
            return true;

        var sha = environment("GITHUB_SHA");
        return environment("GITHUB_EVENT_NAME") == "push"
            && environment("GITHUB_REPOSITORY") == "NYGsatoshi/Coglatas"
            && environment("GITHUB_REF") == "refs/heads/main"
            && sha is { Length: 40 }
            && sha.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.IsNullOrEmpty(environment("COGLATAS_PR_HEAD_SHA"))
            && string.IsNullOrEmpty(environment("COGLATAS_PR_BASE_SHA"));
    }
}
