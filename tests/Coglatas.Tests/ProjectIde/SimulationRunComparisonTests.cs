using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SimulationRunComparisonTests
{
    private static ProjectSource OneTaskSource(long duration = 5, int revision = 5, int tenant = 1, int project = 2, int branch = 3, string name = "project")
    {
        var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(tenant)), ProjectId.Parse(Id(project)), BranchId.Parse(Id(branch))), RevisionId.Parse(Id(revision))));
        var document = SourceDocument.Create(DocumentId.Parse(Id(4)), name, Json(Obj(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, payload = new { } })));
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = duration.ToString(CultureInfo.InvariantCulture) },
            ports = Array.Empty<object>() } });
        return ProjectSource.Create(context, [document], Json(new JsonArray(task)), Json(new JsonArray()));
    }
    private static CompilerExecutionPlan Plan(ProjectSource source, JsonObject? settings = null, IEnumerable<CompilerMaterializedSnapshot>? snapshots = null)
    {
        var input = CompilerExecutionPlanTests.Request(Input(settings, source));
        var result = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
        Assert.True(result.Publication?.ExecutionPlan is not null, result.Result.Data.CanonicalText);
        return Assert.IsType<CompilerExecutionPlan>(result.Publication?.ExecutionPlan);
    }
    private static ProjectSimulationRun Run(CompilerExecutionPlan plan, int id, long start = 100, SimulationRunLimits? limits = null, ContentDigest? expected = null) =>
        ProjectSimulation.Execute(new(plan, Guid.Parse(Id(id)), start, limits), expected ?? plan.Input.InputDigest);
    private static ProjectSimulationRunComparison Compare(ProjectSimulationRun before, ProjectSimulationRun after) =>
        ProjectSimulationRunComparison.Compare(before, before.Digest, after, after.Digest);
    private static void Refused(ProjectSimulationRunComparison comparison, string reason)
    {
        Assert.Equal(SimulationRunComparisonState.Refused, comparison.State); Assert.Equal(reason, comparison.Reason);
        Assert.Null(comparison.Before); Assert.Null(comparison.After); Assert.Null(comparison.SameInput); Assert.Null(comparison.SameLimits); Assert.Null(comparison.ObservedElapsedDelta);
        Assert.Equal(JsonValueKind.Null, comparison.Data.Value.GetProperty("before").ValueKind);
        Assert.Equal(JsonValueKind.Null, comparison.Data.Value.GetProperty("after").ValueKind);
    }
    private static CompilerExecutionPlan Captured(ProjectSource root, ProjectSource captured, int snapshotId, string version)
    {
        var settings = Settings(); settings["authorizedExternalSnapshots"] = JsonSerializer.SerializeToNode(new[] { new {
            snapshotId = Id(snapshotId), version, context = captured.Context.Data.Value, digest = captured.Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null } });
        return Plan(root, settings, [new(ExternalSnapshotId.Parse(Id(snapshotId)), version, new(captured, true, true, true))]);
    }

    [Fact]
    public void DistinctFixedInputsCompareObservedVirtualElapsedWithoutEvaluatingOutcomes()
    {
        var before = Run(Plan(OneTaskSource()), 1000); var after = Run(Plan(OneTaskSource(20, revision: 10)), 1001);
        var comparison = Compare(before, after);
        Assert.Equal(SimulationRunComparisonState.Complete, comparison.State); Assert.False(comparison.SameInput); Assert.True(comparison.SameLimits);
        Assert.Equal(15, comparison.ObservedElapsedDelta); Assert.Same(before, comparison.Before); Assert.Same(after, comparison.After);
        Assert.Equal("NotRun", comparison.Data.Value.GetProperty("verdict").GetString()); Assert.Equal("NotEvaluated", comparison.Data.Value.GetProperty("authority").GetString());
        Assert.Equal(before.Digest.Data.Value.GetRawText(), comparison.Data.Value.GetProperty("before").GetProperty("runDigest").GetRawText());
        Assert.Equal(after.Manifest.Plan.Input.InputDigest.Data.Value.GetRawText(), comparison.Data.Value.GetProperty("after").GetProperty("inputDigest").GetRawText());
        Assert.Equal(ContentDigest.Compute(ProjectSimulationRunComparison.Format, comparison.Data.ToCanonicalBytes()), comparison.Digest);
    }
    [Fact]
    public void OffsetClocksAndDifferentCompleteBudgetsDoNotFabricateElapsedChanges()
    {
        var plan = Plan(OneTaskSource()); var before = Run(plan, 1000); var after = Run(plan, 1001, 500, new(MaximumEvents: 10));
        var comparison = Compare(before, after);
        Assert.Equal(SimulationRunComparisonState.Complete, comparison.State); Assert.True(comparison.SameInput); Assert.False(comparison.SameLimits); Assert.Equal(0, comparison.ObservedElapsedDelta);
        Assert.NotEqual(before.VirtualTicks, after.VirtualTicks); Assert.Equal("500", comparison.Data.Value.GetProperty("after").GetProperty("startTicks").GetString());
    }
    [Theory]
    [InlineData(long.MaxValue, 0, -long.MaxValue)] [InlineData(0, long.MaxValue, long.MaxValue)] [InlineData(0, 0, 0)]
    public void CompleteElapsedDifferencesPreserveSignedExactIntegerExtremes(long first, long second, long delta)
    {
        var before = Run(Plan(OneTaskSource(first)), 1000, 0); var after = Run(Plan(OneTaskSource(second, revision: 10)), 1001, 0);
        Assert.Equal(CompilerExecutionState.Completed, before.Execution); Assert.Equal(CompilerExecutionState.Completed, after.Execution);
        var comparison = Compare(before, after); Assert.Equal(SimulationRunComparisonState.Complete, comparison.State); Assert.Equal(delta, comparison.ObservedElapsedDelta);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(5)]
    public void BoundedPrefixesKeepTheirActualCountsWithoutACompleteElapsedDelta(int events)
    {
        var plan = ProjectSimulationTests.Plan(); var full = Run(plan, 1000); var partial = Run(plan, 1001, limits: new(MaximumEvents: events));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, partial.Execution);
        var comparison = Compare(full, partial); Assert.Equal(SimulationRunComparisonState.Incomplete, comparison.State); Assert.Null(comparison.ObservedElapsedDelta);
        Assert.Equal(events, comparison.Data.Value.GetProperty("after").GetProperty("eventCount").GetInt32()); Assert.Equal("Partial", comparison.Data.Value.GetProperty("after").GetProperty("coverage").GetString());
    }
    [Fact]
    public void StaleInputAndCancelledRunsRemainIncompleteWithoutPassOrCurrentness()
    {
        var plan = Plan(OneTaskSource()); var full = Run(plan, 1000); var wrong = ContentDigest.Compute("example.wrong-input", [1]);
        var stale = Run(plan, 1001, expected: wrong); Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        var comparison = Compare(full, stale); Assert.Equal(SimulationRunComparisonState.Incomplete, comparison.State); Assert.Null(comparison.ObservedElapsedDelta);
        Assert.Equal("Stale", comparison.Data.Value.GetProperty("after").GetProperty("freshness").GetString());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = ProjectSimulation.Execute(new(plan, Guid.Parse(Id(1002))), plan.Input.InputDigest, cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Execution); Assert.Equal(SimulationRunComparisonState.Incomplete, Compare(full, cancelled).State);
    }
    [Fact]
    public void ExpectedRecordingDigestsAreCheckedBeforeReturningAnySummary()
    {
        var before = Run(Plan(OneTaskSource()), 1000); var after = Run(Plan(OneTaskSource(revision: 10)), 1001);
        Refused(ProjectSimulationRunComparison.Compare(before, before.Manifest.Digest, after, after.Digest), "coglatas.comparison.wrong-run");
        Refused(ProjectSimulationRunComparison.Compare(before, before.Digest, after, after.Manifest.Digest), "coglatas.comparison.wrong-run");
    }
    [Fact]
    public void SameRunIdentityMayRepeatOnlyTheSameImmutableRecording()
    {
        var plan = Plan(OneTaskSource()); var before = Run(plan, 1000); var replay = ProjectSimulation.Execute(before.Manifest, plan.Input.InputDigest);
        var same = Compare(before, replay); Assert.Equal(SimulationRunComparisonState.Complete, same.State); Assert.Equal(0, same.ObservedElapsedDelta);
        var conflict = Run(Plan(OneTaskSource(20, revision: 10)), 1000); Refused(Compare(before, conflict), "coglatas.comparison.run-identity-conflict");
    }
    [Theory]
    [InlineData(1, 2, 30)] [InlineData(1, 20, 3)] [InlineData(10, 2, 3)]
    public void DifferentRootBranchesProjectsAndTenantsReturnNoComparison(int tenant, int project, int branch)
    {
        var before = Run(Plan(OneTaskSource()), 1000); var after = Run(Plan(OneTaskSource(tenant: tenant, project: project, branch: branch)), 1001);
        Refused(Compare(before, after), "coglatas.comparison.different-root-branch");
    }
    [Fact]
    public void AStableSourceMayBeComparedUnderDifferentFixedInputSettings()
    {
        var source = OneTaskSource(); var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 100000;
        var before = Run(Plan(source), 1000); var after = Run(Plan(source, settings), 1001);
        Assert.Equal(before.Manifest.Plan.Input.Source.Digest, after.Manifest.Plan.Input.Source.Digest);
        Assert.Equal(before.Manifest.Plan.Input.Source.Context, after.Manifest.Plan.Input.Source.Context);
        Assert.NotEqual(before.Manifest.Plan.Input.InputDigest, after.Manifest.Plan.Input.InputDigest);
        var comparison = Compare(before, after); Assert.Equal(SimulationRunComparisonState.Complete, comparison.State);
        Assert.False(comparison.SameInput); Assert.Equal(0, comparison.ObservedElapsedDelta);
    }
    [Fact]
    public void AReusedRootContextCannotNameTwoDifferentSourceBodies()
    {
        var before = Run(Plan(OneTaskSource()), 1000); var after = Run(Plan(OneTaskSource(20)), 1001);
        Refused(Compare(before, after), "coglatas.comparison.capture-identity-conflict");
    }
    [Theory]
    [InlineData(1900, "one")] [InlineData(1901, "two")]
    public void ACapturedContextKeepsOneSourceBodyAcrossBothRunInputs(int afterId, string afterVersion)
    {
        var beforeCapture = OneTaskSource(5, revision: 50, project: 20, branch: 30, name: "before");
        var afterCapture = OneTaskSource(5, revision: 50, project: 20, branch: 30, name: "after");
        var before = Run(Captured(OneTaskSource(), beforeCapture, 1900, "one"), 1000);
        var after = Run(Captured(OneTaskSource(revision: 10), afterCapture, afterId, afterVersion), 1001);
        Refused(Compare(before, after), "coglatas.comparison.capture-identity-conflict");
    }
    [Fact]
    public void OneSnapshotVersionCannotSilentlyMoveToAnotherCapturedContext()
    {
        var before = Run(Captured(OneTaskSource(), OneTaskSource(revision: 50, project: 20, branch: 30), 1900, "one"), 1000);
        var after = Run(Captured(OneTaskSource(revision: 10), OneTaskSource(revision: 51, project: 20, branch: 30), 1900, "one"), 1001);
        Refused(Compare(before, after), "coglatas.comparison.capture-identity-conflict");
    }
    [Fact]
    public void RootAndCapturedSourceIdentitiesShareTheSameConflictCheck()
    {
        var before = Run(Plan(OneTaskSource()), 1000);
        var after = Run(Captured(OneTaskSource(revision: 10), OneTaskSource(20), 1900, "one"), 1001);
        Refused(Compare(before, after), "coglatas.comparison.capture-identity-conflict");
    }
    [Fact]
    public void EqualCapturedBodiesMayKeepTheirIdentityAcrossSeparateInputs()
    {
        var captured = OneTaskSource(revision: 50, project: 20, branch: 30);
        var before = Run(Captured(OneTaskSource(), captured, 1900, "one"), 1000);
        var after = Run(Captured(OneTaskSource(revision: 10), captured, 1900, "one"), 1001);
        var comparison = Compare(before, after); Assert.Equal(SimulationRunComparisonState.Complete, comparison.State); Assert.Equal(0, comparison.ObservedElapsedDelta);
    }
    [Fact]
    public void UnsupportedEnginesRefuseComparisonAndCancellationStopsBeforeProjection()
    {
        var plan = Plan(OneTaskSource()); var before = Run(plan, 1000); var engine = JsonNode.Parse(ProjectSimulation.Engine.Data.CanonicalText)!; engine["version"] = "2";
        var unsupported = ProjectSimulation.Execute(new(plan, Guid.Parse(Id(1001)), engine: new(Json(engine))), plan.Input.InputDigest);
        Assert.Equal(CompilerExecutionState.NotRun, unsupported.Execution); Refused(Compare(before, unsupported), "coglatas.comparison.engine-unsupported");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSimulationRunComparison.Compare(before, before.Digest, before, before.Digest, cancellation.Token));
    }
    [Theory]
    [InlineData(256)] [InlineData(257)]
    public void TheDeclaredCaptureRosterIsAdmittedAtTheBoundAndRefusedAboveIt(int count)
    {
        var captured = OneTaskSource(revision: 50, project: 20, branch: 30); var settings = Settings();
        settings["authorizedExternalSnapshots"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, count).Select(index => new {
            snapshotId = Id(2000 + index), version = "one", context = captured.Context.Data.Value, digest = captured.Digest.Data.Value,
            coverage = "coglatas.complete", asOf = (string?)null }));
        var plan = Plan(OneTaskSource(), settings, [new(ExternalSnapshotId.Parse(Id(2000)), "one", new(captured, true, true, true))]);
        Assert.Equal(2, plan.Scope.Regions.Count);
        Assert.Equal(count, plan.Input.Settings.Value.GetProperty("authorizedExternalSnapshots").GetArrayLength());
        var run = Run(plan, 1000);
        Assert.Equal(CompilerExecutionState.Completed, run.Execution);
        if (count == ProjectSimulationRunComparison.MaximumCapturedInputsPerRun) Assert.Equal(SimulationRunComparisonState.Complete, Compare(run, run).State);
        else Refused(Compare(run, run), "coglatas.comparison.capture-bound");
    }
    [Fact]
    public void RepeatedProjectionRetainsBothOriginalRecordingsAndTheirDigests()
    {
        var before = Run(Plan(OneTaskSource()), 1000); var after = Run(Plan(OneTaskSource(20, revision: 10)), 1001);
        var beforeBytes = before.Data.CanonicalText; var afterBytes = after.Data.CanonicalText;
        var first = Compare(before, after); var second = Compare(before, after);
        Assert.Equal(first.Data, second.Data); Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(beforeBytes, before.Data.CanonicalText); Assert.Equal(afterBytes, after.Data.CanonicalText);
    }
}
