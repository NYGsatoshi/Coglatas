using System.Text;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerOutputSnapshotMode { Manifest, AvailableOutputs }
public enum CompilerOutputSnapshotState
{
    ManifestOnly, CapturedAvailableOutputs, NoOutputsProduced, BodyBudgetExceeded, UnavailableBodyProvider,
    RefusedWrongResult, RefusedInputBinding, RefusedOutputBinding, UnsupportedBodyKind
}

/// <summary>A bounded local snapshot of actual produced outputs, not a pass trace, run identity or trusted retention artifact.</summary>
public sealed class ProjectCompilerOutputSnapshot
{
    public const string Format = "coglatas.local-compiler-output-snapshot/1";
    public const int DefaultBodyBudgetBytes = 1024 * 1024;
    public const int MaximumBodyBudgetBytes = 32 * 1024 * 1024;
    public CompilerOutputSnapshotState State { get; }
    public CompilerOutputSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public ContentDigest? InputDigest { get; }
    public ContentDigest? ResultDigest { get; }
    public ContentDigest? SourceDigest { get; }
    public SourceRevisionContext? Context { get; }
    public CompilerFreshness Freshness { get; }
    public IReadOnlyList<string> ChangedDimensions { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectCompilerOutputSnapshot(CompilerOutputSnapshotState state, CompilerOutputSnapshotMode mode, int budget,
        CoreCompilation? compilation = null, Dictionary<string, SourceJson>? retained = null, CompilerInputManifest? desiredInput = null)
    {
        var freshness = compilation is null ? null : CompilerFreshnessAssessment.Assess(compilation.Result, desiredInput);
        State = state; Mode = mode; MaximumBodyBytes = budget;
        InputDigest = compilation?.Input.InputDigest; ResultDigest = compilation?.Result.ResultDigest;
        SourceDigest = compilation?.Input.Source.Digest; Context = compilation?.Input.Source.Context;
        Freshness = freshness?.State ?? CompilerFreshness.Undetermined;
        ChangedDimensions = new ImmutableListView<string>(freshness?.ChangedDimensions ?? Array.Empty<string>());
        Bodies = new ImmutableMapView<string, SourceJson>(retained ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText));
        Data = SourceJson.Parse(JsonSerializer.Serialize(new {
            format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes = budget,
            captureKind = "AvailableResultSnapshot", intermediateStates = "NotRecorded", runIdentity = "NotAllocated",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            basis = compilation is null ? null : new { inputDigest = compilation.Input.InputDigest.Data.Value,
                sourceDigest = compilation.Input.Source.Digest.Data.Value, context = compilation.Input.Source.Context.Data.Value,
                compiler = compilation.Input.Compiler.Data.Value, resultDigest = compilation.Result.ResultDigest.Data.Value },
            result = compilation is null ? null : new { execution = compilation.Result.Execution.ToString(), coverage = compilation.Result.Coverage.ToString(),
                verdict = compilation.Result.Verdict.ToString(), evidenceComplete = compilation.Result.EvidenceComplete },
            desired = compilation is null || desiredInput is null ? null : new { inputDigest = desiredInput.InputDigest.Data.Value,
                sourceDigest = desiredInput.Source.Digest.Data.Value, context = desiredInput.Source.Context.Data.Value },
            freshness = freshness is null ? null : new { state = freshness.State.ToString(), desiredInputDigest = freshness.DesiredInputDigest?.Data.Value,
                changedDimensions = freshness.ChangedDimensions },
            stages = compilation?.Result.Stages.Select(stage => stage.Data.Value).ToArray(),
            outputs = compilation?.Result.Artifacts.Select(artifact => new { reference = artifact.Data.Value,
                body = Bodies.ContainsKey(artifact.Role) ? "Captured" : "NotCaptured" }).ToArray(),
            gaps = new[] { "Original Source bodies are not retained by this produced-output snapshot.",
                "Earlier intermediate states were not recorded.", "This snapshot is not FullDump or FullTrace.",
                "Diagnostic reruns require a separately allocated execution and origin; no prior execution is reconstructed." }
        }));
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectCompilerOutputSnapshot Read(CoreCompilation compilation, ContentDigest expectedResult,
        CompilerOutputSnapshotMode mode = CompilerOutputSnapshotMode.Manifest, int maximumBodyBytes = DefaultBodyBudgetBytes,
        CancellationToken cancellationToken = default, CompilerInputManifest? desiredInput = null)
    {
        ArgumentNullException.ThrowIfNull(compilation); ArgumentNullException.ThrowIfNull(expectedResult);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!compilation.Result.ResultDigest.Equals(expectedResult))
            return new(CompilerOutputSnapshotState.RefusedWrongResult, mode, maximumBodyBytes);
        if (!compilation.Result.Input.Data.Equals(compilation.Input.Data))
            return new(CompilerOutputSnapshotState.RefusedInputBinding, mode, maximumBodyBytes);
        if (mode == CompilerOutputSnapshotMode.Manifest)
            return new(CompilerOutputSnapshotState.ManifestOnly, mode, maximumBodyBytes, compilation, desiredInput: desiredInput);
        var bodies = new Dictionary<string, SourceJson>(StringComparer.Ordinal); long bytes = 0;
        if (compilation.Publication is { } publication && publication.IsDeclaredBy(compilation.Result)) {
            foreach (var artifact in compilation.Result.Artifacts) {
                cancellationToken.ThrowIfCancellationRequested();
                var body = artifact.Role switch { "coglatas.semantic-model" => publication.SemanticModel,
                    "coglatas.ir" => publication.Ir, "coglatas.source-map" => publication.SourceMap, _ => null };
                if (body is null) return new(CompilerOutputSnapshotState.UnsupportedBodyKind, mode, maximumBodyBytes, compilation, desiredInput: desiredInput);
                var expected = artifact.Role switch { "coglatas.semantic-model" => publication.SemanticModelDigest,
                    "coglatas.ir" => publication.IrDigest, _ => publication.SourceMapDigest };
                if (!artifact.Digest.Equals(expected)) return new(CompilerOutputSnapshotState.RefusedOutputBinding, mode, maximumBodyBytes);
                bytes += Encoding.UTF8.GetByteCount(body.CanonicalText);
                if (bytes > maximumBodyBytes)
                    return new(CompilerOutputSnapshotState.BodyBudgetExceeded, mode, maximumBodyBytes, compilation, desiredInput: desiredInput);
                bodies.Add(artifact.Role, body);
            }
        } else if (compilation.Result.Artifacts.Count != 0)
            return new(CompilerOutputSnapshotState.UnavailableBodyProvider, mode, maximumBodyBytes, compilation, desiredInput: desiredInput);
        cancellationToken.ThrowIfCancellationRequested();
        return new(bodies.Count == 0 ? CompilerOutputSnapshotState.NoOutputsProduced : CompilerOutputSnapshotState.CapturedAvailableOutputs,
            mode, maximumBodyBytes, compilation, bodies, desiredInput);
    }
}
