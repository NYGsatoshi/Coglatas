using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

public enum PredicateEvaluationCaptureMode { Manifest, ObservedOperators }
public enum PredicateEvaluationCaptureState { ManifestOnly, RecordedOperators, BodyBudgetExceeded, NodeLimitReached, CaptureInterrupted }

/// <summary>Predeclared local observation limits; not an adopted runtime retention or device budget.</summary>
public sealed class PredicateEvaluationCapturePolicy
{
    public const int HardNodeLimit = 1024;
    public PredicateEvaluationCaptureMode Mode { get; }
    public int MaximumNodes { get; }
    public int MaximumBodyBytes { get; }

    public PredicateEvaluationCapturePolicy(PredicateEvaluationCaptureMode mode = PredicateEvaluationCaptureMode.Manifest,
        int maximumNodes = HardNodeLimit, int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumNodes is < 1 or > HardNodeLimit) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        Mode = mode; MaximumNodes = maximumNodes; MaximumBodyBytes = maximumBodyBytes;
    }
}

/// <summary>One actual pure Boolean evaluation. This is not a Compiler invocation or intended host-run selection.</summary>
public sealed class RecordedPredicateEvaluation
{
    private readonly PurePredicate _predicate;
    [JsonIgnore]
    public PredicateEvaluation? Evaluation { get; }
    public PredicateEvaluationObservation Observation { get; }

    internal RecordedPredicateEvaluation(PurePredicate predicate, PredicateEvaluation? evaluation, PredicateEvaluationRecorder recorder)
    {
        if ((evaluation is null) != recorder.Interrupted) throw new InvalidOperationException("Observation completion differs from the actual evaluation.");
        _predicate = predicate; Evaluation = evaluation;
        Observation = new(this, predicate, evaluation, recorder);
    }

    public bool IsFor(PurePredicate predicate) => ReferenceEquals(_predicate, predicate);
}

public sealed class PredicateObservationNode
{
    public int Ordinal { get; }
    public ContentDigest Digest { get; }
    public int Bytes { get; }
    internal PredicateObservationNode(int ordinal, ContentDigest digest, int bytes) { Ordinal = ordinal; Digest = digest; Bytes = bytes; }
}

