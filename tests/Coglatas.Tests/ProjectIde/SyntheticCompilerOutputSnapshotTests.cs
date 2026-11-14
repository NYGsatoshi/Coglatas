using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticCompilerOutputSnapshotTests
{
    private static async Task<(SyntheticWorkbenchViewModel View, CompilerHostRequest Request)> Ready()
    {
        var f = await SyntheticSimulationTests.Ready(); Assert.True(f.View.LoadSyntheticScopedSources()); return f;
    }
    private static bool ReadBodies(SyntheticWorkbenchViewModel view) => view.ReadSyntheticCompilerOutputs(view.Result!.ResultDigest,
        CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
    [Fact]
    public async Task DefaultManifestAndExplicitOriginalBodiesDoNotRunTheCompilerOrReplaceItsResult()
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; var plan = view.ScopedExecutionPlan;
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(23000)))); var run = view.SimulationRun;
        Assert.True(view.ReadSyntheticCompilerOutputs(result.ResultDigest)); var manifest = view.CompilerOutputSnapshot!;
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, manifest.State); Assert.Empty(manifest.Bodies); Assert.Equal(CompilerFreshness.Current, manifest.Freshness);
        Assert.True(ReadBodies(view)); var captured = view.CompilerOutputSnapshot!;
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, captured.State); Assert.Equal(3, captured.Bodies.Count);
        Assert.Equal(result.ResultDigest, captured.ResultDigest); Assert.Equal(result.Input.InputDigest, captured.InputDigest);
        Assert.Same(result, view.Result); Assert.Same(plan, view.ScopedExecutionPlan); Assert.Same(run, view.SimulationRun);
        Assert.Equal(manifest.Data.Value.GetProperty("result").GetRawText(), captured.Data.Value.GetProperty("result").GetRawText());
    }
    [Fact]
    public async Task MissingSourceAdmissionCannotCompileOrRestoreTheOldOutputSnapshot()
    {
        var f = await SyntheticSimulationTests.Ready(); using var view = f.View; var result = view.Result!;
        Assert.False(view.ReadSyntheticCompilerOutputs(result.ResultDigest)); Assert.Null(view.CompilerOutputSnapshot); Assert.Same(result, view.Result);
        Assert.True(view.LoadSyntheticScopedSources()); Assert.True(ReadBodies(view)); view.Reset();
        Assert.False(view.ReadSyntheticCompilerOutputs(result.ResultDigest)); Assert.Null(view.CompilerOutputSnapshot); Assert.Null(view.Result);
    }
    [Fact]
    public async Task WrongExpectedResultWithdrawsPreviousBodiesAndRetainsOnlyAnUnboundRefusal()
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; Assert.True(ReadBodies(view));
        Assert.False(view.ReadSyntheticCompilerOutputs(result.Input.InputDigest, CompilerOutputSnapshotMode.AvailableOutputs));
        var refused = view.CompilerOutputSnapshot!; Assert.Equal(CompilerOutputSnapshotState.RefusedWrongResult, refused.State);
        Assert.Empty(refused.Bodies); Assert.Null(refused.InputDigest); Assert.Null(refused.ResultDigest); Assert.Null(refused.Context); Assert.Same(result, view.Result);
    }
    [Fact]
    public async Task BodyBudgetGapPreservesTheOwnedCompilerResultWithoutACompleteBodyClaim()
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; Assert.True(ReadBodies(view));
        Assert.True(view.ReadSyntheticCompilerOutputs(result.ResultDigest, CompilerOutputSnapshotMode.AvailableOutputs, 1));
        Assert.Equal(CompilerOutputSnapshotState.BodyBudgetExceeded, view.CompilerOutputSnapshot!.State); Assert.Empty(view.CompilerOutputSnapshot.Bodies);
        Assert.Equal(result.ResultDigest, view.CompilerOutputSnapshot.ResultDigest); Assert.Same(result, view.Result);
    }
    [Fact]
    public async Task CancellationWithdrawsPreviousSnapshotWithoutChangingAnyOwnedInputs()
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; Assert.True(ReadBodies(view));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.ReadSyntheticCompilerOutputs(result.ResultDigest, token: cancellation.Token));
        Assert.Null(view.CompilerOutputSnapshot); Assert.Same(result, view.Result); Assert.NotNull(view.ScopedExecutionPlan);
    }
    [Theory]
    [InlineData("reset")] [InlineData("dispose")] [InlineData("begin")] [InlineData("reload")]
    public async Task ContextReplacementWithdrawsTheViewButDoesNotPretendToEraseAlreadyReturnedBodies(string mode)
    {
        var f = await Ready(); using var view = f.View; Assert.True(ReadBodies(view)); var previous = view.CompilerOutputSnapshot!;
        Change(view, f.Request, mode); Assert.Null(view.CompilerOutputSnapshot); Assert.Equal(3, previous.Bodies.Count);
        Assert.Equal("NotAdopted", previous.Data.Value.GetProperty("retentionPolicy").GetString());
    }
    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)] [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)] [InlineData("reload", false)] [InlineData("reload", true)]
    public async Task NotificationReentryCannotPublishOrRetainAnInterruptedSnapshot(string mode, bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.CompilerOutputSnapshot is not null) != afterPublication) return;
            entered = true; Change(subject, f.Request, mode);
        };
        Assert.False(view.ReadSyntheticCompilerOutputs(result.ResultDigest, CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes));
        Assert.True(entered); Assert.Null(view.CompilerOutputSnapshot);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TheNewerReentrantSnapshotQueryOwnsThePublishedView(bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; var entered = false; ProjectCompilerOutputSnapshot? latest = null;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.CompilerOutputSnapshot is not null) != afterPublication) return;
            entered = true; Assert.True(ReadBodies(subject)); latest = subject.CompilerOutputSnapshot;
        };
        Assert.False(view.ReadSyntheticCompilerOutputs(result.ResultDigest)); Assert.True(entered); Assert.NotNull(latest); Assert.Same(latest, view.CompilerOutputSnapshot);
    }
    [Fact]
    public async Task InvalidQueryOptionsWithdrawTheOldSnapshotWithoutChangingItsCompiler()
    {
        var f = await Ready(); using var view = f.View; var result = view.Result!; Assert.True(ReadBodies(view));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ReadSyntheticCompilerOutputs(result.ResultDigest, maximumBodyBytes: 0));
        Assert.Null(view.CompilerOutputSnapshot); Assert.Same(result, view.Result);
        Assert.True(ReadBodies(view)); Assert.Throws<ArgumentOutOfRangeException>(() => view.ReadSyntheticCompilerOutputs(result.ResultDigest, (CompilerOutputSnapshotMode)999));
        Assert.Null(view.CompilerOutputSnapshot); Assert.Same(result, view.Result);
    }
    private static void Change(SyntheticWorkbenchViewModel view, CompilerHostRequest request, string mode)
    {
        if (mode == "reset") view.Reset(); if (mode == "dispose") view.Dispose();
        if (mode == "begin") view.BeginSynthetic(new(request.Input, 2)); if (mode == "reload") Assert.True(view.LoadSyntheticScopedSources());
    }
}
