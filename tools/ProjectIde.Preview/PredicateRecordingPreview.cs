using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde;

/// <summary>Actual offline synthetic predicate sessions; original frame bytes and random run/attempt identities stay separate.</summary>
internal static class PredicateRecordingPreview
{
    internal static async Task<JsonNode> ExportAsync(CompilerInputManifest input, CompilerResult expected)
    {
        CompilerArtifactIdentity At(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        var core = new CompilerHostRequest(new(input), 7);
        var manifestRequest = new PredicateCompilerHostRequest(core, new());
        var arrayRequest = new PredicateCompilerHostRequest(core, new(CompilerPredicateCaptureMode.OriginalRootArrays));
        var budgetRequest = new PredicateCompilerHostRequest(core, new(CompilerPredicateCaptureMode.OriginalRootArrays, 1));
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        var original = await session.ExecuteAsync(session.Prepare(manifestRequest, Guid.NewGuid()), manifestRequest);
        var originalMetadata = original.Receipt.PredicateMetadata;
        var diagnostic = await session.ExecuteAsync(session.Prepare(arrayRequest, Guid.NewGuid(), original.Receipt), arrayRequest);
        var budget = await session.ExecuteAsync(session.Prepare(budgetRequest, Guid.NewGuid(), diagnostic.Receipt), budgetRequest);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await session.ExecuteAsync(session.Prepare(arrayRequest, Guid.NewGuid()), arrayRequest, cancellation.Token);
        var retry = await session.ExecuteAsync(session.Prepare(arrayRequest, Guid.NewGuid(), cancelled.Receipt), arrayRequest);
        foreach (var invocation in new[] { original, diagnostic, budget, retry })
            if (invocation.Response.Status != CompilerHostStatus.Completed || invocation.Response.Compilation is not { } compilation ||
                !compilation.Result.Data.Equals(expected.Data) || !compilation.Result.ResultDigest.Equals(expected.ResultDigest) ||
                compilation.PredicatePremises?.IsOwnedBy(compilation) != true)
                throw new InvalidOperationException("Actual predicate invocation differs from the original Compiler fixture.");
        if (cancelled.Response.Status != CompilerHostStatus.Cancelled || cancelled.Response.Compilation is not null || cancelled.Receipt.PredicateMetadata is not null ||
            cancelled.Receipt.Bodies.Count != 0 || original.Receipt.Bodies.Count != 0 || !ReferenceEquals(originalMetadata, original.Receipt.PredicateMetadata))
            throw new InvalidOperationException("A host-only outcome or original Manifest acquired invented Compiler data.");
        var invocations = new[] { original, diagnostic, budget, cancelled, retry };
        if (invocations.Select(value => value.Receipt.AttemptId).Distinct().Count() != 5 || invocations.Select(value => value.Receipt.Manifest.RunId).Distinct().Count() != 5)
            throw new InvalidOperationException("An actual run or attempt was reused.");
        var projection = JsonSerializer.SerializeToNode(new { format = "coglatas.local-web-predicate-recording-fixture/1",
            source = Text(input.Source.Data, input.Source.Digest), input = Text(input.Data, input.InputDigest),
            original = Frame(manifestRequest, original), diagnostic = Frame(arrayRequest, diagnostic), budget = Frame(budgetRequest, budget),
            cancelled = Frame(arrayRequest, cancelled), retry = Frame(arrayRequest, retry) })!;
        if (Encoding.UTF8.GetByteCount(projection.ToJsonString()) > 16_777_216) throw new FormatException("Predicate fixture projection exceeds its local byte bound.");
        return projection;
    }

    private static object Text(SourceJson data, ContentDigest digest) => new { text = data.CanonicalText, digest = digest.Data.Value };
    private static object Frame(PredicateCompilerHostRequest request, PredicateCompilerRecordedInvocation invocation)
    {
        var response = invocation.Response; var receipt = invocation.Receipt;
        if (!response.IsFor(request) || !receipt.IsFor(response)) throw new InvalidOperationException("Actual predicate invocation ownership changed.");
        var compilation = response.Compilation; var record = compilation?.PredicatePremises;
        object Snapshot(ProjectRootSourceAdmission admission, CompilerDiagnostic diagnostic, DiagnosticPremiseSnapshotMode mode, int budget)
        {
            var snapshot = ProjectRecordedPredicatePremiseSnapshot.Read(admission, record!, diagnostic, admission.ResultDigest, mode, budget);
            foreach (var body in snapshot.Bodies)
                if (!ReferenceEquals(body.Value, receipt.Bodies[body.Key])) throw new InvalidOperationException("A snapshot replaced an original predicate body.");
            return Text(snapshot.Data, snapshot.Digest);
        }
        var admission = compilation is null ? null : ProjectRootSourceAdmission.Read(compilation, compilation.Result, compilation.Result.ResultDigest)
            ?? throw new InvalidOperationException("Original predicate invocation Source is unavailable.");
        return new {
            request = Text(request.Data, request.ExecutionInputDigest), policy = Text(request.PolicyData, request.PolicyDigest),
            baseExecution = Text(request.Core.ExecutionInputData, request.Core.ExecutionInputDigest),
            materializations = Text(request.Core.Materializations, request.Core.MaterializationDigest),
            manifest = Text(receipt.Manifest.Data, receipt.Manifest.Digest), response = Text(response.Data, response.Digest), receipt = Text(receipt.Data, receipt.Digest),
            result = compilation is null ? null : Text(compilation.Result.Data, compilation.Result.ResultDigest),
            predicate = record is null ? null : Text(record.Data, record.Digest),
            bodies = receipt.Bodies.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => {
                var digest = ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, pair.Value.ToCanonicalBytes());
                if (digest.Value != pair.Key) throw new InvalidOperationException("Original predicate body hash changed.");
                return new { text = pair.Value.CanonicalText, digest = digest.Data.Value, bytes = Encoding.UTF8.GetByteCount(pair.Value.CanonicalText) };
            }).ToArray(),
            diagnostics = compilation?.Result.Diagnostics.Where(diagnostic => diagnostic.Location.Context.Equals(compilation.Input.Source.Context)).Select(diagnostic => new {
                diagnostic = Text(diagnostic.Data, ContentDigest.Compute("coglatas.compiler-diagnostic/1", diagnostic.Data.ToCanonicalBytes())),
                manifest = Snapshot(admission!, diagnostic, DiagnosticPremiseSnapshotMode.Manifest, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                available = Snapshot(admission!, diagnostic, DiagnosticPremiseSnapshotMode.AvailableBodies, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                budget = Snapshot(admission!, diagnostic, DiagnosticPremiseSnapshotMode.AvailableBodies, 1)
            }).ToArray() ?? []
        };
    }

    internal static object Producer()
    {
        string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        return new { format = "coglatas.local-web-predicate-recording-observation/1", mode = "ObservedSyntheticPredicateHostInvocations",
            observedPlatform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            applicationAssemblySha256 = Hash(typeof(PredicateCompilerRecordedInvocation).Assembly.Location),
            domainAssemblySha256 = Hash(typeof(ProjectRecordedPredicatePremiseSnapshot).Assembly.Location),
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" };
    }
}