/// <summary>Actual visited operator frames; original Proof arrays, Compiler results and causal claims stay separate.</summary>
public sealed class PredicateEvaluationObservation
{
    public const string Format = "coglatas.local-predicate-evaluation-record/1";
    public const string PredicateDomain = "coglatas.predicate/1";
    public const string LiteralDomain = "coglatas.evaluated-predicate-literal/1";
    public const string NodeDomain = "coglatas.observed-predicate-node/1";
    public const string EvaluationDomain = "coglatas.local-predicate-evaluation/1";
    private readonly RecordedPredicateEvaluation _owner;
    private readonly IReadOnlyDictionary<string, SourceJson> _bodies;
    public PredicateEvaluationCapturePolicy Policy { get; }
    public PredicateEvaluationCaptureState State { get; }
    public bool Completed { get; }
    public int ObservedNodes { get; }
    public int RetainedBodyBytes { get; }
    public ContentDigest PredicateDigest { get; }
    public ContentDigest? EvaluationDigest { get; }
    /// <summary>Exact derived hash subject containing ordered literal digests, not literal bodies or a Compiler result.</summary>
    [JsonIgnore]
    public SourceJson? EvaluationFrame { get; }
    public IReadOnlyList<PredicateObservationNode> Nodes { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal PredicateEvaluationObservation(RecordedPredicateEvaluation owner, PurePredicate predicate, PredicateEvaluation? evaluation,
        PredicateEvaluationRecorder recorder)
    {
        _owner = owner; Policy = recorder.Policy; State = recorder.State; Completed = evaluation is not null; ObservedNodes = recorder.ObservedNodes;
        PredicateDigest = ContentDigest.Compute(PredicateDomain, predicate.Data.ToCanonicalBytes());
        if (evaluation is not null)
        {
            // Ordered original literal digests avoid adding depth or raw bodies to the local result frame.
            var result = SourceJson.FromObject(new { format = EvaluationDomain, truth = evaluation.Truth.ToString(),
                premises = evaluation.EvaluatedPremises.Select(value => ContentDigest.Compute(LiteralDomain, value.ToCanonicalBytes()).Data.Value).ToArray() },
                new(ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes, 16));
            EvaluationFrame = result;
            EvaluationDigest = ContentDigest.Compute(EvaluationDomain, result.ToCanonicalBytes());
        }
        var entries = recorder.Entries.OrderBy(entry => entry.Descriptor.Ordinal).ToArray();
        if (State == PredicateEvaluationCaptureState.RecordedOperators && entries.Length != ObservedNodes)
            throw new InvalidOperationException("A recorded graph must contain every observed node.");
        Nodes = new ImmutableListView<PredicateObservationNode>(entries.Select(entry => entry.Descriptor));
        _bodies = new ImmutableMapView<string, SourceJson>(entries.ToDictionary(entry => entry.Descriptor.Digest.Value, entry => entry.Body, StringComparer.Ordinal), StringComparer.Ordinal);
        RetainedBodyBytes = Nodes.Sum(node => node.Bytes);
        Data = SourceJson.FromObject(new {
            format = Format, state = State.ToString(), execution = Completed ? "Completed" : "Cancelled",
            scope = "ActualStandaloneBooleanEvaluation", compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated",
            regionalEvaluation = "NotRecorded", causalMeaning = "NotRecorded",
            relationshipStructure = State == PredicateEvaluationCaptureState.RecordedOperators ? "ObservedBooleanEvaluationTree" : "NotRecorded",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            capture = new { declared = "BeforeEvaluation", mode = Policy.Mode.ToString(), maximumNodes = Policy.MaximumNodes, maximumBodyBytes = Policy.MaximumBodyBytes },
            predicateDigest = PredicateDigest.Data.Value, evaluationDigest = EvaluationDigest?.Data.Value,
            truth = evaluation?.Truth.ToString(), observedNodes = ObservedNodes, retainedBodyBytes = RetainedBodyBytes,
            nodes = Nodes.Select(node => new { ordinal = node.Ordinal, digest = node.Digest.Data.Value, bytes = node.Bytes }).ToArray()
        });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsOwnedBy(RecordedPredicateEvaluation evaluation) => ReferenceEquals(_owner, evaluation) && ReferenceEquals(evaluation.Observation, this);

    internal SourceJson Body(PredicateObservationNode node)
    {
        var body = _bodies[node.Digest.Value];
        if (body.ToCanonicalBytes().Length != node.Bytes || !ContentDigest.Compute(NodeDomain, body.ToCanonicalBytes()).Equals(node.Digest))
            throw new InvalidOperationException("An original operator frame binding changed.");
        return body;
    }
}

internal sealed class PredicateEvaluationRecorder(PredicateEvaluationCapturePolicy policy, CancellationToken cancellationToken)
{
    internal sealed class Visit(int ordinal, int[] path)
    {
        internal int Ordinal { get; } = ordinal;
        internal int[] Path { get; } = path;
        internal List<int> Children { get; } = [];
    }
    internal sealed record Entry(PredicateObservationNode Descriptor, SourceJson Body);
    private readonly List<Entry> _entries = [];
    private PredicateEvaluationCaptureState? _gap;
    private int _bytes;
    internal PredicateEvaluationCapturePolicy Policy => policy;
    internal int ObservedNodes { get; private set; }
    internal bool Interrupted { get; private set; }
    internal IEnumerable<Entry> Entries => _entries;
    internal PredicateEvaluationCaptureState State => _gap ?? (policy.Mode == PredicateEvaluationCaptureMode.Manifest
        ? PredicateEvaluationCaptureState.ManifestOnly : PredicateEvaluationCaptureState.RecordedOperators);

    internal Visit? Enter(Visit? parent, int operand)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ordinal = ObservedNodes++;
        if (policy.Mode == PredicateEvaluationCaptureMode.Manifest || _gap is not null) return null;
        if (ObservedNodes > policy.MaximumNodes) { Gap(PredicateEvaluationCaptureState.NodeLimitReached); return null; }
        var path = parent is null ? Array.Empty<int>() : parent.Path.Append(operand).ToArray();
        parent?.Children.Add(ordinal);
        return new(ordinal, path);
    }

    internal PredicateTruth Complete(Visit? visit, PurePredicate predicate, PredicateTruth truth, int premiseOrdinal)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (visit is null || _gap is not null || policy.Mode == PredicateEvaluationCaptureMode.Manifest) return truth;
        var body = SourceJson.FromObject(new {
            format = PredicateEvaluationObservation.NodeDomain, ordinal = visit.Ordinal, path = visit.Path, kind = predicate.Kind, truth = truth.ToString(),
            evaluatedChildren = visit.Children, skippedOperands = predicate.Operands.Count - visit.Children.Count,
            literal = predicate.Literal is null ? null : new { premiseOrdinal,
                digest = ContentDigest.Compute(PredicateEvaluationObservation.LiteralDomain, predicate.Literal.Data.ToCanonicalBytes()).Data.Value }
        });
        var bytes = body.ToCanonicalBytes();
        if ((long)_bytes + bytes.Length > policy.MaximumBodyBytes) Gap(PredicateEvaluationCaptureState.BodyBudgetExceeded);
        else
        {
            _entries.Add(new(new(visit.Ordinal, ContentDigest.Compute(PredicateEvaluationObservation.NodeDomain, bytes), bytes.Length), body));
            _bytes += bytes.Length;
        }
        return truth;
    }

    internal void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();
    // Evaluation cancellation must not overwrite an earlier, independent capture-budget gap.
    internal void Interrupt() { Interrupted = true; if (_gap is null) Gap(PredicateEvaluationCaptureState.CaptureInterrupted); }
    private void Gap(PredicateEvaluationCaptureState state) { _gap = state; _entries.Clear(); _bytes = 0; }
}
