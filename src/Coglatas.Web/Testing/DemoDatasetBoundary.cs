namespace Coglatas.Web.Testing;

/// <summary>
/// The reproducible demo fixture is never a normal startup seed. It is
/// available only to the isolated Test stack after an explicit opt-in.
/// </summary>
public static class DemoDatasetBoundary
{
    public static bool IsEnabled(string environmentName, bool requested) =>
        requested &&
        string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
