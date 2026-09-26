using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Coglatas.Web.Security;

/// <summary>
/// Rejects query-string keys that an action did not explicitly model. ASP.NET
/// Core normally ignores those keys, which lets malformed requests appear to
/// succeed and makes the published API contract less useful as a boundary.
/// </summary>
public sealed class StrictQueryParameterFilter : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var parameters = AllowedQueryParameters(context.ActionDescriptor);
        var query = context.HttpContext.Request.Query;
        if (query.Keys.Any(key => !parameters.ContainsKey(key)) ||
            query.Any(pair => parameters.TryGetValue(pair.Key, out var type) &&
                              RequiresNonEmptyWireValue(type) &&
                              pair.Value.Any(string.IsNullOrWhiteSpace)))
        {
            var modelState = new ModelStateDictionary();
            modelState.AddModelError(string.Empty, "The query contains an unsupported or invalid parameter.");
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(modelState));
            return Task.CompletedTask;
        }

        return next();
    }

    private static Dictionary<string, Type> AllowedQueryParameters(ActionDescriptor descriptor)
    {
        var allowed = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        if (descriptor is not ControllerActionDescriptor action)
            return allowed;

        foreach (var parameter in action.Parameters.OfType<ControllerParameterDescriptor>())
        {
            var fromQuery = parameter.BindingInfo?.BindingSource?.CanAcceptDataFrom(BindingSource.Query) == true ||
                            parameter.ParameterInfo.GetCustomAttribute<FromQueryAttribute>() is not null;
            if (!fromQuery)
                continue;

            var attribute = parameter.ParameterInfo.GetCustomAttribute<FromQueryAttribute>();
            var parameterName = attribute?.Name ?? parameter.BindingInfo?.BinderModelName ?? parameter.Name;
            var parameterType = parameter.ParameterInfo.ParameterType;
            if (IsSimpleQueryType(parameterType))
            {
                allowed[parameterName] = parameterType;
                continue;
            }

            foreach (var property in parameterType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                // Derived display/safety properties (for example SafePage)
                // have no setter and are not request inputs.
                if (!property.CanRead || !property.CanWrite)
                    continue;

                var propertyAttribute = property.GetCustomAttribute<FromQueryAttribute>();
                var propertyName = propertyAttribute?.Name ?? property.Name;
                allowed[propertyName] = property.PropertyType;
                allowed[$"{parameterName}.{propertyName}"] = property.PropertyType;
            }
        }

        return allowed;
    }

    private static bool IsSimpleQueryType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(Guid) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(DateOnly) ||
               type == typeof(TimeOnly) ||
               type == typeof(decimal);
    }

    private static bool RequiresNonEmptyWireValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(Guid) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(DateOnly) ||
               type == typeof(TimeOnly) ||
               type == typeof(decimal);
    }
}
