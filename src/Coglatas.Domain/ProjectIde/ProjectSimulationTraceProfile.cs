using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum SimulationTraceProfileState { Complete, Incomplete, Refused }

/// <summary>Local candidate accounting of a fixed model recording, never current authority or real observations.</summary>
public sealed class ProjectSimulationTraceProfile
{
    public const string Format = "coglatas.local-simulation-trace-profile/1";
    public const int MaximumRecordedEvents = 8192;
    public SimulationTraceProfileState State { get; }
    public string Reason { get; }
    public ProjectSimulationRun? Run { get; }
    public int QueryMaximumEvents { get; }
    public long? RecordedElapsedTicks { get; }
    public long? RecordedBusyTicks { get; }
    public long? UnattributedIdleTicks { get; }
    public BigInteger? RecordedTaskTicks { get; }
    public int? PeakActiveTasks { get; }
    public int? OpenTasks { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    private sealed record Metrics(long Elapsed, long Busy, BigInteger TaskTicks, int Peak, int Open);
    private ProjectSimulationTraceProfile(SimulationTraceProfileState state, string reason, int maximumEvents, ProjectSimulationRun? run = null, Metrics? metrics = null)
    {
        State = state; Reason = reason; Run = run; QueryMaximumEvents = maximumEvents;
        if (metrics is not null) {
            RecordedElapsedTicks = metrics.Elapsed; RecordedBusyTicks = metrics.Busy; UnattributedIdleTicks = metrics.Elapsed - metrics.Busy;
            RecordedTaskTicks = metrics.TaskTicks; PeakActiveTasks = metrics.Peak; OpenTasks = metrics.Open;
        }
        Data = Json(new { format = Format, state = state.ToString(), reason, inputKind = "SimulatedModel", evidenceKind = "coglatas.simulation",
            verdict = "NotRun", authority = "NotEvaluated", queryBudget = new { maximumEvents }, capacityModel = "coglatas.unlimited", attribution = "Unattributed",
            basis = run is null ? null : new { runId = run.Manifest.RunId.ToString("D"), runDigest = run.Digest.Data.Value,
                inputDigest = run.Manifest.Plan.Input.InputDigest.Data.Value, sourceDigest = run.Manifest.Plan.Input.Source.Digest.Data.Value, manifestDigest = run.Manifest.Digest.Data.Value,
                planDigest = run.Manifest.Plan.Digest.Data.Value, context = run.Manifest.Plan.Input.Source.Context.Data.Value, engine = run.Manifest.Engine.Data.Value },
            asOf = run is null ? null : new { kind = "FixedRecording", runDigest = run.Digest.Data.Value, inputDigest = run.Manifest.Plan.Input.InputDigest.Data.Value },
            recording = run is null ? null : new { execution = run.Execution.ToString(), coverage = run.Coverage.ToString(), freshness = run.Freshness.ToString(),
                reason = run.Reason, events = run.Events.Count, startedTasks = run.StartedTasks, completedTasks = run.CompletedTasks },
            period = run is null ? null : new { kind = "VirtualTicks", start = Text(run.Manifest.StartTicks), through = Text(run.VirtualTicks) },
            metrics = metrics is null ? null : new { recordedElapsedTicks = Text(metrics.Elapsed), recordedBusyTicks = Text(metrics.Busy),
                unattributedIdleTicks = Text(metrics.Elapsed - metrics.Busy), recordedTaskTicks = metrics.TaskTicks.ToString(CultureInfo.InvariantCulture),
                peakActiveTasks = metrics.Peak, openTasks = metrics.Open },
            definitions = new[] {
                new { metric = "recordedElapsedTicks", unit = "virtual-tick", definition = "Last committed tick minus fixed start, over this recorded prefix only." },
                new { metric = "recordedBusyTicks", unit = "virtual-tick", definition = "Union of recorded intervals with at least one active Task; overlapping Tasks count once." },
                new { metric = "unattributedIdleTicks", unit = "virtual-tick", definition = "Recorded elapsed ticks with no active Task; no dependency, approval, Resource or person cause is assigned." },
                new { metric = "recordedTaskTicks", unit = "task-virtual-tick", definition = "Sum of active Task intervals, counting each overlapping Task separately; open Tasks end at the recorded boundary." },
                new { metric = "peakActiveTasks", unit = "task-count", definition = "Maximum active Task count after a committed transition in the engine's declared event order, including zero-duration transitions." },
                new { metric = "openTasks", unit = "task-count", definition = "Started but not ended Tasks at the recorded boundary; incomplete prefixes are not whole-run totals." }
            } });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
    private static string Text(long ticks) => ticks.ToString(CultureInfo.InvariantCulture);
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    public static ProjectSimulationTraceProfile Read(ProjectSimulationRun run, ContentDigest expectedRun, int maximumEvents = MaximumRecordedEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run); ArgumentNullException.ThrowIfNull(expectedRun);
        if (maximumEvents is < 1 or > MaximumRecordedEvents) throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        cancellationToken.ThrowIfCancellationRequested();
        if (!run.Digest.Equals(expectedRun)) return new(SimulationTraceProfileState.Refused, "coglatas.profile.wrong-recording", maximumEvents);
        if (!run.Manifest.Engine.Data.Equals(ProjectSimulation.Engine.Data)) return new(SimulationTraceProfileState.Refused, "coglatas.profile.engine-unsupported", maximumEvents);
        if (run.Freshness != CompilerFreshness.Current || run.Execution == CompilerExecutionState.NotRun)
            return new(SimulationTraceProfileState.Refused, "coglatas.profile.recording-unavailable", maximumEvents);
        if (run.Events.Count > maximumEvents) return new(SimulationTraceProfileState.Refused, "coglatas.profile.event-bound", maximumEvents);
        var active = new HashSet<string>(StringComparer.Ordinal); var starts = new HashSet<string>(StringComparer.Ordinal); var ends = new HashSet<string>(StringComparer.Ordinal);
        var previous = run.Manifest.StartTicks; long busy = 0; var taskTicks = BigInteger.Zero; var peak = 0;
        foreach (var row in run.Events) {
            cancellationToken.ThrowIfCancellationRequested(); var e = row.Value;
            var time = long.Parse(e.GetProperty("ticks").GetString()!, CultureInfo.InvariantCulture);
            if (time < previous || time > run.VirtualTicks) return new(SimulationTraceProfileState.Refused, "coglatas.profile.inconsistent-recording", maximumEvents);
            var delta = time - previous;
            if (active.Count != 0) busy = checked(busy + delta);
            taskTicks += (BigInteger)delta * active.Count; previous = time;
            var context = SourceRevisionContext.Parse(SourceJson.Parse(e.GetProperty("context").GetRawText()));
            var key = context.Data.CanonicalText + "\0" + e.GetProperty("irNodeId").GetString();
            var kind = e.GetProperty("kind").GetString();
            if (kind == "coglatas.task-start") { if (!starts.Add(key) || !active.Add(key)) return new(SimulationTraceProfileState.Refused, "coglatas.profile.inconsistent-recording", maximumEvents); }
            else if (kind == "coglatas.task-end") { if (!active.Remove(key) || !ends.Add(key)) return new(SimulationTraceProfileState.Refused, "coglatas.profile.inconsistent-recording", maximumEvents); }
            else return new(SimulationTraceProfileState.Refused, "coglatas.profile.event-unsupported", maximumEvents);
            peak = Math.Max(peak, active.Count);
        }
        if (previous != run.VirtualTicks || starts.Count != run.StartedTasks || ends.Count != run.CompletedTasks ||
            run.Execution == CompilerExecutionState.Completed && active.Count != 0)
            return new(SimulationTraceProfileState.Refused, "coglatas.profile.inconsistent-recording", maximumEvents);
        cancellationToken.ThrowIfCancellationRequested();
        var complete = run.Execution == CompilerExecutionState.Completed && run.Coverage == CompilerCoverage.Complete;
        return new(complete ? SimulationTraceProfileState.Complete : SimulationTraceProfileState.Incomplete,
            complete ? "coglatas.profile.complete-recording" : "coglatas.profile.recorded-prefix", maximumEvents, run,
            new(run.VirtualTicks - run.Manifest.StartTicks, busy, taskTicks, peak, active.Count));
    }
}
