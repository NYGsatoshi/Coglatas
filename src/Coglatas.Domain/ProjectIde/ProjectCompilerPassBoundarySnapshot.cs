using System.Text;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerPassBoundarySnapshotState
{
    RefusedInvocation, RefusedInputBinding, RefusedWrongResult, BoundaryNotRecorded,
    ManifestOnly, NoRecordedOriginalBodies, AvailableOriginalJson, BodyBudgetExceeded
}

/// <summary>One actually retained boundary and its original bodies. A selected read cannot recover capture gaps or authorize access.</summary>
public sealed class ProjectCompilerPassBoundarySnapshot
{
    public const string Format = "coglatas.local-initial-root-pass-boundary-snapshot/1";
    public CompilerPassBoundarySnapshotState State { get; }
    public CompilerPassObservationSnapshotMode Mode { get; }
    public int Ordinal { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public CompilerPassObservationState? OriginalCaptureState { get; }
    public SourceJson? OriginalBoundaryMetadata { get; }
    public ContentDigest? OriginalRecordDigest { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectCompilerPassBoundarySnapshot(CompilerPassBoundarySnapshotState state, CompilerPassObservationSnapshotMode mode,
        int ordinal, int maximumBodyBytes, CoreCompilation? compilation = null, ProjectCompilerPassObservation? record = null,
        SourceJson? boundary = null, Dictionary<string, SourceJson>? bodies = null)
    {
        State = state; Mode = mode; Ordinal = ordinal; MaximumBodyBytes = maximumBodyBytes;
        OriginalCaptureState = record?.State; OriginalBoundaryMetadata = boundary; OriginalRecordDigest = record?.Digest;
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(v => Encoding.UTF8.GetByteCount(v.CanonicalText));
        Data = SourceJson.FromObject(new { format = Format, state = state.ToString(), mode = mode.ToString(), ordinal,
            maximumBodyBytes, retainedBodyBytes = RetainedBodyBytes,
            basis = compilation is null ? null : new { inputDigest = compilation.Input.InputDigest.Data.Value,
                resultDigest = compilation.Result.ResultDigest.Data.Value },
            originalRecord = record is null ? null : new { digest = record.Digest.Data.Value, state = record.State.ToString(),
                visitedEvents = record.VisitedEvents, retainedEvents = record.RetainedEvents },
            originalBoundary = boundary?.Value, scope = "OneOwnedInitialRootBoundary",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            causalMeaning = "NotRecorded", compilerRunIdentity = "NotAllocated" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectCompilerPassBoundarySnapshot Read(RecordedPassCompilation execution, CoreCompilation compilation,
        ProjectCompilerPassObservation record, ContentDigest expectedInputDigest, ContentDigest expectedResultDigest, int ordinal,
        CompilerPassObservationSnapshotMode mode = CompilerPassObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution); ArgumentNullException.ThrowIfNull(compilation); ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(expectedInputDigest); ArgumentNullException.ThrowIfNull(expectedResultDigest);
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!record.IsOwnedBy(execution) || !execution.IsFor(compilation))
            return new(CompilerPassBoundarySnapshotState.RefusedInvocation, mode, ordinal, maximumBodyBytes);
        if (!expectedInputDigest.Equals(compilation.Input.InputDigest))
            return new(CompilerPassBoundarySnapshotState.RefusedInputBinding, mode, ordinal, maximumBodyBytes);
        if (!expectedResultDigest.Equals(compilation.Result.ResultDigest))
            return new(CompilerPassBoundarySnapshotState.RefusedWrongResult, mode, ordinal, maximumBodyBytes);
        if (ordinal >= record.BoundaryEvents.Count)
            return new(CompilerPassBoundarySnapshotState.BoundaryNotRecorded, mode, ordinal, maximumBodyBytes, compilation, record);
        var boundary = record.BoundaryEvents[ordinal];
        if (mode == CompilerPassObservationSnapshotMode.Manifest)
            return new(CompilerPassBoundarySnapshotState.ManifestOnly, mode, ordinal, maximumBodyBytes, compilation, record, boundary);
        var bodies = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        var retainedBytes = 0;
        foreach (var reference in boundary.Value.GetProperty("originalBodies").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = reference.GetProperty("domain").GetString() + ":" + reference.GetProperty("digest").GetProperty("value").GetString();
            if (bodies.ContainsKey(key)) continue;
            if (!record.Bodies.TryGetValue(key, out var body))
                return new(CompilerPassBoundarySnapshotState.NoRecordedOriginalBodies, mode, ordinal, maximumBodyBytes, compilation, record, boundary);
            var bytes = Encoding.UTF8.GetByteCount(body.CanonicalText);
            if (bytes > maximumBodyBytes - retainedBytes)
                return new(CompilerPassBoundarySnapshotState.BodyBudgetExceeded, mode, ordinal, maximumBodyBytes, compilation, record, boundary);
            bodies.Add(key, body); retainedBytes += bytes;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(bodies.Count == 0 ? CompilerPassBoundarySnapshotState.NoRecordedOriginalBodies : CompilerPassBoundarySnapshotState.AvailableOriginalJson,
            mode, ordinal, maximumBodyBytes, compilation, record, boundary, bodies);
    }
}
