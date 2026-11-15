using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerPredicateObservationCaptureState
{
    ManifestOnly, RecordedInitialRootEvaluations, NoActualEvaluations, BodyBudgetExceeded,
    NodeLimitReached, EvaluationLimitReached, CaptureInterrupted
}

/// <summary>Predeclared local limits for actual initial-root evaluations, not adopted retention or host-run policy.</summary>
public sealed class CompilerPredicateObservationPolicy
{
    public const int HardEvaluationLimit = 128;
    public PredicateEvaluationCaptureMode Mode { get; }
    public int MaximumEvaluations { get; }
    public int MaximumNodes { get; }
    public int MaximumBodyBytes { get; }
    public CompilerPredicateObservationPolicy(PredicateEvaluationCaptureMode mode = PredicateEvaluationCaptureMode.Manifest,
        int maximumEvaluations = HardEvaluationLimit, int maximumNodes = PredicateEvaluationCapturePolicy.HardNodeLimit,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes)
    {
        _ = new PredicateEvaluationCapturePolicy(mode, maximumNodes, maximumBodyBytes);
        if (maximumEvaluations is < 1 or > HardEvaluationLimit) throw new ArgumentOutOfRangeException(nameof(maximumEvaluations));
        Mode = mode; MaximumEvaluations = maximumEvaluations; MaximumNodes = maximumNodes; MaximumBodyBytes = maximumBodyBytes;
    }
}

/// <summary>A fresh actual compile and its separately owned initial-root observation; old compilation/host contracts stay unchanged.</summary>
public sealed class RecordedPredicateCompilation
{
    [JsonIgnore]
    public CoreCompilation Compilation { get; }
    public ProjectCompilerPredicateObservation Observation { get; }
    internal RecordedPredicateCompilation(CoreCompilation compilation, CompilerPredicateObservationPolicy policy,
        CompilerPredicateObservationCaptureState state, int started, int completed, int nodes,
        IEnumerable<CompilerPredicateObservationEntry> entries)
    {
        Compilation = compilation;
        Observation = new(this, compilation, policy, state, started, completed, nodes, entries);
    }
    public bool IsFor(CoreCompilation compilation) => ReferenceEquals(Compilation, compilation);
}

