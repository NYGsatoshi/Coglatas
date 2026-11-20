using System.Globalization;
using System.Numerics;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Signed changes in existing simulation profile measures, computed as Scenario minus baseline.</summary>
public sealed record SimulationTraceProfileDelta(long RecordedElapsedTicks, long RecordedBusyTicks,
    long UnattributedIdleTicks, BigInteger RecordedTaskTicks, int PeakActiveTasks);

/// <summary>Compares complete deterministic profiles for a Scenario and its exact frozen baseline.</summary>
public sealed class ProjectScenarioTraceProfileComparison
{
    public const string Format = "coglatas.local-scenario-trace-profile-comparison/1";
    public SimulationTraceProfileState State { get; }
    public string Reason { get; }
    public ProjectSimulationTraceProfile? Baseline { get; }
    public ProjectSimulationTraceProfile? Scenario { get; }
    public SimulationTraceProfileDelta? Delta { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectScenarioTraceProfileComparison(SimulationTraceProfileState state, string reason,
        int maximumEvents, ContentDigest? overlay = null, ProjectSimulationRunComparison? recordings = null,
        ProjectSimulationTraceProfile? baseline = null,
        ProjectSimulationTraceProfile? scenario = null, SimulationTraceProfileDelta? delta = null)
    {
        State = state;
        Reason = reason;
        Baseline = baseline;
        Scenario = scenario;
        Delta = delta;
        Data = SourceJson.FromObject(new
        {
            format = Format,
            state = state.ToString(),
            reason,
            inputKind = "SimulatedModel",
            evidenceKind = "coglatas.simulation",
            verdict = "NotRun",
            authority = "NotEvaluated",
            queryBudget = new { maximumEvents },
            overlayDigest = overlay?.Data.Value,
            recordings = recordings?.Data.Value,
            baseline = Frame(baseline),
            scenario = Frame(scenario),
            delta = delta is null ? null : new
            {
                recordedElapsedTicks = SimulationRunManifest.Text(delta.RecordedElapsedTicks),
                recordedBusyTicks = SimulationRunManifest.Text(delta.RecordedBusyTicks),
                unattributedIdleTicks = SimulationRunManifest.Text(delta.UnattributedIdleTicks),
                recordedTaskTicks = delta.RecordedTaskTicks.ToString(CultureInfo.InvariantCulture),
                peakActiveTasks = delta.PeakActiveTasks
            }
        });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectScenarioTraceProfileComparison Compare(ProjectScenarioPlan scenario,
        ProjectSimulationRun baseline, ContentDigest expectedBaseline, ProjectSimulationRun hypothesis,
        ContentDigest expectedHypothesis, int maximumEvents = ProjectSimulationTraceProfile.MaximumRecordedEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(expectedBaseline);
        ArgumentNullException.ThrowIfNull(hypothesis);
        ArgumentNullException.ThrowIfNull(expectedHypothesis);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEvents, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEvents, ProjectSimulationTraceProfile.MaximumRecordedEvents);
        cancellationToken.ThrowIfCancellationRequested();

        ProjectScenarioTraceProfileComparison Refuse(string reason) =>
            new(SimulationTraceProfileState.Refused, reason, maximumEvents);

        var requestedInput = scenario.Materialize(baseline.Manifest.Plan.Input, cancellationToken);
        var actualInput = hypothesis.Manifest.Plan.Input;
        if (requestedInput is null || !requestedInput.Data.Equals(actualInput.Data) ||
            !requestedInput.Source.Data.Equals(actualInput.Source.Data))
            return Refuse("coglatas.profile-comparison.wrong-scenario-input");

        var pair = ProjectSimulationRunComparison.Compare(baseline, expectedBaseline, hypothesis, expectedHypothesis, cancellationToken);
        if (pair.State == SimulationRunComparisonState.Refused)
            return Refuse(pair.Reason);
        if (pair.SameLimits != true || baseline.Manifest.StartTicks != hypothesis.Manifest.StartTicks)
            return Refuse("coglatas.profile-comparison.different-run-conditions");
        if (pair.State != SimulationRunComparisonState.Complete)
            return new(SimulationTraceProfileState.Incomplete, pair.Reason, maximumEvents, scenario.OverlayDigest, pair);

        var before = ProjectSimulationTraceProfile.Read(baseline, expectedBaseline, maximumEvents, cancellationToken);
        if (before.State != SimulationTraceProfileState.Complete)
            return Refuse(before.Reason);
        var after = ProjectSimulationTraceProfile.Read(hypothesis, expectedHypothesis, maximumEvents, cancellationToken);
        if (after.State != SimulationTraceProfileState.Complete)
            return Refuse(after.Reason);
        cancellationToken.ThrowIfCancellationRequested();

        return new(SimulationTraceProfileState.Complete, "coglatas.profile-comparison.complete-recordings",
            maximumEvents, scenario.OverlayDigest, pair, before, after, new(
                after.RecordedElapsedTicks!.Value - before.RecordedElapsedTicks!.Value,
                after.RecordedBusyTicks!.Value - before.RecordedBusyTicks!.Value,
                after.UnattributedIdleTicks!.Value - before.UnattributedIdleTicks!.Value,
                after.RecordedTaskTicks!.Value - before.RecordedTaskTicks!.Value,
                after.PeakActiveTasks!.Value - before.PeakActiveTasks!.Value));
    }

    private static object? Frame(ProjectSimulationTraceProfile? profile) =>
        profile is null ? null : new
        {
            digest = profile.Digest.Data.Value,
            data = profile.Data.Value
        };
}
