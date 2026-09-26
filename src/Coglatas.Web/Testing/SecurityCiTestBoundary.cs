namespace Coglatas.Web.Testing;

/// <summary>
/// SEC-02 security fixtures are never a normal application seed. They are
/// available only inside the isolated Test environment after an explicit opt-in.
/// </summary>
public static class SecurityCiTestBoundary
{
    public static bool IsEnabled(string environmentName, bool requested) =>
        requested &&
        string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
