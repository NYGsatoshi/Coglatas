using System.Text;
using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerPassObservationMode { Manifest, AvailableOriginalJson }
public enum CompilerPassObservationState { ManifestOnly, AvailableOriginalJson, NoActualPasses, BodyBudgetExceeded, EventLimitExceeded }
public enum CompilerPassObservationPhase { Entered, Returned, CancelledBeforeReturn }

/// <summary>Predeclared local initial-root capture limits, not a FullDump, host-run, access or retention policy.</summary>
public sealed class CompilerPassObservationPolicy
{
    public const int DefaultEventLimit = 16;
    public const int HardEventLimit = 64;
    public CompilerPassObservationMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public int MaximumEvents { get; }
    public CompilerPassObservationPolicy(CompilerPassObservationMode mode = CompilerPassObservationMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, int maximumEvents = DefaultEventLimit)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        if (maximumEvents is < 1 or > HardEventLimit) throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        Mode = mode; MaximumBodyBytes = maximumBodyBytes; MaximumEvents = maximumEvents;
    }
}

/// <summary>An actual compile and its separately owned original root boundary observation; ordinary Compile remains unchanged.</summary>
public sealed class RecordedPassCompilation
{
    [JsonIgnore]
    public CoreCompilation Compilation { get; }
    public ProjectCompilerPassObservation Observation { get; }
    internal RecordedPassCompilation(CoreCompilation compilation, CompilerPassObservationPolicy policy,
        CompilerPassObservationState state, int visited, IReadOnlyList<SourceJson> events, Dictionary<string, SourceJson> bodies)
    {
        Compilation = compilation;
        Observation = new(this, compilation, policy, state, visited, events, bodies);
    }
    public bool IsFor(CoreCompilation compilation) => ReferenceEquals(Compilation, compilation);
}

