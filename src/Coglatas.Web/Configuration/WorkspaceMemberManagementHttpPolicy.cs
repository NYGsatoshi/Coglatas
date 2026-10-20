namespace Coglatas.Web.Configuration;

public static class WorkspaceMemberManagementHttpPolicy
{
    public static string? ReadOperation(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return null;
        var segments = request.Path.Value?.Trim('/').Split('/');
        if (segments is not { Length: 6 } ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("workspaces", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(segments[2], out _) ||
            !segments[3].Equals("members", StringComparison.OrdinalIgnoreCase) ||
            !segments[4].Equals("management", StringComparison.OrdinalIgnoreCase)) return null;
        if (segments[5].Equals("options", StringComparison.OrdinalIgnoreCase)) return "options";
        return segments[5].Equals("candidates", StringComparison.OrdinalIgnoreCase) ? "candidates" : null;
    }
}
