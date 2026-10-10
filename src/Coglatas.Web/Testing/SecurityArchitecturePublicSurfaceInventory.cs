namespace Coglatas.Web.Testing;

// These are observations of current handlers, not approved normative classifications.
internal static class SecurityArchitecturePublicSurfaceInventory
{
    public static object Describe(string path, string method) => new
    {
        observedAccessPath = AccessPath(path, method),
        authenticationAuthority = Authority(path, method),
        sourceReference = Source(path),
        normativeClassification = "UNVERIFIED", approval = "DRAFT", runtimeOutcome = "UNVERIFIED"
    };

    private static string AccessPath(string path, string method) => (path, method) switch
    {
        ("/api/auth/login", "POST") => "PUBLIC_CREDENTIAL_EXCHANGE",
        ("/api/auth/register-by-invite", "POST") or ("/api/invites/accept", "POST") => "PUBLIC_INVITE_CREDENTIAL_EXCHANGE",
        ("/api/invites/validate", "GET") => "PUBLIC_INVITE_TOKEN_VALIDATION",
        ("/api/auth/status", "GET") => "PUBLIC_AUTHENTICATION_STATUS",
        ("/api/security/csrf-token", "GET") => "PUBLIC_ANTIFORGERY_BOOTSTRAP",
        ("/api/ui/runtime-config.js", "GET") => "PUBLIC_FEATURE_FLAG_SCRIPT",
        ("/health/live", "GET") => "PUBLIC_LIVENESS",
        ("/health/ready", "GET") => "PUBLIC_DEPENDENCY_READINESS",
        ("/health/realtime", "GET") or ("/health/task-deadline-digests", "GET") => "PUBLIC_AGGREGATE_WORKER_DIAGNOSTICS",
        ("/api/projects/{projectId}/gantt", "GET") or
        ("/api/tasks/{taskItemId}/dependencies", "GET" or "POST") or
        ("/api/tasks/{taskItemId}/dependencies/{dependencyId}", "DELETE") or
        ("/api/tasks/{taskItemId}/progress", "PATCH") or
        ("/api/tasks/{taskItemId}/schedule", "PATCH") => "APPLICATION_OWNED_AUTHENTICATION_AND_RESOURCE_AUTHORIZATION",
        ("/", "GET") or ("/health", "GET") or ("/favicon.ico", "GET") => "PUBLIC_NON_OPENAPI_ENTRY_OR_REDIRECT",
        _ => "UNKNOWN_REQUIRES_REVIEW"
    };

    private static string Authority(string path, string method) => (path, method) switch
    {
        ("/api/auth/login", "POST") => "AUTH_SERVICE_PASSWORD_AND_ACCOUNT_STATE",
        ("/api/auth/register-by-invite", "POST") or ("/api/invites/accept", "POST") or
        ("/api/invites/validate", "GET") => "AUTH_SERVICE_CURRENT_HASHED_INVITE_SCOPE_AND_EXPIRY",
        ("/api/projects/{projectId}/gantt", "GET") => "PLANNING_SERVICE_CURRENT_ACTOR_AND_PROJECT_AUTHORITY",
        ("/api/tasks/{taskItemId}/dependencies", "GET" or "POST") or
        ("/api/tasks/{taskItemId}/dependencies/{dependencyId}", "DELETE") => "PROJECT_SERVICE_CURRENT_ACTOR_AND_TASK_PROJECT_AUTHORITY",
        ("/api/tasks/{taskItemId}/progress", "PATCH") or ("/api/tasks/{taskItemId}/schedule", "PATCH") =>
            "TASK_COMMAND_SERVICE_CURRENT_ACTOR_AND_TASK_PROJECT_AUTHORITY",
        _ => "NO_AUTHENTICATION_REQUIREMENT_OBSERVED"
    };

    private static string Source(string path) => path switch
    {
        "/api/auth/login" or "/api/auth/register-by-invite" or "/api/auth/status" => "src/Coglatas.Web/Controllers/AuthController.cs",
        "/api/invites/accept" or "/api/invites/validate" => "src/Coglatas.Web/Controllers/InvitesController.cs",
        "/api/security/csrf-token" => "src/Coglatas.Web/Controllers/SecurityController.cs",
        "/api/projects/{projectId}/gantt" => "src/Coglatas.Web/Controllers/PlanningController.cs",
        "/api/tasks/{taskItemId}/dependencies" or "/api/tasks/{taskItemId}/dependencies/{dependencyId}" or
        "/api/tasks/{taskItemId}/progress" or "/api/tasks/{taskItemId}/schedule" => "src/Coglatas.Web/Controllers/ProjectsController.cs",
        _ => "src/Coglatas.Web/Program.cs"
    };
}
