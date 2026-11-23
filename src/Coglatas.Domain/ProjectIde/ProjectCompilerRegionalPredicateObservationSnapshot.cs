using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerRegionalPredicateSnapshotMode { Manifest, AvailableOperators }
public enum CompilerRegionalPredicateSnapshotState
{
    ManifestOnly, AvailableOperators, NoRecordedOperators, NoObservedRegionalDiagnostic, BodyBudgetExceeded, RefusedInvocation
}

/// <summary>Original operators for an actual owned regional diagnostic; root summaries confer no ownership.</summary>
public sealed class ProjectCompilerRegionalPredicateObservationSnapshot
{
    public const string Format = "coglatas.local-regional-predicate-observation-snapshot/1";
    public CompilerRegionalPredicateSnapshotState State { get; }
    public CompilerRegionalPredicateSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int RetainedBodyBytes { get; }
    public int RetainedEvaluationFrameBytes { get; }
    public int SelectedEvaluations { get; }
    public CompilerRegionalPredicateCaptureState? OriginalCaptureState { get; }
    [JsonIgnore]
    public SourceJson? OriginalRecordMetadata { get; }
    public ContentDigest? OriginalRecordDigest { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public IReadOnlyDictionary<string, SourceJson> EvaluationFrames { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectCompilerRegionalPredicateObservationSnapshot(CompilerRegionalPredicateSnapshotState state,
        CompilerRegionalPredicateSnapshotMode mode, int maximumBodyBytes, ProjectCompilerRegionalPredicateObservation? record = null,
        CompilerDependencyRegion? region = null, CompilerDiagnostic? diagnostic = null,
        IReadOnlyList<CompilerObservedRegionalPredicateEvaluation>? selected = null,
        Dictionary<string, SourceJson>? bodies = null, Dictionary<string, SourceJson>? evaluations = null)
    {
        State = state; Mode = mode; MaximumBodyBytes = maximumBodyBytes;
        OriginalCaptureState = record?.State; OriginalRecordMetadata = record?.Data; OriginalRecordDigest = record?.Digest;
        SelectedEvaluations = selected?.Count ?? 0;
        Bodies = new ImmutableMapView<string, SourceJson>(bodies ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        EvaluationFrames = new ImmutableMapView<string, SourceJson>(evaluations ?? new(StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => body.ToCanonicalBytes().Length);
        RetainedEvaluationFrameBytes = EvaluationFrames.Values.Sum(body => body.ToCanonicalBytes().Length);
        Data = SourceJson.FromObject(new
        {
            format = Format, state = state.ToString(), mode = mode.ToString(), maximumBodyBytes,
            scope = "OriginalRegionalPredicateFrames", initialRootEvaluation = "NotRecorded",
            rootSummaryDiagnosticLinks = "NotRecorded", compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated",
            causalMeaning = "NotRecorded", authority = "NotEvaluated", producerTrust = "NotEvaluated",
            eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            selectedEvaluations = SelectedEvaluations, retainedBodyBytes = RetainedBodyBytes,
            retainedEvaluationFrameBytes = RetainedEvaluationFrameBytes,
            selection = record is null ? "NotEvaluated" :
                record.State is not (CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations or CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations)
                    ? "NotRecorded" : SelectedEvaluations == 0 ? "NoObservedRegionalDiagnostic" : "ObservedRegionalEvaluations",
            originalRecord = record is null ? null : new { digest = record.Digest.Data.Value, captureState = record.State.ToString() },
            region = region is null ? null : new { bindingDigest = region.Binding.Digest.Data.Value, publishedDigest = region.Digest.Data.Value },
            diagnostic = diagnostic is null ? null : new { diagnosticId = diagnostic.DiagnosticId.ToString(),
                digest = ContentDigest.Compute("coglatas.compiler-diagnostic/1", diagnostic.Data.ToCanonicalBytes()).Data.Value },
            evaluations = selected?.Select(e => new { ordinal = e.Entry.Ordinal, metadata = e.Metadata.Value,
                evaluationFrame = new { digest = e.Entry.Run.Observation.EvaluationDigest!.Data.Value,
                    bytes = e.Entry.Run.Observation.EvaluationFrame!.ToCanonicalBytes().Length,
                    bodyCaptured = EvaluationFrames.ContainsKey(e.Entry.Run.Observation.EvaluationDigest.Value) },
                nodes = e.Entry.Run.Observation.Nodes.Select(node => new { ordinal = node.Ordinal,
                    digest = node.Digest.Data.Value, bytes = node.Bytes, bodyCaptured = Bodies.ContainsKey(node.Digest.Value) }).ToArray() }).ToArray()
        });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectCompilerRegionalPredicateObservationSnapshot Read(RecordedRegionalPredicateCompilation execution,
        CoreCompilation compilation, ProjectCompilerRegionalPredicateObservation record, CompilerDependencyRegion region,
        CompilerDiagnostic diagnostic, CompilerRegionalPredicateSnapshotMode mode = CompilerRegionalPredicateSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution); ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(region); ArgumentNullException.ThrowIfNull(diagnostic);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        cancellationToken.ThrowIfCancellationRequested();
        ProjectCompilerRegionalPredicateObservationSnapshot Finish(CompilerRegionalPredicateSnapshotState state,
            ProjectCompilerRegionalPredicateObservation? owned = null, IReadOnlyList<CompilerObservedRegionalPredicateEvaluation>? entries = null,
            Dictionary<string, SourceJson>? frames = null, Dictionary<string, SourceJson>? evaluations = null)
        {
            var view = new ProjectCompilerRegionalPredicateObservationSnapshot(state, mode, maximumBodyBytes,
                owned, owned is null ? null : region, owned is null ? null : diagnostic, entries, frames, evaluations);
            cancellationToken.ThrowIfCancellationRequested(); return view;
        }
        if (!record.IsOwnedBy(execution) || !execution.IsFor(compilation) ||
            compilation.Dependencies is null || !compilation.Dependencies.Regions.Any(r => ReferenceEquals(r, region)) ||
            !region.Diagnostics.Any(d => ReferenceEquals(d, diagnostic)))
            return Finish(CompilerRegionalPredicateSnapshotState.RefusedInvocation);
        var selected = record.Select(region, diagnostic);
        if (mode == CompilerRegionalPredicateSnapshotMode.Manifest)
            return Finish(CompilerRegionalPredicateSnapshotState.ManifestOnly, record, selected);
        if (record.State == CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations)
            return Finish(CompilerRegionalPredicateSnapshotState.NoObservedRegionalDiagnostic, record, selected);
        if (record.State != CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations)
            return Finish(CompilerRegionalPredicateSnapshotState.NoRecordedOperators, record, selected);
        if (selected.Count == 0) return Finish(CompilerRegionalPredicateSnapshotState.NoObservedRegionalDiagnostic, record, selected);
        if (selected.Sum(e => (long)e.Entry.Run.Observation.RetainedBodyBytes) > maximumBodyBytes)
            return Finish(CompilerRegionalPredicateSnapshotState.BodyBudgetExceeded, record, selected);
        var bodies = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        var evaluationFrames = new Dictionary<string, SourceJson>(StringComparer.Ordinal);
        foreach (var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = item.Entry;
            var original = PredicateObservationSnapshot.Read(entry.Run, entry.Predicate, entry.Run.Observation,
                PredicateObservationSnapshotMode.AvailableOperators, maximumBodyBytes, cancellationToken);
            if (original.State != PredicateObservationSnapshotState.AvailableOperators)
                throw new InvalidOperationException("An owned regional evaluation lost its original graph.");
            foreach (var pair in original.Bodies)
                if (!bodies.TryAdd(pair.Key, pair.Value) && !bodies[pair.Key].Equals(pair.Value))
                    throw new InvalidOperationException("Conflicting original regional operator frame.");
            var frame = entry.Run.Observation.EvaluationFrame!; var digest = entry.Run.Observation.EvaluationDigest!;
            if (!ContentDigest.Compute(PredicateEvaluationObservation.EvaluationDomain, frame.ToCanonicalBytes()).Equals(digest))
                throw new InvalidOperationException("An original regional evaluation hash subject changed.");
            if (!evaluationFrames.TryAdd(digest.Value, frame) && !evaluationFrames[digest.Value].Equals(frame))
                throw new InvalidOperationException("Conflicting original regional evaluation frame.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Finish(CompilerRegionalPredicateSnapshotState.AvailableOperators, record, selected, bodies, evaluationFrames);
    }
}
