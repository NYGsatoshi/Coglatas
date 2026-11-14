using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectSimulationRunComparison? _simulationComparison;
    private ProjectSimulationRun? _comparisonAfter;
    private long _comparisonGeneration;
    public ProjectSimulationRunComparison? SimulationComparison => _comparisonAfter is not null &&
        ReferenceEquals(SimulationRun, _comparisonAfter) ? _simulationComparison : null;

    /// <summary>Compare an already admitted immutable recording supplied by the synthetic caller with the current owned run. Starts no execution.</summary>
    public bool CompareSyntheticSimulationRuns(ProjectSimulationRun before, ContentDigest expectedBefore, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(expectedBefore);
        ClearSimulationComparison(); var generation = _comparisonGeneration;
        var simulationGeneration = _simulationGeneration; var after = SimulationRun; var request = _desired;
        bool Current() => !_disposed && generation == _comparisonGeneration && simulationGeneration == _simulationGeneration &&
            after is not null && ReferenceEquals(after, SimulationRun) && ReferenceEquals(request, _desired);
        Changed(); if (!Current()) return false;
        var comparison = ProjectSimulationRunComparison.Compare(before, expectedBefore, after!, after!.Digest, token);
        if (!Current()) return false;
        _simulationComparison = comparison; _comparisonAfter = after; Changed();
        return Current() && ReferenceEquals(SimulationComparison, comparison) && comparison.State != SimulationRunComparisonState.Refused;
    }
    private void ClearSimulationComparison()
    {
        _comparisonGeneration++; _simulationComparison = null; _comparisonAfter = null;
    }
}
