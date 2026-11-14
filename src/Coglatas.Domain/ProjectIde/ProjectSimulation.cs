using System.Globalization;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Provisional per-run bounds, not product capacity, elapsed wall time or CI policy.</summary>
public sealed record SimulationRunLimits(int MaximumEvents = 4096, int MaximumOperations = 200000,
    long MaximumElapsedTicks = long.MaxValue, int MaximumTraceBytes = 1048576)
{
    internal void Validate()
    {
        if (MaximumEvents is < 1 or > 8192 || MaximumOperations is < 1 or > 1000000 ||
            MaximumElapsedTicks < 0 || MaximumTraceBytes is < 2 or > 2097152)
            throw new ArgumentOutOfRangeException(nameof(SimulationRunLimits));
    }
}

/// <summary>Owned immutable request for an already produced plan. No Source parser or ambient run identity.</summary>
public sealed class SimulationRunManifest
{
    public const string Format = "coglatas.simulation-run-manifest/1";
    public CompilerExecutionPlan Plan { get; }
    public Guid RunId { get; }
    public long StartTicks { get; }
    public SimulationRunLimits Limits { get; }
    public CompilerArtifactIdentity Engine { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    public SimulationRunManifest(CompilerExecutionPlan plan, Guid runId, long startTicks = 0,
        SimulationRunLimits? limits = null, CompilerArtifactIdentity? engine = null, ContentDigest? originRunDigest = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (runId == Guid.Empty || startTicks < 0) throw new ArgumentException("A nonempty run identity and nonnegative virtual start are required.");
        Plan = plan; RunId = runId; StartTicks = startTicks; Limits = limits ?? new(); Limits.Validate(); Engine = engine ?? ProjectSimulation.Engine;
        Data = SourceJson.FromObject(new { format = Format, runId = runId.ToString("D"), planDigest = plan.Digest.Data.Value,
            compilerInputId = plan.Input.InputId.ToString(), inputDigest = plan.Input.InputDigest.Data.Value,
            sourceDigest = plan.Input.Source.Digest.Data.Value, engine = Engine.Data.Value,
            startTicks = Text(startTicks), stochastic = "coglatas.none", sideEffects = "coglatas.none",
            policy = ProjectSimulation.Policy, limits = new { maximumEvents = Limits.MaximumEvents, maximumOperations = Limits.MaximumOperations,
                maximumElapsedTicks = Text(Limits.MaximumElapsedTicks), maximumTraceBytes = Limits.MaximumTraceBytes },
            originRunDigest = originRunDigest?.Data.Value });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
    internal static string Text(long ticks) => ticks.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A bounded immutable transition prefix. Completed simulation is not a Test/Project PASS or execution authority.</summary>
public sealed class ProjectSimulationRun
{
    public const string Format = "coglatas.simulation-run/1";
    public SimulationRunManifest Manifest { get; }
    public CompilerExecutionState Execution { get; }
    public CompilerCoverage Coverage { get; }
    public CompilerFreshness Freshness { get; }
    public CompilerVerdict Verdict => CompilerVerdict.NotRun;
    public string Reason { get; }
    public long VirtualTicks { get; }
    public int LogicalOperationsConsumed { get; }
    public int StartedTasks { get; }
    public int CompletedTasks { get; }
    public IReadOnlyList<SourceJson> Events { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal ProjectSimulationRun(SimulationRunManifest manifest, CompilerExecutionState execution, CompilerFreshness freshness,
        string reason, long ticks, int operations, int started, int completed, IEnumerable<SourceJson> events)
    {
        Manifest = manifest; Execution = execution; Freshness = freshness; Reason = reason; VirtualTicks = ticks;
        LogicalOperationsConsumed = operations; StartedTasks = started; CompletedTasks = completed;
        Events = new ImmutableListView<SourceJson>(events);
        Coverage = execution == CompilerExecutionState.Completed ? CompilerCoverage.Complete : Events.Count == 0 ? CompilerCoverage.None : CompilerCoverage.Partial;
        Data = SourceJson.FromObject(new { format = Format, manifest = manifest.Data.Value, manifestDigest = manifest.Digest.Data.Value,
            execution = execution.ToString(), coverage = Coverage.ToString(), freshness = freshness.ToString(),
            verdict = Verdict.ToString(), verdictReason = "coglatas.no-outcome-assertions", evidenceKind = "coglatas.simulation",
            reason, virtualTicks = SimulationRunManifest.Text(ticks), logicalOperationsConsumed = operations,
            startedTasks = started, completedTasks = completed, traceCoverage = Coverage.ToString(),
            events = Events.Select(item => item.Value).ToArray() });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
}

/// <summary>Pure elapsed FinishToStart event engine. No clock read, PRNG, provider, mutable plan or hot reload.</summary>
public static class ProjectSimulation
{
    public const string Policy = "coglatas.elapsed-fs-end-before-start-context-node-sequence/1";
    private static readonly SourceJson Contract = SourceJson.FromObject(new { format = "coglatas.simulation-engine/1",
        planEngine = CompilerExecutionPlan.Engine.Data.Value, clock = "coglatas.explicit-elapsed-ticks", policy = Policy,
        capacity = "coglatas.unlimited", stochastic = "coglatas.none", sideEffects = "coglatas.none", events = new[] { "coglatas.task-start", "coglatas.task-end" } });
    public static CompilerArtifactIdentity Engine { get; } = new(SourceJson.FromObject(new { artifactId = "coglatas.elapsed-fs-simulator", version = "1",
        digest = ContentDigest.Compute("coglatas.simulation-engine/1", Contract.ToCanonicalBytes()).Data.Value }));
    public static ProjectSimulationRun Execute(SimulationRunManifest manifest, ContentDigest expectedInputDigest, CancellationToken cancellationToken = default)
    {
        var cursor = new SimulationCursor(manifest, expectedInputDigest, cancellationToken);
        while (cursor.Result is null) cursor.Advance(cancellationToken);
        return cursor.Result;
    }
}
