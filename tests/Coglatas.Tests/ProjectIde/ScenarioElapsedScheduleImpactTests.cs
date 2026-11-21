using System.Globalization;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ScenarioElapsedScheduleImpactTests
{
    [Theory]
    [InlineData(8, 15, 3, 0)]
    [InlineData(11, 15, 0, 0)]
    [InlineData(14, 18, 0, 3)]
    public void TaskDelayConsumesSlackBeforeMovingTheModeledFinish(long duration, long horizon, long firstFloat, long delta)
    {
        var (scenario, before, after) = Pair(ProjectSimulationTests.Plan(), duration);
        var impact = Compare(scenario, before, after);

        Assert.Equal(ScenarioElapsedScheduleImpactState.Complete, impact.State);
        Assert.Equal(delta, impact.ModeledFinishDeltaTicks);
        var basis = Assert.IsType<ElapsedRunSchedule>(impact.Baseline);
        var changed = Assert.IsType<ElapsedRunSchedule>(impact.Hypothesis);
        Assert.Equal(before.Digest, basis.RunDigest);
        Assert.Equal(after.Digest, changed.RunDigest);
        Assert.Equal(15, basis.FinishOffsetTicks);
        Assert.Equal(horizon, changed.FinishOffsetTicks);
        Assert.Equal(6, Task(basis, 7).TotalFloatTicks);
        Assert.Equal(0, Task(basis, 8).TotalFloatTicks);
        Assert.Equal(12, Task(basis, 9).StartOffsetTicks);
        Assert.Equal(15, Task(basis, 9).FinishOffsetTicks);
        Assert.Equal(firstFloat, Task(changed, 7).TotalFloatTicks);
        Assert.Equal(delta, Task(changed, 8).TotalFloatTicks);
        Assert.Equal(horizon, Task(changed, 9).FinishOffsetTicks);
        Assert.All(basis.Tasks, row => Assert.Equal(scenario.Basis.Source.Context, row.Task.Context));
        Assert.All(changed.Tasks, row => Assert.Equal(scenario.Input.Source.Context, row.Task.Context));
    }

    [Fact]
    public void MultipleSuccessorsUseTheTightestLatestFinish()
    {
        var input = ProjectSimulationTests.Plan().Input;
        var tasks = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        var task = tasks.Single(node => node!["entityId"]!.GetValue<string>() == Id(9))!.DeepClone();
        task["entityId"] = Id(10);
        task["payload"]!["duration"]!["value"] = "10";
        tasks.Add(task);

        var relations = JsonNode.Parse(input.Source.Relations.CanonicalText)!.AsArray();
        var dependency = relations.Single(node => node!["relationId"]!.GetValue<string>() == Id(37))!.DeepClone();
        dependency["relationId"] = Id(39);
        dependency["source"]!["reference"]!["entityId"] = Id(10);
        dependency["payload"]!["lag"]!["value"] = "8";
        relations.Add(dependency);

        var source = ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(tasks), Json(relations));
        var (scenario, before, after) = Pair(Plan(new(source, input.Settings)), 8);
        var impact = Compare(scenario, before, after);

        Assert.Equal(ScenarioElapsedScheduleImpactState.Complete, impact.State);
        Assert.Equal(3, impact.ModeledFinishDeltaTicks);
        var basis = Assert.IsType<ElapsedRunSchedule>(impact.Baseline);
        var changed = Assert.IsType<ElapsedRunSchedule>(impact.Hypothesis);
        Assert.Equal(23, basis.FinishOffsetTicks);
        Assert.Equal(26, changed.FinishOffsetTicks);
        Assert.Equal(0, Task(basis, 7).TotalFloatTicks);
        Assert.Equal(8, Task(basis, 8).TotalFloatTicks);
        Assert.Equal(8, Task(basis, 9).TotalFloatTicks);
        Assert.Equal(11, Task(changed, 9).TotalFloatTicks);
        Assert.Equal(0, Task(changed, 10).TotalFloatTicks);
    }

    [Fact]
    public void AnOlderScenarioOrAnotherBaselineCannotSupplyScheduleEvidence()
    {
        var plan = ProjectSimulationTests.Plan();
        var (scenario, before, after) = Pair(plan, 8);
        var (_, _, earlier) = Pair(plan, 9, revision: 802);
        var (_, unrelated, _) = Pair(ProjectSimulationTests.Plan(first: 6), 8);

        Assert.Equal("coglatas.schedule-impact.wrong-scenario-input", AssertRefusal(Compare(scenario, before, earlier)));
        Assert.Equal("coglatas.schedule-impact.wrong-scenario-input", AssertRefusal(Compare(scenario, unrelated, after)));
        Assert.Equal("coglatas.comparison.wrong-run", AssertRefusal(
            ProjectScenarioElapsedScheduleImpact.Compare(scenario, before, before.Digest, after, after.Manifest.Digest)));
    }

    [Fact]
    public void BothCompleteRunConditionsMustMatch()
    {
        var (scenario, before, after) = Pair(ProjectSimulationTests.Plan(), 8);
        var differentClock = Run(after.Manifest.Plan, 823, start: 200);
        var differentBound = Run(after.Manifest.Plan, 824, limits: new(MaximumEvents: 100));

        Assert.Equal("coglatas.schedule-impact.different-run-conditions", AssertRefusal(Compare(scenario, before, differentClock)));
        Assert.Equal("coglatas.schedule-impact.different-run-conditions", AssertRefusal(Compare(scenario, before, differentBound)));
    }

    [Fact]
    public void PartialAndStaleRunsHaveNoSchedulesOrDelta()
    {
        var (scenario, before, after) = Pair(ProjectSimulationTests.Plan(), 8);
        var partial = Run(after.Manifest.Plan, 823, limits: new(MaximumEvents: 2));
        var stale = ProjectSimulation.Execute(after.Manifest, scenario.Basis.InputDigest);
        Assert.Equal(CompilerCoverage.Partial, partial.Coverage);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        foreach (var run in new[] { partial, stale })
        {
            var impact = Compare(scenario, before, run);
            Assert.Equal(ScenarioElapsedScheduleImpactState.Incomplete, impact.State);
            AssertNoProjection(impact);
        }
    }

    [Fact]
    public void ExactProjectionBudgetAndCancellationPreserveTheOriginalRecordings()
    {
        var (scenario, before, after) = Pair(ProjectSimulationTests.Plan(), 8);
        var beforeData = before.Data;
        var afterData = after.Data;
        var first = Compare(scenario, before, after);
        Assert.Equal(ScenarioElapsedScheduleImpactState.Complete, first.State);
        var bounded = Compare(scenario, before, after, first.LogicalOperationsConsumed - 1);
        Assert.Equal(ScenarioElapsedScheduleImpactState.OperationLimit, bounded.State);
        AssertNoProjection(bounded);
        Assert.Equal(first.LogicalOperationsConsumed - 1, bounded.LogicalOperationsConsumed);
        var exact = Compare(scenario, before, after, first.LogicalOperationsConsumed);
        Assert.Equal(first.Baseline!.Tasks.ToArray(), exact.Baseline!.Tasks.ToArray());
        Assert.Equal(first.Hypothesis!.Tasks.ToArray(), exact.Hypothesis!.Tasks.ToArray());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ProjectScenarioElapsedScheduleImpact.Compare(scenario, before, before.Digest, after, after.Digest,
                cancellationToken: cancellation.Token));
        Assert.Same(beforeData, before.Data);
        Assert.Same(afterData, after.Data);
    }

    private static (ProjectScenarioPlan Scenario, ProjectSimulationRun Before, ProjectSimulationRun After) Pair(
        CompilerExecutionPlan plan, long duration, int revision = 801)
    {
        var node = plan.Data.Value.GetProperty("tasks").EnumerateArray().Single(row => row.GetProperty("sourceEntityId").GetString() == Id(7));
        var target = ProjectLocation.Parse(SourceJson.Parse(node.GetProperty("location").GetRawText()));
        var value = JsonNode.Parse(node.GetProperty("duration").GetRawText())!;
        value["value"] = duration.ToString(CultureInfo.InvariantCulture);
        var prepared = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(plan.Input), plan.Input.InputDigest,
            ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(revision)), [new(target, ProjectScenarioOverrideKind.TaskDuration, Json(value))]);
        Assert.Equal(ProjectScenarioState.Ready, prepared.State);
        var scenario = Assert.IsType<ProjectScenarioPlan>(prepared.Plan);
        return (scenario, Run(plan, 821), Run(Plan(scenario.Input), 822));
    }

    private static CompilerExecutionPlan Plan(CompilerInputManifest input) =>
        Assert.IsType<CompilerExecutionPlan>(PureCoreCompiler.Compile(input, input.Compiler).Publication?.ExecutionPlan);

    private static ProjectSimulationRun Run(CompilerExecutionPlan plan, int id, long start = 100, SimulationRunLimits? limits = null) =>
        ProjectSimulation.Execute(new(plan, Guid.Parse(Id(id)), start, limits), plan.Input.InputDigest);

    private static ProjectScenarioElapsedScheduleImpact Compare(ProjectScenarioPlan scenario, ProjectSimulationRun before,
        ProjectSimulationRun after, int? limit = null) =>
        ProjectScenarioElapsedScheduleImpact.Compare(scenario, before, before.Digest, after, after.Digest, limit);

    private static ElapsedTaskSchedule Task(ElapsedRunSchedule run, int entityId) =>
        Assert.Single(run.Tasks, row => row.Task.EntityId == EntityId.Parse(Id(entityId)));

    private static string AssertRefusal(ProjectScenarioElapsedScheduleImpact impact)
    {
        Assert.Equal(ScenarioElapsedScheduleImpactState.Refused, impact.State);
        AssertNoProjection(impact);
        return Assert.IsType<string>(impact.Reason);
    }

    private static void AssertNoProjection(ProjectScenarioElapsedScheduleImpact impact)
    {
        Assert.Null(impact.Baseline);
        Assert.Null(impact.Hypothesis);
        Assert.Null(impact.ModeledFinishDeltaTicks);
    }
}
