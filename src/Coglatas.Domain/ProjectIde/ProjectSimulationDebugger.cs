namespace Coglatas.Domain.ProjectIde;

/// <summary>Separate provisional control/inspection bounds; never changes the execution manifest or CI policy.</summary>
public sealed record SimulationDebugLimits(int MaximumCommands = 16384, int MaximumBreakpoints = 128,
    int MaximumWaitingReasons = 64, int MaximumInspectedEdges = 4096, int MaximumInspectionBytes = 1048576, int MaximumBreakpointBytes = 262144)
{
    internal void Validate()
    {
        if (MaximumCommands is < 1 or > 16384 || MaximumBreakpoints is < 1 or > 256 || MaximumWaitingReasons is < 1 or > 256 ||
            MaximumInspectedEdges is < 1 or > 20000 || MaximumInspectionBytes is < 256 or > 2097152 || MaximumBreakpointBytes is < 256 or > 524288) throw new ArgumentOutOfRangeException(nameof(SimulationDebugLimits));
    }
}
public enum SimulationControlState { Ready, Paused, Terminated }
public enum SimulationBreakpointPoint { TaskStart = 1, TaskEnd = 2 }
public sealed record SimulationControlStamp(ContentDigest ManifestDigest, long Revision);

public sealed class SimulationBreakpoint
{
    public Guid Id { get; }
    public ContentDigest PlanDigest { get; }
    public ProjectLocation Location { get; }
    public SimulationBreakpointPoint Point { get; }
    public SourceJson Data { get; }
    public SimulationBreakpoint(Guid id, ContentDigest planDigest, ProjectLocation location, SimulationBreakpointPoint point)
    {
        ArgumentNullException.ThrowIfNull(planDigest); ArgumentNullException.ThrowIfNull(location);
        if (id == Guid.Empty) throw new ArgumentException("A breakpoint identity is required.");
        Id = id; PlanDigest = planDigest; Location = location; Point = point;
        Data = SourceJson.FromObject(new { breakpointId = id.ToString("D"), planDigest = planDigest.Data.Value, location = location.Data.Value, point = (int)point });
    }
}
public sealed class SimulationBreakpointResolution
{
    public SimulationBreakpoint Request { get; }
    public bool Resolved { get; }
    public ProjectLocation? BoundLocation { get; }
    public string Reason { get; }
    public SourceJson Data { get; }
    internal SimulationBreakpointResolution(SimulationBreakpoint request, string reason, ProjectLocation? boundLocation)
    {
        Request = request; Reason = reason; Resolved = reason == "coglatas.debug.resolved"; BoundLocation = Resolved ? boundLocation : null;
        Data = SourceJson.FromObject(new { request = request.Data.Value, resolved = Resolved, boundLocation = BoundLocation?.Data.Value, reason });
    }
}

