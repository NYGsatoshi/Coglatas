using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectSimulationTraceProfile? _simulationTraceProfile;
    private ProjectSimulationRun? _profiledRun;
    private long _profileGeneration;
    public ProjectSimulationTraceProfile? SimulationTraceProfile => _profiledRun is not null &&
        ReferenceEquals(SimulationRun, _profiledRun) ? _simulationTraceProfile : null;

    /// <summary>Account only for the current owned synthetic recording. Starts no simulation or external operation.</summary>
    public bool ProfileSyntheticSimulation(ContentDigest expectedRun, int maximumEvents = ProjectSimulationTraceProfile.MaximumRecordedEvents,
        CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedRun);
        ClearSimulationTraceProfile(); var generation = _profileGeneration;
        var simulationGeneration = _simulationGeneration; var run = SimulationRun; var request = _desired;
        bool Current() => !_disposed && generation == _profileGeneration && simulationGeneration == _simulationGeneration &&
            run is not null && ReferenceEquals(run, SimulationRun) && ReferenceEquals(request, _desired);
        Changed();
        if (!Current())
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        var profile = ProjectSimulationTraceProfile.Read(run!, expectedRun, maximumEvents, token);
        if (!Current()) return false;
        _simulationTraceProfile = profile;
        _profiledRun = run;
        Exception? withdrawalFailure = null;
        bool cancelledAtPublication;
        try { Changed(); }
        finally
        {
            cancelledAtPublication = token.IsCancellationRequested;
            if (cancelledAtPublication && generation == _profileGeneration &&
                ReferenceEquals(_simulationTraceProfile, profile))
            {
                ClearSimulationTraceProfile();
                try { Changed(); }
                catch (Exception error) { withdrawalFailure = error; }
            }
        }
        // If publication threw, its original failure is already propagating.
        if (cancelledAtPublication)
            throw new OperationCanceledException("Profiler read was cancelled.", withdrawalFailure, token);
        return Current() && ReferenceEquals(SimulationTraceProfile, profile) && profile.State != SimulationTraceProfileState.Refused;
    }
    /// <summary>Read the existing fixed model recording through its current owned summary.</summary>
    public ProjectSimulationRun? ReadSimulationTraceProfileRun(ProjectSimulationTraceProfile basis)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(basis);
        return ReferenceEquals(SimulationTraceProfile, basis) ? basis.Run : null;
    }
    private void ClearSimulationTraceProfile()
    {
        _profileGeneration++; _simulationTraceProfile = null; _profiledRun = null;
    }
}
