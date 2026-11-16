using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde;

/// <summary>Actual synthetic local pass invocations. Original frames are exported only through explicit owned root-pass reads.</summary>
internal static class PassRecordingPreview
{
    internal static async Task<JsonNode> ExportAsync(CompilerInputManifest input, CompilerResult expected, long expectedOperations)
    {
        CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"),
            Artifact("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(v => v.GetString()), input.PlanLowerer);
        var core = new CompilerHostRequest(new(input), 7);
        var manifestRequest = new PassObservationCompilerHostRequest(core, new());
        var passRequest = new PassObservationCompilerHostRequest(core, new(CompilerPassObservationMode.AvailableOriginalJson));
        var budgetRequest = new PassObservationCompilerHostRequest(core, new(CompilerPassObservationMode.AvailableOriginalJson, maximumBodyBytes: 1));
        var eventRequest = new PassObservationCompilerHostRequest(core, new(CompilerPassObservationMode.AvailableOriginalJson, maximumEvents: 1));
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        var original = await session.ExecuteAsync(session.Prepare(manifestRequest, Guid.NewGuid()), manifestRequest);
        var originalMetadata = original.Receipt.ObservationMetadata;
        var diagnostic = await session.ExecuteAsync(session.Prepare(passRequest, Guid.NewGuid(), original.Receipt), passRequest);
        var budget = await session.ExecuteAsync(session.Prepare(budgetRequest, Guid.NewGuid(), diagnostic.Receipt), budgetRequest);
        var eventGap = await session.ExecuteAsync(session.Prepare(eventRequest, Guid.NewGuid(), budget.Receipt), eventRequest);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await session.ExecuteAsync(session.Prepare(passRequest, Guid.NewGuid()), passRequest, cancellation.Token);
        var retry = await session.ExecuteAsync(session.Prepare(passRequest, Guid.NewGuid(), cancelled.Receipt), passRequest);
        foreach (var actual in new[] { original, diagnostic, budget, eventGap, retry })
            if (actual.Response.Status != CompilerHostStatus.Completed || actual.Response.Execution is not { } execution ||
                !execution.Observation.IsOwnedBy(execution) || !execution.Compilation.Result.Data.Equals(expected.Data) ||
                execution.Compilation.LogicalOperationsConsumed != expectedOperations)
                throw new InvalidOperationException("Actual pass invocation differs from the original Compiler fixture.");
        if (cancelled.Response.Status != CompilerHostStatus.Cancelled || cancelled.Response.Execution is not null ||
            cancelled.Receipt.ObservationMetadata is not null || cancelled.Receipt.ObservationRecordDigest is not null ||
            !ReferenceEquals(originalMetadata, original.Receipt.ObservationMetadata))
            throw new InvalidOperationException("Original Manifest or cancelled host acquired invented observation data.");
        var invocations = new[] { original, diagnostic, budget, eventGap, cancelled, retry };
        if (invocations.Select(v => v.Response.AttemptId).Distinct().Count() != 6 || invocations.Select(v => v.Receipt.Manifest.RunId).Distinct().Count() != 6)
            throw new InvalidOperationException("An actual pass run or attempt was reused.");
        var projection = JsonSerializer.SerializeToNode(new { format = "coglatas.local-web-pass-recording-fixture/1",
            source = Text(input.Source.Data, input.Source.Digest), input = Text(input.Data, input.InputDigest),
            original = Frame(manifestRequest, original), diagnostic = Frame(passRequest, diagnostic), budget = Frame(budgetRequest, budget), eventGap = Frame(eventRequest, eventGap),
            cancelled = Frame(passRequest, cancelled), retry = Frame(passRequest, retry) })!;
        if (Encoding.UTF8.GetByteCount(projection.ToJsonString()) > 16_777_216)
            throw new FormatException("Pass fixture projection exceeds its local byte bound.");
        return projection;
    }

    private static object Text(SourceJson data, ContentDigest digest) => new { text = data.CanonicalText, digest = digest.Data.Value };
    private static object Body(string key, SourceJson original)
    {
        var split = key.LastIndexOf(':');
        var domain = key[..split];
        var digest = ContentDigest.Compute(domain, original.ToCanonicalBytes());
        if (digest.Value != key[(split + 1)..]) throw new InvalidOperationException("An original pass body changed.");
        return new { key, domain, text = original.CanonicalText, digest = digest.Data.Value, bytes = original.ToCanonicalBytes().Length };
    }
    private static object Snapshot(RecordedPassCompilation execution, CompilerPassObservationSnapshotMode mode, int budget)
    {
        var snapshot = ProjectCompilerPassObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
            execution.Compilation.Input.InputDigest, execution.Compilation.Result.ResultDigest, mode, budget);
        if (!ReferenceEquals(snapshot.OriginalRecordMetadata, execution.Observation.Data))
            throw new InvalidOperationException("An explicit pass read lost its original observation.");
        return new { frame = Text(snapshot.Data, snapshot.Digest),
            bodies = snapshot.Bodies.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Body(p.Key, p.Value)).ToArray() };
    }
    private static object Frame(PassObservationCompilerHostRequest request, PassObservationCompilerRecordedInvocation actual)
    {
        if (!actual.IsFor(request, actual.Receipt.Manifest)) throw new InvalidOperationException("Actual pass invocation ownership changed.");
        var response = actual.Response; var receipt = actual.Receipt; var execution = response.Execution; var compilation = execution?.Compilation;
        if (compilation is not null && ProjectRootSourceAdmission.Read(compilation, compilation.Result, compilation.Result.ResultDigest) is null)
            throw new InvalidOperationException("Original pass invocation Source is unavailable.");
        return new { request = Text(request.Data, request.ExecutionInputDigest), policy = Text(request.PolicyData, request.PolicyDigest),
            baseExecution = Text(request.Core.ExecutionInputData, request.Core.ExecutionInputDigest),
            materializations = Text(request.Core.Materializations, request.Core.MaterializationDigest),
            manifest = Text(receipt.Manifest.Data, receipt.Manifest.Digest), response = Text(response.Data, response.Digest), receipt = Text(receipt.Data, receipt.Digest),
            result = compilation is null ? null : Text(compilation.Result.Data, compilation.Result.ResultDigest),
            observation = execution is null ? null : Text(execution.Observation.Data, execution.Observation.Digest),
            snapshots = execution is null ? null : new {
                manifest = Snapshot(execution, CompilerPassObservationSnapshotMode.Manifest, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                available = Snapshot(execution, CompilerPassObservationSnapshotMode.AvailableOriginalJson, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                budget = Snapshot(execution, CompilerPassObservationSnapshotMode.AvailableOriginalJson, 1) } };
    }

    internal static object Producer()
    {
        string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        return new { format = "coglatas.local-web-pass-recording-observation/1", mode = "ObservedSyntheticPassHostInvocations",
            observedPlatform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            domainAssemblySha256 = Hash(typeof(ProjectCompilerPassObservation).Assembly.Location),
            applicationAssemblySha256 = Hash(typeof(PassObservationCompilerRecordedInvocation).Assembly.Location),
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" };
    }
}
