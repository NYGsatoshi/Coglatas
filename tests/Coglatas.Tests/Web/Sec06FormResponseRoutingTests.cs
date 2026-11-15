using System.Net;
using System.Text.Json;
using Coglatas.Web;
using Coglatas.Web.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coglatas.Tests.Web;

[Trait("Portability", "CrossPlatform")]
public sealed class Sec06FormResponseRoutingTests
{
    // Issue #1685: This proves only actual controller route selection and the
    // SPA fallback status, not any historical scanner detector attribution.
    [Fact]
    public async Task LiteralFormIdPlaceholderCannotSelectGuidConstrainedResponsesAction()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "coglatas-sec06-form-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html>synthetic marker</html>");
        File.WriteAllText(Path.Combine(webRoot, AngularSpaFallback.BuildMarkerFileName), "marker");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Testing
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(FormsController).Assembly);

            await using var app = builder.Build();
            app.UseRouting();
            var admittedActionCalls = 0;
            app.Use(async (context, next) =>
            {
                var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                if (action is not null &&
                    action.ControllerTypeInfo.AsType() == typeof(FormsController) &&
                    action.ActionName == nameof(FormsController.ListResponses))
                {
                    admittedActionCalls++;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        selectedAction = nameof(FormsController.ListResponses),
                        routeFormId = context.Request.RouteValues["formId"]?.ToString()
                    });
                    return;
                }
                await next();
            });
            app.MapControllers();
            AngularSpaFallback.MapEndpointFallback(app, webRoot);

            await app.StartAsync();
            try
            {
                var addresses = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses;
                // ReSharper disable once ShortLivedHttpClient
                using var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };
                foreach (var query in new[] { "page=1", "page=2", "page=0", "page=999" })
                {
                    using var response = await client.GetAsync("/api/forms/formId/responses?" + query);
                    var body = await response.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
                    Assert.Contains("\"code\":\"NotFound\"", body);
                    Assert.DoesNotContain("<html>", body, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(0, admittedActionCalls);
                }

                var validId = Guid.Parse("11111111-1111-4111-8111-111111111111");
                using var positive = await client.GetAsync($"/api/forms/{validId:D}/responses?page=1&pageSize=20");
                Assert.Equal(HttpStatusCode.OK, positive.StatusCode);
                using var document = JsonDocument.Parse(await positive.Content.ReadAsStringAsync());
                Assert.Equal(nameof(FormsController.ListResponses),
                    document.RootElement.GetProperty("selectedAction").GetString());
                Assert.Equal(validId, Guid.Parse(document.RootElement.GetProperty("routeFormId").GetString()!));
                Assert.Equal(1, admittedActionCalls);
            }
            finally
            {
                await app.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
