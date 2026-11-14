using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectSimulationRun? _simulationRun;
    private ProjectSimulationDebugger? _simulationDebugger;
    private SimulationDebugSnapshot? _simulationDebugSnapshot;
    private bool _debugBusy;
    private long _simulationGeneration;
    public ProjectSimulationRun? SimulationRun => ScopedExecutionPlan is { } plan &&
        ReferenceEquals(plan, _simulationRun?.Manifest.Plan) ? _simulationRun : null;
    public SimulationDebugSnapshot? SimulationDebug => ScopedExecutionPlan is { } plan && _simulationDebugger is not null &&
        ReferenceEquals(plan, _simulationDebugSnapshot?.Manifest.Plan) ? _simulationDebugSnapshot : null;
    public string SimulationDebugControlReason { get; private set; } = "";

    /// <summary>Explicit offline pure run of the admitted plan. Not runtime authorization or an external action.</summary>
    public bool RunSyntheticSimulation(Guid runId, long startTicks = 0, SimulationRunLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var plan = ScopedExecutionPlan; var request = _desired;
        if (plan is null || request is null) { ClearSimulation(); Changed(); return false; }
        var manifest = new SimulationRunManifest(plan, runId, startTicks, limits);
        ClearSimulation(); var generation = _simulationGeneration; Changed();
        bool Current() => !_disposed && generation == _simulationGeneration && ReferenceEquals(request, _desired) && ReferenceEquals(plan, ScopedExecutionPlan);
        if (!Current()) return false;
        var run = ProjectSimulation.Execute(manifest, request.Input.ExpectedInputDigest, cancellationToken);
        if (!Current()) return false;
        _simulationRun = run; Changed();
        return Current() && ReferenceEquals(SimulationRun, run);
    }

    public bool NavigateSimulationEvent(SourceJson traceEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(traceEvent);
        var run = SimulationRun;
        if (run is null || !run.Events.Any(item => ReferenceEquals(item, traceEvent))) return false;
        var location = ProjectLocation.Parse(SourceJson.Parse(traceEvent.Value.GetProperty("location").GetRawText()));
        return NavigateScopedSource(location) && ReferenceEquals(SimulationRun, run);
    }
    public bool BeginSyntheticSimulationDebug(Guid runId, long startTicks = 0, SimulationRunLimits? runLimits = null,
        SimulationDebugLimits? debugLimits = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_debugBusy) return false;
        var plan = ScopedExecutionPlan; var request = _desired;
        if (plan is null || request is null) return false;
        var manifest = new SimulationRunManifest(plan, runId, startTicks, runLimits);
        ClearSimulation(); var generation = _simulationGeneration; _debugBusy = true;
        bool Current() => !_disposed && generation == _simulationGeneration && ReferenceEquals(request, _desired) && ReferenceEquals(plan, ScopedExecutionPlan);
        try {
            Changed(); if (!Current()) return false;
            var debugger = new ProjectSimulationDebugger(manifest, request.Input.ExpectedInputDigest, debugLimits, cancellationToken);
            if (!Current()) { debugger.Dispose(); return false; }
            _simulationDebugger = debugger; _simulationDebugSnapshot = debugger.Snapshot; _simulationRun = debugger.Snapshot.Result;
            SimulationDebugControlReason = "coglatas.debug.prepared"; Changed();
            return Current() && ReferenceEquals(_simulationDebugger, debugger) && ReferenceEquals(SimulationDebug, debugger.Snapshot);
        }
        finally { if (generation == _simulationGeneration) _debugBusy = false; }
    }
    public bool StepSyntheticSimulation(SimulationDebugSnapshot basis, CancellationToken token = default) => ControlSimulation(basis, debugger => debugger.Step(basis.Stamp, token));
    public bool ContinueSyntheticSimulation(SimulationDebugSnapshot basis, CancellationToken token = default) => ControlSimulation(basis, debugger => debugger.Continue(basis.Stamp, token));
    public bool StepOverSyntheticSimulation(SimulationDebugSnapshot basis, ProjectLocation location, CancellationToken token = default) => ControlSimulation(basis, debugger => debugger.StepOver(basis.Stamp, location, token));
    public bool InspectSyntheticSimulation(SimulationDebugSnapshot basis, ProjectLocation location) => ControlSimulation(basis, debugger => debugger.Inspect(basis.Stamp, location));
    public bool SetSyntheticSimulationBreakpoints(SimulationDebugSnapshot basis, IEnumerable<SimulationBreakpoint> requests) => ControlSimulation(basis, debugger => debugger.SetBreakpoints(basis.Stamp, requests));
    private bool ControlSimulation(SimulationDebugSnapshot basis, Func<ProjectSimulationDebugger, SimulationControlResponse> command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(basis);
        var debugger = _simulationDebugger; var generation = _simulationGeneration;
        if (_debugBusy || debugger is null || !ReferenceEquals(SimulationDebug, basis)) return false;
        bool ContextCurrent() => !_disposed && generation == _simulationGeneration && ReferenceEquals(_simulationDebugger, debugger);
        bool Current() => ContextCurrent() && ReferenceEquals(SimulationDebug, basis);
        _debugBusy = true;
        try {
            var response = command(debugger);
            if (!Current()) return false;
            SimulationDebugControlReason = response.Reason;
            if (!response.Accepted) { Changed(); return false; }
            _simulationDebugSnapshot = response.Snapshot!; _simulationRun = response.Snapshot!.Result; Changed();
            return !_disposed && generation == _simulationGeneration && ReferenceEquals(_simulationDebugger, debugger) && ReferenceEquals(SimulationDebug, response.Snapshot);
        }
        catch (ObjectDisposedException) when (!ContextCurrent()) { return false; }
        finally { if (generation == _simulationGeneration) _debugBusy = false; }
    }
    public bool NavigateSimulationDebugEvent(SimulationDebugSnapshot basis, SourceJson traceEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(basis); ArgumentNullException.ThrowIfNull(traceEvent);
        if (!ReferenceEquals(SimulationDebug, basis) || !basis.Events.Any(item => ReferenceEquals(item, traceEvent))) return false;
        return NavigateScopedSource(ProjectLocation.Parse(SourceJson.Parse(traceEvent.Value.GetProperty("location").GetRawText()))) && ReferenceEquals(SimulationDebug, basis);
    }
    private void ClearSimulation()
    {
        var previous = _simulationDebugger;
        _simulationGeneration++; _debugBusy = false; _simulationDebugger = null; _simulationDebugSnapshot = null;
        _simulationRun = null; SimulationDebugControlReason = "";
        previous?.Dispose();
    }
}
