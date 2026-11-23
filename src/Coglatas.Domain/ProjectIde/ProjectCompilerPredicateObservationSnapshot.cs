namespace Coglatas.Domain.ProjectIde;

public enum CompilerPredicateObservationSnapshotMode { Manifest, AvailableOperators }
public enum CompilerPredicateObservationSnapshotState
{
    ManifestOnly, AvailableOperators, NoRecordedOperators, NoObservedPublishedDiagnostic, BodyBudgetExceeded, RefusedInvocation
}

/// <summary>Explicit original frames for one actual published diagnostic; no host-run selection or current access grant.</summary>
public sealed class ProjectCompilerPredicateObservationSnapshot
{
    public const string Format = "coglatas.local-initial-root-predicate-observation-snapshot/1";
    public CompilerPredicateObservationSnapshotState State { get; }
    public CompilerPredicateObservationSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public int RetainedEvaluationFrameBytes { get; }
    public int SelectedEvaluations { get; }
    public CompilerPredicateObservationCaptureState? OriginalCaptureState { get; }
    public SourceJson? OriginalRecordMetadata { get; }
    public ContentDigest? OriginalRecordDigest { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public IReadOnlyDictionary<string, SourceJson> EvaluationFrames { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectCompilerPredicateObservationSnapshot(CompilerPredicateObservationSnapshotState state,
        CompilerPredicateObservationSnapshotMode mode, int maximumBodyBytes, ProjectCompilerPredicateObservation? record = null,
        CompilerDiagnostic? diagnostic = null, IReadOnlyList<CompilerObservedPredicateEvaluation>? selected = null,
        Dictionary<string, SourceJson>? bodies = null, Dictionary<string, SourceJson>? evaluations = null)
    {
        State = state; Mode = mode; MaximumBodyBytes = maximumBodyBytes;
        OriginalCaptureState = record?.State; OriginalRecordMetadata = record?.Data; OriginalRecordDigest = record?.Digest;
        SelectedEvaluations = selected?.Count ?? 0;
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        EvaluationFrames = new ImmutableMapView<string, SourceJson>(evaluations ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => body.ToCanonicalBytes().Length);
        RetainedEvaluationFrameBytes = EvaluationFrames.Values.Sum(frame => frame.ToCanonicalBytes().Length);
        Data = SourceJson.FromObject(new {
            format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes,
            retainedBodyBytes = RetainedBodyBytes, retainedEvaluationFrameBytes = RetainedEvaluationFrameBytes,
            selectedEvaluations = SelectedEvaluations, scope = "OriginalInitialRootPredicateFrames",
            regionalEvaluation = "NotRecorded", compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated",
            causalMeaning = "NotRecorded", authority = "NotEvaluated", producerTrust = "NotEvaluated",
            eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            selection = record is null ? "NotEvaluated" :
                record.State is not (CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations or CompilerPredicateObservationCaptureState.NoActualEvaluations)
                    ? "NotRecorded" : SelectedEvaluations == 0 ? "NoObservedPublishedDiagnostic" : "ObservedInitialRootEvaluations",
            originalRecord = record is null ? null : new { digest = record.Digest.Data.Value, metadata = record.Data.Value },
            diagnostic = diagnostic is null ? null : new { diagnosticId = diagnostic.DiagnosticId.ToString(),
                digest = ContentDigest.Compute("coglatas.compiler-diagnostic/1", diagnostic.Data.ToCanonicalBytes()).Data.Value },
            evaluations = selected?.Select(entry => new { ordinal = entry.Ordinal, metadata = entry.Metadata.Value,
                evaluationFrame = new { digest = entry.Run.Observation.EvaluationDigest!.Data.Value,
                    bytes = entry.Run.Observation.EvaluationFrame!.ToCanonicalBytes().Length,
                    bodyCaptured = EvaluationFrames.ContainsKey(entry.Run.Observation.EvaluationDigest.Value) },
                nodes = entry.Run.Observation.Nodes.Select(node => new { ordinal = node.Ordinal, digest = node.Digest.Data.Value,
                    bytes = node.Bytes, bodyCaptured = Bodies.ContainsKey(node.Digest.Value) }).ToArray() }).ToArray()
        });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectCompilerPredicateObservationSnapshot Read(RecordedPredicateCompilation execution, CoreCompilation compilation,
        ProjectCompilerPredicateObservation record, CompilerDiagnostic diagnostic,
        CompilerPredicateObservationSnapshotMode mode = CompilerPredicateObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution); ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(diagnostic);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!record.IsOwnedBy(execution) || !execution.IsFor(compilation) || !compilation.Result.Diagnostics.Any(d => ReferenceEquals(d, diagnostic)))
            return new(CompilerPredicateObservationSnapshotState.RefusedInvocation, mode, maximumBodyBytes);
        var selected = record.Select(diagnostic);
        if (mode == CompilerPredicateObservationSnapshotMode.Manifest)
            return new(CompilerPredicateObservationSnapshotState.ManifestOnly, mode, maximumBodyBytes, record, diagnostic, selected);
        if (record.State == CompilerPredicateObservationCaptureState.NoActualEvaluations)
            return new(CompilerPredicateObservationSnapshotState.NoObservedPublishedDiagnostic, mode, maximumBodyBytes, record, diagnostic, selected);
        if (record.State != CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations)
            return new(CompilerPredicateObservationSnapshotState.NoRecordedOperators, mode, maximumBodyBytes, record, diagnostic, selected);
        if (selected.Count == 0)
            return new(CompilerPredicateObservationSnapshotState.NoObservedPublishedDiagnostic, mode, maximumBodyBytes, record, diagnostic, selected);
        // This is the operator body budget; derived digest-only evaluation frames are separately counted.
        if (selected.Sum(e => (long)e.Run.Observation.RetainedBodyBytes) > maximumBodyBytes)
            return new(CompilerPredicateObservationSnapshotState.BodyBudgetExceeded, mode, maximumBodyBytes, record, diagnostic, selected);
        var bodies = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        var evaluations = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        foreach (var entry in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = PredicateObservationSnapshot.Read(entry.Run, entry.Predicate, entry.Run.Observation,
                PredicateObservationSnapshotMode.AvailableOperators, maximumBodyBytes, cancellationToken);
            if (original.State != PredicateObservationSnapshotState.AvailableOperators)
                throw new InvalidOperationException("An owned completed evaluation lost its original graph.");
            foreach (var pair in original.Bodies)
            {
                if (!bodies.TryAdd(pair.Key, pair.Value) && !bodies[pair.Key].Equals(pair.Value))
                    throw new InvalidOperationException("Conflicting original operator frame binding.");
            }
            var frame = entry.Run.Observation.EvaluationFrame!;
            var digest = entry.Run.Observation.EvaluationDigest!;
            if (!ContentDigest.Compute(PredicateEvaluationObservation.EvaluationDomain, frame.ToCanonicalBytes()).Equals(digest))
                throw new InvalidOperationException("An original evaluation hash subject changed.");
            if (!evaluations.TryAdd(digest.Value, frame) && !evaluations[digest.Value].Equals(frame))
                throw new InvalidOperationException("Conflicting original evaluation frame binding.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(CompilerPredicateObservationSnapshotState.AvailableOperators, mode, maximumBodyBytes, record, diagnostic, selected, bodies, evaluations);
    }
}