/// <summary>Immutable control state and recorded prefix. Paused is distinct from a terminal execution result.</summary>
public sealed class SimulationDebugSnapshot
{
    public const string Format = "coglatas.simulation-debug-snapshot/1";
    public SimulationRunManifest Manifest { get; }
    public SimulationControlStamp Stamp { get; }
    public SimulationControlState State { get; }
    public string Reason { get; }
    public IReadOnlyList<SourceJson> Events { get; }
    public IReadOnlyList<SimulationBreakpointResolution> Breakpoints { get; }
    public SourceJson? Pending { get; }
    public SourceJson? Inspection { get; }
    public ProjectSimulationRun? Result { get; }
    public ContentDigest TracePrefixDigest { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal SimulationDebugSnapshot(SimulationCursor cursor, ContentDigest controlsDigest, long revision, SimulationControlState state, string reason,
        IReadOnlyList<SourceJson> events, ContentDigest prefixDigest, IEnumerable<SimulationBreakpointResolution> breakpoints, Guid? hit, SourceJson? inspection)
    {
        Manifest = cursor.Manifest; Stamp = new(Manifest.Digest, revision); State = state; Reason = reason; Events = events;
        TracePrefixDigest = prefixDigest; Breakpoints = new ImmutableListView<SimulationBreakpointResolution>(breakpoints);
        Pending = cursor.Peek()?.Data; Inspection = inspection; Result = cursor.Result;
        Data = SourceJson.FromObject(new { format = Format, controlsDigest = controlsDigest.Data.Value, manifestDigest = Manifest.Digest.Data.Value,
            inputDigest = Manifest.Plan.Input.InputDigest.Data.Value, planDigest = Manifest.Plan.Digest.Data.Value,
            revision, state = state.ToString(), reason, virtualTicks = SimulationRunManifest.Text(cursor.Ticks),
            inputFreshness = cursor.Freshness.ToString(), logicalOperationsConsumed = cursor.Operations, startedTasks = cursor.Started, completedTasks = cursor.Completed,
            eventCount = events.Count, traceCoverage = Result is null ? "Prefix" : Result.Coverage.ToString(), tracePrefixDigest = prefixDigest.Data.Value,
            pending = Pending?.Value, breakpoints = Breakpoints.Select(item => item.Data.Value).ToArray(), hitBreakpointId = hit?.ToString("D"),
            inspectionDigest = inspection is null ? (System.Text.Json.JsonElement?)null : ContentDigest.Compute("coglatas.simulation-task-inspection/1", inspection.ToCanonicalBytes()).Data.Value,
            resultDigest = Result?.Digest.Data.Value });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
}
public sealed record SimulationControlResponse(bool Accepted, string Reason, SimulationDebugSnapshot? Snapshot);

/// <summary>One owned run's semantic controls. No Source editing, runtime authority, composites or hot reload.</summary>
public sealed class ProjectSimulationDebugger : IDisposable
{
    private readonly object _gate = new();
    private readonly SimulationCursor _cursor;
    private readonly SimulationDebugLimits _limits;
    private SimulationBreakpointResolution[] _breakpoints = [];
    private SimulationDebugSnapshot _snapshot;
    private IReadOnlyList<SourceJson> _prefix = Array.Empty<SourceJson>();
    private ContentDigest _prefixDigest = ContentDigest.Compute("coglatas.simulation-trace-prefix/1", SourceJson.Parse("[]").ToCanonicalBytes());
    private long _revision;
    private long? _resumeSequence;
    private SimulationControlState _state = SimulationControlState.Ready;
    private int _commands;
    private bool _disposed;
    public SourceJson Controls { get; }
    public ContentDigest ControlsDigest { get; }
    public SimulationDebugSnapshot Snapshot { get { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _snapshot; } } }
    public ProjectSimulationDebugger(SimulationRunManifest manifest, ContentDigest expectedInputDigest, SimulationDebugLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(expectedInputDigest);
        _limits = limits ?? new(); _limits.Validate();
        Controls = SourceJson.FromObject(new { format = "coglatas.simulation-debug-controls/1", manifestDigest = manifest.Digest.Data.Value,
            engine = ProjectSimulation.Engine.Data.Value, points = new[] { "coglatas.task-start", "coglatas.task-end" },
            commands = new[] { "coglatas.event-step", "coglatas.task-step-over", "coglatas.continue", "coglatas.inspect-fixed-task" },
            limits = new { _limits.MaximumCommands, _limits.MaximumBreakpoints, _limits.MaximumWaitingReasons, _limits.MaximumInspectedEdges, _limits.MaximumInspectionBytes, _limits.MaximumBreakpointBytes } });
        ControlsDigest = ContentDigest.Compute("coglatas.simulation-debug-controls/1", Controls.ToCanonicalBytes());
        _cursor = new(manifest, expectedInputDigest, cancellationToken);
        _snapshot = Capture("coglatas.debug.prepared");
    }
    public SimulationControlResponse SetBreakpoints(SimulationControlStamp stamp, IEnumerable<SimulationBreakpoint> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        lock (_gate) { if (Refuse(stamp, requireLive: true) is { } early) return early; }
        // Caller enumeration may reenter or race another command. Never hold the gate around it.
        var rows = requests.Take(_limits.MaximumBreakpoints + 1).ToArray();
        lock (_gate) {
            if (Refuse(stamp, requireLive: true) is { } refused) return refused;
            if (rows.Length > _limits.MaximumBreakpoints || rows.Any(item => item is null) || rows.Select(item => item.Id).Distinct().Count() != rows.Length)
                return new(false, "coglatas.debug.breakpoint-bound-or-duplicate", null);
            var bytes = 0;
            foreach (var row in rows) {
                var size = System.Text.Encoding.UTF8.GetByteCount(row.Data.CanonicalText);
                if (size > _limits.MaximumBreakpointBytes - bytes) return new(false, "coglatas.debug.breakpoint-byte-bound", null);
                bytes += size;
            }
            _breakpoints = rows.OrderBy(item => item.Id.ToString("D"), StringComparer.Ordinal).Select(item => new SimulationBreakpointResolution(item,
                !item.PlanDigest.Equals(_cursor.Manifest.Plan.Digest) ? "coglatas.debug.wrong-plan" :
                !Enum.IsDefined(item.Point) ? "coglatas.debug.point-unsupported" :
                _cursor.Resolve(item.Location) is null ? "coglatas.debug.position-unresolved" : "coglatas.debug.resolved", _cursor.Resolve(item.Location))).ToArray();
            return Commit("coglatas.debug.breakpoints-set", inspection: _snapshot.Inspection);
        }
    }
    public SimulationControlResponse Step(SimulationControlStamp stamp, CancellationToken cancellationToken = default)
    {
        lock (_gate) {
            if (Refuse(stamp, requireLive: true) is { } refused) return refused;
            _resumeSequence = null; _cursor.Advance(cancellationToken);
            return Commit("coglatas.debug.event-step");
        }
    }
    public SimulationControlResponse Continue(SimulationControlStamp stamp, CancellationToken cancellationToken = default)
    {
        lock (_gate) {
            if (Refuse(stamp, requireLive: true) is { } refused) return refused;
            return Pump(null, cancellationToken);
        }
    }
    public SimulationControlResponse StepOver(SimulationControlStamp stamp, ProjectLocation location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        lock (_gate) {
            if (Refuse(stamp, requireLive: true) is { } refused) return refused;
            var next = _cursor.Peek();
            if (next is null || next.End || !next.Location.Equals(_cursor.Resolve(location))) return new(false, "coglatas.debug.not-current-task-start", null);
            // An explicit step passes the selected atomic Task start; later parallel breakpoints remain effective.
            _resumeSequence = next.Sequence;
            return Pump(location, cancellationToken);
        }
    }
    public SimulationControlResponse StepOut(SimulationControlStamp stamp)
    {
        lock (_gate) { return Refuse(stamp) ?? new(false, "coglatas.debug.composite-not-supported", null); }
    }
    public SimulationControlResponse Inspect(SimulationControlStamp stamp, ProjectLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        lock (_gate) {
            if (Refuse(stamp) is { } refused) return refused;
            if (!_cursor.PreparationComplete) return new(false, "coglatas.debug.preparation-incomplete", null);
            try {
                if (_cursor.Inspect(location, _limits) is not { } inspection) return new(false, "coglatas.debug.position-unresolved", null);
                return Commit("coglatas.debug.inspected", inspection: inspection);
            } catch (FormatException) { return new(false, "coglatas.debug.inspection-bound", null); }
        }
    }
    private SimulationControlResponse Pump(ProjectLocation? taskEnd, CancellationToken token)
    {
        while (_cursor.Result is null) {
            if (token.IsCancellationRequested) { _cursor.Advance(token); break; }
            var next = _cursor.Peek();
            var hit = next is null ? null : _breakpoints.FirstOrDefault(item => item.Resolved && item.BoundLocation!.Equals(next.Location) &&
                item.Request.Point == (next.End ? SimulationBreakpointPoint.TaskEnd : SimulationBreakpointPoint.TaskStart));
            if (next is not null && hit is not null && _resumeSequence != next.Sequence) {
                _resumeSequence = next.Sequence;
                return Commit("coglatas.debug.breakpoint", hit.Request.Id);
            }
            _resumeSequence = null; _cursor.Advance(token);
            if (taskEnd is not null && _cursor.HasEnded(taskEnd)) break;
        }
        return Commit(taskEnd is null ? "coglatas.debug.continued" : "coglatas.debug.task-step-over");
    }
    private SimulationControlResponse? Refuse(SimulationControlStamp stamp, bool requireLive = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(stamp); ArgumentNullException.ThrowIfNull(stamp.ManifestDigest);
        if (!stamp.ManifestDigest.Equals(_cursor.Manifest.Digest)) return new(false, "coglatas.debug.wrong-run", null);
        if (stamp.Revision != _revision) return new(false, "coglatas.debug.stale-control", null);
        if (_commands == _limits.MaximumCommands) return new(false, "coglatas.debug.command-bound", null);
        if (requireLive && _cursor.Result is not null) return new(false, "coglatas.debug.terminated", null);
        return null;
    }
    private SimulationControlResponse Commit(string reason, Guid? hit = null, SourceJson? inspection = null)
    {
        _commands++; _revision++;
        if (reason is "coglatas.debug.breakpoint" or "coglatas.debug.event-step" or "coglatas.debug.task-step-over") _state = SimulationControlState.Paused;
        _snapshot = Capture(reason, hit, inspection);
        return new(true, reason, _snapshot);
    }
    private SimulationDebugSnapshot Capture(string reason, Guid? hit = null, SourceJson? inspection = null)
    {
        if (_prefix.Count != _cursor.EventCount) {
            _prefix = _cursor.CaptureEvents();
            _prefixDigest = ContentDigest.Compute("coglatas.simulation-trace-prefix/1", SourceJson.FromObject(_prefix.Select(item => item.Value).ToArray()).ToCanonicalBytes());
        }
        return new(_cursor, ControlsDigest, _revision, _cursor.Result is not null ? SimulationControlState.Terminated : _state,
            reason, _prefix, _prefixDigest, _breakpoints, hit, inspection);
    }
    public void Dispose() { lock (_gate) { _disposed = true; } }
}
