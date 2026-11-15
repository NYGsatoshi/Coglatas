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
}
