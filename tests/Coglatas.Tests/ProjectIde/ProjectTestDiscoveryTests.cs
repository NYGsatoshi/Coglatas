using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestDiscoveryTests
{
    private static CompilerInputManifest Fixture(Action<JsonObject, JsonArray, JsonObject>? mutate = null) =>
        ProjectTestRunnerTests.Fixture(mutate: mutate);
    private static ProjectTestPlan Plan(CompilerInputManifest input, ProjectTestLimits? limits = null) =>
        ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), new(SourceJson.FromElement(input.Settings.Value.GetProperty("evaluator"))), limits);

    [Fact]
    public void DiscoveryOwnsExactTypedLocationsWithoutChangingThePlanOrRun()
    {
        var input = Fixture(); var plan = Plan(input); var before = plan.Data; var run = plan.Execute(input.InputDigest);
        var inventory = plan.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.Equal(input.InputDigest, inventory.InputDigest);
        Assert.Equal(plan.Digest, inventory.PlanDigest); Assert.True(inventory.BindingComplete); Assert.True(inventory.EvaluatorMatches);
        var test = Assert.Single(inventory.Tests); Assert.Equal(1, inventory.DiscoveredTests);
        Assert.Equal(EntityId.Parse(Id(9)), test.TestId); Assert.Equal("Input contract", test.DisplayName);
        Assert.Equal(ValueDecodeStatus.Decoded, test.DecodeStatus); Assert.Equal(1, test.AssertionCount);
        Assert.Equal(ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: test.TestId).Data, test.Location.Data);
        Assert.Equal(before, plan.Data); Assert.Equal(run.Data, plan.Execute(input.InputDigest).Data);
        Assert.False(inventory.Tests is IList);
        var legacy = Assert.IsAssignableFrom<IList<ProjectDiscoveredTest>>(inventory.Tests); Assert.True(legacy.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => legacy.Clear()); Assert.Single(inventory.Tests);
    }

    [Fact]
    public void DiscoveringACounterexampleIsNotTestExecutionOrPass()
    {
        var input = Fixture((port, _, _) => port["defaultValue"]!["value"] = false); var plan = Plan(input);
        Assert.Equal(ProjectTestDiscoveryState.Ready, plan.Discover(input.InputDigest).State);
        Assert.Equal(CompilerVerdict.Fail, plan.Execute(input.InputDigest).Verdict);
    }

    [Theory]
    [InlineData(true, ValueDecodeStatus.Unsupported)]
    [InlineData(false, ValueDecodeStatus.Invalid)]
    public void InventoryPreservesUnsupportedAndInvalidDefinitionsWithoutInventingAssertionCounts(bool future, ValueDecodeStatus expected)
    {
        var input = Fixture((_, assertions, _) =>
        {
            if (future) assertions[0]!["kind"] = "example.future";
            else assertions[0]!["expected"]!["type"]!["kind"] = "coglatas.decimal";
        });
        var inventory = Plan(input).Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.False(inventory.BindingComplete);
        var test = Assert.Single(inventory.Tests); Assert.Equal(EntityId.Parse(Id(9)), test.TestId);
        Assert.Equal(expected, test.DecodeStatus); Assert.Null(test.AssertionCount);
    }

    [Fact]
    public void ZeroTestsRemainAnExplicitEmptyInventory()
    {
        var input = Fixture((_, _, test) => test["kind"] = "example.not-test");
        var inventory = Plan(input).Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.Equal(0, inventory.DiscoveredTests); Assert.Empty(inventory.Tests);
    }

    [Fact]
    public void AStaleQueryReturnsNoEarlierIdentifiersOrStamps()
    {
        var input = Fixture(); var plan = Plan(input);
        var stale = plan.Discover(Input(source: Source(99)).InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.StaleInput, stale.State); Assert.Empty(stale.Tests);
        Assert.Null(stale.InputDigest); Assert.Null(stale.PlanDigest); Assert.Equal(0, stale.DiscoveredTests);
        Assert.Single(plan.Discover(input.InputDigest).Tests);
    }

    [Fact]
    public void SharedBindingExhaustionIsUnavailableInsteadOfAnEmptySuccessfulInventory()
    {
        var basis = Fixture(); var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = 1;
        var input = new CompilerInputManifest(basis.Source, Json(settings)); var plan = Plan(input);
        Assert.Equal(1, plan.DiscoveredTests); Assert.False(plan.DiscoveryComplete);
        var inventory = plan.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Unavailable, inventory.State); Assert.Empty(inventory.Tests);
        Assert.Null(inventory.InputDigest); Assert.Null(inventory.PlanDigest);
    }

    [Fact]
    public void PlanBoundsRefuseTheWholeInventoryAndNeverPublishATruncatedPrefix()
    {
        var basis = Fixture(); var declarations = JsonNode.Parse(basis.Source.Declarations.CanonicalText)!.AsArray();
        var second = declarations[1]!.DeepClone(); second["entityId"] = Id(10); declarations.Insert(0, second);
        var input = new CompilerInputManifest(ProjectSource.Create(basis.Source.Context, basis.Source.Documents, Json(declarations)), basis.Settings);
        var bounded = Plan(input, new(MaximumTests: 1)).Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.ResourceLimit, bounded.State); Assert.Empty(bounded.Tests); Assert.Null(bounded.InputDigest);
        var complete = Plan(input).Discover(input.InputDigest);
        Assert.Equal(new[] { EntityId.Parse(Id(9)), EntityId.Parse(Id(10)) }, complete.Tests.Select(test => test.TestId));
    }

    [Fact]
    public void AssertionBoundAndCancellationAreDistinctRefusals()
    {
        var input = Fixture((_, assertions, _) => { var second = assertions[0]!.DeepClone(); second["key"] = "example.second"; assertions.Add(second); });
        var plan = Plan(input, new(MaximumAssertions: 1));
        Assert.Equal(ProjectTestDiscoveryState.ResourceLimit, plan.Discover(input.InputDigest).State);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => plan.Discover(input.InputDigest, cancelled.Token));
    }

    [Fact]
    public void PartialSourceAndEvaluatorMismatchRemainVisibleInventoryFacts()
    {
        var basis = Fixture(); var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject(); settings["sourceCoverage"] = "coglatas.partial";
        var input = new CompilerInputManifest(basis.Source, Json(settings));
        var evaluator = new CompilerArtifactIdentity(Json(Artifact("example.other-evaluator")));
        var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), evaluator);
        var inventory = plan.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.Single(inventory.Tests);
        Assert.False(inventory.BindingComplete); Assert.False(inventory.EvaluatorMatches);
        Assert.Equal(CompilerVerdict.NotRun, plan.Execute(input.InputDigest).Verdict);
    }

    [Fact]
    public void CandidateLocationsRetainTheExactCurrentDraftIdentity()
    {
        var input = Fixture(); var basis = input.Source.Context.CommittedRevision!;
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(input), new(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(51)), basis, basis));
        var inventory = Plan(draft).Discover(draft.InputDigest);
        Assert.Equal(draft.Source.Context, Assert.Single(inventory.Tests).Location.Context);
        Assert.Equal(draft.InputDigest, inventory.InputDigest); Assert.NotEqual(input.InputDigest, inventory.InputDigest);
    }
}
