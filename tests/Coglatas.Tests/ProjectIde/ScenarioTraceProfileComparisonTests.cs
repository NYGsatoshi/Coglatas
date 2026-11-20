using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ScenarioTraceProfileComparisonTests
{
    [Theory]
    [InlineData(1, 0, -3, 3, -4)]
    [InlineData(8, 0, 3, -3, 3)]
    [InlineData(14, 3, 9, -6, 9)]
    public void ComparesExistingProfileMeasuresForTheExactScenario(long duration, long elapsed, long busy, long idle, long taskTime)
    {
        var (scenario, before, after) = Pair(duration);
        var result = Compare(scenario, before, after);

        Assert.Equal(SimulationTraceProfileState.Complete, result.State);
        Assert.Equal(new SimulationTraceProfileDelta(elapsed, busy, idle, new BigInteger(taskTime), 0), result.Delta);
        var baseline = Assert.IsType<ProjectSimulationTraceProfile>(result.Baseline);
        var hypothesis = Assert.IsType<ProjectSimulationTraceProfile>(result.Scenario);
        Assert.Same(before, baseline.Run);
        Assert.Same(after, hypothesis.Run);
        Assert.Equal(ProjectSimulationTraceProfile.Read(before, before.Digest).Digest, baseline.Digest);
        Assert.Equal(ProjectSimulationTraceProfile.Read(after, after.Digest).Digest, hypothesis.Digest);
        var data = result.Data.Value;
        Assert.Equal("SimulatedModel", data.GetProperty("inputKind").GetString());
        Assert.Equal("NotRun", data.GetProperty("verdict").GetString());
        Assert.Equal("NotEvaluated", data.GetProperty("authority").GetString());
        Assert.Equal(scenario.OverlayDigest.Data, SourceJson.Parse(data.GetProperty("overlayDigest").GetRawText()));
        Assert.Equal(hypothesis.Digest.Data, SourceJson.Parse(data.GetProperty("scenario").GetProperty("digest").GetRawText()));
        Assert.Equal(ContentDigest.Compute(ProjectScenarioTraceProfileComparison.Format, result.Data.ToCanonicalBytes()), result.Digest);
    }

    [Fact]
    public void AForeignBaseAnOlderScenarioAndAWrongRunDigestAreRefused()
    {
        var (scenario, before, after) = Pair(8);
        var (_, _, older) = Pair(8, revision: 7101);
        var foreign = Run(ProjectSimulationTests.Plan(first: 6), 7130);

        AssertRefused(Compare(scenario, before, older), "coglatas.profile-comparison.wrong-scenario-input");
        AssertRefused(Compare(scenario, foreign, after), "coglatas.profile-comparison.wrong-scenario-input");
        AssertRefused(ProjectScenarioTraceProfileComparison.Compare(scenario, before, before.Digest, after, after.Manifest.Digest),
            "coglatas.comparison.wrong-run");
    }

    [Fact]
    public void RequiresMatchingVirtualStartAndRunLimits()
    {
        var (scenario, before, after) = Pair(8);
        var shifted = Run(after.Manifest.Plan, 7130, start: 200);
        var otherBudget = Run(after.Manifest.Plan, 7131, limits: new(MaximumEvents: 5));

        AssertRefused(Compare(scenario, before, shifted), "coglatas.profile-comparison.different-run-conditions");
        AssertRefused(Compare(scenario, before, otherBudget), "coglatas.profile-comparison.different-run-conditions");
    }

    [Fact]
    public void BoundedAndStaleRunsKeepTheirRecordingIdentitiesWithoutMetricDeltas()
    {
        var (scenario, before, after) = Pair(8, limits: new(MaximumEvents: 3));
        Assert.Equal(CompilerCoverage.Partial, before.Coverage);
        Assert.Equal(CompilerCoverage.Partial, after.Coverage);
        var stale = ProjectSimulation.Execute(after.Manifest, before.Manifest.Plan.Input.InputDigest);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);

        foreach (var recording in new[] { after, stale })
        {
            var result = Compare(scenario, before, recording);
            Assert.Equal(SimulationTraceProfileState.Incomplete, result.State);
            AssertNoMetrics(result);
            var summary = result.Data.Value.GetProperty("recordings").GetProperty("after");
            Assert.Equal(recording.Digest.Data, SourceJson.Parse(summary.GetProperty("runDigest").GetRawText()));
        }
    }

    [Fact]
    public void AQueryBudgetAppliesToBothProfilesAndCancellationRetainsTheirOriginalRuns()
    {
        var (scenario, before, after) = Pair(8);
        var beforeData = before.Data;
        var afterData = after.Data;

        AssertRefused(Compare(scenario, before, after, maximumEvents: 5), "coglatas.profile.event-bound");
        Assert.Equal(SimulationTraceProfileState.Complete, Compare(scenario, before, after, maximumEvents: 6).State);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectScenarioTraceProfileComparison.Compare(scenario,
            before, before.Digest, after, after.Digest, cancellationToken: cancelled.Token));
        Assert.Same(beforeData, before.Data);
        Assert.Same(afterData, after.Data);
    }

    private static (ProjectScenarioPlan Scenario, ProjectSimulationRun Before, ProjectSimulationRun After) Pair(
        long duration, int revision = 7102, SimulationRunLimits? limits = null)
    {
        var plan = ProjectSimulationTests.Plan();
        var task = plan.Data.Value.GetProperty("tasks").EnumerateArray().Single(row => row.GetProperty("sourceEntityId").GetString() == Id(7));
        var target = ProjectLocation.Parse(SourceJson.Parse(task.GetProperty("location").GetRawText()));
        var value = JsonNode.Parse(task.GetProperty("duration").GetRawText())!.AsObject();
        value["value"] = duration.ToString(CultureInfo.InvariantCulture);
        var prepared = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(plan.Input), plan.Input.InputDigest,
            ScenarioId.Parse(Id(7100)), ScenarioRevisionId.Parse(Id(revision)),
            [new(target, ProjectScenarioOverrideKind.TaskDuration, Json(value))]);
        Assert.Equal(ProjectScenarioState.Ready, prepared.State);
        var scenario = Assert.IsType<ProjectScenarioPlan>(prepared.Plan);
        var changed = Assert.IsType<CompilerExecutionPlan>(PureCoreCompiler.Compile(scenario.Input, scenario.Input.Compiler).Publication?.ExecutionPlan);
        return (scenario, Run(plan, 7121, limits: limits), Run(changed, 7122, limits: limits));
    }

    private static ProjectSimulationRun Run(CompilerExecutionPlan plan, int id, long start = 100, SimulationRunLimits? limits = null) =>
        ProjectSimulation.Execute(new(plan, Guid.Parse(Id(id)), start, limits), plan.Input.InputDigest);

    private static ProjectScenarioTraceProfileComparison Compare(ProjectScenarioPlan scenario, ProjectSimulationRun before,
        ProjectSimulationRun after, int maximumEvents = ProjectSimulationTraceProfile.MaximumRecordedEvents) =>
        ProjectScenarioTraceProfileComparison.Compare(scenario, before, before.Digest, after, after.Digest, maximumEvents);

    private static void AssertRefused(ProjectScenarioTraceProfileComparison result, string reason)
    {
        Assert.Equal(SimulationTraceProfileState.Refused, result.State);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(JsonValueKind.Null, result.Data.Value.GetProperty("recordings").ValueKind);
        AssertNoMetrics(result);
    }

    private static void AssertNoMetrics(ProjectScenarioTraceProfileComparison result)
    {
        Assert.Null(result.Baseline);
        Assert.Null(result.Scenario);
        Assert.Null(result.Delta);
        Assert.Equal(JsonValueKind.Null, result.Data.Value.GetProperty("delta").ValueKind);
    }
}
