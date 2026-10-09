using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Controllers;

namespace Coglatas.Tests.SecurityArchitecture;

// Explicit asserted HTTP observations, never coverage inferred from a test name.
// The accounting adapter independently reconciles source, assemblies and TRX.
internal sealed class SecurityArchitectureHttpControlRecorder
{
    private readonly string _method;
    private readonly string _source;
    private readonly string _sourceDigest;
    private readonly string _environment;
    private readonly List<object> _observations = [];

    private SecurityArchitectureHttpControlRecorder(Type test, string environment, string member, string source)
    {
        _method = test.FullName + "." + member;
        _environment = environment;
        var directory = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coglatas.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("HTTP verifier source root unavailable.");
        _source = Path.GetRelativePath(directory.FullName, source).Replace('\\', '/');
        _sourceDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
    }

    public static SecurityArchitectureHttpControlRecorder Create(Type test, string environment,
        [CallerMemberName] string member = "", [CallerFilePath] string source = "") =>
        new(test, environment, member, source);

    public void Observe(HttpResponseMessage response, string route, string control,
        HttpStatusCode expected, string? errorCode = null, string? responseAssertion = null)
    {
        Assert.Equal(expected, response.StatusCode);
        var request = response.RequestMessage ?? throw new InvalidOperationException("HTTP request identity missing.");
        var path = request.RequestUri?.AbsolutePath ?? throw new InvalidOperationException("HTTP path missing.");
        if (path.Length > 1) path = path.TrimEnd('/');
        var pattern = "^" + string.Concat(Regex.Split(route, @"(\{[^}]+\})")
            .Select(part => part.StartsWith('{') && part.EndsWith('}') ? "[^/]+" : Regex.Escape(part))) + "$";
        Assert.Matches(pattern, path);
        _observations.Add(new
        {
            path = route, method = request.Method.Method, control,
            observedStatus = (int)response.StatusCode, expectedStatus = (int)expected,
            errorCode, responseAssertion, observedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public Task SaveAsync() => SecurityArchitectureInventoryTests.WritePrivateInventoryAsync(
        "http-controls-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_method))) + ".json", new
        {
            schemaVersion = 1, verifierMethod = _method, sourcePath = _source, sourceDigest = _sourceDigest,
            environment = _environment,
            assemblyDigests = new[] { typeof(SecurityArchitectureHttpControlRecorder).Assembly, typeof(AuthController).Assembly,
                    typeof(CanonicalCreateProjectRequest).Assembly, typeof(AppDbContext).Assembly, typeof(CapabilityGrant).Assembly }
                .ToDictionary(assembly => assembly.GetName().Name!, assembly =>
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)))),
            observations = _observations,
            contractCompletion = "UNVERIFIED", ownerApproval = (string?)null,
            limits = new[] { "Explicit assertions need passed TRX and exact source/build/candidate reconciliation.",
                "Missing controls, approved SPEC mapping and product API-to-RLS authority remain UNVERIFIED.",
                "No protected rows, request credentials, query values or response bodies are recorded." }
        });
}
