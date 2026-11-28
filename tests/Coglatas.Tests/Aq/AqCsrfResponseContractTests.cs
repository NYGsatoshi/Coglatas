using System.Net;
using System.Reflection;
using System.Text.Json;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Coglatas.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqCsrfResponseContractTests
{
    [Fact]
    public async Task CoreCsrfRejectionUsesTheDocumentedEnvelopeEvenWhenRoutingRejectsTheResourceId()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options => HttpSecurityPolicy.ConfigureAntiforgery(
            options, new SecurityOptions { CookieSecurePolicy = CookieSecurePolicy.SameAsRequest }));
        builder.Services.AddHttpClient();
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new ProgramCodeControllerPart());
        });

        await using var host = builder.Build();
        host.UseMiddleware<SecurityHeadersMiddleware>();
        host.UseRouting();
        host.UseAuthentication();
        host.UseMiddleware<CsrfProtectionMiddleware>();
        host.UseAuthorization();
        host.MapControllers();
        host.MapPost("/api/v1/core/forms/unmarked-probe", () => Results.Ok());
        await host.StartAsync();

        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        client.BaseAddress = new Uri(host.Urls.Single());
        const string route = "/api/v1/core/events";
        var knownResource = $"{route}/{Guid.NewGuid():D}/program-code/rotate";
        var malformedResource = $"{route}/malformed-resource-id/program-code/rotate";
        await AssertSurveyRejectionAsync(client, knownResource);
        await AssertSurveyRejectionAsync(client, malformedResource);

        using var unrelated = await client.PostAsync("/api/v1/core/forms/unmarked-probe", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, unrelated.StatusCode);
        using var unrelatedBody = JsonDocument.Parse(await unrelated.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, unrelatedBody.RootElement.GetProperty("error").ValueKind);

        using var unsupportedMethod = await client.PutAsync(knownResource, content: null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupportedMethod.StatusCode);
    }

    private static async Task AssertSurveyRejectionAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsync(path, content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var envelope = document.RootElement;
        Assert.False(string.IsNullOrEmpty(envelope.GetProperty("requestId").GetString()));
        Assert.False(string.IsNullOrEmpty(envelope.GetProperty("traceId").GetString()));
        Assert.Equal((int)HttpStatusCode.Forbidden, envelope.GetProperty("status").GetInt32());

        var error = envelope.GetProperty("error");
        Assert.Equal("CsrfRejected", error.GetProperty("code").GetString());
        Assert.Equal("A valid CSRF token is required.", error.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("target").ValueKind);
        Assert.Empty(error.GetProperty("details").EnumerateArray());
        Assert.False(error.GetProperty("redactionApplied").GetBoolean());
    }

    private sealed class ProgramCodeControllerPart : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "AQ program code controller";
        public IEnumerable<TypeInfo> Types => [typeof(AqSurveyEntryCodesController).GetTypeInfo()];
    }
}
