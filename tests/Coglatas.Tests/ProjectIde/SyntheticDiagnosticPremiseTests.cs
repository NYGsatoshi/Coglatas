using System.ComponentModel;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticDiagnosticPremiseTests
{
    public static IEnumerable<object[]> Cases() => DiagnosticPremiseSnapshotTests.Cases();
    private static async Task<SyntheticWorkbenchViewModel> Admit(CompilerHostRequest request, LocalNetCompilerHost host)
    {
        var vm = new SyntheticWorkbenchViewModel(); vm.BeginSynthetic(request);
        Assert.True(vm.TryAccept(await host.CompileAsync(request))); return vm;
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CapturedBodiesNeedExplicitCurrentSourceAndExactDiagnosticAdmission(string text)
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare(text);
        using var vm = await Admit(request, host); var result = vm.Result!; var diagnostic = vm.Problems.Single();
        Assert.False(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest)); Assert.Null(vm.DiagnosticPremises);
        Assert.True(vm.SelectDiagnostic(diagnostic)); Assert.False(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest));
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest));
        Assert.Equal(DiagnosticPremiseSnapshotState.ManifestOnly, vm.DiagnosticPremises!.State); Assert.Empty(vm.DiagnosticPremises.Bodies);
        Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));
        Assert.Same(diagnostic, vm.DiagnosticPremises!.Diagnostic); Assert.Same(result, vm.Result);
        Assert.Equal(diagnostic.Evidence.Kind == CompilerEvidenceKind.Missing ? 0 : 3, vm.DiagnosticPremises.Bodies.Count);
        Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies, 1));
        Assert.Empty(vm.DiagnosticPremises!.Bodies); Assert.Same(result, vm.Result);
        Assert.False(vm.ReadSyntheticDiagnosticPremises(result.Input.InputDigest));
        Assert.Equal(DiagnosticPremiseSnapshotState.RefusedWrongResult, vm.DiagnosticPremises!.State); Assert.Null(vm.DiagnosticPremises.Diagnostic);
    }

    [Theory]
    [InlineData("reset")] [InlineData("dispose")] [InlineData("new-input")] [InlineData("selection")]
    [InlineData("readmission")] [InlineData("foreign-copy")] [InlineData("same-selection")]
    public async Task CurrentBodiesWithdrawAcrossEveryOwnedSelectionAndInputBoundary(string action)
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)DiagnosticPremiseSnapshotTests.ProofCases().First()[0]);
        var vm = await Admit(request, host);
        try
        {
            Assert.True(vm.LoadSyntheticScopedSources()); var diagnostic = vm.Problems.Single(); Assert.True(vm.SelectDiagnostic(diagnostic));
            Assert.True(vm.ReadSyntheticDiagnosticPremises(vm.Result!.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies)); var retained = vm.DiagnosticPremises!;
            if (action == "reset") vm.Reset();
            else if (action == "dispose") vm.Dispose();
            else if (action == "new-input") vm.BeginSynthetic(new(request.Input, 8, request.Snapshots));
            else if (action == "selection") vm.Select(diagnostic.Location);
            else if (action == "readmission") Assert.True(vm.LoadSyntheticScopedSources());
            else if (action == "same-selection") Assert.True(vm.SelectDiagnostic(diagnostic));
            else {
                var other = await host.CompileAsync(request); var copy = other.Compilation!.Result.Diagnostics.Single();
                Assert.Equal(diagnostic.DiagnosticId, copy.DiagnosticId); Assert.False(vm.SelectDiagnostic(copy));
            }
            Assert.Null(vm.DiagnosticPremises); Assert.Equal(3, retained.Bodies.Count);
        }
        finally { vm.Dispose(); }
    }

    private sealed class Reentry(SyntheticWorkbenchViewModel vm, ContentDigest result, bool reset)
    {
        public bool Entered { get; private set; }
        public bool InnerSucceeded { get; private set; }
        public void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (Entered) return; Entered = true;
            if (reset) vm.Reset(); else InnerSucceeded = vm.ReadSyntheticDiagnosticPremises(result, DiagnosticPremiseSnapshotMode.AvailableBodies);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NotificationReentryCannotPublishAnOlderPremiseRead(bool reset)
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)DiagnosticPremiseSnapshotTests.ProofCases().First()[0]);
        using var vm = await Admit(request, host); Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.SelectDiagnostic(vm.Problems.Single()));
        var probe = new Reentry(vm, vm.Result!.ResultDigest, reset); vm.PropertyChanged += probe.OnChanged;
        try {
            Assert.False(vm.ReadSyntheticDiagnosticPremises(vm.Result.ResultDigest)); Assert.True(probe.Entered);
            if (reset) Assert.Null(vm.DiagnosticPremises);
            else { Assert.True(probe.InnerSucceeded); Assert.Equal(3, vm.DiagnosticPremises!.Bodies.Count); }
        }
        finally { vm.PropertyChanged -= probe.OnChanged; }
    }
    private sealed class CancelOnChange : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public CancellationToken Token => _source.Token;
        public void OnChanged(object? sender, PropertyChangedEventArgs args) => _source.Cancel();
        public void Dispose() => _source.Dispose();
    }
    [Fact]
    public async Task CancellationDuringAdmissionWithdrawsThePreviousReturnedView()
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)DiagnosticPremiseSnapshotTests.ProofCases().First()[0]);
        using var vm = await Admit(request, host); Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.SelectDiagnostic(vm.Problems.Single()));
        Assert.True(vm.ReadSyntheticDiagnosticPremises(vm.Result!.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies)); var original = vm.DiagnosticPremises!;
        using var cancellation = new CancelOnChange(); var token = cancellation.Token;
        vm.PropertyChanged += cancellation.OnChanged;
        try { Assert.Throws<OperationCanceledException>(() => vm.ReadSyntheticDiagnosticPremises(vm.Result.ResultDigest, token: token)); }
        finally { vm.PropertyChanged -= cancellation.OnChanged; }
        Assert.Null(vm.DiagnosticPremises); Assert.Equal(3, original.Bodies.Count);
    }

    private sealed class CancelOnPublication : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _replace;
        private readonly ContentDigest _expectedResult;
        private int _notifications;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectDiagnosticPremiseSnapshot? Replacement { get; private set; }
        public CancelOnPublication(SyntheticWorkbenchViewModel vm, bool replace)
        { _vm = vm; _replace = replace; _expectedResult = vm.Result!.ResultDigest; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ReadSyntheticDiagnosticPremises(_expectedResult, DiagnosticPremiseSnapshotMode.AvailableBodies, token: Token);
        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.ReadSyntheticDiagnosticPremises(_vm.Result!.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies);
                Replacement = _vm.DiagnosticPremises;
            }
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAtPremisePublicationWithdrawsOnlyItsOwnedView(bool replace)
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)DiagnosticPremiseSnapshotTests.ProofCases().First()[0]);
        using var vm = await Admit(request, host);
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.SelectDiagnostic(vm.Problems.Single()));
        var result = vm.Result!; var resultData = result.Data;
        Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));
        var previous = vm.DiagnosticPremises!; Assert.Equal(3, previous.Bodies.Count);
        using var cancellation = new CancelOnPublication(vm, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested); Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data);
        Assert.Equal(3, previous.Bodies.Count);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, vm.DiagnosticPremises); Assert.Equal(3, vm.DiagnosticPremises!.Bodies.Count);
        }
        else Assert.Null(vm.DiagnosticPremises);
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
        public ProjectDiagnosticPremiseSnapshot Initial { get; }
        public ProjectDiagnosticPremiseSnapshot? Replacement { get; private set; }
        public CancelInitialRecordedRead(SyntheticWorkbenchViewModel view, bool admitted)
        {
            _view = view; _admitted = admitted;
            _expected = view.Result!.ResultDigest; _refused = view.Result.Input.InputDigest;
            Assert.True(ReadCurrent(_expected, default));
            Initial = view.DiagnosticPremises!;
            view.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => ReadCurrent(_expected, Token);
        private bool ReadCurrent(ContentDigest expected, CancellationToken token) =>
            _view.ReadSyntheticDiagnosticPremises(expected, DiagnosticPremiseSnapshotMode.AvailableBodies, token: token);
        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (Fired || _view.DiagnosticPremises is not null) return;
            Fired = true;
            ReplacementSucceeded = ReadCurrent(_admitted ? _expected : _refused, default);
            Replacement = _view.DiagnosticPremises;
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtInitialRecordedReadNotificationReportsCancellationWithoutErasingNewerRead(bool admitted)
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)DiagnosticPremiseSnapshotTests.ProofCases().First()[0]);
        using var view = await Admit(request, host);
        Assert.True(view.SelectDiagnostic(view.Problems.Single()));
        Assert.True(view.LoadSyntheticScopedSources());
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
        Assert.Same(latest, view.DiagnosticPremises);
        if (admitted)
        {
            Assert.NotNull(latest);
            Assert.NotSame(previous, latest);
            Assert.NotEmpty(latest.Bodies);
        }
        else
        {
            Assert.NotNull(latest);
            Assert.Equal(DiagnosticPremiseSnapshotState.RefusedWrongResult, latest.State);
            Assert.Empty(latest.Bodies);
        }
        Assert.Same(result, view.Result);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(previousData, previous.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
