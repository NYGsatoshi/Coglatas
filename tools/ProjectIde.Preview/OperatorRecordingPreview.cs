using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde;

/// <summary>Actual synthetic local operator invocations. Original frames are exported only through explicit root diagnostic reads.</summary>
internal static class OperatorRecordingPreview
{
    internal static async Task<JsonNode> ExportAsync(CompilerInputManifest input, CompilerResult expected, long expectedOperations)
    {
        CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"),
            Artifact("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(v => v.GetString()), input.PlanLowerer);
        var core = new CompilerHostRequest(new(input), 7);
        var manifestRequest = new PredicateObservationCompilerHostRequest(core, new());
        var operatorRequest = new PredicateObservationCompilerHostRequest(core, new(PredicateEvaluationCaptureMode.ObservedOperators));
        var budgetRequest = new PredicateObservationCompilerHostRequest(core, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1));
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
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
                throw new InvalidOperationException("Actual operator invocation differs from the original Compiler fixture.");
        if (cancelled.Response.Status != CompilerHostStatus.Cancelled || cancelled.Response.Execution is not null ||
            cancelled.Receipt.ObservationMetadata is not null || cancelled.Receipt.ObservationRecordDigest is not null ||
            !ReferenceEquals(originalMetadata, original.Receipt.ObservationMetadata))
            throw new InvalidOperationException("Original Manifest or cancelled host acquired invented observation data.");
        var invocations = new[] { original, diagnostic, budget, cancelled, retry };
        if (invocations.Select(v => v.Response.AttemptId).Distinct().Count() != 5 || invocations.Select(v => v.Receipt.Manifest.RunId).Distinct().Count() != 5)
            throw new InvalidOperationException("An actual operator run or attempt was reused.");
        var projection = JsonSerializer.SerializeToNode(new { format = "coglatas.local-web-operator-recording-fixture/1",
            source = Text(input.Source.Data, input.Source.Digest), input = Text(input.Data, input.InputDigest),
            original = Frame(manifestRequest, original), diagnostic = Frame(operatorRequest, diagnostic), budget = Frame(budgetRequest, budget),
            cancelled = Frame(operatorRequest, cancelled), retry = Frame(operatorRequest, retry) })!;
        if (Encoding.UTF8.GetByteCount(projection.ToJsonString()) > 16_777_216)
            throw new FormatException("Operator fixture projection exceeds its local byte bound.");
        return projection;
    }

    private static object Text(SourceJson data, ContentDigest digest) => new { text = data.CanonicalText, digest = digest.Data.Value };
    private static object Body(string key, SourceJson original, string domain)
    {
        var digest = ContentDigest.Compute(domain, original.ToCanonicalBytes());
        if (digest.Value != key) throw new InvalidOperationException("An original operator frame changed.");
        return new { text = original.CanonicalText, digest = digest.Data.Value, bytes = original.ToCanonicalBytes().Length };
    }
    private static object Snapshot(RecordedPredicateCompilation execution, CompilerDiagnostic diagnostic,
        CompilerPredicateObservationSnapshotMode mode, int budget)
    {
        var snapshot = ProjectCompilerPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation, diagnostic, mode, budget);
        if (!ReferenceEquals(snapshot.OriginalRecordMetadata, execution.Observation.Data))
            throw new InvalidOperationException("An explicit read lost its actual original observation.");
        return new { frame = Text(snapshot.Data, snapshot.Digest),
            operatorBodies = snapshot.Bodies.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Body(p.Key, p.Value, "coglatas.observed-predicate-node/1")).ToArray(),
            evaluationFrames = snapshot.EvaluationFrames.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Body(p.Key, p.Value, "coglatas.local-predicate-evaluation/1")).ToArray() };
    }
    private static object Frame(PredicateObservationCompilerHostRequest request, PredicateObservationCompilerRecordedInvocation actual)
    {
        if (!actual.IsFor(request, actual.Receipt.Manifest)) throw new InvalidOperationException("Actual operator invocation ownership changed.");
        var response = actual.Response; var receipt = actual.Receipt; var execution = response.Execution; var compilation = execution?.Compilation;
        if (compilation is not null && ProjectRootSourceAdmission.Read(compilation, compilation.Result, compilation.Result.ResultDigest) is null)
            throw new InvalidOperationException("Original operator invocation Source is unavailable.");
        return new { request = Text(request.Data, request.ExecutionInputDigest), policy = Text(request.PolicyData, request.PolicyDigest),
            baseExecution = Text(request.Core.ExecutionInputData, request.Core.ExecutionInputDigest),
            materializations = Text(request.Core.Materializations, request.Core.MaterializationDigest),
            manifest = Text(receipt.Manifest.Data, receipt.Manifest.Digest), response = Text(response.Data, response.Digest), receipt = Text(receipt.Data, receipt.Digest),
            result = compilation is null ? null : Text(compilation.Result.Data, compilation.Result.ResultDigest),
            observation = execution is null ? null : Text(execution.Observation.Data, execution.Observation.Digest),
            diagnostics = compilation?.Result.Diagnostics.Where(d => d.Location.Context.Equals(compilation.Input.Source.Context)).Select(d => new {
                diagnostic = Text(d.Data, ContentDigest.Compute("coglatas.compiler-diagnostic/1", d.Data.ToCanonicalBytes())),
                manifest = Snapshot(execution!, d, CompilerPredicateObservationSnapshotMode.Manifest, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                available = Snapshot(execution!, d, CompilerPredicateObservationSnapshotMode.AvailableOperators, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                budget = Snapshot(execution!, d, CompilerPredicateObservationSnapshotMode.AvailableOperators, 1) }).ToArray() ?? [] };
    }

    internal static object Producer()
    {
        string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        return new { format = "coglatas.local-web-operator-recording-observation/1", mode = "ObservedSyntheticOperatorHostInvocations",
            observedPlatform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            domainAssemblySha256 = Hash(typeof(ProjectCompilerPredicateObservation).Assembly.Location),
            applicationAssemblySha256 = Hash(typeof(PredicateObservationCompilerRecordedInvocation).Assembly.Location),
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" };
    }
}
