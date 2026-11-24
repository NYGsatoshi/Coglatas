namespace Coglatas.Web.Models;

public static class AqPublicRoutes
{
    public static bool IsSurveySubmission(PathString path) =>
        path.StartsWithSegments("/api/v1/public/surveys", out var remaining) &&
        remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var action] &&
        action.Equals("submissions", StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedSurvey(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<AqProtectedEndpointAttribute>() is not null;

    public static bool IsPublicSurveyPage(PathString path) =>
        path.StartsWithSegments("/app/aq/surveys") ||
        path.StartsWithSegments("/app/aq/events");

    public static bool IsPublicSurvey(PathString path) =>
        path.StartsWithSegments("/api/v1/public/surveys") ||
        path.StartsWithSegments("/api/v1/public/events");
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class AqProtectedEndpointAttribute : Attribute
{
}
