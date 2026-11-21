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

    private sealed class CancelPublishedRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly ContentDigest _expectedResult;
        private readonly bool _replace;
        private int _notifications;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectCompilerOutputSnapshot? Replacement { get; private set; }
        public CancelPublishedRead(SyntheticWorkbenchViewModel vm, bool replace)
        { _vm = vm; _expectedResult = vm.Result!.ResultDigest; _replace = replace; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ReadSyntheticCompilerOutputs(_expectedResult, CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.ReadSyntheticCompilerOutputs(_expectedResult, CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
                Replacement = _vm.CompilerOutputSnapshot;
            }
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAtAdjacentSnapshotPublicationWithdrawsOnlyItsOwnedView(bool replace)
    {
        var f = await Ready();
        using var vm = f.View;
        var result = vm.Result!; var resultData = result.Data; var plan = vm.ScopedExecutionPlan; Assert.NotNull(plan);
        Assert.True(ReadBodies(vm));
        var previous = vm.CompilerOutputSnapshot!; var previousData = previous.Data; Assert.NotEmpty(previous.Bodies);
        using var cancellation = new CancelPublishedRead(vm, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data); Assert.Same(plan, vm.ScopedExecutionPlan);
        Assert.Equal(previousData, previous.Data); Assert.NotEmpty(previous.Bodies);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, vm.CompilerOutputSnapshot); Assert.NotEmpty(vm.CompilerOutputSnapshot!.Bodies);
        }
        else Assert.Null(vm.CompilerOutputSnapshot);
        Assert.IsType<OperationCanceledException>(error);
    }

    private sealed class CancelInitialRecordedRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _admitted;
        private readonly ContentDigest _expected;
        private readonly ContentDigest _refused;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectCompilerOutputSnapshot Initial { get; }
        public ProjectCompilerOutputSnapshot? Replacement { get; private set; }
        public CancelInitialRecordedRead(SyntheticWorkbenchViewModel view, bool admitted)
        {
            _view = view; _admitted = admitted;
            _expected = view.Result!.ResultDigest; _refused = view.Result.Input.InputDigest;
            Assert.True(ReadCurrent(_expected, default));
            Initial = view.CompilerOutputSnapshot!;
            view.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => ReadCurrent(_expected, Token);
        private bool ReadCurrent(ContentDigest expected, CancellationToken token) =>
            _view.ReadSyntheticCompilerOutputs(expected, CompilerOutputSnapshotMode.AvailableOutputs, token: token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || _view.CompilerOutputSnapshot is not null) return;
            Fired = true;
            ReplacementSucceeded = ReadCurrent(_admitted ? _expected : _refused, default);
            Replacement = _view.CompilerOutputSnapshot;
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtInitialRecordedReadNotificationReportsCancellationWithoutErasingNewerRead(bool admitted)
    {
        var f = await Ready();
        using var view = f.View;
        var result = view.Result!;
        var resultData = result.Data;
        using var cancellation = new CancelInitialRecordedRead(view, admitted);
        var previous = cancellation.Initial;
        var previousData = previous.Data;
        Assert.NotEmpty(previous.Bodies);
        var error = cancellation.Read();
        Assert.True(cancellation.Fired);
        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Equal(admitted, cancellation.ReplacementSucceeded);
        var latest = cancellation.Replacement;
        Assert.Same(latest, view.CompilerOutputSnapshot);
        if (admitted)
        {
            Assert.NotNull(latest);
            Assert.NotSame(previous, latest);
            Assert.NotEmpty(latest.Bodies);
        }
        else
        {
            Assert.NotNull(latest);
            Assert.Equal(CompilerOutputSnapshotState.RefusedWrongResult, latest.State);
            Assert.Empty(latest.Bodies);
        }
        Assert.Same(result, view.Result);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(previousData, previous.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
