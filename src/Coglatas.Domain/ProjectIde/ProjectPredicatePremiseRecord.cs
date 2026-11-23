using System.Text;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerPredicateCaptureMode { Manifest, OriginalRootArrays }
public enum PredicatePremiseRecordState
{
    ManifestOnly, CapturedOriginalArrays, NoOriginalArraysObserved, BodyBudgetExceeded, BodyCountLimitReached, CaptureInterrupted
}

/// <summary>Explicit pre-invocation local limits; not a host execution or adopted retention policy.</summary>
public sealed class CompilerPredicateCapturePolicy
{
    public const int MaximumBodies = 1024;
    public CompilerPredicateCaptureMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int MaximumDistinctBodies { get; }
    public CompilerPredicateCapturePolicy(CompilerPredicateCaptureMode mode = CompilerPredicateCaptureMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, int maximumDistinctBodies = MaximumBodies)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        if (maximumDistinctBodies is < 1 or > MaximumBodies) throw new ArgumentOutOfRangeException(nameof(maximumDistinctBodies));
        Mode = mode; MaximumBodyBytes = maximumBodyBytes; MaximumDistinctBodies = maximumDistinctBodies;
    }
}

/// <summary>Original root Proof arrays observed during one pure compile. No regional coverage, reason DAG or run identity is inferred.</summary>
public sealed class ProjectPredicatePremiseRecord
{
    public const string Format = "coglatas.local-root-predicate-premise-record/1";
    public const string BodyDomain = "coglatas.predicate-premises/1";
    private readonly CompilerInputManifest _input;
    private readonly CompilerResult _result;
    public CompilerPredicateCapturePolicy Policy { get; }
    public PredicatePremiseRecordState State { get; }
    public int RetainedBodyBytes { get; }
    public ContentDigest InputDigest => _input.InputDigest;
    public ContentDigest ResultDigest => _result.ResultDigest;
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal ProjectPredicatePremiseRecord(CoreCompilation compilation, CompilerPredicateCapturePolicy policy,
        PredicatePremiseRecordState state, Dictionary<string, SourceJson> bodies)
    {
        _input = compilation.Input; _result = compilation.Result; Policy = policy; State = state;
        Bodies = new ImmutableMapView<string, SourceJson>(bodies, StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText));
        Data = SourceJson.Parse(JsonSerializer.Serialize(new {
            format = Format, state = state.ToString(), capture = new { declared = "BeforeExecution", mode = policy.Mode.ToString(),
                maximumBodyBytes = policy.MaximumBodyBytes, maximumDistinctBodies = policy.MaximumDistinctBodies },
            scope = "RootInitialRuleEvaluation", regionalEvaluation = "NotRecorded", relationshipStructure = "NotRecorded",
            earlierStates = "NotRecorded", runIdentity = "NotAllocated", authority = "NotEvaluated", producerTrust = "NotEvaluated",
            eligibility = "NotEvaluated", retentionPolicy = "NotAdopted", retainedBodyBytes = RetainedBodyBytes,
            basis = new { inputDigest = InputDigest.Data.Value, sourceDigest = _input.Source.Digest.Data.Value,
                context = _input.Source.Context.Data.Value, compiler = _input.Compiler.Data.Value, resultDigest = ResultDigest.Data.Value },
            result = new { execution = _result.Execution.ToString(), coverage = _result.Coverage.ToString(),
                verdict = _result.Verdict.ToString(), evidenceComplete = _result.EvidenceComplete },
            bodies = Bodies.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new {
                digest = ContentDigest.Compute(BodyDomain, item.Value.ToCanonicalBytes()).Data.Value,
                bytes = Encoding.UTF8.GetByteCount(item.Value.CanonicalText) }).ToArray()
        }));
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsOwnedBy(CoreCompilation compilation) => ReferenceEquals(compilation.PredicatePremises, this) && MatchesResult(compilation);
    internal bool MatchesResult(CoreCompilation compilation) => ReferenceEquals(compilation.Input, _input) && ReferenceEquals(compilation.Result, _result);
}

/// <summary>Fresh collector per call; callers cannot attach observations or complete an older same-input result.</summary>
internal sealed class CompilerPredicateCaptureCollector(CompilerPredicateCapturePolicy policy)
{
    private readonly Dictionary<string, SourceJson> _bodies = new(StringComparer.Ordinal);
    private int _bytes;
    private PredicatePremiseRecordState? _gap;
    internal void Observe(ContentDigest digest, SourceJson body, int bytes)
    {
        if (policy.Mode == CompilerPredicateCaptureMode.Manifest || _gap is not null || _bodies.ContainsKey(digest.Value)) return;
        if (_bodies.Count >= policy.MaximumDistinctBodies) _gap = PredicatePremiseRecordState.BodyCountLimitReached;
        else if ((long)_bytes + bytes > policy.MaximumBodyBytes) _gap = PredicatePremiseRecordState.BodyBudgetExceeded;
        if (_gap is not null) { _bodies.Clear(); _bytes = 0; return; }
        _bodies.Add(digest.Value, body); _bytes += bytes;
    }

    internal ProjectPredicatePremiseRecord Complete(CoreCompilation compilation, CancellationToken cancellationToken)
    {
        var state = policy.Mode == CompilerPredicateCaptureMode.Manifest ? PredicatePremiseRecordState.ManifestOnly
            : cancellationToken.IsCancellationRequested || compilation.Result.Execution != CompilerExecutionState.Completed
                ? PredicatePremiseRecordState.CaptureInterrupted
                : _gap ?? (_bodies.Count == 0 ? PredicatePremiseRecordState.NoOriginalArraysObserved : PredicatePremiseRecordState.CapturedOriginalArrays);
        if (state != PredicatePremiseRecordState.CapturedOriginalArrays) { _bodies.Clear(); _bytes = 0; }
        return new(compilation, policy, state, _bodies);
    }
}
