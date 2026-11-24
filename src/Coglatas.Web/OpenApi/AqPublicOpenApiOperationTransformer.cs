using Coglatas.Application.Forms.PublicSurveys;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Coglatas.Web.OpenApi;

/// <summary>Describes the strictly parsed public survey command.</summary>
public sealed class AqPublicOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath;
        if (path == "api/v1/public/events/{code}/presentations")
        {
            operation.OperationId = "getPublishedSurveyProgram";
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "cursor", In = ParameterLocation.Query, Schema = Text(1),
                Description = "Continuation token for this published program."
            });
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "pageSize", In = ParameterLocation.Query,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer, Minimum = "1",
                    Maximum = AqPublicSurveyService.MaximumPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            });
            return Task.CompletedTask;
        }

        if (path == "api/v1/public/surveys/{code}")
        {
            operation.OperationId = "getPublishedSurveyForm";
            AllowOptionalSession(operation, context);
            return Task.CompletedTask;
        }

        if (path != "api/v1/public/surveys/{code}/submissions" ||
            !string.Equals(context.Description.HttpMethod, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        operation.OperationId = "submitPublicSurvey";
        AllowOptionalSession(operation, context);

        operation.Parameters ??= [];
        operation.Parameters.Add(Header("X-CSRF-Token", Text(1),
            "Send the request token and matching antiforgery cookie from the guest-capable bootstrap."));
        var key = Text(8, 128, "[!-~]");
        key.Not = Text(pattern: "[^ -~]");
        operation.Parameters.Add(Header("Idempotency-Key", key,
            "Use a fresh unpredictable key for this intent and reuse it for retries."));

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType()
                {
                    Schema = ClosedObject(new Dictionary<string, IOpenApiSchema>
                    {
                        ["versionRef"] = Text(1),
                        ["schemaDigest"] = Text(64, 64, "^[0-9a-f]{64}$"),
                        ["answers"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Array,
                            Items = ClosedObject(new Dictionary<string, IOpenApiSchema>
                            {
                                ["fieldKey"] = Text(1),
                                ["value"] = new OpenApiSchema
                                {
                                    OneOf =
                                    [
                                        Text(),
                                        new OpenApiSchema { Type = JsonSchemaType.Number },
                                        new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                        new OpenApiSchema
                                        {
                                            Type = JsonSchemaType.Array, Items = Text(1), UniqueItems = true
                                        }
                                    ]
                                }
                            }, "fieldKey", "value")
                        },
                        ["name"] = Text(1),
                        ["accountOptIn"] = new OpenApiSchema { Type = JsonSchemaType.Boolean }
                    }, "versionRef", "schemaDigest", "answers")
                }
            }
        };

        return Task.CompletedTask;
    }

    private static void AllowOptionalSession(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var document = context.Document ??
            throw new InvalidOperationException("OpenAPI operation requires its document context.");
        operation.Security =
        [
            new OpenApiSecurityRequirement(),
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SecurityOpenApiOperationTransformer.CookieSchemeName, document)] = []
            }
        ];
    }

    private static OpenApiParameter Header(string name, IOpenApiSchema schema, string description) =>
        new() { In = ParameterLocation.Header, Name = name, Required = true, Schema = schema, Description = description };

    private static OpenApiSchema Text(int? minimum = null, int? maximum = null, string? pattern = null) =>
        new() { Type = JsonSchemaType.String, MinLength = minimum, MaxLength = maximum, Pattern = pattern };

    private static OpenApiSchema ClosedObject(
        Dictionary<string, IOpenApiSchema> properties, params string[] required) =>
        new()
        {
            Type = JsonSchemaType.Object, AdditionalPropertiesAllowed = false,
            Properties = properties, Required = required.ToHashSet(StringComparer.Ordinal)
        };
}
