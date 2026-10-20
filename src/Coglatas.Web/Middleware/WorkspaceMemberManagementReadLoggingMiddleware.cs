using Coglatas.Web.Configuration;

namespace Coglatas.Web.Middleware;

/// <summary>Logs finite operation outcomes without directory query or response data.</summary>
public sealed class WorkspaceMemberManagementReadLoggingMiddleware(
    RequestDelegate next, ILogger<WorkspaceMemberManagementReadLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var operation = WorkspaceMemberManagementHttpPolicy.ReadOperation(context.Request);
        if (operation is null)
        {
            await next(context);
            return;
        }

        var completed = false;
        try
        {
            await next(context);
            completed = true;
        }
        finally
        {
            logger.LogInformation(
                "Workspace member management {Operation} completed with status {StatusCode}. TraceId: {TraceId}",
                operation, completed ? context.Response.StatusCode : StatusCodes.Status500InternalServerError,
                context.TraceIdentifier);
        }
    }
}
