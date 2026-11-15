using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde;

/// <summary>Offline observed synthetic recordings. Original random host/session/run IDs are never restamped for fixture parity.</summary>
internal static class CompilerRecordingPreview
{
    internal static async Task<JsonNode> ExportAsync(CompilerInputManifest input, IEnumerable<CompilerMaterializedSnapshot> snapshots, CompilerResult expected)
    {
        CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        var request = new CompilerHostRequest(new(input), 7, snapshots);
        using var session = new LocalCompilerRecordingSession(profile);
        var original = await LocalCompilerRecordedInvocation.ExecuteAsync(session, session.Prepare(request, Guid.NewGuid()), request);
        var diagnostic = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, diagnosticOrigin: original.Receipt), request);
        var budget = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, 1, original.Receipt), request);
        var cancelledManifest = session.Prepare(request, Guid.NewGuid(), diagnosticOrigin: original.Receipt);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await LocalCompilerRecordedInvocation.ExecuteAsync(session, cancelledManifest, request, cancellation.Token);
        foreach (var actual in new[] { original, diagnostic, budget })
            if (actual.Response.Status != CompilerHostStatus.Completed || !actual.Response.Compilation!.Result.Data.Equals(expected.Data) ||
                !actual.Response.Compilation.Result.ResultDigest.Equals(expected.ResultDigest)) throw new InvalidOperationException("Observed recording differs from its original Compiler fixture.");
        if (cancelled.Response.Status != CompilerHostStatus.Cancelled || cancelled.Response.Compilation is not null || cancelled.Receipt.Snapshot is not null)
            throw new InvalidOperationException("Cancelled recording must not fabricate a Compiler result.");
        return JsonSerializer.SerializeToNode(new { format = "coglatas.local-web-recording-fixture/1",
            original = Frame(original), diagnostic = Frame(diagnostic), budget = Frame(budget), cancelled = Frame(cancelled) })!;
    }

    private static object Frame(LocalCompilerRecordedInvocation actual)
    {
        var response = actual.Response; var receipt = actual.Receipt; var snapshot = receipt.Snapshot;
        return new {
            manifest = new { text = receipt.Manifest.Data.CanonicalText, digest = receipt.Manifest.Digest.Data.Value },
            receipt = new { text = receipt.Data.CanonicalText, digest = receipt.Digest.Data.Value },
            host = new { status = response.Status.ToString(), reasonCode = response.ReasonCode, attemptId = response.AttemptId.ToString(),
                requestedInputDigest = response.RequestedInputDigest.Data.Value, clientGeneration = response.ClientGeneration,
                execution = response.Execution?.Data.Value, executionDigest = response.Execution?.Digest.Data.Value,
                result = response.Compilation?.Result.Data.Value, resultDigest = response.Compilation?.Result.ResultDigest.Data.Value },
            snapshot = snapshot is null ? null : new { text = snapshot.Data.CanonicalText, digest = snapshot.Digest.Data.Value, retainedBodyBytes = snapshot.RetainedBodyBytes,
                bodies = snapshot.Bodies.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { role = pair.Key, text = pair.Value.CanonicalText,
                    digest = response.Compilation!.Result.Artifacts.Single(artifact => artifact.Role == pair.Key).Digest.Data.Value }).ToArray() }
        };
    }

    internal static object Producer()
    {
        using var stream = File.OpenRead(typeof(LocalCompilerRecordedInvocation).Assembly.Location);
        return new { format = "coglatas.local-web-recording-observation/1", mode = "ObservedSyntheticHostInvocations",
            observedPlatform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            applicationAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(stream)), authority = "NotEvaluated", producerTrust = "NotEvaluated",
            eligibility = "NotEvaluated", retentionPolicy = "NotAdopted", originalCoreProductField = "PreservedLegacyLineage" };
    }
}
