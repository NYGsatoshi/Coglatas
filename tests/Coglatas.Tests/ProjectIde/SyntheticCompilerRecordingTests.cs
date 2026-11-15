using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticCompilerRecordingTests
{
    public static IEnumerable<object[]> Cases() => LocalCompilerRecordingTests.Cases();
    private static (CompilerInputManifest Input, CompilerHostRequest Request, SourceJson Expected) Case(string key)
    {
        var text = WebCompilerOutputSnapshotContractTests.Cases().Select(row => (string)row[0]).Single(text => {
            using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("key").GetString() == key;
        });
        using var data = JsonDocument.Parse(text); var row = data.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        return (input, new(new(input), 7, WebPreviewContractTests.Snapshots(row, input)), SourceJson.Parse(row.GetProperty("result").GetRawText()));
    }
    private static (CompilerInputManifest Input, CompilerHostRequest Request) Probe()
    {
        var (input, request, _) = Case((string)Cases().First()[0]); return (input, request);
    }
    private static CompilerHostProfile Profile(CompilerInputManifest input)
    {
        CompilerArtifactIdentity At(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        return new(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
    }
    private static SyntheticWorkbenchViewModel Admit(CompilerHostRequest request, LocalCompilerRecordedInvocation invocation)
    {
        var vm = new SyntheticWorkbenchViewModel(); vm.BeginSynthetic(request);
        Assert.True(vm.TryAccept(invocation.Response)); Assert.True(vm.LoadSyntheticScopedSources()); return vm;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalInvocationAndCaptureModesRemainDistinctFromTheAdmittedCompilerTruth(string key)
    {
        var (input, request, expected) = Case(key);
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var original = await LocalCompilerRecordedInvocation.ExecuteAsync(session, session.Prepare(request, Guid.NewGuid()), request);
        using var vm = Admit(request, original);
        Assert.True(vm.ReadSyntheticCompilerRecording(original)); Assert.Same(original.Receipt, vm.CompilerRecordingReceipt);
        Assert.Equal(expected, vm.Result!.Data); Assert.Equal(original.Response.AttemptId, original.Receipt.AttemptId);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, original.Receipt.Snapshot!.State); Assert.Empty(original.Receipt.Snapshot.Bodies);
        Assert.True(vm.ReadSyntheticCompilerOutputs(vm.Result.ResultDigest, CompilerOutputSnapshotMode.AvailableOutputs));
        Assert.Same(original.Receipt, vm.CompilerRecordingReceipt); Assert.Empty(original.Receipt.Snapshot.Bodies);
        var diagnostic = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, diagnosticOrigin: original.Receipt), request);
        Assert.Equal(expected, diagnostic.Response.Compilation!.Result.Data); Assert.NotEqual(original.Response.AttemptId, diagnostic.Response.AttemptId);
        Assert.False(vm.ReadSyntheticCompilerRecording(diagnostic)); Assert.Null(vm.CompilerRecordingReceipt);
        using var diagnosticVm = Admit(request, diagnostic);
        Assert.True(diagnosticVm.ReadSyntheticCompilerRecording(diagnostic)); Assert.Equal(3, diagnosticVm.CompilerRecordingReceipt!.Snapshot!.Bodies.Count);
        Assert.Equal(original.Response.AttemptId.ToString(), diagnostic.Receipt.Manifest.Data.Value.GetProperty("origin").GetProperty("attemptId").GetString());
        var limited = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, 1, original.Receipt), request);
        using var limitedVm = Admit(request, limited);
        Assert.True(limitedVm.ReadSyntheticCompilerRecording(limited));
        Assert.Equal(CompilerOutputSnapshotState.BodyBudgetExceeded, limitedVm.CompilerRecordingReceipt!.Snapshot!.State);
        Assert.Empty(limitedVm.CompilerRecordingReceipt.Snapshot.Bodies); Assert.Equal(expected, limitedVm.Result!.Data);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ScopeReloadAndCopiedInvocationMetadataCannotResurrectOrMisassociateARecording(string key)
    {
        var (input, request, _) = Case(key);
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var actual = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs), request);
        var vm = new SyntheticWorkbenchViewModel();
        vm.BeginSynthetic(request);
        Assert.False(vm.ReadSyntheticCompilerRecording(actual)); Assert.True(vm.TryAccept(actual.Response));
        Assert.False(vm.ReadSyntheticCompilerRecording(actual)); Assert.True(vm.LoadSyntheticScopedSources());
        Assert.True(vm.ReadSyntheticCompilerRecording(actual)); var callerOwned = vm.CompilerRecordingReceipt;
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.Null(vm.CompilerRecordingReceipt);
        Assert.True(vm.ReadSyntheticCompilerRecording(actual)); vm.Reset(); Assert.Null(vm.CompilerRecordingReceipt);
        var nextRequest = new CompilerHostRequest(new(input), 8, request.Snapshots);
        vm.BeginSynthetic(nextRequest); Assert.False(vm.TryAccept(actual.Response)); Assert.False(vm.ReadSyntheticCompilerRecording(actual));
        var next = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(nextRequest, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, diagnosticOrigin: actual.Receipt), nextRequest);
        // A public response can copy metadata while retaining a different compilation with the same deterministic digests.
        var copied = next.Response with { Execution = actual.Response.Execution };
        Assert.Equal(actual.Response.Compilation!.Result.ResultDigest, next.Response.Compilation!.Result.ResultDigest);
        Assert.True(vm.TryAccept(copied)); Assert.True(vm.LoadSyntheticScopedSources());
        Assert.False(vm.ReadSyntheticCompilerRecording(next)); Assert.False(vm.ReadSyntheticCompilerRecording(actual)); Assert.Null(vm.CompilerRecordingReceipt);
        Assert.Equal(actual.Response.Execution!.Digest, next.Response.Execution!.Digest);
        Assert.NotSame(copied.Compilation, next.Response.Compilation);
        Assert.Equal(3, callerOwned!.Snapshot!.Bodies.Count); // Withdrawal is not physical deletion of caller-owned data.
        vm.Dispose(); Assert.Null(vm.CompilerRecordingReceipt);
        Assert.Throws<ObjectDisposedException>(() => vm.ReadSyntheticCompilerRecording(next));
    }

    private sealed class ReentryProbe(SyntheticWorkbenchViewModel vm, LocalCompilerRecordedInvocation invocation, bool reset)
    {
        public bool Entered { get; private set; }
        public bool InnerSucceeded { get; private set; }
        public void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (Entered) return; Entered = true;
            if (reset) vm.Reset(); else InnerSucceeded = vm.ReadSyntheticCompilerRecording(invocation);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotificationReentryKeepsOnlyTheNewestOwnedRecording(bool reset)
    {
        var (input, request) = Probe();
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var actual = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs), request);
        using var vm = Admit(request, actual);
        var probe = new ReentryProbe(vm, actual, reset);
        vm.PropertyChanged += probe.OnChanged;
        try {
            Assert.False(vm.ReadSyntheticCompilerRecording(actual)); Assert.True(probe.Entered);
            if (reset) Assert.Null(vm.CompilerRecordingReceipt);
            else { Assert.True(probe.InnerSucceeded); Assert.Same(actual.Receipt, vm.CompilerRecordingReceipt); }
        }
        finally { vm.PropertyChanged -= probe.OnChanged; }
    }

    [Fact]
    public async Task HostOnlyCancellationNeverBecomesAnAdmittedCompiledRecording()
    {
        var (input, request) = Probe();
        using var session = new LocalCompilerRecordingSession(Profile(input));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var actual = await LocalCompilerRecordedInvocation.ExecuteAsync(session, session.Prepare(request, Guid.NewGuid()), request, cancellation.Token);
        using var vm = new SyntheticWorkbenchViewModel();
        vm.BeginSynthetic(request);
        Assert.Equal(CompilerHostStatus.Cancelled, actual.Response.Status); Assert.Null(actual.Response.Compilation); Assert.Null(actual.Receipt.Snapshot);
        Assert.False(vm.TryAccept(actual.Response)); Assert.False(vm.ReadSyntheticCompilerRecording(actual)); Assert.Null(vm.CompilerRecordingReceipt);
    }
}
