using Coglatas.Web.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Coglatas.Tests.OpenApi;

public sealed class SecurityOpenApiOperationTransformerTests
{
    [Fact]
    public async Task Boolean_query_schema_describes_only_runtime_boolean_wire_spellings()
    {
        var query = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        var header = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        var text = new OpenApiSchema { Type = JsonSchemaType.String };
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter { Name = "Archived", In = ParameterLocation.Query, Schema = query },
                new OpenApiParameter { Name = "Flag", In = ParameterLocation.Header, Schema = header },
                new OpenApiParameter { Name = "Search", In = ParameterLocation.Query, Schema = text }
            ]
        };

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, CreateContext(), CancellationToken.None);

        Assert.Equal(JsonSchemaType.Boolean | JsonSchemaType.String, query.Type);
        Assert.NotNull(query.Pattern);
        foreach (var value in new[] { "true", "false", "TRUE", "FALSE", "True", "False", " true ", "\tFALSE\r\n", "\0false\0", "\u0085false\u0085", "\u00a0true\u3000" })
        {
            Assert.Matches(query.Pattern, value);
        }
        foreach (var value in new[] { "", "0", "1", "null", "invalid", "false-extra", "\u001cfalse\u001c", "\ufefffalse\ufeff" })
        {
            Assert.DoesNotMatch(query.Pattern, value);
        }
        Assert.Equal(JsonSchemaType.Boolean, header.Type);
        Assert.Null(header.Pattern);
        Assert.Equal(JsonSchemaType.String, text.Type);
        Assert.Null(text.Pattern);
    }

    [Fact]
    public async Task Public_operation_without_authorization_metadata_emits_explicit_empty_security()
    {
        var operation = new OpenApiOperation();
        var context = CreateContext();

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Fact]
    public async Task AllowAnonymous_emits_explicit_empty_operation_security()
    {
        var operation = new OpenApiOperation();
        var context = CreateContext(new AllowAnonymousAttribute());

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Fact]
    public async Task AllowAnonymous_overrides_authorization_security_requirement()
    {
        var operation = new OpenApiOperation();
        var context = CreateContext(new AuthorizeAttribute(), new AllowAnonymousAttribute());

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Theory]
    [InlineData("api/me/tasks")]
    [InlineData("api/me/tasks/counts")]
    [InlineData("api/auth/login")]
    public async Task Operations_document_empty_request_uri_too_long_response(string relativePath)
    {
        var operation = new OpenApiOperation();
        var context = CreateContext();
        context.Description.RelativePath = relativePath;

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        var response = Assert.IsType<OpenApiResponse>(operation.Responses!["414"]);
        Assert.False(string.IsNullOrWhiteSpace(response.Description));
        Assert.True(response.Content is null || response.Content.Count == 0);
    }

    [Theory]
    [InlineData("api/search")]
    [InlineData("api/search/message-authors")]
    public async Task Search_query_parameters_exclude_postgresql_unsafe_nul(string relativePath)
    {
        var qSchema = new OpenApiSchema { Type = JsonSchemaType.String };
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "Q",
                    In = ParameterLocation.Query,
                    Schema = qSchema
                }
            ]
        };
        var context = CreateContext();
        context.Description.RelativePath = relativePath;

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.Equal("^[^\\u0000]*$", qSchema.Pattern);
    }

    [Fact]
    public async Task Missing_http_method_does_not_apply_search_query_pattern()
    {
        var qSchema = new OpenApiSchema { Type = JsonSchemaType.String };
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "Q",
                    In = ParameterLocation.Query,
                    Schema = qSchema
                }
            ]
        };
        var context = CreateContext();
        context.Description.HttpMethod = null;
        context.Description.RelativePath = "api/search";

        await new SecurityOpenApiOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.Null(qSchema.Pattern);
    }

    private static OpenApiOperationTransformerContext CreateContext(params object[] endpointMetadata) =>
        new()
        {
            DocumentName = "v1",
            Document = new OpenApiDocument(),
            ApplicationServices = null!,
            Description = new ApiDescription
            {
                HttpMethod = "GET",
                ActionDescriptor = new ActionDescriptor
                {
                    EndpointMetadata = endpointMetadata.ToList(),
                    Parameters = []
                }
            }
        };
}
