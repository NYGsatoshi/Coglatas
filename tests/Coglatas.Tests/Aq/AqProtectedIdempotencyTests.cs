using System.Reflection;
using System.Text.Json;
using Coglatas.Web.Controllers;
using Coglatas.Web.Models;
using Coglatas.Web.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqProtectedIdempotencyTests
{
    private static readonly HttpClient Http = new();

    [Fact]
    public async Task GeneratedDocumentRequiresTheKeyOnlyOnTheThreeProtectedCommands()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new ProtectedControllers());
        });
        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<SecurityOpenApiOperationTransformer>();
            options.AddOperationTransformer<AqProtectedIdempotencyOpenApiOperationTransformer>();
        });
        await using var host = builder.Build();
        host.MapControllers();
        host.MapOpenApi();
        await host.StartAsync();

        var documentUri = new Uri(new Uri(host.Urls.Single()), "/openapi/v1.json");
        using var document = JsonDocument.Parse(await Http.GetStringAsync(documentUri));
        var paths = document.RootElement.GetProperty("paths");
        const string api = "/api/v1/core";
        const string roster = api + "/events/{eventId}/presentations";
        const string export = api + "/presentation-feedback-exports/{id}";
        AssertRequiredKey(paths.GetProperty(roster + "/import-commit").GetProperty("post"));
        AssertRequiredKey(paths.GetProperty(api + "/presentations/{id}/feedback-export").GetProperty("post"));
        AssertRequiredKey(paths.GetProperty(export + "/download-grants").GetProperty("post"));
        Assert.Empty(IdempotencyHeaders(paths.GetProperty(roster + "/import-preview").GetProperty("post")));
        Assert.Empty(IdempotencyHeaders(paths.GetProperty(export).GetProperty("get")));
        Assert.Empty(IdempotencyHeaders(paths.GetProperty(export + "/download-grants/{grantId}/content").GetProperty("get")));
    }

    private static void AssertRequiredKey(JsonElement operation)
    {
        var header = Assert.Single(IdempotencyHeaders(operation));
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        var schema = header.GetProperty("schema");
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(8, schema.GetProperty("minLength").GetInt32());
        Assert.Equal(128, schema.GetProperty("maxLength").GetInt32());
        Assert.Equal("[!-~]", schema.GetProperty("pattern").GetString());
        Assert.Equal("[^ -~]", schema.GetProperty("not").GetProperty("pattern").GetString());
    }

    private static IEnumerable<JsonElement> IdempotencyHeaders(JsonElement operation) =>
        operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Where(parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key")
            : [];

    private sealed class ProtectedControllers : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "AQ protected idempotency controllers";
        public IEnumerable<TypeInfo> Types =>
            [typeof(AqRosterImportController).GetTypeInfo(), typeof(FeedbackCsvController).GetTypeInfo()];
    }

    [Theory]
    [InlineData(null, "MissingIdempotencyKey")]
    [InlineData("        ", "MissingIdempotencyKey")]
    [InlineData("seven77", "InvalidIdempotencyKey")]
    [InlineData("key-🙂-1", "InvalidIdempotencyKey")]
    public Task CommandsRejectInvalidHeaderBeforeCallingServices(string? key, string code) =>
        AssertCommandErrorsAsync(new StringValues(key), code);

    [Fact]
    public Task CommandsRejectRepeatedHeaderBeforeCallingServices() =>
        AssertCommandErrorsAsync(new StringValues(["key-0001", "key-0002"]), "InvalidIdempotencyKey");

    [Fact]
    public Task CommandsRejectOversizeHeaderBeforeCallingServices() =>
        AssertCommandErrorsAsync(new StringValues(new string('x', 129)), "InvalidIdempotencyKey");

    private static async Task AssertCommandErrorsAsync(StringValues key, string code)
    {
        var context = new DefaultHttpContext();
        if (key.Count > 0)
            context.Request.Headers["Idempotency-Key"] = key;
        var controllerContext = new ControllerContext { HttpContext = context };
        var roster = new AqRosterImportController(null!, new EphemeralDataProtectionProvider())
        {
            ControllerContext = controllerContext
        };
        var csv = new FeedbackCsvController(null!) { ControllerContext = controllerContext };
        var id = Guid.NewGuid();

        AssertHeaderError(await roster.Commit(id, new("preview-ref", [], []), CancellationToken.None), code);
        AssertHeaderError(await csv.Create(id, new("summary", ["rating"]), CancellationToken.None), code);
        AssertHeaderError(await csv.CreateGrant(id, CancellationToken.None), code);
    }

    private static void AssertHeaderError(IActionResult result, string code)
    {
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var envelope = Assert.IsType<ApiErrorEnvelope>(response.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, envelope.Status);
        Assert.Equal(code, envelope.Error.Code);
        Assert.Equal("header.Idempotency-Key", envelope.Error.Target);
    }
}
