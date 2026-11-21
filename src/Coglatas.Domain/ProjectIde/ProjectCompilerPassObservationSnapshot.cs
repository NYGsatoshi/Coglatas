using System.Text;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerPassObservationSnapshotMode { Manifest, AvailableOriginalJson }
public enum CompilerPassObservationSnapshotState
{
    RefusedInvocation, RefusedInputBinding, RefusedWrongResult, ManifestOnly, NoRecordedOriginalBodies, AvailableOriginalJson, BodyBudgetExceeded
}

/// <summary>Explicit original bodies from one owned local observation. No current access, global run identity or recovery of uncaptured bytes.</summary>
public sealed class ProjectCompilerPassObservationSnapshot
{
    public const string Format = "coglatas.local-initial-root-pass-observation-snapshot/1";
    public CompilerPassObservationSnapshotState State { get; }
    public CompilerPassObservationSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public CompilerPassObservationState? OriginalCaptureState { get; }
    public SourceJson? OriginalRecordMetadata { get; }
    public ContentDigest? OriginalRecordDigest { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    private ProjectCompilerPassObservationSnapshot(CompilerPassObservationSnapshotState state, CompilerPassObservationSnapshotMode mode,
        int maximumBodyBytes, CoreCompilation? compilation = null, ProjectCompilerPassObservation? record = null, Dictionary<string, SourceJson>? bodies = null)
    {
        State = state; Mode = mode; MaximumBodyBytes = maximumBodyBytes;
        OriginalCaptureState = record?.State; OriginalRecordMetadata = record?.Data; OriginalRecordDigest = record?.Digest;
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(v => Encoding.UTF8.GetByteCount(v.CanonicalText));
        Data = SourceJson.FromObject(new { format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes, retainedBodyBytes = RetainedBodyBytes,
            basis = compilation is null ? null : new { inputDigest = compilation.Input.InputDigest.Data.Value, sourceDigest = compilation.Input.Source.Digest.Data.Value,
                context = compilation.Input.Source.Context.Data.Value, compiler = compilation.Input.Compiler.Data.Value, resultDigest = compilation.Result.ResultDigest.Data.Value },
            originalRecord = record is null ? null : new { digest = record.Digest.Data.Value, metadata = record.Data.Value },
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            scope = "OwnedInitialRootOriginalBodiesOnly", regionalEvaluation = "NotRecorded", compilerRunIdentity = "NotAllocated",
            runSelection = "NotEvaluated", causalMeaning = "NotRecorded" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
    public static ProjectCompilerPassObservationSnapshot Read(RecordedPassCompilation execution, CoreCompilation compilation,
        ProjectCompilerPassObservation record, ContentDigest expectedInputDigest, ContentDigest expectedResultDigest,
        CompilerPassObservationSnapshotMode mode = CompilerPassObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution); ArgumentNullException.ThrowIfNull(compilation); ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(expectedInputDigest); ArgumentNullException.ThrowIfNull(expectedResultDigest);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!record.IsOwnedBy(execution) || !execution.IsFor(compilation))
            return new(CompilerPassObservationSnapshotState.RefusedInvocation, mode, maximumBodyBytes);
        if (!expectedInputDigest.Equals(compilation.Input.InputDigest))
            return new(CompilerPassObservationSnapshotState.RefusedInputBinding, mode, maximumBodyBytes);
        if (!expectedResultDigest.Equals(compilation.Result.ResultDigest))
            return new(CompilerPassObservationSnapshotState.RefusedWrongResult, mode, maximumBodyBytes);
        if (mode == CompilerPassObservationSnapshotMode.Manifest)
            return new(CompilerPassObservationSnapshotState.ManifestOnly, mode, maximumBodyBytes, compilation, record);
        if (record.Bodies.Count == 0)
            return new(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, mode, maximumBodyBytes, compilation, record);
        if (record.RetainedBodyBytes > maximumBodyBytes)
            return new(CompilerPassObservationSnapshotState.BodyBudgetExceeded, mode, maximumBodyBytes, compilation, record);
        var bodies = record.Bodies.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        return new(CompilerPassObservationSnapshotState.AvailableOriginalJson, mode, maximumBodyBytes, compilation, record, bodies);
    }
}
