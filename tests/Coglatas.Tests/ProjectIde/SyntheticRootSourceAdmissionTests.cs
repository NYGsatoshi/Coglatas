using System.ComponentModel;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticRootSourceAdmissionTests
{
    public static IEnumerable<object[]> Cases() => RootSourceAdmissionTests.ProofCases();
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RootSourceCanBeAdmittedWithoutRegionalIrAndMustPrecedePremiseReads(string text)
    {
        var (_,request,host)=DiagnosticPremiseSnapshotTests.Prepare(text);
        using var vm=new SyntheticWorkbenchViewModel(); vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await host.CompileAsync(request)));
        var result=vm.Result!; var diagnostic=vm.Problems.First(d=>d.Evidence.Kind==CompilerEvidenceKind.ModelProof);
        Assert.True(vm.SelectDiagnostic(diagnostic)); Assert.False(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest)); Assert.Null(vm.RootSource);
        Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest)); Assert.Equal("Ready",vm.RootSourceReadState); Assert.Equal(request.Input.Materialize().Source.Data,vm.RootSource!.Data);
        Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest)); Assert.Equal("RootCompilerInputObjects",vm.DiagnosticPremises!.BodyScope); Assert.Empty(vm.DiagnosticPremises.Bodies);
        Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest,DiagnosticPremiseSnapshotMode.AvailableBodies)); Assert.Equal(2,vm.DiagnosticPremises!.Bodies.Count);
        Assert.Equal(vm.Source!.Data,vm.DiagnosticPremises.Bodies[vm.Source.Digest.Value]); Assert.Same(result,vm.Result);
        Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest)); Assert.Null(vm.DiagnosticPremises);
        Assert.False(vm.LoadSyntheticScopedSources()); Assert.Null(vm.RootSource); Assert.Null(vm.DiagnosticPremises);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task WrongExpectedResultCancellationResetAndDisposalWithdrawRootAdmission(string text)
    {
        var (_,request,host)=DiagnosticPremiseSnapshotTests.Prepare(text);
        var vm=new SyntheticWorkbenchViewModel();
        try
        {
            vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await host.CompileAsync(request))); var result=vm.Result!;
            Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest));
            Assert.False(vm.LoadSyntheticRootSource(result.Input.InputDigest)); Assert.Null(vm.RootSource); Assert.Equal("Unavailable",vm.RootSourceReadState);
            using var cancel=new CancellationTokenSource(); cancel.Cancel(); var token=cancel.Token;
            Assert.Throws<OperationCanceledException>(()=>vm.LoadSyntheticRootSource(result.ResultDigest,token)); Assert.Null(vm.RootSource); Assert.Equal("Idle",vm.RootSourceReadState);
            Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest)); vm.Reset(); Assert.Null(vm.RootSource);
            request=new(request.Input,request.ClientGeneration+1,request.Snapshots);
            vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await host.CompileAsync(request))); Assert.Null(vm.RootSource);
            Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); vm.Dispose(); Assert.Null(vm.RootSource); Assert.Null(vm.DiagnosticPremises);
            Assert.Throws<ObjectDisposedException>(()=>vm.LoadSyntheticRootSource(result.ResultDigest));
        }
        finally { vm.Dispose(); }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ResetDuringRootReadAndNewerAdmissionDuringPremiseNotificationRejectOlderPublication(string text)
    {
        var (_,request,host)=DiagnosticPremiseSnapshotTests.Prepare(text);
        using var vm=new SyntheticWorkbenchViewModel(); vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await host.CompileAsync(request)));
        var expected=vm.Result!.ResultDigest;
        using(var reset=new ReadInterference(vm,expected,true))
        { Assert.False(vm.LoadSyntheticRootSource(expected)); Assert.Null(vm.RootSource); Assert.True(reset.Fired); }
        request=new(request.Input,request.ClientGeneration+1,request.Snapshots);
        vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await host.CompileAsync(request))); expected=vm.Result!.ResultDigest;
        Assert.True(vm.LoadSyntheticRootSource(expected)); Assert.True(vm.SelectDiagnostic(vm.Problems.First(d=>d.Evidence.Kind==CompilerEvidenceKind.ModelProof)));
        using(var newer=new ReadInterference(vm,expected,false))
        { Assert.False(vm.ReadSyntheticDiagnosticPremises(expected,DiagnosticPremiseSnapshotMode.AvailableBodies)); Assert.Null(vm.DiagnosticPremises); Assert.NotNull(vm.RootSource); Assert.True(newer.Fired); }
    }
    private sealed class ReadInterference : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly ContentDigest _expected;
        private readonly bool _reset;
        public bool Fired { get; private set; }
        public ReadInterference(SyntheticWorkbenchViewModel vm,ContentDigest expected,bool reset)
        { _vm=vm;_expected=expected;_reset=reset;_vm.PropertyChanged+=Changed; }
        private void Changed(object? sender,PropertyChangedEventArgs e)
        {
            if(Fired||_reset&&_vm.RootSourceReadState!="Loading")return;
            Fired=true;
            if(_reset)_vm.Reset();else Assert.True(_vm.LoadSyntheticRootSource(_expected));
        }
        public void Dispose(){_vm.PropertyChanged-=Changed;}
    }

    private sealed class CancelRootPublication : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly ContentDigest _expectedResult;
        private readonly string _boundary;
        private readonly bool _replace;
        private readonly InvalidOperationException? _publicationFailure;
        private readonly InvalidOperationException? _withdrawalFailure;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public int WithdrawalNotifications { get; private set; }
        public List<bool> LaterWithdrawalStates { get; } = [];
        public bool ReplacementPremisesRead { get; private set; }
        public ProjectSource? Replacement { get; private set; }
        public ProjectDiagnosticPremiseSnapshot? ReplacementPremises { get; private set; }
        public CancelRootPublication(SyntheticWorkbenchViewModel vm, string boundary, bool replace, InvalidOperationException? publicationFailure,
            InvalidOperationException? withdrawalFailure)
        {
            _vm = vm;
            _expectedResult = vm.Result!.ResultDigest;
            _boundary = boundary;
            _replace = replace;
            _publicationFailure = publicationFailure;
            _withdrawalFailure = withdrawalFailure;
            vm.PropertyChanged += Changed;
            vm.PropertyChanged += RecordWithdrawal;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.LoadSyntheticRootSource(_expectedResult, Token);
        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (Fired)
            {
                if (Token.IsCancellationRequested)
                {
                    WithdrawalNotifications++;
                    if (_vm.RootSource is null && _withdrawalFailure is not null) throw _withdrawalFailure;
                }
                return;
            }
            if (_vm.RootSourceReadState != _boundary) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.LoadSyntheticRootSource(_expectedResult);
                Replacement = _vm.RootSource;
                ReplacementPremisesRead = _vm.ReadSyntheticDiagnosticPremises(_expectedResult, DiagnosticPremiseSnapshotMode.AvailableBodies);
                ReplacementPremises = _vm.DiagnosticPremises;
            }
            _source.Cancel();
            if (_publicationFailure is not null) throw _publicationFailure;
        }
        private void RecordWithdrawal(object? sender, PropertyChangedEventArgs args)
        {
            if (WithdrawalNotifications > LaterWithdrawalStates.Count)
                LaterWithdrawalStates.Add(_vm.RootSource is null && _vm.RootSourceReadState == "Idle" &&
                    _vm.DiagnosticPremises is null);
        }
        public void Dispose()
        {
            _vm.PropertyChanged -= Changed;
            _vm.PropertyChanged -= RecordWithdrawal;
            _source.Dispose();
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task CancellationAtRootSourcePublicationWithdrawsOnlyItsOwnedAdmission(bool replace) =>
        VerifyPublicationOwnership(replace, null);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task ThrowingObserverWithdrawsOnlyItsCancelledAdjacentView(bool replace) =>
        VerifyPublicationOwnership(replace, new InvalidOperationException("Publication observer failed."),
            new InvalidOperationException("Withdrawal observer failed."));

    [Fact]
    public Task ThrowingAdjacentViewWithdrawalObserverPreservesCancellation() =>
        VerifyPublicationOwnership(false, null, new InvalidOperationException("Withdrawal observer failed."));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RootSourceAdmissionCancellationPreservesCallerToken(bool withdrawalThrows) =>
        VerifyPublicationOwnership(false, null, withdrawalThrows ? new InvalidOperationException("Withdrawal observer failed.") : null,
            boundary: "Loading");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThrowingRootSourceLoadingObserverWithdrawsOnlyItsOwnRead(bool replace) =>
        VerifyPublicationOwnership(replace, new InvalidOperationException("Loading observer failed."),
            new InvalidOperationException("Withdrawal observer failed."), boundary: "Loading");

    private static async Task VerifyPublicationOwnership(bool replace, InvalidOperationException? publicationFailure,
        InvalidOperationException? withdrawalFailure = null, string boundary = "Ready")
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)Cases().First()[0]);
        using var vm = new SyntheticWorkbenchViewModel(); vm.BeginSynthetic(request);
        Assert.True(vm.TryAccept(await host.CompileAsync(request)));
        var result = vm.Result!; var resultData = result.Data;
        Assert.True(vm.SelectDiagnostic(vm.Problems.First(d => d.Evidence.Kind == CompilerEvidenceKind.ModelProof)));
        Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest));
        var previous = vm.RootSource!; var previousData = previous.Data; Assert.NotEmpty(previousData.CanonicalText);
        using var cancellation = new CancelRootPublication(vm, boundary, replace, publicationFailure, withdrawalFailure); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data); Assert.Equal(previousData, previous.Data);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, vm.RootSource); Assert.Equal("Ready", vm.RootSourceReadState);
            Assert.True(cancellation.ReplacementPremisesRead); Assert.NotNull(cancellation.ReplacementPremises);
            Assert.Same(cancellation.ReplacementPremises, vm.DiagnosticPremises); Assert.NotEmpty(vm.DiagnosticPremises!.Bodies);
        }
        else { Assert.Null(vm.RootSource); Assert.Equal("Idle", vm.RootSourceReadState); Assert.Null(vm.DiagnosticPremises); }
        Assert.Equal(replace ? 0 : 1, cancellation.WithdrawalNotifications);
        bool[] expectedWithdrawal = replace ? [] : [true];
        Assert.Equal(expectedWithdrawal, cancellation.LaterWithdrawalStates);
        if (publicationFailure is null)
        {
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(withdrawalFailure, cancelled.InnerException);
        }
        else Assert.Same(publicationFailure, error);
    }

    private sealed class CancelInitialRootRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly CancellationTokenSource _source = new();
        private readonly ContentDigest _expected;
        private readonly bool _admitted;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public bool ReplacementPremisesRead { get; private set; }
        public ProjectSource? Replacement { get; private set; }
        public ProjectDiagnosticPremiseSnapshot? ReplacementPremises { get; private set; }
        public CancellationToken Token => _source.Token;
        public CancelInitialRootRead(SyntheticWorkbenchViewModel view, bool admitted)
        { _view = view; _expected = view.Result!.ResultDigest; _admitted = admitted; view.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _view.LoadSyntheticRootSource(_expected, Token);
        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (Fired || _view.RootSourceReadState != "Loading") return;
            Fired = true;
            ReplacementSucceeded = _view.LoadSyntheticRootSource(_admitted ? _expected : _view.Result!.Input.InputDigest);
            Replacement = _view.RootSource;
            if (_admitted)
            {
                ReplacementPremisesRead = _view.ReadSyntheticDiagnosticPremises(_expected, DiagnosticPremiseSnapshotMode.AvailableBodies);
                ReplacementPremises = _view.DiagnosticPremises;
            }
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialRootReadCancellationPreservesNewerAdmissionOrRefusalAndItsPremises(bool admitted)
    {
        var (_, request, host) = DiagnosticPremiseSnapshotTests.Prepare((string)Cases().First()[0]);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await host.CompileAsync(request)));
        var result = view.Result!;
        var resultData = result.Data;
        Assert.True(view.SelectDiagnostic(view.Problems.First(d => d.Evidence.Kind == CompilerEvidenceKind.ModelProof)));
        Assert.True(view.LoadSyntheticRootSource(result.ResultDigest));
        var previous = view.RootSource!;
        var previousData = previous.Data;
        Assert.NotEmpty(previousData.CanonicalText);
        using var cancellation = new CancelInitialRootRead(view, admitted);
        var error = cancellation.Read();
        Assert.True(cancellation.Fired);
        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Equal(admitted, cancellation.ReplacementSucceeded);
        Assert.Same(cancellation.Replacement, view.RootSource);
        Assert.Same(result, view.Result);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(previousData, previous.Data);
        if (admitted)
        {
            Assert.NotNull(cancellation.Replacement);
            Assert.Equal("Ready", view.RootSourceReadState);
            Assert.True(cancellation.ReplacementPremisesRead);
            Assert.NotNull(cancellation.ReplacementPremises);
            Assert.Same(cancellation.ReplacementPremises, view.DiagnosticPremises);
            Assert.NotEmpty(view.DiagnosticPremises!.Bodies);
        }
        else
        {
            Assert.Null(view.RootSource);
            Assert.Equal("Unavailable", view.RootSourceReadState);
            Assert.Null(view.DiagnosticPremises);
        }
        Assert.IsType<OperationCanceledException>(error);
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RootReadWithoutCompilerResultFinishesItsOwnedLoadingState(bool cancelWhileLoading, bool throwOnWithdrawal)
    {
        using var view = new SyntheticWorkbenchViewModel();
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        var expected = ContentDigest.Compute("coglatas.test.missing-result/1", []);
        var states = new List<string>();
        var withdrawalFailure = new InvalidOperationException("withdrawal failed");
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            states.Add(subject.RootSourceReadState);
            if (cancelWhileLoading && subject.RootSourceReadState == "Loading")
                cancel();
            if (throwOnWithdrawal && subject.RootSourceReadState == "Idle")
                throw withdrawalFailure;
        };

        var returned = false;
        OperationCanceledException? cancelled = null;
        view.PropertyChanged += observer;
        try { returned = view.LoadSyntheticRootSource(expected, token); }
        catch (OperationCanceledException error) { cancelled = error; }
        finally { view.PropertyChanged -= observer; }

        Assert.False(returned);
        Assert.Null(view.RootSource);
        Assert.Null(view.Result);
        var finalState = cancelWhileLoading ? "Idle" : "Unavailable";
        Assert.Equal(finalState, view.RootSourceReadState);
        Assert.Equal(new[] { "Loading", finalState }, states);
        if (cancelWhileLoading)
        {
            Assert.NotNull(cancelled);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(throwOnWithdrawal ? withdrawalFailure : null, cancelled.InnerException);
        }
        else
        {
            Assert.Null(cancelled);
        }
    }
}
