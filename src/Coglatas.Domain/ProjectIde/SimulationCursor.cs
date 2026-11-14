using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

internal sealed record SimulationPending(string TaskKey, long Ticks, bool End, long Sequence, ProjectLocation Location, SourceJson Data);

/// <summary>The single bounded transition implementation used by full runs and semantic debugging.</summary>
internal sealed class SimulationCursor
{
    private sealed record TaskRow(string Key, JsonElement Definition, long Duration, ProjectLocation Location);
    private sealed record Edge(string Dependent, long Lag);
    private sealed record Incoming(string Prerequisite, JsonElement Definition);
    private sealed record Event(TaskRow Task, long Ticks, bool End, long Enqueued);
    private sealed class BoundExceeded(string reason) : Exception { public string Reason { get; } = reason; }
    private readonly SortedDictionary<string, TaskRow> _tasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskRow> _locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Edge>> _outgoing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Incoming>> _incoming = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _readyAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _starts = new(StringComparer.Ordinal), _ends = new(StringComparer.Ordinal), _finishes = new(StringComparer.Ordinal);
    private readonly List<SourceJson> _events = [];
    private readonly PriorityQueue<Event, (long Ticks, int Phase, string Key, long Sequence)> _queue;
    private long _sequence;
    private int _traceBytes = 2;
    private CancellationToken _token;
    internal bool PreparationComplete { get; private set; }
    internal SimulationRunManifest Manifest { get; }
    internal CompilerFreshness Freshness { get; }
    internal long Ticks { get; private set; }
    internal int Operations { get; private set; }
    internal int Started => _starts.Count;
    internal int Completed => _ends.Count;
    internal int EventCount => _events.Count;
    internal ProjectSimulationRun? Result { get; private set; }
    internal IReadOnlyList<SourceJson> CaptureEvents() => new ImmutableListView<SourceJson>(_events);
    internal SimulationCursor(SimulationRunManifest manifest, ContentDigest expectedInputDigest, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(expectedInputDigest);
        Manifest = manifest; Ticks = manifest.StartTicks; _token = token;
        Freshness = manifest.Plan.Input.InputDigest.Equals(expectedInputDigest) ? CompilerFreshness.Current : CompilerFreshness.Stale;
        var comparer = Comparer<(long Ticks, int Phase, string Key, long Sequence)>.Create((a, b) => {
            var value = a.Ticks.CompareTo(b.Ticks); if (value != 0) return value;
            value = a.Phase.CompareTo(b.Phase); if (value != 0) return value;
            value = StringComparer.Ordinal.Compare(a.Key, b.Key); return value != 0 ? value : a.Sequence.CompareTo(b.Sequence);
        });
        _queue = new(comparer);
        if (Freshness != CompilerFreshness.Current) { Finish(CompilerExecutionState.NotRun, "coglatas.stale-input"); return; }
        if (!manifest.Engine.Data.Equals(ProjectSimulation.Engine.Data)) { Finish(CompilerExecutionState.NotRun, "coglatas.simulation.engine-unsupported"); return; }
        Guard(() => {
            Charge(1 + manifest.Plan.Data.ToCanonicalBytes().Length / 64);
            foreach (var task in manifest.Plan.Data.Value.GetProperty("tasks").EnumerateArray()) {
                Charge(); var key = Key(task); var location = ProjectLocation.Parse(SourceJson.FromElement(task.GetProperty("location")));
                var row = new TaskRow(key, task, ElapsedDuration.Parse(task.GetProperty("duration").GetProperty("value").GetString()!).Ticks, location);
                _tasks.Add(key, row); _locations.Add(LocationKey(location), row);
                _outgoing.Add(key, []); _incoming.Add(key, []); _pending.Add(key, 0); _readyAt.Add(key, manifest.StartTicks);
            }
            if (_tasks.Count == 0) { Finish(CompilerExecutionState.NotRun, "coglatas.simulation.empty-plan"); return; }
            foreach (var edge in manifest.Plan.Data.Value.GetProperty("dependencies").EnumerateArray()) {
                Charge(); var dependent = Key(edge.GetProperty("dependent")); var prerequisite = Key(edge.GetProperty("prerequisite"));
                _outgoing[prerequisite].Add(new(dependent, ElapsedDuration.Parse(edge.GetProperty("lag").GetProperty("value").GetString()!).Ticks));
                _incoming[dependent].Add(new(prerequisite, edge)); _pending[dependent]++;
            }
            foreach (var task in _tasks.Values) { Charge(); if (_pending[task.Key] == 0) Enqueue(task, Ticks, false); }
            PreparationComplete = true;
        });
    }
    internal SimulationPending? Peek()
    {
        if (Result is not null || !_queue.TryPeek(out var item, out _)) return null;
        return new(item.Task.Key, item.Ticks, item.End, item.Enqueued, item.Task.Location, SourceJson.FromObject(new {
            scheduledSequence = item.Enqueued, kind = item.End ? "coglatas.task-end" : "coglatas.task-start", ticks = SimulationRunManifest.Text(item.Ticks),
            irNodeId = item.Task.Definition.GetProperty("irNodeId"), location = item.Task.Location.Data.Value }));
    }
    internal ProjectLocation? Resolve(ProjectLocation location) => _locations.TryGetValue(LocationKey(location), out var task) ? task.Location : null;
    internal bool HasEnded(ProjectLocation location) => _locations.TryGetValue(LocationKey(location), out var task) && _ends.ContainsKey(task.Key);
    internal void Advance(CancellationToken token)
    {
        if (Result is not null) return;
        _token = token;
        Guard(() => {
            if (!_queue.TryDequeue(out var next, out _)) { Finish(CompilerExecutionState.Completed, "coglatas.simulation.completed"); return; }
            Charge();
            if (_events.Count == Manifest.Limits.MaximumEvents) throw new BoundExceeded("coglatas.simulation.event-bound");
            if (next.Ticks - Manifest.StartTicks > Manifest.Limits.MaximumElapsedTicks) throw new BoundExceeded("coglatas.simulation.time-bound");
            var updates = new List<(string Key, long Ready)>();
            var finish = next.End ? next.Ticks : checked(next.Ticks + next.Task.Duration);
            if (next.End) foreach (var edge in _outgoing[next.Task.Key]) { Charge(); updates.Add((edge.Dependent, Math.Max(_readyAt[edge.Dependent], checked(next.Ticks + edge.Lag)))); }
            Charge(1 + updates.Count);
            var record = SourceJson.FromObject(new { sequence = _events.Count + 1, scheduledSequence = next.Enqueued,
                kind = next.End ? "coglatas.task-end" : "coglatas.task-start", ticks = SimulationRunManifest.Text(next.Ticks),
                context = next.Task.Definition.GetProperty("context"), irNodeId = next.Task.Definition.GetProperty("irNodeId"), location = next.Task.Definition.GetProperty("location") });
            var size = record.ToCanonicalBytes().Length + (_events.Count == 0 ? 0 : 1);
            if (size > Manifest.Limits.MaximumTraceBytes - _traceBytes) throw new BoundExceeded("coglatas.simulation.trace-bound");
            Charge(1 + size / 64);
            // Commit only a fully checked event; failed transitions preserve the exact recorded prefix.
            if (next.End) {
                _ends.Add(next.Task.Key, next.Ticks);
                foreach (var update in updates) {
                    _readyAt[update.Key] = Math.Max(_readyAt[update.Key], update.Ready);
                    if (--_pending[update.Key] == 0) Enqueue(_tasks[update.Key], _readyAt[update.Key], false);
                }
            } else { _starts.Add(next.Task.Key, next.Ticks); _finishes.Add(next.Task.Key, finish); Enqueue(next.Task, finish, true); }
            Ticks = next.Ticks; _traceBytes += size; _events.Add(record);
            if (_queue.Count == 0) Finish(CompilerExecutionState.Completed, "coglatas.simulation.completed");
        });
    }
    internal SourceJson? Inspect(ProjectLocation location, SimulationDebugLimits limits)
    {
        if (!_locations.TryGetValue(LocationKey(location), out var task)) return null;
        var running = _starts.ContainsKey(task.Key) && !_ends.ContainsKey(task.Key);
        var pending = _pending[task.Key]; var waits = new List<object>(); var inspected = 0;
        if (pending > 0) foreach (var edge in _incoming[task.Key]) {
            if (inspected++ == limits.MaximumInspectedEdges || waits.Count == limits.MaximumWaitingReasons) break;
            if (!_ends.ContainsKey(edge.Prerequisite)) waits.Add(new { kind = "coglatas.waiting-prerequisite", prerequisite = edge.Definition.GetProperty("prerequisite"), location = edge.Definition.GetProperty("location") });
        }
        var lag = pending == 0 && !_starts.ContainsKey(task.Key) && _readyAt[task.Key] > Ticks;
        if (lag) waits.Add(new { kind = "coglatas.waiting-lag", readyAtTicks = SimulationRunManifest.Text(_readyAt[task.Key]) });
        return SourceJson.FromObject(new { format = "coglatas.simulation-task-inspection/1", manifestDigest = Manifest.Digest.Data.Value, planDigest = Manifest.Plan.Digest.Data.Value,
            location = task.Location.Data.Value, context = task.Definition.GetProperty("context"), irNodeId = task.Definition.GetProperty("irNodeId"),
            state = _ends.ContainsKey(task.Key) ? "Completed" : running ? "Running" : pending > 0 ? "WaitingPrerequisites" : lag ? "WaitingLag" : "Ready",
            virtualTicks = SimulationRunManifest.Text(Ticks), startTicks = _starts.TryGetValue(task.Key, out var start) ? SimulationRunManifest.Text(start) : null,
            finishTicks = _ends.TryGetValue(task.Key, out var end) ? SimulationRunManifest.Text(end) : null,
            remainingTicks = running ? SimulationRunManifest.Text(_finishes[task.Key] - Ticks) : null,
            duration = task.Definition.GetProperty("duration"), ports = task.Definition.GetProperty("ports"), values = "coglatas.fixed-input-defaults",
            waitingReasonCount = pending > 0 ? pending : lag ? 1 : 0, waitingReasons = waits.ToArray(), waitingCoverage = waits.Count == (pending > 0 ? pending : lag ? 1 : 0) ? "Complete" : "Partial" },
            new SourceCodecLimits(MaximumBytes: limits.MaximumInspectionBytes));
    }
    private void Enqueue(TaskRow task, long ticks, bool end) { var item = new Event(task, ticks, end, ++_sequence); _queue.Enqueue(item, (ticks, end ? 0 : 1, task.Key, item.Enqueued)); }
    private void Charge(int amount = 1) { _token.ThrowIfCancellationRequested(); if (amount > Manifest.Limits.MaximumOperations - Operations) throw new BoundExceeded("coglatas.simulation.operation-bound"); Operations += amount; }
    private void Finish(CompilerExecutionState state, string reason) => Result = new(Manifest, state, Freshness, reason, Ticks, Operations, Started, Completed, _events);
    private void Guard(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { Finish(CompilerExecutionState.Cancelled, "coglatas.cancelled"); }
        catch (BoundExceeded error) { Finish(CompilerExecutionState.ResourceExhausted, error.Reason); }
        catch (OverflowException) { Finish(CompilerExecutionState.InternalError, "coglatas.simulation.tick-overflow"); }
    }
    private static string Key(JsonElement task) => SourceJson.FromElement(task.GetProperty("context")).CanonicalText + "|" + task.GetProperty("irNodeId").GetString();
    private static string LocationKey(ProjectLocation location) => location.Context.Data.CanonicalText + "|" + location.DocumentId + "|" + location.EntityId + "|" + location.RelationId + "|" + location.PropertyPath;
}
