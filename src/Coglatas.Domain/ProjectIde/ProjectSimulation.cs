using System.Globalization;
using System.Text.Json;

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
    private sealed record TaskRow(string Key, JsonElement Definition, long Duration);
    private sealed record Edge(string Dependent, long Lag);
    private sealed record Event(TaskRow Task, long Ticks, bool End, long Enqueued);
    private sealed class BoundExceeded(string reason) : Exception { public string Reason { get; } = reason; }

    public static ProjectSimulationRun Execute(SimulationRunManifest manifest, ContentDigest expectedInputDigest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(expectedInputDigest);
        var events = new List<SourceJson>(); var ticks = manifest.StartTicks; var operations = 0; var started = 0; var completed = 0; var traceBytes = 2;
        var freshness = manifest.Plan.Input.InputDigest.Equals(expectedInputDigest) ? CompilerFreshness.Current : CompilerFreshness.Stale;
        ProjectSimulationRun Result(CompilerExecutionState state, string reason) => new(manifest, state, freshness, reason, ticks, operations, started, completed, events);
        if (freshness != CompilerFreshness.Current) return Result(CompilerExecutionState.NotRun, "coglatas.stale-input");
        if (!manifest.Engine.Data.Equals(Engine.Data)) return Result(CompilerExecutionState.NotRun, "coglatas.simulation.engine-unsupported");
        void Charge(int amount = 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (amount > manifest.Limits.MaximumOperations - operations) throw new BoundExceeded("coglatas.simulation.operation-bound");
            operations += amount;
        }
        try
        {
            // Charge the owned plan's bounded representation before allocating run indexes.
            Charge(1 + manifest.Plan.Data.ToCanonicalBytes().Length / 64);
            var tasks = new SortedDictionary<string, TaskRow>(StringComparer.Ordinal);
            var outgoing = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);
            var pending = new Dictionary<string, int>(StringComparer.Ordinal);
            var readyAt = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var task in manifest.Plan.Data.Value.GetProperty("tasks").EnumerateArray())
            {
                Charge(); var key = Key(task); tasks.Add(key, new(key, task, ElapsedDuration.Parse(task.GetProperty("duration").GetProperty("value").GetString()!).Ticks));
                outgoing.Add(key, []); pending.Add(key, 0); readyAt.Add(key, manifest.StartTicks);
            }
            if (tasks.Count == 0) return Result(CompilerExecutionState.NotRun, "coglatas.simulation.empty-plan");
            foreach (var edge in manifest.Plan.Data.Value.GetProperty("dependencies").EnumerateArray())
            {
                Charge(); var dependent = Key(edge.GetProperty("dependent")); var prerequisite = Key(edge.GetProperty("prerequisite"));
                outgoing[prerequisite].Add(new(dependent, ElapsedDuration.Parse(edge.GetProperty("lag").GetProperty("value").GetString()!).Ticks)); pending[dependent]++;
            }
            var comparer = Comparer<(long Ticks, int Phase, string Key, long Sequence)>.Create((a, b) => {
                var value = a.Ticks.CompareTo(b.Ticks); if (value != 0) return value;
                value = a.Phase.CompareTo(b.Phase); if (value != 0) return value;
                value = StringComparer.Ordinal.Compare(a.Key, b.Key); return value != 0 ? value : a.Sequence.CompareTo(b.Sequence);
            });
            var queue = new PriorityQueue<Event, (long, int, string, long)>(comparer); long sequence = 0;
            void Enqueue(TaskRow task, long time, bool end) { var item = new Event(task, time, end, ++sequence); queue.Enqueue(item, (time, end ? 0 : 1, task.Key, item.Enqueued)); }
            foreach (var task in tasks.Values) { Charge(); if (pending[task.Key] == 0) Enqueue(task, ticks, false); }
            while (queue.TryDequeue(out var next, out _))
            {
                Charge();
                if (events.Count == manifest.Limits.MaximumEvents) throw new BoundExceeded("coglatas.simulation.event-bound");
                // Compare a difference, avoiding overflow in start + maximum elapsed time.
                if (next.Ticks - manifest.StartTicks > manifest.Limits.MaximumElapsedTicks) throw new BoundExceeded("coglatas.simulation.time-bound");
                var updates = new List<(string Key, long Ready)>();
                var finish = next.End ? next.Ticks : checked(next.Ticks + next.Task.Duration);
                if (next.End) foreach (var edge in outgoing[next.Task.Key]) { Charge(); updates.Add((edge.Dependent, Math.Max(readyAt[edge.Dependent], checked(next.Ticks + edge.Lag)))); }
                Charge(1 + updates.Count);
                var record = SourceJson.FromObject(new { sequence = events.Count + 1, scheduledSequence = next.Enqueued,
                    kind = next.End ? "coglatas.task-end" : "coglatas.task-start", ticks = SimulationRunManifest.Text(next.Ticks),
                    context = next.Task.Definition.GetProperty("context"), irNodeId = next.Task.Definition.GetProperty("irNodeId"), location = next.Task.Definition.GetProperty("location") });
                var size = record.ToCanonicalBytes().Length + (events.Count == 0 ? 0 : 1);
                if (size > manifest.Limits.MaximumTraceBytes - traceBytes) throw new BoundExceeded("coglatas.simulation.trace-bound");
                Charge(1 + size / 64);
                // Commit one fully bounded transition. A refused transition does not alter the recorded state.
                if (next.End) {
                    completed++;
                    foreach (var update in updates) {
                        readyAt[update.Key] = Math.Max(readyAt[update.Key], update.Ready);
                        if (--pending[update.Key] == 0) Enqueue(tasks[update.Key], readyAt[update.Key], false);
                    }
                } else { started++; Enqueue(next.Task, finish, true); }
                ticks = next.Ticks; traceBytes += size; events.Add(record);
            }
            return Result(CompilerExecutionState.Completed, "coglatas.simulation.completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Result(CompilerExecutionState.Cancelled, "coglatas.cancelled"); }
        catch (BoundExceeded error) { return Result(CompilerExecutionState.ResourceExhausted, error.Reason); }
        catch (OverflowException) { return Result(CompilerExecutionState.InternalError, "coglatas.simulation.tick-overflow"); }
    }
    private static string Key(JsonElement task) => SourceJson.FromElement(task.GetProperty("context")).CanonicalText + "|" + task.GetProperty("irNodeId").GetString();
}
