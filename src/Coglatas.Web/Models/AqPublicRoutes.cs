namespace Coglatas.Web.Models;

public static class AqPublicRoutes
{
    public static bool IsSurveySubmission(PathString path) =>
        path.StartsWithSegments("/api/v1/public/surveys", out var remaining) &&
        remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var action] &&
        action.Equals("submissions", StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedSurvey(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        // A route constraint can reject a malformed resource ID before endpoint metadata is available.
        return endpoint is null
            ? IsCoreSurveyPath(context.Request.Path)
            : endpoint.Metadata.GetMetadata<AqProtectedEndpointAttribute>() is not null;
    }

    public static bool IsPublicSurveyPage(PathString path) =>
        path.StartsWithSegments("/app/aq/surveys") ||
        path.StartsWithSegments("/app/aq/events");

    public static bool IsPublicSurvey(PathString path) =>
        path.StartsWithSegments("/api/v1/public/surveys") ||
        path.StartsWithSegments("/api/v1/public/events");

    internal static bool IsCoreSurveyPath(PathString path)
    {
        // Native request-start logging runs before endpoint metadata exists.
        // The AQ route families here also scope the Sakura proxy error sink.
        if (!path.StartsWithSegments("/api/v1/core", out var relative))
            return false;

        var segments = relative.Value?.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments switch
        {
            ["feedback" or "feedback-invitations" or "forms" or "presentations" or
                "presentation-feedback-exports" or "survey-campaigns" or "survey-targets", ..] => true,
            ["events", _, "presentations" or "program-code" or "feedback-settings" or "survey-campaigns" or
                "feedback-summary" or "feedback-invitations" or "feedback-invitation-policy", ..] => true,
            ["me", "presentation-feedback" or "feedback-invitations", ..] => true,
            _ => false
        };
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class AqProtectedEndpointAttribute : Attribute
{
}
