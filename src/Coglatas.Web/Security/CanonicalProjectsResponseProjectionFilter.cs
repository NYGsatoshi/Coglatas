using Coglatas.Application.Projects;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Coglatas.Web.Security;

/// <summary>
/// ProjectsController has a large compatibility surface. Enforce the canonical
/// response projection once at the MVC result boundary so individual actions
/// cannot accidentally return a successful DTO without FieldAccessPolicy.
/// Task File responses use the stricter FileMetadata profile because filename,
/// uploader, target label, and classification are independently protected even
/// after record-level Task authorization succeeds.
/// </summary>
public sealed class CanonicalProjectsResponseProjectionFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (context.Controller is ProjectsController &&
            context.Result is ObjectResult objectResult &&
            objectResult.Value is not null &&
            IsSuccess(objectResult.StatusCode))
        {
            objectResult.Value = CanonicalRedactionProjection.Apply(
                context.HttpContext,
                objectResult.Value,
                ProfileFor(objectResult.Value),
                "ProjectsController",
                RedactionAuthorizationState.Allowed);
        }

        await next();
    }

    public static RedactionProfile ProfileFor(object value) => value switch
    {
        TaskFileAssociationResponse or TaskFileAssociationPage => RedactionProfile.FileMetadata,
        _ => RedactionProfile.UiDetail
    };

    private static bool IsSuccess(int? statusCode)
    {
        var status = statusCode ?? StatusCodes.Status200OK;
        return status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
    }
}
