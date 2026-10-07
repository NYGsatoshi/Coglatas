using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Coglatas.Web.Extensions;
using Coglatas.Web.Models;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Web;

public sealed class PublicTraceIdTests
{
    private const string SyntheticParent = "00-abcdefab4111111111111111cdefabcd-abcdef1234567890-01";

    [Fact]
    public void VersionedRepresentationMatchesPinnedNativeClassifierControl() =>
        Assert.Equal("trace-v1:MDAtYWJj.ZGVmYWI0.MTExMTEx.MTExMTEx.MTExY2Rl.ZmFiY2Qt.YWJjZGVm.MTIzNDU2.Nzg5MC0w.MQ",
            PublicTraceId.Encode(SyntheticParent));

    [Theory]
    [InlineData(SyntheticParent)]
    [InlineData("|upstream.4111111111111111.span.")]
    [InlineData("native-request-123:00000001")]
    [InlineData("trace-v1:4111111111111111")]
    [InlineData("unicode-é-🙂")]
    public void EncodingPreservesEveryByteAndSeparatesNumericSequences(string raw)
    {
        var encoded = PublicTraceId.Encode(raw);
        Assert.Equal(raw, Decode(encoded));
        Assert.Equal(encoded, PublicTraceId.Encode(raw));
        Assert.Matches(@"^trace-v1:[A-Za-z0-9_-]{1,8}(?:\.[A-Za-z0-9_-]{1,8})*$", encoded);
        Assert.DoesNotMatch(@"[0-9]{12}", encoded);
        Assert.InRange(encoded.Length, 1, 256);
    }

    [Fact]
    public void InvalidUtf16IsRejectedRatherThanSilentlyReinterpreted() =>
        Assert.Throws<EncoderFallbackException>(() => PublicTraceId.Encode("\ud800"));

    [Fact]
    public void CanonicalErrorRetainsFullActivityAndUnchangedErrorSemantics()
    {
        using var activity = TracedRequest();
        var rawId = activity.Id!;
        var context = new DefaultHttpContext { TraceIdentifier = "server-request" };
        var error = ApiEnvelope.Error(context, 400, "ValidationFailed", "Invalid input.", "query");
        Assert.Equal(rawId, Decode(error.TraceId));
        Assert.Equal(rawId, Activity.Current!.Id);
        Assert.Equal("server-request", error.RequestId);
        Assert.Equal(400, error.Status);
        Assert.Equal("ValidationFailed", error.Error.Code);
        Assert.Equal("Invalid input.", error.Error.Message);
        Assert.Equal("query", error.Error.Target);
        Assert.False(error.Error.RedactionApplied);
    }

    [Fact]
    public void MissingActivityPreservesServerFallbackIdentifier()
    {
        var prior = Activity.Current;
        try
        {
            Activity.Current = null;
            var context = new DefaultHttpContext { TraceIdentifier = "native-request:00000001" };
            Assert.Equal(context.TraceIdentifier, Decode(PublicTraceId.ForResponse(context)));
        }
        finally { Activity.Current = prior; }
    }

    [Fact]
    public void DefaultMvcProblemDetailsRetainsTraceWhileOnlyTraceRepresentationChanges()
    {
        using var activity = TracedRequest();
        using var provider = Services();
        var context = new DefaultHttpContext { RequestServices = provider, TraceIdentifier = "server-request" };
        var details = provider.GetRequiredService<ProblemDetailsFactory>().CreateValidationProblemDetails(
            context, new ModelStateDictionary(), 400);
        Assert.Equal(activity.Id, details.Extensions["traceId"]);
        details.Extensions["otherData"] = "4111111111111111";
        ApplyFilter(context, details);
        Assert.Equal(activity.Id, Decode(Assert.IsType<string>(details.Extensions["traceId"])));
        Assert.Equal("4111111111111111", details.Extensions["otherData"]);
        Assert.Equal(400, details.Status);
        Assert.False(string.IsNullOrEmpty(details.Type));
    }

    [Fact]
    public void CustomRadialValidationKeepsFieldOwnershipAndNeverReflectsQuery()
    {
        using var activity = TracedRequest();
        using var provider = Services();
        var context = new DefaultHttpContext { RequestServices = provider, TraceIdentifier = "server-request" };
        context.Request.Path = "/api/ui/radial-menu";
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        action.ModelState.SetModelValue("contextId", new ValueProviderResult("4111111111111111"));
        action.ModelState.AddModelError("contextId", "The value '4111111111111111' is not valid for contextId.");
        var options = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;
        var result = Assert.IsType<BadRequestObjectResult>(options.InvalidModelStateResponseFactory(action));
        var details = Assert.IsType<ValidationProblemDetails>(result.Value);
        ApplyFilter(context, details);
        Assert.Equal(activity.Id, Decode(Assert.IsType<string>(details.Extensions["traceId"])));
        Assert.Equal("The supplied value is invalid.", Assert.Single(details.Errors["contextId"]));
        Assert.Equal(400, result.StatusCode);
        Assert.DoesNotContain("4111111111111111", JsonSerializer.Serialize(details), StringComparison.Ordinal);
    }

    [Fact]
    public void FilterIsRegisteredForMvcAndNeverAddsOrChangesUnownedTraceFields()
    {
        using var provider = Services();
        Assert.Contains(provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters,
            f => f is TypeFilterAttribute { ImplementationType: var type } && type == typeof(PublicTraceIdResultFilter));
        var context = new DefaultHttpContext();
        var missing = new ProblemDetails();
        ApplyFilter(context, missing);
        Assert.False(missing.Extensions.ContainsKey("traceId"));
        var numeric = new ProblemDetails();
        numeric.Extensions["traceId"] = 123;
        ApplyFilter(context, numeric);
        Assert.Equal(123, numeric.Extensions["traceId"]);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWebServices(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static Activity TracedRequest() => new Activity("public-trace-test")
        .SetIdFormat(ActivityIdFormat.W3C).SetParentId(SyntheticParent).Start();

    private static string Decode(string encoded) => Encoding.UTF8.GetString(
        WebEncoders.Base64UrlDecode(encoded["trace-v1:".Length..].Replace(".", string.Empty)));

    private static void ApplyFilter(HttpContext context, ProblemDetails details)
    {
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        new PublicTraceIdResultFilter().OnResultExecuting(new ResultExecutingContext(
            action, new List<IFilterMetadata>(), new BadRequestObjectResult(details), new object()));
    }
}
