using System.Collections.Concurrent;
using System.Net;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqRequestLogPrivacyHostedTests
{
    [Fact]
    public async Task NativeKestrelLogsAndScopesProtectAqRequestsAndRetainOrdinaryRequestContext()
    {
        using var sink = new CapturingSink();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(sink);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Debug);
        builder.Services.AddAqRequestLogPrivacy();

        await using var app = builder.Build();
        app.MapPost("/api/v1/public/surveys/{code}/submissions", (SyntheticResponse response) =>
            string.IsNullOrWhiteSpace(response.Text) || string.IsNullOrWhiteSpace(response.Name) ? Results.BadRequest() : Results.Ok());
        app.MapGet("/api/v1/public/events/{code}/presentations", () => Results.Ok());
        app.MapGet("/app/aq/surveys/{code}", () => Results.Ok());
        app.MapGet("/api/v1/core/presentations/{id:guid}/feedback", () => Results.Ok());
        app.MapGet("/api/v1/core/ordinary/{value}", () => Results.Ok());
        await app.StartAsync();

        using var client = new HttpClient();
        client.BaseAddress = new Uri(app.Urls.Single());
        using var submitted = await client.PostAsJsonAsync(
            "/api/v1/public/surveys/synthetic-aq-code/submissions",
            new SyntheticResponse("synthetic-aq-answer", "synthetic-aq-name"));
        using var program = await client.GetAsync("/api/v1/public/events/synthetic-aq-program/presentations?cursor=synthetic-aq-cursor");
        using var page = await client.GetAsync("/app/aq/surveys/synthetic-aq-page");
        using var protectedFeed = await client.GetAsync(
            $"/api/v1/core/presentations/{Guid.NewGuid():D}/feedback?actor=synthetic-aq-actor");
        using var ordinary = await client.GetAsync("/api/v1/core/ordinary/ordinary-visible?search=ordinary-query");
        foreach (var response in new[] { submitted, program, page, protectedFeed, ordinary })
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
        var records = sink.Records.ToArray();
        Assert.DoesNotContain(records, record => record.Text.Contains("synthetic-aq-", StringComparison.Ordinal));
        Assert.Contains(records, record => record is { Category: "Microsoft.AspNetCore.Hosting.Diagnostics", Id: 1 } &&
            record.Text.Contains("/api/v1/public/surveys/{code}/submissions", StringComparison.Ordinal));
        Assert.Contains(records, record => record is { Category: "Microsoft.AspNetCore.Hosting.Diagnostics", Id: 2 } &&
            record.Text.Contains("/api/v1/core/{aq-resource}", StringComparison.Ordinal) &&
            record.Text.Contains("StatusCode=200", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Category == "Microsoft.AspNetCore.Routing.EndpointMiddleware" &&
            record.Text.Contains("/app/aq/{public-entry}", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Category == "Microsoft.AspNetCore.Hosting.Diagnostics" &&
            record.Text.Contains("/api/v1/core/ordinary/ordinary-visible?search=ordinary-query", StringComparison.Ordinal));
    }

    private sealed record SyntheticResponse(string Text, string Name);
    private sealed record CapturedRecord(string Category, int Id, string Text);

    private sealed class CapturingSink : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ConcurrentQueue<CapturedRecord> Records { get; } = new();

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingSink sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var evidence = new List<string> { formatter(state, exception) };
                AddPairs(state, evidence);
                sink._scopes.ForEachScope((scope, texts) =>
                {
                    if (scope?.ToString() is { } text)
                        texts.Add(text);
                    AddPairs(scope, texts);
                }, evidence);
                sink.Records.Enqueue(new CapturedRecord(category, eventId.Id, string.Join(" ", evidence)));
            }

            private static void AddPairs(object? state, List<string> evidence)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                    evidence.AddRange(pairs.Select(pair => $"{pair.Key}={pair.Value}"));
            }
        }
    }
}
