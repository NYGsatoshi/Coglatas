using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Coglatas.Web.Security;

/// <summary>
/// Covers both custom validation and MVC's default ProblemDetails factory,
/// including client-error results that skip ordinary result filters.
/// </summary>
public sealed class PublicTraceIdResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails details } &&
            details.Extensions.TryGetValue("traceId", out var value) && value is string traceId)
        {
            details.Extensions["traceId"] = PublicTraceId.Encode(traceId);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