/// <summary>Original initial-root traversal records. Regional re-evaluations cannot borrow these records by diagnostic ID.</summary>
public sealed class ProjectCompilerPredicateObservation
{
    public const string Format = "coglatas.local-initial-root-predicate-observation/1";
    private readonly RecordedPredicateCompilation _owner;
    private readonly CoreCompilation _compilation;
    private readonly IReadOnlyList<CompilerObservedPredicateEvaluation> _entries;
    public CompilerPredicateObservationPolicy Policy { get; }
    public CompilerPredicateObservationCaptureState State { get; }
    public int StartedEvaluations { get; }
    public int CompletedEvaluations { get; }
    public int ObservedNodes { get; }
    public int RetainedBodyBytes { get; }
    public ContentDigest InputDigest => _compilation.Input.InputDigest;
    public ContentDigest ResultDigest => _compilation.Result.ResultDigest;
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal ProjectCompilerPredicateObservation(RecordedPredicateCompilation owner, CoreCompilation compilation,
        CompilerPredicateObservationPolicy policy, CompilerPredicateObservationCaptureState state,
        int started, int completed, int nodes, IEnumerable<CompilerPredicateObservationEntry> entries)
    {
        _owner = owner; _compilation = compilation; Policy = policy; State = state;
        StartedEvaluations = started; CompletedEvaluations = completed; ObservedNodes = nodes;
        _entries = new ImmutableListView<CompilerObservedPredicateEvaluation>(entries.Select(e => new CompilerObservedPredicateEvaluation(e, compilation)));
        RetainedBodyBytes = _entries.Sum(e => e.Run.Observation.RetainedBodyBytes);
        if (state != CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations && _entries.Count != 0)
            throw new InvalidOperationException("An incomplete capture cannot retain a partial evaluation graph.");
        Data = SourceJson.FromObject(new {
            format = Format, state = state.ToString(), scope = "ActualInitialRootEvaluationOnly",
            regionalEvaluation = "NotRecorded", compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            causalMeaning = "NotRecorded", startedEvaluations = started, completedEvaluations = completed, observedNodes = nodes,
            retainedBodyBytes = RetainedBodyBytes,
            capture = new { declared = "BeforeCompile", mode = policy.Mode.ToString(), maximumEvaluations = policy.MaximumEvaluations,
                maximumNodes = policy.MaximumNodes, maximumBodyBytes = policy.MaximumBodyBytes },
            basis = new { inputDigest = InputDigest.Data.Value, sourceDigest = compilation.Input.Source.Digest.Data.Value,
                context = compilation.Input.Source.Context.Data.Value, compiler = compilation.Input.Compiler.Data.Value,
                resultDigest = ResultDigest.Data.Value },
            result = new { execution = CompilerContractTokens.Execution(compilation.Result.Execution),
                coverage = compilation.Result.Coverage.ToString(), verdict = compilation.Result.Verdict.ToString(),
                evidenceComplete = compilation.Result.EvidenceComplete },
            evaluations = _entries.Select(e => e.Metadata.Value).ToArray()
        });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsOwnedBy(RecordedPredicateCompilation execution) => ReferenceEquals(_owner, execution) &&
        ReferenceEquals(execution.Observation, this) && ReferenceEquals(execution.Compilation, _compilation);

    internal IReadOnlyList<CompilerObservedPredicateEvaluation> Select(CompilerDiagnostic diagnostic) =>
        new ImmutableListView<CompilerObservedPredicateEvaluation>(_entries.Where(e => e.PublishedDiagnostics.Any(d => ReferenceEquals(d, diagnostic))));
}

internal sealed class CompilerObservedPredicateEvaluation
{
    internal int Ordinal { get; }
    internal PurePredicate Predicate { get; }
    internal RecordedPredicateEvaluation Run { get; }
    internal IReadOnlyList<CompilerDiagnostic> PublishedDiagnostics { get; }
    internal SourceJson Metadata { get; }
    internal CompilerObservedPredicateEvaluation(CompilerPredicateObservationEntry entry, CoreCompilation compilation)
    {
        Ordinal = entry.Ordinal; Predicate = entry.Predicate; Run = entry.Run;
        PublishedDiagnostics = new ImmutableListView<CompilerDiagnostic>(entry.Diagnostics
            .Where(d => compilation.Result.Diagnostics.Any(p => ReferenceEquals(d, p))).Distinct());
        Metadata = SourceJson.FromObject(new { ordinal = Ordinal, ruleId = entry.Rule.RuleId, ruleVersion = entry.Rule.Version,
            location = entry.Location.Data.Value, originalObservation = Run.Observation.Data.Value,
            originalObservationDigest = Run.Observation.Digest.Data.Value, originalRootDiagnosticLinks = entry.Diagnostics.Count,
            publishedDiagnostics = PublishedDiagnostics.Select(d => new { diagnosticId = d.DiagnosticId.ToString(),
                digest = ContentDigest.Compute("coglatas.compiler-diagnostic/1", d.Data.ToCanonicalBytes()).Data.Value }).ToArray() });
    }
}

internal sealed class CompilerPredicateObservationEntry(int ordinal, CompilerRuleContract rule,
    ProjectLocation location, PurePredicate predicate, RecordedPredicateEvaluation run)
{
    internal int Ordinal { get; } = ordinal;
    internal CompilerRuleContract Rule { get; } = rule;
    internal ProjectLocation Location { get; } = location;
    internal PurePredicate Predicate { get; } = predicate;
    internal RecordedPredicateEvaluation Run { get; } = run;
    internal List<CompilerDiagnostic> Diagnostics { get; } = [];
}

internal sealed class CompilerPredicateObservationCollector(CompilerPredicateObservationPolicy policy)
{
    private readonly List<CompilerPredicateObservationEntry> _entries = [];
    private int _started, _completed, _nodes, _bytes;
    private CompilerPredicateObservationCaptureState? _gap;