/// <summary>Actual root function boundaries and existing canonical JSON only. Missing engine-object and supply bodies remain gaps.</summary>
public sealed class ProjectCompilerPassObservation
{
    public const string Format = "coglatas.local-initial-root-pass-observation/1";
    private readonly RecordedPassCompilation _owner;
    private readonly CoreCompilation _compilation;
    internal IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    internal IReadOnlyList<SourceJson> BoundaryEvents { get; }
    public CompilerPassObservationPolicy Policy { get; }
    public CompilerPassObservationState State { get; }
    public int VisitedEvents { get; }
    public int RetainedEvents { get; }
    public int RetainedBodyBytes { get; }
    public ContentDigest InputDigest => _compilation.Input.InputDigest;
    public ContentDigest ResultDigest => _compilation.Result.ResultDigest;
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal ProjectCompilerPassObservation(RecordedPassCompilation owner, CoreCompilation compilation, CompilerPassObservationPolicy policy,
        CompilerPassObservationState state, int visited, IReadOnlyList<SourceJson> events, Dictionary<string, SourceJson> bodies)
    {
        _owner = owner; _compilation = compilation; Policy = policy; State = state;
        VisitedEvents = visited; RetainedEvents = events.Count;
        BoundaryEvents = Array.AsReadOnly(events.ToArray());
        Bodies = new ImmutableMapView<string, SourceJson>(bodies, StringComparer.Ordinal);
        RetainedBodyBytes = Bodies.Values.Sum(v => Encoding.UTF8.GetByteCount(v.CanonicalText));
        Data = SourceJson.FromObject(new { format = Format, state = state.ToString(), scope = "ActualInitialRootFunctionBoundaries",
            capture = new { declared = "BeforeCompile", mode = policy.Mode.ToString(), maximumEvents = policy.MaximumEvents, maximumBodyBytes = policy.MaximumBodyBytes },
            visitedEvents = visited, retainedEvents = events.Count, retainedBodyBytes = RetainedBodyBytes,
            basis = new { inputDigest = InputDigest.Data.Value, sourceDigest = compilation.Input.Source.Digest.Data.Value,
                context = compilation.Input.Source.Context.Data.Value, compiler = compilation.Input.Compiler.Data.Value, resultDigest = ResultDigest.Data.Value },
            result = new { execution = compilation.Result.Execution.ToString(), coverage = compilation.Result.Coverage.ToString(),
                verdict = compilation.Result.Verdict.ToString(), evidenceComplete = compilation.Result.EvidenceComplete },
            logicalOperationsConsumed = compilation.LogicalOperationsConsumed, events = events.Select(e => e.Value).ToArray(),
            regionalEvaluation = "NotRecorded", compilerRunIdentity = "NotAllocated", runSelection = "NotEvaluated", causalMeaning = "NotRecorded",
            producerTrust = "NotEvaluated", authority = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted",
            gaps = new[] { "No optimization or execution-plan trace is claimed.", "Materialized supplies are not pre-enumerated or serialized by these hooks.",
                "Binding and validation engine objects without an existing canonical body remain explicit gaps.",
                "Root publication precedes dependency attachment and does not impersonate the final result.",
                "Thrown failures that do not return a compilation do not return an observation.", "This observation is not FullDump or FullTrace." } });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
    public bool IsOwnedBy(RecordedPassCompilation execution) => ReferenceEquals(_owner, execution) &&
        ReferenceEquals(execution.Observation, this) && ReferenceEquals(execution.Compilation, _compilation);
}

internal sealed record CompilerPassObservationFrame(string Role, string Domain, SourceJson Data);
internal sealed class CompilerPassObservationCollector(CompilerPassObservationPolicy policy)
{
    private readonly List<SourceJson> _events = [];
    private readonly Dictionary<string, SourceJson> _bodies = new(StringComparer.Ordinal);
    private string? _openPass;
    private int _visited, _retainedBytes;
    private bool _eventLimit, _bodyLimit;
    internal void Enter(string pass, string gap, params CompilerPassObservationFrame[] frames)
    {
        if (_openPass is not null) throw new InvalidOperationException("Root pass boundaries cannot be nested.");
        _openPass = pass; Observe(pass, CompilerPassObservationPhase.Entered, gap, frames);
    }
    internal void Returned(string pass, string gap, params CompilerPassObservationFrame[] frames)
    {
        if (_openPass != pass) throw new InvalidOperationException("The original root pass return is not paired with its entry.");
        Observe(pass, CompilerPassObservationPhase.Returned, gap, frames); _openPass = null;
    }
    internal void Cancelled()
    {
        if (_openPass is { } pass)
        { Observe(pass, CompilerPassObservationPhase.CancelledBeforeReturn, "The original function did not return a body.", []); _openPass = null; }
    }
    private void Observe(string pass, CompilerPassObservationPhase phase, string gap, CompilerPassObservationFrame[] frames)
    {
        var ordinal = _visited++;
        if (_visited > policy.MaximumEvents) { _eventLimit = true; _events.Clear(); _bodies.Clear(); _retainedBytes = 0; }
        if (_eventLimit) return;
        var references = new List<object>();
        foreach (var frame in frames)
        {
            var bytes = Encoding.UTF8.GetByteCount(frame.Data.CanonicalText);
            var digest = ContentDigest.Compute(frame.Domain, frame.Data.ToCanonicalBytes());
            var key = frame.Domain + ":" + digest.Value;
            references.Add(new { role = frame.Role, domain = frame.Domain, digest = digest.Data.Value, bytes });
            if (policy.Mode != CompilerPassObservationMode.AvailableOriginalJson || _bodyLimit || _bodies.ContainsKey(key)) continue;
            if (bytes > policy.MaximumBodyBytes - _retainedBytes) { _bodyLimit = true; _bodies.Clear(); _retainedBytes = 0; continue; }
            _bodies.Add(key, frame.Data); _retainedBytes += bytes;
        }
        _events.Add(SourceJson.FromObject(new { ordinal, passId = pass, phase = phase.ToString(), originalBodies = references, gap }));
    }
    internal RecordedPassCompilation Complete(CoreCompilation compilation)
    {
        if (_openPass is not null) throw new InvalidOperationException("An original root pass is still open.");
        var state = _eventLimit ? CompilerPassObservationState.EventLimitExceeded : _visited == 0 ? CompilerPassObservationState.NoActualPasses
            : policy.Mode == CompilerPassObservationMode.Manifest ? CompilerPassObservationState.ManifestOnly : _bodyLimit ? CompilerPassObservationState.BodyBudgetExceeded : CompilerPassObservationState.AvailableOriginalJson;
        return new(compilation, policy, state, _visited, _events, _bodies);
    }
}
