using Coglatas.Application.Projects;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed class FirstPartyProjectFilesRuntimeContractTests
{
    [Fact]
    [Trait("Scope", "Issue461")]
    public void RuntimeHandleCarriesOnlyAnOpaqueServerOwnedRunIdentity()
    {
        var properties = typeof(TaskExecutionRuntimeHandle)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            [
                nameof(TaskExecutionRuntimeHandle.RunId),
                nameof(TaskExecutionRuntimeHandle.RuntimeContractVersion),
                nameof(TaskExecutionRuntimeHandle.TenantId)
            ],
            properties);
    }

    [Fact]
    [Trait("Scope", "Issue461")]
    public void RuntimeContractUsesTheFixedFirstPartyProjectFilesProvider()
    {
        Assert.Equal(TaskExecutionProvider.FirstPartyProjectFilesRuntimeV1, FirstPartyProjectFilesRuntimeV1.Provider);
        Assert.Equal(1, FirstPartyProjectFilesRuntimeV1.ContractVersion);

        var noWebOrFiles = FirstPartyProjectFilesRuntimeV1.EvaluateScope(webEnabled: false, projectFilesEnabled: true);
        var webEnabled = FirstPartyProjectFilesRuntimeV1.EvaluateScope(webEnabled: true, projectFilesEnabled: true);
        var filesDisabled = FirstPartyProjectFilesRuntimeV1.EvaluateScope(webEnabled: false, projectFilesEnabled: false);

        Assert.True(noWebOrFiles.IsEligible);
        Assert.Equal("TASK_EXECUTION_WEB_UNSUPPORTED", webEnabled.FailureCode);
        Assert.Equal("TASK_EXECUTION_PROJECT_FILES_REQUIRED", filesDisabled.FailureCode);
    }

    [Theory]
    [Trait("Scope", "Issue461")]
    [InlineData(TaskExecutionRunStatus.Accepted, TaskExecutionRunStatus.Queued, true)]
    [InlineData(TaskExecutionRunStatus.Queued, TaskExecutionRunStatus.Running, true)]
    [InlineData(TaskExecutionRunStatus.Running, TaskExecutionRunStatus.Succeeded, true)]
    [InlineData(TaskExecutionRunStatus.Running, TaskExecutionRunStatus.Failed, true)]
    [InlineData(TaskExecutionRunStatus.Accepted, TaskExecutionRunStatus.Running, false)]
    [InlineData(TaskExecutionRunStatus.Queued, TaskExecutionRunStatus.Failed, false)]
    [InlineData(TaskExecutionRunStatus.Succeeded, TaskExecutionRunStatus.Running, false)]
    public void LifecycleAllowsOnlyTheCanonicalV1Transitions(
        TaskExecutionRunStatus from,
        TaskExecutionRunStatus to,
        bool expected) =>
        Assert.Equal(expected, TaskExecutionRunLifecycle.CanTransition(from, to));
}
