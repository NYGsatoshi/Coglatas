namespace Coglatas.Domain.ProjectIde;

public enum ScenarioElapsedScheduleImpactState { Complete, Incomplete, Refused, OperationLimit }

/// <summary>Task times relative to the run start and total float against the modeled finish.</summary>
public sealed record ElapsedTaskSchedule(ProjectLocation Task, long StartOffsetTicks, long FinishOffsetTicks, long TotalFloatTicks);

public sealed class ElapsedRunSchedule
{
    public ContentDigest RunDigest { get; }
    public long FinishOffsetTicks { get; }
    public IReadOnlyList<ElapsedTaskSchedule> Tasks { get; }

    internal ElapsedRunSchedule(ContentDigest runDigest, long finishOffsetTicks, IEnumerable<ElapsedTaskSchedule> tasks)
    {
        RunDigest = runDigest;
        FinishOffsetTicks = finishOffsetTicks;
        Tasks = new ImmutableListView<ElapsedTaskSchedule>(tasks);
    }
}

/// <summary>Quantitative comparison for a fixed Scenario in the elapsed finish-to-start model.</summary>
public sealed class ProjectScenarioElapsedScheduleImpact
{
    public ScenarioElapsedScheduleImpactState State { get; }
    public string Reason { get; }
    public ElapsedRunSchedule? Baseline { get; }
    public ElapsedRunSchedule? Hypothesis { get; }
    public long? ModeledFinishDeltaTicks { get; }
    public int LogicalOperationsConsumed { get; }

    private ProjectScenarioElapsedScheduleImpact(ScenarioElapsedScheduleImpactState state, string reason,
        int consumed = 0, ElapsedRunSchedule? baseline = null, ElapsedRunSchedule? hypothesis = null, long? delta = null)
    {
        State = state;
        Reason = reason;
        LogicalOperationsConsumed = consumed;
        Baseline = baseline;
        Hypothesis = hypothesis;
        ModeledFinishDeltaTicks = delta;
    }

    public static ProjectScenarioElapsedScheduleImpact Compare(ProjectScenarioPlan scenario,
        ProjectSimulationRun baseline, ContentDigest expectedBaseline, ProjectSimulationRun hypothesis,
        ContentDigest expectedHypothesis, int? maximumOperations = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(expectedBaseline);
        ArgumentNullException.ThrowIfNull(hypothesis);
        ArgumentNullException.ThrowIfNull(expectedHypothesis);
        cancellationToken.ThrowIfCancellationRequested();

        var runLimit = baseline.Manifest.Limits.MaximumOperations;
        var maximum = maximumOperations ?? runLimit;
        if (maximum < 1 || maximum > runLimit) throw new ArgumentOutOfRangeException(nameof(maximumOperations));

        var hypothesisInput = hypothesis.Manifest.Plan.Input;
        if (scenario.Materialize(baseline.Manifest.Plan.Input, cancellationToken) is null ||
            !scenario.Input.Data.Equals(hypothesisInput.Data) || !scenario.Input.Source.Data.Equals(hypothesisInput.Source.Data))
            return new(ScenarioElapsedScheduleImpactState.Refused, "coglatas.schedule-impact.wrong-scenario-input");

        var recordings = ProjectSimulationRunComparison.Compare(baseline, expectedBaseline,
            hypothesis, expectedHypothesis, cancellationToken);
        if (recordings.State == SimulationRunComparisonState.Refused)
            return new(ScenarioElapsedScheduleImpactState.Refused, recordings.Reason);
        if (recordings.State == SimulationRunComparisonState.Incomplete)
            return new(ScenarioElapsedScheduleImpactState.Incomplete, recordings.Reason);
        if (recordings.SameLimits != true || baseline.Manifest.StartTicks != hypothesis.Manifest.StartTicks)
            return new(ScenarioElapsedScheduleImpactState.Refused, "coglatas.schedule-impact.different-run-conditions");

        var remaining = maximum;
        bool Charge()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remaining == 0) return false;
            remaining--;
            return true;
        }

        var before = ReadSchedule(baseline, Charge);
        var after = before is null ? null : ReadSchedule(hypothesis, Charge);
        cancellationToken.ThrowIfCancellationRequested();
        return before is null || after is null
            ? new(ScenarioElapsedScheduleImpactState.OperationLimit, "coglatas.schedule-impact.operation-limit", maximum - remaining)
            : new(ScenarioElapsedScheduleImpactState.Complete, "coglatas.schedule-impact.elapsed-model",
                maximum - remaining, before, after, recordings.ObservedElapsedDelta);
    }

    private sealed class TaskTiming(ProjectLocation location, long horizon)
    {
        public ProjectLocation Location { get; } = location;
        public long Start { get; set; }
        public long Finish { get; set; }
        public long LatestFinish { get; set; } = horizon;
        public List<(TaskTiming Task, long Lag)> Prerequisites { get; } = [];
    }

    private static ElapsedRunSchedule? ReadSchedule(ProjectSimulationRun run, Func<bool> charge)
    {
        var plan = run.Manifest.Plan.Data.Value;
        var horizon = run.VirtualTicks - run.Manifest.StartTicks;
        var tasks = new Dictionary<string, TaskTiming>(StringComparer.Ordinal);
        // The complete immutable recording has one start and one end for every Task in this plan.
        foreach (var definition in plan.GetProperty("tasks").EnumerateArray())
        {
            if (!charge()) return null;
            var location = ProjectLocation.Parse(SourceJson.FromElement(definition.GetProperty("location")));
            tasks.Add(SimulationCursor.TaskIdentityKey(definition), new(location, horizon));
        }
        foreach (var item in run.Events)
        {
            if (!charge()) return null;
            var data = item.Value;
            var task = tasks[SimulationCursor.TaskIdentityKey(data)];
            var offset = ElapsedDuration.Parse(data.GetProperty("ticks").GetString()!).Ticks - run.Manifest.StartTicks;
            if (data.GetProperty("kind").GetString() == "coglatas.task-start")
                task.Start = offset;
            else
                task.Finish = offset;
        }
        foreach (var dependency in plan.GetProperty("dependencies").EnumerateArray())
        {
            if (!charge()) return null;
            var prerequisite = tasks[SimulationCursor.TaskIdentityKey(dependency.GetProperty("prerequisite"))];
            var dependent = tasks[SimulationCursor.TaskIdentityKey(dependency.GetProperty("dependent"))];
            var lag = ElapsedDuration.Parse(dependency.GetProperty("lag").GetProperty("value").GetString()!).Ticks;
            dependent.Prerequisites.Add((prerequisite, lag));
        }
        var order = new List<TaskTiming>(tasks.Count);
        foreach (var definition in plan.GetProperty("definitionOrder").EnumerateArray())
        {
            if (!charge()) return null;
            order.Add(tasks[SimulationCursor.TaskIdentityKey(definition)]);
        }

        var rows = new ElapsedTaskSchedule[order.Count];
        // Flow latest starts backwards through each lag. A Task with several successors must satisfy them all.
        for (var index = order.Count - 1; index >= 0; index--)
        {
            if (!charge()) return null;
            var task = order[index];
            var latestStart = task.LatestFinish - (task.Finish - task.Start);
            rows[index] = new(task.Location, task.Start, task.Finish, task.LatestFinish - task.Finish);
            foreach (var (prerequisite, lag) in task.Prerequisites)
            {
                if (!charge()) return null;
                prerequisite.LatestFinish = Math.Min(prerequisite.LatestFinish, latestStart - lag);
            }
        }
        return new(run.Digest, horizon, rows);
    }
}
