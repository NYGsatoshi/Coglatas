using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerRegionalPredicateCaptureState
{
    ManifestOnly, RecordedRegionalEvaluations, NoActualRegionalEvaluations, BodyBudgetExceeded,
    NodeLimitReached, EvaluationLimitReached, CaptureInterrupted, MetadataBudgetExceeded
}

/// <summary>Aggregate local regional capture limits, not an adopted host, retention or Registry policy.</summary>
public sealed class CompilerRegionalPredicateObservationPolicy
{
    internal CompilerPredicateObservationPolicy CorePolicy { get; }
    public PredicateEvaluationCaptureMode Mode => CorePolicy.Mode;
    public int MaximumEvaluations => CorePolicy.MaximumEvaluations;
    public int MaximumNodes => CorePolicy.MaximumNodes;
    public int MaximumBodyBytes => CorePolicy.MaximumBodyBytes;
    public CompilerRegionalPredicateObservationPolicy(PredicateEvaluationCaptureMode mode = PredicateEvaluationCaptureMode.Manifest,
        int maximumEvaluations = CompilerPredicateObservationPolicy.HardEvaluationLimit,
        int maximumNodes = PredicateEvaluationCapturePolicy.HardNodeLimit,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes)
        => CorePolicy = new(mode, maximumEvaluations, maximumNodes, maximumBodyBytes);
}

/// <summary>One actual pure compile and separately owned regional observations; it allocates no host run.</summary>
public sealed class RecordedRegionalPredicateCompilation
{
    [JsonIgnore]
    public CoreCompilation Compilation { get; }
    public ProjectCompilerRegionalPredicateObservation Observation { get; }
    internal RecordedRegionalPredicateCompilation(CoreCompilation compilation, CompilerRegionalPredicateObservationPolicy policy,
        CompilerRegionalCaptureSnapshot capture, IReadOnlyList<CompilerRegionalCaptureInvocation> invocations)
    {
        Compilation = compilation; Observation = new(this, compilation, policy, capture, invocations);
    }
    public bool IsFor(CoreCompilation compilation) => ReferenceEquals(Compilation, compilation);
}

