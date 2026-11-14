using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectSimulationRun? _simulationRun;
    private long _simulationGeneration;
    public ProjectSimulationRun? SimulationRun => ScopedExecutionPlan is { } plan &&
        ReferenceEquals(plan, _simulationRun?.Manifest.Plan) ? _simulationRun : null;

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
    private void ClearSimulation() { _simulationGeneration++; _simulationRun = null; }
}
