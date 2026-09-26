namespace Coglatas.Web.Testing;

/// <summary>
/// PERF-02 fixture generation is an explicit Test-only capability. Production,
/// Development, school, and public deployments cannot enable it by configuration.
/// </summary>
public static class PerformanceCiTestBoundary
{
    public static bool IsEnabled(string environmentName, bool requested) =>
        requested &&
        string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