    internal PredicateEvaluation Evaluate(PurePredicate predicate, CompilerRuleContract rule, ProjectLocation location, CancellationToken token)
    {
        var ordinal = _started++;
        if (policy.Mode == PredicateEvaluationCaptureMode.ObservedOperators)
        {
            if (_started > policy.MaximumEvaluations) Gap(CompilerPredicateObservationCaptureState.EvaluationLimitReached);
            if (_gap is null && _nodes >= policy.MaximumNodes) Gap(CompilerPredicateObservationCaptureState.NodeLimitReached);
            if (_gap is null && _bytes >= policy.MaximumBodyBytes) Gap(CompilerPredicateObservationCaptureState.BodyBudgetExceeded);
        }
        var mode = _gap is null ? policy.Mode : PredicateEvaluationCaptureMode.Manifest;
        var run = predicate.EvaluateWithObservation(new(mode, maximumNodes: Math.Max(1, policy.MaximumNodes - _nodes),
            maximumBodyBytes: Math.Max(1, policy.MaximumBodyBytes - _bytes)), token);
        _nodes += run.Observation.ObservedNodes;
        if (run.Evaluation is null) token.ThrowIfCancellationRequested();
        if (run.Evaluation is null) throw new InvalidOperationException("An interrupted evaluation cannot supply a completed predicate result.");
        _completed++;
        if (mode == PredicateEvaluationCaptureMode.ObservedOperators && _gap is null)
        {
            if (run.Observation.State != PredicateEvaluationCaptureState.RecordedOperators)
                Gap(run.Observation.State switch {
                    PredicateEvaluationCaptureState.NodeLimitReached => CompilerPredicateObservationCaptureState.NodeLimitReached,
                    PredicateEvaluationCaptureState.BodyBudgetExceeded => CompilerPredicateObservationCaptureState.BodyBudgetExceeded,
                    _ => CompilerPredicateObservationCaptureState.CaptureInterrupted });
            else if (_nodes > policy.MaximumNodes) Gap(CompilerPredicateObservationCaptureState.NodeLimitReached);
            else if ((long)_bytes + run.Observation.RetainedBodyBytes > policy.MaximumBodyBytes) Gap(CompilerPredicateObservationCaptureState.BodyBudgetExceeded);
            else { _entries.Add(new(ordinal, rule, location, predicate, run)); _bytes += run.Observation.RetainedBodyBytes; }
        }
        return run.Evaluation;
    }

    internal void Link(PredicateEvaluation evaluation, CompilerDiagnostic diagnostic)
    {
        var entry = _entries.SingleOrDefault(e => ReferenceEquals(e.Run.Evaluation, evaluation));
        if (entry is not null && !entry.Diagnostics.Any(d => ReferenceEquals(d, diagnostic))) entry.Diagnostics.Add(diagnostic);
    }

    internal RecordedPredicateCompilation Complete(CoreCompilation compilation, CancellationToken token)
    {
        if (token.IsCancellationRequested || compilation.Result.Execution != CompilerExecutionState.Completed)
            Gap(CompilerPredicateObservationCaptureState.CaptureInterrupted);
        var state = _gap ?? (policy.Mode == PredicateEvaluationCaptureMode.Manifest ? CompilerPredicateObservationCaptureState.ManifestOnly :
            _entries.Count == 0 ? CompilerPredicateObservationCaptureState.NoActualEvaluations : CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations);
        return new(compilation, policy, state, _started, _completed, _nodes, _entries);
    }

    private void Gap(CompilerPredicateObservationCaptureState state) { _gap ??= state; _entries.Clear(); _bytes = 0; }
}
