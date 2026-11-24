using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Coglatas.Web.Middleware;
using Coglatas.Web.Models;
using Coglatas.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqPublicSubmissionHeaderTests
{
    [Fact]
    public async Task ValidatesRequiredHeaderAfterCsrfAndBeforeDisabledSurveyLookup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["AQ:EnablePublicSurveys"] = "false";
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new GuestControllers());
        });
        builder.Services.AddAntiforgery(options => HttpSecurityPolicy.ConfigureAntiforgery(
            options, new SecurityOptions { CookieSecurePolicy = CookieSecurePolicy.SameAsRequest }));
        builder.Services.AddRateLimiter(HttpSecurityPolicy.ConfigureRateLimiting);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
        builder.Services.AddSingleton<IClock, SyntheticClock>();
        var store = new RecordingSurveyStore();
        builder.Services.AddSingleton<IAqPublicSurveyStore>(store);
        builder.Services.AddScoped<AqPublicSurveyService>();
        await using var host = builder.Build();
        host.UseMiddleware<SecurityHeadersMiddleware>();
        host.UseRateLimiter();
        host.MapControllers();
        await host.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/security/csrf-token");
        Assert.NotNull(csrf);

        await AssertFailureAsync(client, Submission(null, null), HttpStatusCode.Forbidden, "CsrfRejected");
        await AssertFailureAsync(client, Submission(csrf, null), HttpStatusCode.BadRequest, "MissingIdempotencyKey");
        await AssertFailureAsync(client, Submission(csrf, "short"), HttpStatusCode.BadRequest, "InvalidIdempotencyKey");
        await AssertFailureAsync(client, Submission(csrf, "synthetic-key"), HttpStatusCode.NotFound, "NotFound");
        Assert.Equal(0, store.SubmitCalls);

        host.Configuration["AQ:EnablePublicSurveys"] = "true";
        await AssertFailureAsync(client, Submission(csrf, null), HttpStatusCode.BadRequest, "MissingIdempotencyKey");
        await AssertFailureAsync(client, Submission(csrf, "synthetic-key"), HttpStatusCode.BadRequest, "MalformedJson");
        Assert.Equal(0, store.SubmitCalls);
        using var validRequest = Submission(csrf, "synthetic-key", """
            {"versionRef":"synthetic-version","schemaDigest":"0000000000000000000000000000000000000000000000000000000000000000","answers":[]}
            """);
        using var accepted = await client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.True(accepted.Headers.CacheControl?.NoStore);
        var receipt = await accepted.Content.ReadFromJsonAsync<ApiSuccessEnvelope<AqPublicAcceptance>>();
        Assert.True(receipt?.Data.Accepted);
        Assert.Equal(1, store.SubmitCalls);
    }

    private static HttpRequestMessage Submission(CsrfTokenResponse? csrf, string? key, string json = "{")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/surveys/synthetic-entry/submissions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (csrf is not null)
            request.Headers.Add(csrf.HeaderName, csrf.Token);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task AssertFailureAsync(HttpClient client, HttpRequestMessage request, HttpStatusCode status, string code)
    {
        using (request)
        {
            using var response = await client.SendAsync(request);
            Assert.Equal(status, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();
            Assert.Equal((int)status, error?.Status);
            Assert.Equal(code, error?.Error.Code);
        }
    }

    private sealed class GuestControllers : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "AQ public submission";
        public IEnumerable<TypeInfo> Types => [typeof(AqPublicSurveyController).GetTypeInfo(), typeof(SecurityController).GetTypeInfo()];
    }

    private sealed class SyntheticClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class RecordingSurveyStore : IAqPublicSurveyStore
    {
        public int SubmitCalls { get; private set; }

        public Task<AqPublicProgram?> ReadProgramAsync(string codeDigest, string? cursor, int pageSize, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The submission endpoint must not read the public program.");

        public Task<AqPublicSurveyTarget?> ReadTargetAsync(string codeDigest, AqPublicSession? session, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The submission endpoint must not read a public form projection.");

        public Task<AqPublicCommandResult> SubmitAsync(
            string codeDigest, string keyDigest, AqPublicSession? session,
            Func<AqPublicSurveyTarget, SubmissionIdempotency?, AqPublicWriteDecision> decide,
            CancellationToken cancellationToken)
        {
            SubmitCalls++;
            return Task.FromResult(AqPublicCommandResult.Accept(new AqPublicReceipt("synthetic-receipt")));
        }
    }
}
