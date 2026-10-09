using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.SignalR;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Web.Testing;

// This private, opt-in inspection runs inside the existing Test-only AV-MIG exit.
internal static class SecurityArchitectureRuntimeInventory
{
    public static async Task WriteDraftAsync(WebApplication app, string directory, string? openApiPath,
        IEnumerable<ServiceDescriptor> registrations)
    {
        if (!app.Environment.IsEnvironment("Test"))
            throw new InvalidOperationException("SEC-ARCH inventory inspection is Test-only.");
        var provider = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var rows = new List<object>();
        var surface = new List<(string Path, string Method)>();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            var data = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();
            var policy = await AuthorizationPolicy.CombineAsync(provider, data, policies);
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var controller = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var hub = endpoint.Metadata.GetMetadata<HubMetadata>();
            var path = NormalizedPath(endpoint.RoutePattern);
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY_UNCLASSIFIED"])
            {
                surface.Add((path, method));
                rows.Add(new
                {
                    surfaceId = "SEC-ARCH-SURFACE-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + method))),
                    route = endpoint.RoutePattern.RawText, normalizedPath = path, method,
                    kind = controller is not null ? "CONTROLLER" : hub is not null ? "HUB" : "MINIMAL_OR_FALLBACK",
                    controller = controller?.ControllerTypeInfo.FullName, action = controller?.MethodInfo.Name,
                    requestContentTypes = endpoint.Metadata.OfType<ConsumesAttribute>().SelectMany(item => item.ContentTypes)
                        .Concat(endpoint.Metadata.OfType<IAcceptsMetadata>().SelectMany(item => item.ContentTypes))
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    hub = hub?.HubType.FullName, allowAnonymous = anonymous,
                    authorizationMetadata = data.Select(item => new { item.Policy, item.Roles, item.AuthenticationSchemes }).ToArray(),
                    policySource = anonymous ? "ANONYMOUS_BYPASS" : data.Count > 0 || policies.Count > 0 ? "ENDPOINT" : policy is null ? "NONE" : "FALLBACK",
                    effectivePolicy = Describe(policy),
                    authorizationRequired = !anonymous && policy is not null,
                    authenticatedUserRequired = !anonymous && policy?.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Any() == true,
                    contractClassification = "UNVERIFIED", runtimeAuthorizationOutcome = "UNVERIFIED"
                });
            }
        }
        var documentation = new List<(string Path, string Method)>();
        string? documentationDigest = null;
        if (!string.IsNullOrWhiteSpace(openApiPath))
        {
            if (new FileInfo(openApiPath).Length > 32 * 1024 * 1024)
                throw new InvalidOperationException("OpenAPI inventory input exceeds its bound.");
            var bytes = await File.ReadAllBytesAsync(openApiPath);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.GetProperty("openapi").GetString()?.StartsWith("3.", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("SEC-ARCH requires an OpenAPI 3 document.");
            documentationDigest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
                foreach (var operation in path.Value.EnumerateObject())
                    if (new[] { "get", "post", "put", "patch", "delete", "head", "options", "trace" }.Contains(operation.Name))
                        documentation.Add((path.Name, operation.Name.ToUpperInvariant()));
        }
        await using var scope = app.Services.CreateAsyncScope();
        var serviceRegistrations = registrations.ToArray();
        var report = new
        {
            schemaVersion = 1, approval = "DRAFT", catalogScope = "ACTUAL_COMPOSED_TEST_HOST",
            defaultAuthenticateScheme = (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name,
            defaultChallengeScheme = (await schemes.GetDefaultChallengeSchemeAsync())?.Name,
            defaultPolicy = Describe(await provider.GetDefaultPolicyAsync()),
            fallbackPolicy = Describe(await provider.GetFallbackPolicyAsync()),
            authorizationHandlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>()
                .Select(handler => handler.GetType().FullName).Order(StringComparer.Ordinal).ToArray(),
            endpointCount = rows.Count, endpoints = rows,
            realtime = SecurityArchitectureRealtimeInventory.Observe(),
            serviceTopology = new
            {
                hostedServices = serviceRegistrations.Where(item => item.ServiceType == typeof(IHostedService))
                    .Select(item => new
                    {
                        type = item.ImplementationType?.FullName ?? item.ImplementationInstance?.GetType().FullName,
                        factoryDeclared = item.ImplementationFactory is not null,
                        runtimeOutcome = "UNVERIFIED"
                    }).OrderBy(item => item.type, StringComparer.Ordinal).ToArray(),
                database = new
                {
                    serviceType = typeof(AppDbContext).FullName,
                    provider = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ProviderName,
                    lifetimes = serviceRegistrations.Where(item => item.ServiceType == typeof(AppDbContext))
                        .Select(item => item.Lifetime.ToString()).Distinct().ToArray(),
                    authorityCategory = "COMPOSED_COMMON_APPLICATION_DB_CONTEXT",
                    applicationRoleOutcome = "UNVERIFIED", workerRoleOutcome = "UNVERIFIED"
                },
                kafkaServiceTypes = serviceRegistrations.Where(item =>
                        item.ServiceType.FullName?.Contains("Kafka", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(item => item.ServiceType.FullName).Distinct().Order(StringComparer.Ordinal).ToArray(),
                httpClientServiceTypes = serviceRegistrations.Where(item =>
                        item.ServiceType.FullName?.Contains("HttpClient", StringComparison.Ordinal) == true)
                    .Select(item => item.ServiceType.FullName).Distinct().Order(StringComparer.Ordinal).ToArray(),
                completion = "UNVERIFIED",
                limitations = new[]
                {
                    "Registration names are metadata observations, not executed service/network edges.",
                    "Factories, conditional composition and unnamed protocols are not resolved by type-name searches.",
                    "No connection string, user, host, port, password or credential is recorded.",
                    "A common DbContext registration does not prove separate least-privilege application/worker database identities."
                }
            },
            duplicateSurfaceKeys = surface.GroupBy(item => item).Where(group => group.Count() > 1)
                .Select(group => new { path = group.Key.Path, method = group.Key.Method, count = group.Count() }).ToArray(),
            openApi = new
            {
                outcome = documentationDigest is null ? "UNVERIFIED" : "METADATA_OBSERVED",
                source = documentationDigest is null ? null : "SUPPLIED_OPENAPI_3_DOCUMENT",
                digest = documentationDigest, operationCount = documentation.Count,
                documentationOnly = documentation.Except(surface).OrderBy(item => item.Path, StringComparer.Ordinal)
                    .ThenBy(item => item.Method, StringComparer.Ordinal).Select(item => new { path = item.Path, method = item.Method }).ToArray(),
                runtimeOnly = surface.Except(documentation).OrderBy(item => item.Path, StringComparer.Ordinal)
                    .ThenBy(item => item.Method, StringComparer.Ordinal).Select(item => new
                    {
                        path = item.Path, method = item.Method,
                        observedPurpose = RuntimeOnlyPurpose(item.Path, item.Method),
                        normativeClassification = "UNVERIFIED", approval = "DRAFT"
                    }).ToArray()
            },
            completion = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED",
            blindSpots = new[]
            {
                "Test host composition is observed; operational environment/feature branches need separate inventory.",
                "Endpoint metadata and OpenAPI presence do not execute resource/capability/tenant authorization.",
                "Delegated assertion/handler behavior is not inferred from requirement type names.",
                "Canonical SPEC registration, private mapping approval and complete contract reconciliation remain open.",
                "Middleware, static files and conditional endpoints require separate boundary review."
            }
        };
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, "composed-host-inventory.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string RuntimeOnlyPurpose(string path, string method) => (path, method) switch
    {
        ("/", "GET") => "UI_ENTRY_REDIRECT_EXCLUDED_FROM_OPENAPI",
        ("/favicon.ico", "GET") => "EMPTY_FAVICON_EXCLUDED_FROM_OPENAPI",
        ("/health", "GET") => "READINESS_REDIRECT_EXCLUDED_FROM_OPENAPI",
        ("/hubs/app/negotiate", "POST" or "ANY_UNCLASSIFIED") => "AUTHENTICATED_SIGNALR_NEGOTIATION_NON_OPENAPI_PROTOCOL",
        ("/hubs/app", "ANY_UNCLASSIFIED") => "AUTHENTICATED_SIGNALR_TRANSPORT_NON_OPENAPI_PROTOCOL",
        _ => "UNKNOWN_REQUIRES_REVIEW"
    };

    private static object? Describe(AuthorizationPolicy? policy) => policy is null ? null : new
    {
        authenticationSchemes = policy.AuthenticationSchemes.ToArray(),
        requirementTypes = policy.Requirements.Select(requirement => requirement.GetType().FullName).ToArray()
    };

    private static string NormalizedPath(RoutePattern pattern) => "/" + string.Join('/', pattern.PathSegments.Select(segment =>
        string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
            _ => throw new InvalidOperationException("Unknown route pattern part.")
        }))));
}