public sealed class ProjectCompilerRegionalPredicateObservation
{
    public const string Format = "coglatas.local-regional-predicate-observation/1";
    private readonly RecordedRegionalPredicateCompilation _owner;
    private readonly CoreCompilation _compilation;
    private readonly IReadOnlyList<CompilerObservedRegionalPredicateEvaluation> _entries;
    public CompilerRegionalPredicateObservationPolicy Policy { get; }
    public CompilerRegionalPredicateCaptureState State { get; }
    public int CompletedRegionCalls { get; }
    public int StartedEvaluations { get; }
    public int CompletedEvaluations { get; }
    public int ObservedNodes { get; }
    public int RetainedBodyBytes { get; }
    public ContentDigest InputDigest => _compilation.Input.InputDigest;
    public ContentDigest ResultDigest => _compilation.Result.ResultDigest;
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal ProjectCompilerRegionalPredicateObservation(RecordedRegionalPredicateCompilation owner, CoreCompilation compilation,
        CompilerRegionalPredicateObservationPolicy policy, CompilerRegionalCaptureSnapshot capture,
        IReadOnlyList<CompilerRegionalCaptureInvocation> invocations)
    {
        _owner = owner; _compilation = compilation; Policy = policy;
        CompletedRegionCalls = invocations.Count; StartedEvaluations = capture.Started;
        CompletedEvaluations = capture.Completed; ObservedNodes = capture.Nodes;
        State = capture.Gap switch
        {
            CompilerPredicateObservationCaptureState.BodyBudgetExceeded => CompilerRegionalPredicateCaptureState.BodyBudgetExceeded,
            CompilerPredicateObservationCaptureState.NodeLimitReached => CompilerRegionalPredicateCaptureState.NodeLimitReached,
            CompilerPredicateObservationCaptureState.EvaluationLimitReached => CompilerRegionalPredicateCaptureState.EvaluationLimitReached,
            null => policy.Mode == PredicateEvaluationCaptureMode.Manifest ? CompilerRegionalPredicateCaptureState.ManifestOnly :
                capture.Entries.Count == 0 ? CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations : CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations,
            _ => CompilerRegionalPredicateCaptureState.CaptureInterrupted
        };
        var entries = new List<CompilerObservedRegionalPredicateEvaluation>();
        var regions = new List<object>();
        try
        {
            foreach (var invocation in invocations)
            {
                // Bind the actual call's binding/compilation pair, never equal context, ID or digest.
                var region = compilation.Dependencies?.Regions.SingleOrDefault(r =>
                    ReferenceEquals(r.Binding, invocation.Binding) && ReferenceEquals(r.Compilation, invocation.Compilation));
                regions.Add(new { ordinal = regions.Count, bindingDigest = invocation.Binding.Digest.Data.Value,
                    context = invocation.Binding.Source.Context.Data.Value, sourceDigest = invocation.Binding.Source.Digest.Data.Value,
                    derivedBindingInputDigest = invocation.Compilation.Input.InputDigest.Data.Value,
                    regionResultDigest = invocation.Compilation.Result.ResultDigest.Data.Value,
                    regionalResult = new { execution = CompilerContractTokens.Execution(invocation.Compilation.Result.Execution),
                        coverage = invocation.Compilation.Result.Coverage.ToString(), verdict = invocation.Compilation.Result.Verdict.ToString(),
                        evidenceComplete = invocation.Compilation.Result.EvidenceComplete },
                    publishedRegionDigest = region?.Digest.Data.Value, firstEvaluationOrdinal = invocation.Start,
                    startedEvaluationExclusiveEnd = invocation.End, retainedRegion = region is not null });
                if (State != CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations || region is null) continue;
                foreach (var entry in capture.Entries.Where(e => e.Ordinal >= invocation.Start && e.Ordinal < invocation.End))
                    entries.Add(new(region, entry));
            }
            if (State == CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations && entries.Count != capture.Entries.Count)
            {
                State = CompilerRegionalPredicateCaptureState.CaptureInterrupted; entries.Clear();
            }
            _entries = new ImmutableListView<CompilerObservedRegionalPredicateEvaluation>(entries);
            RetainedBodyBytes = _entries.Sum(e => e.Entry.Run.Observation.RetainedBodyBytes);
            Data = Metadata(regions);
        }
        catch (FormatException)
        {
            // Capture publication cannot replace actual Compiler truth with a failed envelope.
            State = CompilerRegionalPredicateCaptureState.MetadataBudgetExceeded;
            _entries = ImmutableListView<CompilerObservedRegionalPredicateEvaluation>.Empty;
            RetainedBodyBytes = 0; Data = Metadata([]);
        }
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());

        SourceJson Metadata(IEnumerable<object> regionMetadata) => SourceJson.FromObject(new
        {
            format = Format, state = State.ToString(), scope = "ActualRegionalEvaluationsOnly",
            initialRootEvaluation = "NotRecorded", rootSummaryDiagnosticLinks = "NotRecorded",
            compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated", causalMeaning = "NotRecorded",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            completedRegionCalls = CompletedRegionCalls, startedEvaluations = StartedEvaluations,
            completedEvaluations = CompletedEvaluations, observedNodes = ObservedNodes, retainedBodyBytes = RetainedBodyBytes,
            capture = new { declared = "BeforeCompile", mode = policy.Mode.ToString(), maximumEvaluations = policy.MaximumEvaluations,
                maximumNodes = policy.MaximumNodes, maximumBodyBytes = policy.MaximumBodyBytes },
            basis = new { rootInputDigest = InputDigest.Data.Value, sourceDigest = compilation.Input.Source.Digest.Data.Value,
                resultDigest = ResultDigest.Data.Value },
            result = new { execution = CompilerContractTokens.Execution(compilation.Result.Execution),
                coverage = compilation.Result.Coverage.ToString(), verdict = compilation.Result.Verdict.ToString(),
                evidenceComplete = compilation.Result.EvidenceComplete },
            regions = regionMetadata, evaluations = _entries.Select(e => e.Metadata.Value).ToArray()
        });
    }
    public bool IsOwnedBy(RecordedRegionalPredicateCompilation execution) => ReferenceEquals(_owner, execution) &&
        ReferenceEquals(execution.Observation, this) && ReferenceEquals(execution.Compilation, _compilation);
    internal IReadOnlyList<CompilerObservedRegionalPredicateEvaluation> Select(CompilerDependencyRegion region, CompilerDiagnostic diagnostic) =>
        new ImmutableListView<CompilerObservedRegionalPredicateEvaluation>(_entries.Where(e =>
            ReferenceEquals(e.Region, region) && e.Diagnostics.Any(d => ReferenceEquals(d, diagnostic))));
}

