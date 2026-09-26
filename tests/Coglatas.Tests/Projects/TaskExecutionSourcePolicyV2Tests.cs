using Coglatas.Application.Projects;

namespace Coglatas.Tests.Projects;

public sealed class TaskExecutionSourcePolicyV2Tests
{
    [Fact]
    [Trait("Scope", "Issue361")]
    public void LegacyProjectionMapsEnabledKindsToAllowAndKeepsUnsupportedKindsExcluded()
    {
        var policy = TaskExecutionSourcePolicyV2.FromLegacy(webEnabled: true, projectFilesEnabled: false);

        Assert.Equal(TaskExecutionSourcePolicyV2.CurrentSchemaVersion, policy.SchemaVersion);
        Assert.Equal(TaskExecutionSourceState.Allow, policy.Web);
        Assert.Equal(TaskExecutionSourceState.Exclude, policy.WebSite);
        Assert.Equal(TaskExecutionSourceState.Exclude, policy.ProjectFile);
        Assert.Equal(TaskExecutionSourceState.Exclude, policy.ConnectedApp);
        Assert.True(policy.WebEnabled);
        Assert.False(policy.ProjectFilesEnabled);
        Assert.Empty(policy.Items);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    public void ItemRuleOverridesKindDefaultAndPrioritizeImpliesAllowed()
    {
        var fileId = Guid.NewGuid();
        var sourceId = TaskExecutionSourcePolicyV2.ProjectFileSourceId(fileId);
        var policy = new TaskExecutionSourcePolicyV2(
            2,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            [new TaskExecutionSourceRule(TaskExecutionSourceKind.ProjectFile, sourceId, TaskExecutionSourceState.Prioritize)]);

        Assert.Equal(TaskExecutionSourceState.Prioritize, policy.Resolve(TaskExecutionSourceKind.ProjectFile, sourceId));
        Assert.True(policy.ProjectFilesEnabled);
        Assert.False(policy.WebEnabled);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    public void ExplicitExcludeOverridesAnAllowedKindDefault()
    {
        var fileId = Guid.NewGuid();
        var sourceId = TaskExecutionSourcePolicyV2.ProjectFileSourceId(fileId);
        var policy = new TaskExecutionSourcePolicyV2(
            2,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Allow,
            TaskExecutionSourceState.Exclude,
            [new TaskExecutionSourceRule(TaskExecutionSourceKind.ProjectFile, sourceId, TaskExecutionSourceState.Exclude)]);

        Assert.Equal(TaskExecutionSourceState.Exclude, policy.Resolve(TaskExecutionSourceKind.ProjectFile, sourceId));
        Assert.True(policy.ProjectFilesEnabled);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    public void NormalizeCanonicalizesStableIdsAndRejectsDuplicateRules()
    {
        var fileId = Guid.NewGuid();
        var policy = new TaskExecutionSourcePolicyV2(
            2,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            [
                new TaskExecutionSourceRule(TaskExecutionSourceKind.WebSite, "site:EXAMPLE.COM.", TaskExecutionSourceState.Allow),
                new TaskExecutionSourceRule(TaskExecutionSourceKind.ProjectFile, $"FILE:{fileId:D}", TaskExecutionSourceState.Prioritize)
            ]);

        Assert.True(policy.TryNormalize(out var normalized, out var target, out var message), message);
        Assert.Null(target);
        Assert.Equal("site:example.com", normalized.Items[0].SourceId);
        Assert.Equal(TaskExecutionSourcePolicyV2.ProjectFileSourceId(fileId), normalized.Items[1].SourceId);

        var duplicate = normalized with
        {
            Items = [normalized.Items[0], normalized.Items[0] with { SourceId = "site:EXAMPLE.COM" }]
        };
        Assert.False(duplicate.TryNormalize(out _, out target, out _));
        Assert.Equal("policyV2.items[1]", target);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    [Trait("Scope", "CS01")]
    public void NormalizeAcceptsExactlyTheMaximumBoundedItemRuleCount()
    {
        var items = Enumerable.Range(0, TaskExecutionSourcePolicyV2.MaxItemRules)
            .Select(index => new TaskExecutionSourceRule(
                TaskExecutionSourceKind.WebSite,
                $"site:source-{index}.example",
                TaskExecutionSourceState.Allow))
            .ToArray();
        var policy = new TaskExecutionSourcePolicyV2(
            TaskExecutionSourcePolicyV2.CurrentSchemaVersion,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            items);

        Assert.True(policy.TryNormalize(out var normalized, out var target, out var message), message);
        Assert.Null(target);
        Assert.Equal(TaskExecutionSourcePolicyV2.MaxItemRules, normalized.Items.Count);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    [Trait("Scope", "CS01")]
    public void NormalizeRejectsOversizedItemRulesBeforePerItemProcessing()
    {
        var items = Enumerable.Repeat(
                new TaskExecutionSourceRule(
                    TaskExecutionSourceKind.ProjectFile,
                    "file:not-a-guid",
                    TaskExecutionSourceState.Allow),
                TaskExecutionSourcePolicyV2.MaxItemRules + 1)
            .ToArray();
        var policy = new TaskExecutionSourcePolicyV2(
            TaskExecutionSourcePolicyV2.CurrentSchemaVersion,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            items);

        Assert.False(policy.TryNormalize(out _, out var target, out var message));
        Assert.Equal("policyV2.items", target);
        Assert.Contains(TaskExecutionSourcePolicyV2.MaxItemRules.ToString(), message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    public void InvalidSourceIdentifiersFailClosed()
    {
        var policy = new TaskExecutionSourcePolicyV2(
            2,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            [new TaskExecutionSourceRule(TaskExecutionSourceKind.ProjectFile, "file:not-a-guid", TaskExecutionSourceState.Allow)]);

        Assert.False(policy.TryNormalize(out _, out var target, out var message));
        Assert.Equal("policyV2.items[0].sourceId", target);
        Assert.NotNull(message);
    }

    [Fact]
    [Trait("Scope", "Issue361")]
    public void AnyAllowedUnsupportedKindIsVisibleToTheCurrentRuntimeGate()
    {
        var sitePolicy = new TaskExecutionSourcePolicyV2(
            2,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Exclude,
            TaskExecutionSourceState.Allow,
            TaskExecutionSourceState.Exclude,
            [new TaskExecutionSourceRule(TaskExecutionSourceKind.WebSite, "site:example.com", TaskExecutionSourceState.Prioritize)]);
        var projectFilesOnly = sitePolicy with { Items = [] };

        Assert.True(sitePolicy.HasUnsupportedExecutableSources);
        Assert.False(projectFilesOnly.HasUnsupportedExecutableSources);
    }
}
