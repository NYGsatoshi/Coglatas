using System.Text.Json;
using Coglatas.Web.Controllers;
using Coglatas.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coglatas.Tests.Auth;

public sealed class GlobalExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task UnhandledExceptionResponseDoesNotExposeExceptionDetails()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Password=leaked; C:\\internal\\path; SELECT * FROM Users"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-for-test",
            Response =
            {
                Body = new MemoryStream()
            }
        };

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("InternalServerError", root.GetProperty("code").GetString());
        Assert.Equal("An unexpected server error occurred.", root.GetProperty("message").GetString());
        Assert.Equal("trace-for-test", root.GetProperty("traceId").GetString());

        var body = root.GetRawText();
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\internal\\path", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaked", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedMultipartModelBindingReturnsBadRequest()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new ValueProviderException("malformed multipart form"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "multipart-trace",
            Response = { Body = new MemoryStream() },
            Request =
            {
                Method = HttpMethods.Post,
                ContentType = "multipart/form-data; boundary=test-boundary",
                Path = "/api/files"
            }
        };

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("InvalidRequest", root.GetProperty("code").GetString());
        Assert.Equal("The request body is invalid.", root.GetProperty("message").GetString());
        Assert.Equal("multipart-trace", root.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "InvalidRequest", "The request body is invalid.")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, "UnsupportedMediaType", "The request Content-Type is not supported.")]
    [Trait("Feature", "AQ")]
    public async Task ProtectedAqTransportRejectionsUseTheirDocumentedBodies(
        int status, string code, string message)
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new BadHttpRequestException("Synthetic transport detail.", status),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "aq-transport-trace",
            Response = { Body = new MemoryStream() },
            Request =
            {
                Method = HttpMethods.Post,
                Path = "/api/v1/core/events/00000000-0000-0000-0000-000000000001/presentations/import-preview"
            }
        };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(typeof(AqRosterImportController).GetCustomAttributes(inherit: true)),
            "AQ roster import"));

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Contains("application/json", context.Response.ContentType);
        Assert.DoesNotContain("Synthetic transport detail.", root.GetRawText(), StringComparison.Ordinal);
        if (status == StatusCodes.Status400BadRequest)
        {
            Assert.Equal("aq-transport-trace", root.GetProperty("requestId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
            Assert.Equal(status, root.GetProperty("status").GetInt32());
            var error = root.GetProperty("error");
            Assert.Equal(code, error.GetProperty("code").GetString());
            Assert.Equal(message, error.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, error.GetProperty("target").ValueKind);
            Assert.Empty(error.GetProperty("details").EnumerateArray());
            Assert.False(error.GetProperty("redactionApplied").GetBoolean());
        }
        else
        {
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.Equal(message, root.GetProperty("message").GetString());
            Assert.Equal("aq-transport-trace", root.GetProperty("traceId").GetString());
            Assert.False(root.TryGetProperty("error", out _));
        }
    }

    [Theory]
    [InlineData("io", true)]
    [InlineData("argument", true)]
    [InlineData("format", true)]
    [InlineData("invalid-operation", true)]
    [InlineData("null-reference", true)]
    [InlineData("unsupported", true)]
    [InlineData("cancellation", true)]
    [InlineData("other", true)]
    [InlineData("other", false)]
    [Trait("Feature", "AQ")]
    public async Task SurveyUnexpectedFailuresLogOnlyFixedCategoryAndKeepRedacted500(
        string category, bool protectedRoute)
    {
        const string privateDetail = "synthetic-private-detail";
        Exception error = category switch
        {
            "io" => new IOException(privateDetail),
            "argument" => new ArgumentException(privateDetail),
            "format" => new FormatException(privateDetail),
            "invalid-operation" => new InvalidOperationException(privateDetail),
            "null-reference" => new NullReferenceException(privateDetail),
            "unsupported" => new NotSupportedException(privateDetail),
            "cancellation" => new OperationCanceledException(privateDetail),
            "other" => new Exception(privateDetail),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        var logger = new SurveyFailureLogger();
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw error, logger);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "synthetic-aq-trace",
            Response = { Body = new MemoryStream() },
            Request =
            {
                Method = HttpMethods.Post,
                Path = protectedRoute
                    ? "/api/v1/core/events/00000000-0000-0000-0000-000000000001/presentations/import-preview"
                    : "/api/v1/public/surveys/synthetic-private-entry/submissions"
            }
        };
        if (protectedRoute)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(typeof(AqRosterImportController).GetCustomAttributes(inherit: true)),
                "AQ roster import"));
        }

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Error);
        Assert.Equal($"Unhandled survey failure. Category: {category}. TraceId: synthetic-aq-trace", entry.Message);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("synthetic-aq-trace", root.GetProperty("requestId").GetString());
        Assert.Equal("UnexpectedServerError", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("An unexpected server error occurred.", root.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(privateDetail, root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-entry", root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("FailureCategory", root.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class SurveyFailureLogger : ILogger<GlobalExceptionHandlingMiddleware>
    {
        public List<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    [Fact]
    [Trait("Scope", "TaskV1PR06")]
    public async Task GanttSnapshotExceptionUsesSafePr06Envelope()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("SELECT hidden schedule"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "gantt-trace",
            Response = { Body = new MemoryStream() },
            Request =
            {
                Path = "/api/projects/00000000-0000-0000-0000-000000000001/gantt/"
            }
        };

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("gantt-trace", root.GetProperty("requestId").GetString());
        Assert.Equal("GANTT_REQUEST_FAILED", root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("SELECT", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scope", "WPC01")]
    public async Task WorkspaceCreateExceptionUsesCompleteSafeEnvelope()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Password=leaked; SELECT tenant secrets"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "wpc01-exception-request",
            Response = { Body = new MemoryStream() },
            Request =
            {
                Path = "/api/workspaces"
            }
        };

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("wpc01-exception-request", root.GetProperty("requestId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        var error = root.GetProperty("error");
        Assert.Equal("UnexpectedServerError", error.GetProperty("code").GetString());
        Assert.Equal(0, error.GetProperty("details").GetArrayLength());
        Assert.False(error.GetProperty("redactionApplied").GetBoolean());
        Assert.DoesNotContain("Password", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scope", "TaskV1PR06")]
    public async Task RequestAbortCancellationPropagatesWithoutWritingAnErrorResponse()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancellationToken = cancellation.Token;
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(cancellationToken),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellationToken,
            Response = { Body = new MemoryStream() }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }
}
