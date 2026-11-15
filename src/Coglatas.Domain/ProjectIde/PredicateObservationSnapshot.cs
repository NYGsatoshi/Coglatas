namespace Coglatas.Domain.ProjectIde;

public enum PredicateObservationSnapshotMode { Manifest, AvailableOperators }
public enum PredicateObservationSnapshotState { ManifestOnly, AvailableOperators, NoRecordedOperators, BodyBudgetExceeded, RefusedInvocation }

/// <summary>Explicit original operator frames from one owned evaluation; this grants no current access or Compiler-run selection.</summary>
public sealed class PredicateObservationSnapshot
{
    public const string Format = "coglatas.local-predicate-observation-snapshot/1";
    public PredicateObservationSnapshotState State { get; }
    public PredicateObservationSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public PredicateEvaluationCaptureState? OriginalCaptureState { get; }
    public SourceJson? OriginalRecordMetadata { get; }
    public ContentDigest? OriginalRecordDigest { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private PredicateObservationSnapshot(PredicateObservationSnapshotState state, PredicateObservationSnapshotMode mode, int maximumBodyBytes,
        PredicateEvaluationObservation? record = null, Dictionary<string, SourceJson>? bodies = null)
    {
        State = state; Mode = mode; MaximumBodyBytes = maximumBodyBytes;
        OriginalCaptureState = record?.State; OriginalRecordMetadata = record?.Data; OriginalRecordDigest = record?.Digest;
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => body.ToCanonicalBytes().Length);
        Data = SourceJson.FromObject(new {
            format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes, retainedBodyBytes = RetainedBodyBytes,
            scope = "OriginalObservedBooleanOperatorFrames", compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated",
            causalMeaning = "NotRecorded", regionalEvaluation = "NotRecorded", authority = "NotEvaluated",
            producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            originalRecord = record is null ? null : new { digest = record.Digest.Data.Value, metadata = record.Data.Value },
            nodes = record?.Nodes.Select(node => new { ordinal = node.Ordinal, digest = node.Digest.Data.Value, bytes = node.Bytes,
                bodyCaptured = Bodies.ContainsKey(node.Digest.Value) }).ToArray()
        });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static PredicateObservationSnapshot Read(RecordedPredicateEvaluation evaluation, PurePredicate predicate,
        PredicateEvaluationObservation record, PredicateObservationSnapshotMode mode = PredicateObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation); ArgumentNullException.ThrowIfNull(predicate); ArgumentNullException.ThrowIfNull(record);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!record.IsOwnedBy(evaluation) || !evaluation.IsFor(predicate))
            return new(PredicateObservationSnapshotState.RefusedInvocation, mode, maximumBodyBytes);
        if (mode == PredicateObservationSnapshotMode.Manifest)
            return new(PredicateObservationSnapshotState.ManifestOnly, mode, maximumBodyBytes, record);
        if (record.State != PredicateEvaluationCaptureState.RecordedOperators)
            return new(PredicateObservationSnapshotState.NoRecordedOperators, mode, maximumBodyBytes, record);
        if (record.RetainedBodyBytes > maximumBodyBytes)
            return new(PredicateObservationSnapshotState.BodyBudgetExceeded, mode, maximumBodyBytes, record);
        var bodies = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        foreach (var node in record.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bodies.Add(node.Digest.Value, record.Body(node));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(PredicateObservationSnapshotState.AvailableOperators, mode, maximumBodyBytes, record, bodies);
    }
}