internal sealed class CompilerObservedRegionalPredicateEvaluation
{
    internal CompilerDependencyRegion Region { get; }
    internal CompilerPredicateObservationEntry Entry { get; }
    internal IReadOnlyList<CompilerDiagnostic> Diagnostics { get; }
    internal SourceJson Metadata { get; }
    internal CompilerObservedRegionalPredicateEvaluation(CompilerDependencyRegion region, CompilerPredicateObservationEntry entry)
    {
        Region = region; Entry = entry;
        Diagnostics = new ImmutableListView<CompilerDiagnostic>(entry.Diagnostics.Where(d =>
            region.Diagnostics.Any(p => ReferenceEquals(d, p))).Distinct());
        Metadata = SourceJson.FromObject(new
        {
            ordinal = entry.Ordinal, bindingRegionDigest = region.Binding.Digest.Data.Value,
            publishedRegionDigest = region.Digest.Data.Value, rootInputDigest = region.Binding.RootInput.InputDigest.Data.Value,
            context = region.Binding.Source.Context.Data.Value, sourceDigest = region.Binding.Source.Digest.Data.Value,
            ruleId = entry.Rule.RuleId, ruleVersion = entry.Rule.Version, location = entry.Location.Data.Value,
            originalObservation = entry.Run.Observation.Data.Value, originalObservationDigest = entry.Run.Observation.Digest.Data.Value,
            regionalDiagnosticLinks = entry.Diagnostics.Count,
            diagnostics = Diagnostics.Select(d => new { diagnosticId = d.DiagnosticId.ToString(),
                digest = ContentDigest.Compute("coglatas.compiler-diagnostic/1", d.Data.ToCanonicalBytes()).Data.Value }).ToArray()
        });
    }
}
internal sealed record CompilerRegionalCaptureSnapshot(CompilerPredicateObservationCaptureState? Gap,
    int Started, int Completed, int Nodes, IReadOnlyList<CompilerPredicateObservationEntry> Entries);
internal sealed record CompilerRegionalCaptureInvocation(CompilerBindingRegion Binding, CoreCompilation Compilation, int Start, int End);

internal sealed class CompilerRegionalPredicateObservationCollector(CompilerRegionalPredicateObservationPolicy policy)
{
    private readonly List<CompilerRegionalCaptureInvocation> _regions = [];
    internal CompilerPredicateObservationCollector Collector { get; } = new(policy.CorePolicy);
    internal void RegionCompleted(CompilerBindingRegion binding, CoreCompilation compilation, int start)
        => _regions.Add(new(binding, compilation, start, Collector.RegionalStartedEvaluations));
    internal RecordedRegionalPredicateCompilation Complete(CoreCompilation compilation, CancellationToken token)
    {
        var regions = new ImmutableListView<CompilerRegionalCaptureInvocation>(_regions);
        var execution = new RecordedRegionalPredicateCompilation(compilation, policy, Collector.FinishRegional(compilation, token), regions);
        // A cancellation observed while publishing capture metadata withdraws the graph,
        // without rewriting an already completed original Compiler result.
        return token.IsCancellationRequested && execution.Observation.State != CompilerRegionalPredicateCaptureState.CaptureInterrupted
            ? new(compilation, policy, Collector.FinishRegional(compilation, token), regions) : execution;
    }
}
