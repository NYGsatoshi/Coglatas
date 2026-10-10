using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Infrastructure.Persistence;
using Coglatas.SecurityArchitecture;
using Coglatas.Web.Controllers;

namespace Coglatas.Tests.SecurityArchitecture;

// Record only the assertions performed in a passed verifier. The independent
// consumer reconciles the actual source, loaded assemblies and TRX interval.
internal sealed class SecurityArchitectureWorkerControlRecorder
{
    private readonly string _method;
    private readonly string _source;
    private readonly string _sourceDigest;
    private readonly List<object> _observations = [];

    private SecurityArchitectureWorkerControlRecorder(Type test, string member, string source)
    {
        _method = test.FullName + "." + member;
        var directory = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coglatas.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Worker verifier source root unavailable.");
        _source = Path.GetRelativePath(directory.FullName, source).Replace('\\', '/');
        _sourceDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
    }

    public static SecurityArchitectureWorkerControlRecorder Create(Type test,
        [CallerMemberName] string member = "", [CallerFilePath] string source = "") => new(test, member, source);

    public void Observe(string workerType, string control, string assertion) =>
        _observations.Add(new { workerType, control, assertion, outcome = "OBSERVED", observedAtUtc = DateTimeOffset.UtcNow });

    public Task SaveAsync() => SecurityArchitectureInventoryTests.WritePrivateInventoryAsync(
        "worker-controls-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_method))) + ".json", new
        {
            schemaVersion = 2, assemblyBindingScope = "SIX_ASSEMBLIES_WITH_LOADED_COPIES",
            verifierMethod = _method, sourcePath = _source, sourceDigest = _sourceDigest,
            environment = "ACTUAL_TEST_WEB_ENTRY_POINT_MIGRATED_POSTGRESQL_REGISTERED_WORKERS_AND_REAL_WEBSOCKET",
            workerPollSeconds = 1,
            assemblyDigests = new[] { typeof(SecurityArchitectureWorkerControlRecorder).Assembly,
                    typeof(AuthController).Assembly, typeof(DurableEventEnvelope).Assembly,
                    typeof(AppDbContext).Assembly, typeof(CapabilityGrant).Assembly, typeof(SpecRegistryValidator).Assembly }
                .ToDictionary(assembly => assembly.GetName().Name!, assembly =>
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)))),
            observations = _observations, contractCompletion = "UNVERIFIED", ownerApproval = (string?)null,
            limits = new[] { "Non-default disposable-fixture worker cadence is not timing or performance qualification.",
                "Task digest opt-in applies only to disposable synthetic tenant settings; the product default is disabled.",
                "Application and Worker share the synthetic fixture database authority; distinct product roles and RLS remain UNVERIFIED.",
                "Canonical SPEC mappings and owner approvals remain pending. No operational activation is performed.",
                "No tenant, resource, user, job, claim or event identities, protected contents or credentials are recorded." }
        });
}
