using System.Collections;
using Coglatas.Web.Models;

namespace Coglatas.Web.Security;

/// <summary>
/// Masks AQ request descriptors before the native host sends records and scopes
/// to its configured sinks. Hosting request-start logging occurs before middleware.
/// </summary>
public static class AqRequestLogPrivacy
{
    public static IServiceCollection AddAqRequestLogPrivacy(this IServiceCollection services)
    {
        services.AddLogging();
        var factory = services.Last(descriptor => descriptor.ServiceType == typeof(ILoggerFactory));
        services.Remove(factory);
        services.AddSingleton<ILoggerFactory>(provider => new RedactingLoggerFactory(
            factory.ImplementationInstance as ILoggerFactory ??
            (ILoggerFactory?)factory.ImplementationFactory?.Invoke(provider) ??
            (ILoggerFactory)ActivatorUtilities.CreateInstance(provider, factory.ImplementationType!),
            ownsFactory: factory.ImplementationInstance is null));
        return services;
    }

    private sealed class RedactingLoggerFactory(ILoggerFactory inner, bool ownsFactory) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            var logger = inner.CreateLogger(categoryName);
            return categoryName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                ? new RedactingLogger(logger)
                : logger;
        }

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose()
        {
            if (ownsFactory)
                inner.Dispose();
        }
    }

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            TryRedact(state, out var safe) ? inner.BeginScope(safe) : inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (TryRedact(state, out var safe))
            {
                // Never invoke the original formatter for a restricted path. It
                // can retain the original URI or include an exception message.
                inner.Log(logLevel, eventId, safe, exception: null, (record, _) => record.ToString());
                return;
            }

            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    private static bool TryRedact<TState>(TState state, out RedactedRequestState safe)
    {
        safe = null!;
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> fields)
            return false;

        foreach (var field in fields)
        {
            if ((field.Key is "Path" or "RequestPath" or "RawTarget") &&
                RedactedPath(field.Value?.ToString()) is { } route)
            {
                safe = new RedactedRequestState(route, fields);
                return true;
            }
        }
        return false;
    }

    private static string? RedactedPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/')
            return null;

        var end = value.IndexOfAny(['?', '#']);
        var path = new PathString(end < 0 ? value : value[..end]);
        if (path.StartsWithSegments("/api/v1/public/surveys"))
            return AqPublicRoutes.IsSurveySubmission(path)
                ? "/api/v1/public/surveys/{code}/submissions"
                : "/api/v1/public/surveys/{code}";
        if (path.StartsWithSegments("/api/v1/public/events"))
            return "/api/v1/public/events/{code}/presentations";
        if (AqPublicRoutes.IsPublicSurveyPage(path))
            return "/app/aq/{public-entry}";
        if (path.StartsWithSegments("/app/aq/feedback") || path.StartsWithSegments("/app/aq/manage"))
            return "/app/aq/{aq-workspace}";
        return AqPublicRoutes.IsCoreSurveyPath(path) ? "/api/v1/core/{aq-resource}" : null;
    }

    private sealed class RedactedRequestState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly List<KeyValuePair<string, object?>> _fields;

        public RedactedRequestState(string route, IReadOnlyList<KeyValuePair<string, object?>> original)
        {
            _fields = [new("RequestPath", route)];
            foreach (var field in original)
            {
                if (IsSafe(field))
                    _fields.Add(field);
            }
        }

        public int Count => _fields.Count;
        public KeyValuePair<string, object?> this[int index] => _fields[index];
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _fields.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() =>
            "AQ HTTP request " + string.Join(" ", _fields.Select(field => $"{field.Key}={field.Value}"));

        private static bool IsSafe(KeyValuePair<string, object?> field) => field.Key switch
        {
            "Method" => field.Value is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS",
            "StatusCode" => field.Value is int,
            "ElapsedMilliseconds" => field.Value is double or long,
            "RequestId" => field.Value is string requestId &&
                requestId.Length <= 64 && requestId.All(character => char.IsAsciiLetterOrDigit(character) || character == ':'),
            _ => false
        };
    }
}
