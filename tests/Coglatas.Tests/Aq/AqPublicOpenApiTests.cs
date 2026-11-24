using Coglatas.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqPublicOpenApiTests
{
    [Fact]
    public async Task SubmissionDescribesRequiredHeadersAndClosedTypedPayloadWithOptionalAuthentication()
    {
        var operation = new OpenApiOperation();
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Document = new OpenApiDocument(),
            ApplicationServices = null!,
            Description = new ApiDescription
            {
                HttpMethod = "POST",
                RelativePath = "api/v1/public/surveys/{code}/submissions",
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [], Parameters = [] }
            }
        };

        await new AqPublicOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.Equal("submitPublicSurvey", operation.OperationId);
        Assert.NotNull(operation.Security);
        Assert.Contains(operation.Security, requirement => requirement.Count == 0);
        Assert.Contains(operation.Security, requirement => requirement.Count == 1);
        Assert.NotNull(operation.Parameters);
        var csrf = Assert.Single(operation.Parameters, parameter => parameter.Name == "X-CSRF-Token");
        Assert.True(csrf.Required);
        Assert.Equal(ParameterLocation.Header, csrf.In);
        var idempotency = Assert.Single(operation.Parameters, parameter => parameter.Name == "Idempotency-Key");
        Assert.True(idempotency.Required);
        Assert.Equal(ParameterLocation.Header, idempotency.In);
        var key = Assert.IsType<OpenApiSchema>(idempotency.Schema);
        Assert.Equal(8, key.MinLength);
        Assert.Equal(128, key.MaxLength);
        Assert.Equal("[!-~]", key.Pattern);

        var request = Assert.IsType<OpenApiRequestBody>(operation.RequestBody);
        Assert.True(request.Required);
        Assert.NotNull(request.Content);
        var schema = Assert.IsType<OpenApiSchema>(request.Content["application/json"].Schema);
        Assert.False(schema.AdditionalPropertiesAllowed);
        Assert.NotNull(schema.Required);
        Assert.Equal(new[] { "answers", "schemaDigest", "versionRef" }, schema.Required.Order(StringComparer.Ordinal));
        Assert.NotNull(schema.Properties);
        Assert.Equal("^[0-9a-f]{64}$", Assert.IsType<OpenApiSchema>(schema.Properties["schemaDigest"]).Pattern);
        var answers = Assert.IsType<OpenApiSchema>(schema.Properties["answers"]);
        var answer = Assert.IsType<OpenApiSchema>(answers.Items);
        Assert.False(answer.AdditionalPropertiesAllowed);
        Assert.NotNull(answer.Properties);
        var value = Assert.IsType<OpenApiSchema>(answer.Properties["value"]);
        Assert.NotNull(value.OneOf);
        Assert.Equal(new[] { JsonSchemaType.String, JsonSchemaType.Number, JsonSchemaType.Boolean, JsonSchemaType.Array },
            value.OneOf.Select(option => Assert.IsType<OpenApiSchema>(option).Type.GetValueOrDefault()));
        Assert.True(Assert.IsType<OpenApiSchema>(value.OneOf[^1]).UniqueItems);
    }
}
