using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde;

/// <summary>Separate offline actual regional host frames. This does not install a record or transport deserializer.</summary>
internal static class RegionalOperatorRecordingPreview
{
    internal static async Task<JsonNode> ExportAsync(CompilerInputManifest input, IReadOnlyList<CompilerMaterializedSnapshot> snapshots,
        CompilerResult expected, long expectedOperations)
    {
        CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"),
            Artifact("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(v => v.GetString()), input.PlanLowerer);
        var core = new CompilerHostRequest(new(input), 7, snapshots);
        var manifestRequest = new RegionalPredicateObservationCompilerHostRequest(core, new());
        var operatorRequest = new RegionalPredicateObservationCompilerHostRequest(core, new(PredicateEvaluationCaptureMode.ObservedOperators));
        var budgetRequest = new RegionalPredicateObservationCompilerHostRequest(core, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(profile);
        var original = await session.ExecuteAsync(session.Prepare(manifestRequest, Guid.NewGuid()), manifestRequest);
        var originalMetadata = original.Receipt.ObservationMetadata;
        var diagnostic = await session.ExecuteAsync(session.Prepare(operatorRequest, Guid.NewGuid(), original.Receipt), operatorRequest);
        var budget = await session.ExecuteAsync(session.Prepare(budgetRequest, Guid.NewGuid(), diagnostic.Receipt), budgetRequest);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await session.ExecuteAsync(session.Prepare(operatorRequest, Guid.NewGuid()), operatorRequest, cancellation.Token);
        var retry = await session.ExecuteAsync(session.Prepare(operatorRequest, Guid.NewGuid(), cancelled.Receipt), operatorRequest);
        foreach (var actual in new[] { original, diagnostic, budget, retry })
            if (actual.Response.Status != CompilerHostStatus.Completed || actual.Response.Execution is not { } execution ||
                !execution.Observation.IsOwnedBy(execution) || !execution.Compilation.Result.Data.Equals(expected.Data) ||
                execution.Compilation.LogicalOperationsConsumed != expectedOperations)
                throw new InvalidOperationException("Actual regional observation differs from the original Compiler fixture.");
        if (cancelled.Response.Status != CompilerHostStatus.Cancelled || cancelled.Response.Execution is not null ||
            cancelled.Receipt.ObservationMetadata is not null || cancelled.Receipt.ObservationRecordDigest is not null ||
            !ReferenceEquals(originalMetadata, original.Receipt.ObservationMetadata))
            throw new InvalidOperationException("An original Manifest or cancelled host acquired invented regional observations.");
        var invocations = new[] { original, diagnostic, budget, cancelled, retry };
        var runs = invocations.Select(v => v.Receipt.Manifest.RunId).ToHashSet();
        var attempts = invocations.Select(v => v.Response.AttemptId).ToHashSet();
        if (runs.Count != 5 || attempts.Count != 5 || runs.Overlaps(attempts))
            throw new InvalidOperationException("An actual regional run or attempt identity was reused.");
        var projection = JsonSerializer.SerializeToNode(new { format = "coglatas.local-web-regional-operator-recording-fixture/1",
            source = Text(input.Source.Data, input.Source.Digest), input = Text(input.Data, input.InputDigest),
            original = Frame(manifestRequest, original), diagnostic = Frame(operatorRequest, diagnostic), budget = Frame(budgetRequest, budget),
            cancelled = Frame(operatorRequest, cancelled), retry = Frame(operatorRequest, retry) })!;
        if (Encoding.UTF8.GetByteCount(projection.ToJsonString()) > 16_777_216)
            throw new FormatException("Regional operator projection exceeds the local byte profile.");
        return projection;
    }

    private static object Text(SourceJson data, ContentDigest digest) => new { text = data.CanonicalText, digest = digest.Data.Value };
    private static object Body(string key, SourceJson original, string domain)
    {
        var digest = ContentDigest.Compute(domain, original.ToCanonicalBytes());
        if (digest.Value != key) throw new InvalidOperationException("An original regional operator frame changed.");
        return new { text = original.CanonicalText, digest = digest.Data.Value, bytes = original.ToCanonicalBytes().Length };
    }
    private static object Snapshot(RecordedRegionalPredicateCompilation execution, CompilerDependencyRegion region, CompilerDiagnostic diagnostic,
        CompilerRegionalPredicateSnapshotMode mode, int budget)
    {
        var snapshot = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation, region, diagnostic, mode, budget);
        if (!ReferenceEquals(snapshot.OriginalRecordMetadata, execution.Observation.Data))
            throw new InvalidOperationException("Regional read lost the actual original observation.");
        return new { frame = Text(snapshot.Data, snapshot.Digest),
            operatorBodies = snapshot.Bodies.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Body(p.Key, p.Value, "coglatas.observed-predicate-node/1")).ToArray(),
            evaluationFrames = snapshot.EvaluationFrames.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Body(p.Key, p.Value, "coglatas.local-predicate-evaluation/1")).ToArray() };
    }
    private static object Frame(RegionalPredicateObservationCompilerHostRequest request, RegionalPredicateObservationCompilerRecordedInvocation actual)
    {
        if (!actual.IsFor(request, actual.Receipt.Manifest)) throw new InvalidOperationException("Actual regional invocation ownership changed.");
        var response = actual.Response; var receipt = actual.Receipt; var execution = response.Execution; var compilation = execution?.Compilation;
        if (compilation is not null && ProjectRootSourceAdmission.Read(compilation, compilation.Result, compilation.Result.ResultDigest) is null)
            throw new InvalidOperationException("Original regional invocation root Source is unavailable.");
        var dependencies = compilation?.Dependencies;
        return new { request = Text(request.Data, request.ExecutionInputDigest), policy = Text(request.PolicyData, request.PolicyDigest),
            baseExecution = Text(request.Core.ExecutionInputData, request.Core.ExecutionInputDigest),
            materializations = Text(request.Core.Materializations, request.Core.MaterializationDigest),
            manifest = Text(receipt.Manifest.Data, receipt.Manifest.Digest), response = Text(response.Data, response.Digest), receipt = Text(receipt.Data, receipt.Digest),
            result = compilation is null ? null : Text(compilation.Result.Data, compilation.Result.ResultDigest),
            observation = execution is null ? null : Text(execution.Observation.Data, execution.Observation.Digest),
            dependencyAnalysis = dependencies is null ? null : Text(dependencies.Data, dependencies.Digest),
            bindingClosure = dependencies is null ? null : Text(dependencies.Binding.Data, dependencies.Binding.Digest),
            regions = dependencies?.Regions.Select(region => new {
                binding = Text(region.Binding.Data, region.Binding.Digest), source = Text(region.Binding.Source.Data, region.Binding.Source.Digest),
                region = Text(region.Data, region.Digest),
                diagnostics = region.Diagnostics.Select(d => new {
                    diagnostic = Text(d.Data, ContentDigest.Compute("coglatas.compiler-diagnostic/1", d.Data.ToCanonicalBytes())),
                    manifest = Snapshot(execution!, region, d, CompilerRegionalPredicateSnapshotMode.Manifest, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                    available = Snapshot(execution!, region, d, CompilerRegionalPredicateSnapshotMode.AvailableOperators, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                    budget = Snapshot(execution!, region, d, CompilerRegionalPredicateSnapshotMode.AvailableOperators, 1) }).ToArray()
            }).ToArray() ?? [] };
    }
    internal static object Producer()
    {
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        return new { format = "coglatas.local-web-regional-operator-recording-observation/1", mode = "ObservedSyntheticRegionalOperatorHostInvocations",
            observedPlatform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            domainAssemblySha256 = Hash(typeof(ProjectCompilerRegionalPredicateObservation).Assembly.Location),
            applicationAssemblySha256 = Hash(typeof(RegionalPredicateObservationCompilerRecordedInvocation).Assembly.Location),
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" };
    }
}
