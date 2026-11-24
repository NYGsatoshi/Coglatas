namespace Coglatas.Web.Models;

public static class AqPublicRoutes
{
    public static bool IsSurveySubmission(PathString path) =>
        path.StartsWithSegments("/api/v1/public/surveys", out var remaining) &&
        remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var action] &&
        action.Equals("submissions", StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedSurvey(PathString path) => path.StartsWithSegments("/api/v1/core");

    public static bool IsPublicSurvey(PathString path) =>
        path.StartsWithSegments("/api/v1/public/surveys") ||
        path.StartsWithSegments("/api/v1/public/events");
}
