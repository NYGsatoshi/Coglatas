using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed class TaskExecutionInterventionContractTests
{
    [Theory]
    [InlineData(TaskExecutionRunStatus.Accepted)]
    [InlineData(TaskExecutionRunStatus.Queued)]
    [InlineData(TaskExecutionRunStatus.Running)]
    public void Active_runs_allow_stop_and_direction_correction(TaskExecutionRunStatus status)
    {
        Assert.True(TaskExecutionRunLifecycle.CanIntervene(status));
        Assert.True(TaskExecutionRunLifecycle.CanTransition(status, TaskExecutionRunStatus.Stopped));
        Assert.True(TaskExecutionRunLifecycle.CanTransition(status, TaskExecutionRunStatus.Redirected));
    }

    [Theory]
    [InlineData(TaskExecutionRunStatus.Succeeded)]
    [InlineData(TaskExecutionRunStatus.Failed)]
    [InlineData(TaskExecutionRunStatus.Stopped)]
    [InlineData(TaskExecutionRunStatus.Redirected)]
    public void Terminal_runs_cannot_be_intervened_or_revived(TaskExecutionRunStatus status)
    {
        Assert.True(TaskExecutionRunLifecycle.IsTerminal(status));
        Assert.False(TaskExecutionRunLifecycle.CanIntervene(status));
        Assert.False(TaskExecutionRunLifecycle.CanTransition(status, TaskExecutionRunStatus.Stopped));
        Assert.False(TaskExecutionRunLifecycle.CanTransition(status, TaskExecutionRunStatus.Redirected));
        Assert.False(TaskExecutionRunLifecycle.CanTransition(status, TaskExecutionRunStatus.Running));
    }

    [Fact]
    public void Redirected_run_is_distinct_from_stopped_run()
    {
        Assert.NotEqual(TaskExecutionRunStatus.Stopped, TaskExecutionRunStatus.Redirected);
        Assert.NotEqual(TaskExecutionMajorState.Stopped, TaskExecutionMajorState.Redirected);
    }
}
