using System.Reflection;
using System.Text.Json;
using Coglatas.Web.Controllers;
using Coglatas.Web.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqManagementOpenApiTests
{
    [Fact]
    public async Task PublishesOperatorResponsesWithWireStatusesEnvelopesAndQrMedia()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new OperatorControllers());
        });
        builder.Services.AddOpenApi(options => options.AddOperationTransformer<SecurityOpenApiOperationTransformer>());
        await using var host = builder.Build();
        host.MapControllers();
        host.MapOpenApi();
        await host.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
        using var document = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var operations = paths.EnumerateObject().SelectMany(path => path.Value.EnumerateObject()).ToArray();
        Assert.Equal(33, operations.Length);
        foreach (var operation in operations)
        {
            var responses = operation.Value.GetProperty("responses");
            Assert.NotEqual(responses.TryGetProperty("200", out _), responses.TryGetProperty("201", out _));
            Assert.True(JsonSchema(root, responses.GetProperty("400")).TryGetProperty("error", out _));
        }
        var forms = paths.GetProperty("/api/v1/core/forms");
        var create = forms.GetProperty("post").GetProperty("responses");
        Assert.False(create.TryGetProperty("200", out _));
        Assert.True(EnvelopeData(root, create, "201").TryGetProperty("versionNo", out _));
        Assert.True(JsonSchema(root, create.GetProperty("409")).TryGetProperty("error", out _));
        Assert.True(JsonSchema(root, create.GetProperty("413")).TryGetProperty("code", out _));
        Assert.True(create.GetProperty("415").GetProperty("content").TryGetProperty("application/problem+json", out _));
        Assert.True(EnvelopeData(root, forms.GetProperty("get").GetProperty("responses"), "200").TryGetProperty("items", out _));

        const string presentations = "/api/v1/core/events/{eventId}/presentations";
        var preview = paths.GetProperty(presentations + "/import-preview").GetProperty("post");
        Assert.True(preview.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        Assert.True(EnvelopeData(root, preview.GetProperty("responses"), "200").TryGetProperty("proposedAssignments", out _));
        var commit = paths.GetProperty(presentations + "/import-commit").GetProperty("post").GetProperty("responses");
        Assert.True(EnvelopeData(root, commit, "200").TryGetProperty("assignmentsApplied", out _));
        Assert.True(JsonSchema(root, commit.GetProperty("409")).TryGetProperty("error", out _));

        var rotate = paths.GetProperty("/api/v1/core/events/{eventId}/program-code/rotate")
            .GetProperty("post").GetProperty("responses");
        Assert.True(EnvelopeData(root, rotate, "200").TryGetProperty("entryRef", out _));
        var qr = paths.GetProperty("/api/v1/core/survey-campaigns/{campaignId}/qr-export")
            .GetProperty("get").GetProperty("responses");
        var media = qr.GetProperty("200").GetProperty("content");
        Assert.Equal(new[] { "application/pdf", "image/png", "image/svg+xml" },
            media.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
        foreach (var item in media.EnumerateObject())
        {
            var schema = Resolve(root, item.Value.GetProperty("schema"));
            Assert.Equal("string", schema.GetProperty("type").GetString());
            Assert.Equal("binary", schema.GetProperty("format").GetString());
        }
        Assert.True(JsonSchema(root, qr.GetProperty("413")).TryGetProperty("error", out _));
    }

    private static JsonElement EnvelopeData(JsonElement root, JsonElement responses, string status)
    {
        var envelope = JsonSchema(root, responses.GetProperty(status));
        Assert.True(envelope.TryGetProperty("requestId", out _));
        Assert.True(envelope.TryGetProperty("warnings", out _));
        return Resolve(root, envelope.GetProperty("data")).GetProperty("properties");
    }

    private static JsonElement JsonSchema(JsonElement root, JsonElement response) =>
        Resolve(root, response.GetProperty("content").GetProperty("application/json").GetProperty("schema"))
            .GetProperty("properties");

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var component = root.GetProperty("components").GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/')[^1]);
            return Resolve(root, component);
        }
        if (schema.TryGetProperty("oneOf", out var variants))
        {
            var dto = Assert.Single(variants.EnumerateArray(), item => item.TryGetProperty("$ref", out _));
            Assert.Contains(variants.EnumerateArray(), item =>
                item.TryGetProperty("type", out var type) && type.GetString() == "null");
            return Resolve(root, dto);
        }
        return schema;
    }

    private sealed class OperatorControllers : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "AQ operator controllers";

        public IEnumerable<TypeInfo> Types =>
        [
            typeof(AqSurveyManagementController).GetTypeInfo(),
            typeof(AqRosterImportController).GetTypeInfo(),
            typeof(AqSurveyEntryCodesController).GetTypeInfo()
        ];
    }
}
