using Coglatas.Web.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Coglatas.Web.OpenApi;

public sealed class AqProtectedIdempotencyOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<RequiresAqProtectedIdempotencyKeyAttribute>().Any())
            return Task.CompletedTask;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = AqProtectedIdempotencyKey.Name,
            In = ParameterLocation.Header,
            Required = true,
            Description = "Use a fresh key for each intent and reuse it when retrying that intent.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = AqProtectedIdempotencyKey.MinimumLength,
                MaxLength = AqProtectedIdempotencyKey.MaximumLength,
                Pattern = "[!-~]",
                Not = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "[^ -~]" }
            }
        });

        return Task.CompletedTask;
    }
}
