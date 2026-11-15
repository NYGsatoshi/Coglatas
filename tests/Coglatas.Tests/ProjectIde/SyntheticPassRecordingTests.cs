using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticPassRecordingTests
{
    public static IEnumerable<object[]> Keys() => SyntheticOperatorRecordingTests.Keys();
    private static (CoreCompilation Control, PassObservationCompilerHostRequest Request, CompilerHostProfile Profile) Input(
        string key = "fail", long generation = 7, CompilerPassObservationPolicy? policy = null)
    {
        var (control, previous, profile) = SyntheticOperatorRecordingTests.Input(key, generation);
        return (control, new(previous.Core, policy ?? new(CompilerPassObservationMode.AvailableOriginalJson)), profile);
    }
    private static async Task<PassObservationCompilerRecordedInvocation> Run(SyntheticWorkbenchViewModel vm,
        LocalPassObservationCompilerRecordingSession session, PassObservationCompilerHostRequest request,
        LocalPassObservationCompilerRecordingReceipt? origin = null)
    {
        var manifest = session.Prepare(request, Guid.NewGuid(), origin); vm.BeginPassSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(vm.TryAcceptPass(actual)); return actual;
    }
    private static bool Read(SyntheticWorkbenchViewModel vm, PassObservationCompilerRecordedInvocation actual,
        int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) => vm.ReadRecordedPasses(actual.Response.Execution!.Compilation.Result.ResultDigest,
            CompilerPassObservationSnapshotMode.AvailableOriginalJson, bytes);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task EqualGenuineOlderPairCannotSatisfyThePredeclaredIntendedRun(string key)
    {
        var (control, request, profile) = Input(key);
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var old = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var intended = session.Prepare(request, Guid.NewGuid()); vm.BeginPassSynthetic(intended, request);
        var actual = await session.ExecuteAsync(intended, request);
        Assert.Equal(old.Response.Execution!.Compilation.Result.Data, actual.Response.Execution!.Compilation.Result.Data);
        Assert.Equal(old.Receipt.ObservationRecordDigest, actual.Receipt.ObservationRecordDigest);
        Assert.True(old.Response.IsFor(request)); Assert.False(vm.TryAcceptPass(old)); Assert.Equal("Waiting", vm.State); Assert.Null(vm.Result);
        Assert.True(vm.TryAcceptPass(actual)); Assert.Same(actual.Response.Execution.Compilation.Result, vm.Result);
        Assert.Equal(control.Result.Data, vm.Result!.Data); Assert.Equal(control.LogicalOperationsConsumed, actual.Response.Execution.Compilation.LogicalOperationsConsumed);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task ExplicitRootReadExposesManifestThenOriginalWholePassBodiesWithoutSelectingADiagnostic(string key)
    {
        var (_, request, profile) = Input(key);
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var execution = actual.Response.Execution!; var result = execution.Compilation.Result;
        Assert.Null(vm.PassReceiptMetadata); Assert.Null(vm.PassRecordMetadata); Assert.Null(vm.RecordedPasses);
        Assert.False(vm.ReadPassRecordingMetadata()); Assert.False(vm.ReadRecordedPasses(result.ResultDigest));
        Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest)); Assert.True(vm.ReadPassRecordingMetadata());
        Assert.Same(actual.Receipt.Data, vm.PassReceiptMetadata); Assert.Same(execution.Observation.Data, vm.PassRecordMetadata);
        Assert.Null(vm.SelectedDiagnostic); Assert.True(vm.ReadRecordedPasses(result.ResultDigest));
        Assert.Equal(CompilerPassObservationSnapshotState.ManifestOnly, vm.RecordedPasses!.State); Assert.Empty(vm.RecordedPasses.Bodies);
        Assert.True(Read(vm, actual)); var retained = vm.RecordedPasses!;
        var direct = ProjectCompilerPassObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
            execution.Compilation.Input.InputDigest, result.ResultDigest, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
        Assert.Equal(direct.Data, retained.Data); Assert.Equal(direct.Digest, retained.Digest); Assert.NotEmpty(retained.Bodies);
        foreach (var body in retained.Bodies) Assert.Same(direct.Bodies[body.Key], body.Value);
        foreach (var diagnostic in vm.Problems) { Assert.True(vm.SelectDiagnostic(diagnostic)); Assert.Same(retained, vm.RecordedPasses); }
        Assert.True(Read(vm, actual, 1)); Assert.Empty(vm.RecordedPasses!.Bodies);
        Assert.Equal(CompilerPassObservationSnapshotState.BodyBudgetExceeded, vm.RecordedPasses.State);
        Assert.Equal(retained.OriginalCaptureState, vm.RecordedPasses.OriginalCaptureState);
        Assert.Equal(result.Data, vm.Result!.Data); Assert.Null(vm.OperatorReceiptMetadata); Assert.Null(vm.PredicateReceiptMetadata);
        Assert.True(vm.ReadPassRecordingMetadata()); Assert.Null(vm.RecordedPasses); Assert.NotEmpty(retained.Bodies);
    }

    [Fact]
    public async Task ForeignRequestSessionAndExistingHostProtocolsCannotCrossThePassIntent()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var foreign = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginPassSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request);
        var foreignPair = await foreign.ExecuteAsync(foreign.Prepare(request, Guid.NewGuid()), request);
        Assert.False(vm.TryAcceptPass(foreignPair));
        var equal = new PassObservationCompilerHostRequest(request.Core, request.ObservationPolicy);
        using var equalVm = new SyntheticWorkbenchViewModel();
        Assert.Equal(equal.Data, request.Data); Assert.Throws<ArgumentException>(() => equalVm.BeginPassSynthetic(manifest, equal));
        Assert.Equal("Empty", equalVm.State);
        var ordinary = await new LocalNetCompilerHost(profile).CompileAsync(request.Core);
        Assert.False(vm.TryAccept(ordinary)); Assert.True(vm.TryAcceptPass(actual));
        using var ordinaryVm = new SyntheticWorkbenchViewModel(); ordinaryVm.BeginSynthetic(request.Core);
        Assert.False(ordinaryVm.TryAcceptPass(actual)); Assert.True(ordinaryVm.TryAccept(ordinary)); Assert.Null(ordinaryVm.PassReceiptMetadata);
        var operators = new PredicateObservationCompilerHostRequest(request.Core, new(PredicateEvaluationCaptureMode.ObservedOperators));
        using var operatorSession = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var operatorVm = new SyntheticWorkbenchViewModel();
        var operatorManifest = operatorSession.Prepare(operators, Guid.NewGuid()); operatorVm.BeginOperatorSynthetic(operatorManifest, operators);
        var operatorPair = await operatorSession.ExecuteAsync(operatorManifest, operators);
        Assert.False(operatorVm.TryAcceptPass(actual)); Assert.True(operatorVm.TryAcceptOperator(operatorPair));
        Assert.False(vm.TryAcceptOperator(operatorPair)); Assert.Null(operatorVm.PassReceiptMetadata);
    }

    [Theory]
    [InlineData("manifest")] [InlineData("bytes")] [InlineData("events")]
    public async Task ANewDiagnosticRunCannotFillTheOriginalCaptureGap(string kind)
    {
        var policy = kind switch { "manifest" => new CompilerPassObservationPolicy(),
            "bytes" => new(CompilerPassObservationMode.AvailableOriginalJson, maximumBodyBytes: 1),
            _ => new(CompilerPassObservationMode.AvailableOriginalJson, maximumEvents: 1) };
        var (_, request, profile) = Input(policy: policy);
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); Assert.True(Read(vm, actual));
        var original = vm.RecordedPasses!; var metadata = actual.Receipt.ObservationMetadata; Assert.Empty(original.Bodies);
        Assert.Equal(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, original.State);
        var (_, diagnostic, _) = Input(generation: 8); var rerun = await Run(vm, session, diagnostic, actual.Receipt);
        Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); Assert.True(Read(vm, rerun)); Assert.NotEmpty(vm.RecordedPasses!.Bodies);
        Assert.NotEqual(actual.Receipt.Manifest.RunId, rerun.Receipt.Manifest.RunId); Assert.NotEqual(actual.Response.AttemptId, rerun.Response.AttemptId);
        Assert.Same(metadata, actual.Receipt.ObservationMetadata); Assert.Empty(original.Bodies);
        Assert.Same(metadata, original.OriginalRecordMetadata); Assert.Equal(actual.Response.Execution!.Compilation.Result.Data, vm.Result.Data);
    }

    [Theory]
    [InlineData("metadata", 1, "reset")] [InlineData("metadata", 2, "reset")]
    [InlineData("metadata", 1, "dispose")] [InlineData("metadata", 2, "dispose")]
    [InlineData("metadata", 1, "source")] [InlineData("metadata", 2, "source")]
    [InlineData("metadata", 1, "context")] [InlineData("metadata", 2, "context")]
    [InlineData("snapshot", 1, "reset")] [InlineData("snapshot", 2, "reset")]
    [InlineData("snapshot", 1, "dispose")] [InlineData("snapshot", 2, "dispose")]
    [InlineData("snapshot", 1, "source")] [InlineData("snapshot", 2, "source")]
    [InlineData("snapshot", 1, "context")] [InlineData("snapshot", 2, "context")]
    public async Task NotificationsCannotPublishOldFramesAcrossLifecycleOrRootReplacement(string read, int notification, string action)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var digest = vm.Result!.ResultDigest;
        Assert.True(vm.LoadSyntheticRootSource(digest)); Assert.True(Read(vm, actual)); var retained = vm.RecordedPasses!;
        var next = new PassObservationCompilerHostRequest(new(request.Core.Input, 8), request.ObservationPolicy);
        var manifest = session.Prepare(next, Guid.NewGuid(), actual.Receipt); var count = 0; var fired = false;
        vm.PropertyChanged += (sender, _) =>
        {
            if (fired || ++count != notification) return;
            fired = true; var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            switch (action) { case "reset": current.Reset(); break; case "dispose": current.Dispose(); break;
                case "source": Assert.True(current.LoadSyntheticRootSource(digest)); break; default: current.BeginPassSynthetic(manifest, next); break; }
        };
        Assert.False(read == "metadata" ? vm.ReadPassRecordingMetadata() : Read(vm, actual));
        Assert.True(fired); Assert.Null(vm.PassReceiptMetadata); Assert.Null(vm.PassRecordMetadata); Assert.Null(vm.RecordedPasses); Assert.NotEmpty(retained.Bodies);
    }

    [Theory]
    [InlineData("Empty")] [InlineData("Waiting")] [InlineData("Ready")]
    public async Task BeginAndAcceptNotificationsCannotReviveResetLifecycle(string trigger)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); var fired = false;
        vm.PropertyChanged += (sender, _) => { var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (!fired && current.State == trigger) { fired = true; current.Reset(); } };
        vm.BeginPassSynthetic(manifest, request); var actual = await session.ExecuteAsync(manifest, request);
        Assert.False(vm.TryAcceptPass(actual)); Assert.True(fired); Assert.Equal("Empty", vm.State); Assert.Null(vm.Result); Assert.Null(vm.RecordedPasses);
    }

    [Fact]
    public async Task WaitingNotificationAlreadyHasTheExactIntendedPassContext()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); var actual = await session.ExecuteAsync(manifest, request); var fired = false;
        vm.PropertyChanged += (sender, _) => { var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (!fired && current.State == "Waiting") { fired = true; Assert.True(current.TryAcceptPass(actual)); } };
        vm.BeginPassSynthetic(manifest, request); Assert.True(fired); Assert.Equal("Ready", vm.State); Assert.Same(actual.Response.Execution!.Compilation.Result, vm.Result);
    }

    [Fact]
    public async Task WrongResultCancelledReadAndSourceReloadWithdrawViewsButPreserveCallerOriginals()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var execution = actual.Response.Execution!;
        Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); Assert.True(vm.ReadPassRecordingMetadata()); Assert.True(Read(vm, actual)); var retained = vm.RecordedPasses!;
        Assert.False(vm.ReadRecordedPasses(execution.Compilation.Input.InputDigest)); Assert.Null(vm.RecordedPasses); Assert.True(Read(vm, actual));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel(); Assert.Throws<OperationCanceledException>(() => vm.ReadRecordedPasses(execution.Compilation.Result.ResultDigest,
            CompilerPassObservationSnapshotMode.AvailableOriginalJson, token: cancelled.Token)); Assert.Null(vm.RecordedPasses);
        Assert.True(Read(vm, actual)); Assert.False(vm.LoadSyntheticRootSource(execution.Compilation.Input.InputDigest));
        Assert.Null(vm.PassReceiptMetadata); Assert.Null(vm.RecordedPasses); Assert.NotEmpty(retained.Bodies);
        Assert.True(vm.LoadSyntheticRootSource(execution.Compilation.Result.ResultDigest)); Assert.True(Read(vm, actual));
        Assert.Throws<ArgumentException>(() => vm.BeginPassSynthetic(actual.Receipt.Manifest, request)); Assert.NotNull(vm.RecordedPasses);
        vm.Reset(); Assert.Null(vm.RecordedPasses); Assert.False(vm.TryAcceptPass(actual)); Assert.NotEmpty(retained.Bodies);
        vm.Dispose(); Assert.Throws<ObjectDisposedException>(() => vm.ReadPassRecordingMetadata()); Assert.Null(vm.PassRecordMetadata);
    }

    [Fact]
    public async Task ResourceExhaustedCompilerAndCancelledHostRemainIndependentOfAvailablePassFrames()
    {
        var (control, _, profile) = Input(); var settings = JsonNode.Parse(control.Input.Settings.CanonicalText)!;
        settings["executionProfile"]!["maximumOperations"] = 1;
        var input = new CompilerInputManifest(control.Input.Source, SourceJson.Parse(settings.ToJsonString()));
        var request = new PassObservationCompilerHostRequest(new(new(input), 7), new(CompilerPassObservationMode.AvailableOriginalJson));
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Assert.Equal(CompilerHostStatus.Completed, actual.Response.Status);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, vm.Result!.Execution); Assert.False(vm.HasCompleteLocalPass);
        Assert.True(vm.LoadSyntheticRootSource(vm.Result.ResultDigest)); Assert.True(vm.ReadPassRecordingMetadata()); Assert.True(Read(vm, actual));
        Assert.Same(actual.Receipt.ObservationMetadata, vm.RecordedPasses!.OriginalRecordMetadata);
        var (_, next, _) = Input(generation: 8); var manifest = session.Prepare(next, Guid.NewGuid()); vm.BeginPassSynthetic(manifest, next);
        using var token = new CancellationTokenSource();
        token.Cancel(); var cancelled = await session.ExecuteAsync(manifest, next, token.Token);
        Assert.False(vm.TryAcceptPass(cancelled)); Assert.Equal("Cancelled", vm.State); Assert.Null(vm.Result); Assert.Null(vm.RecordedPasses); Assert.Null(vm.PassReceiptMetadata);
        var (_, retry, _) = Input(generation: 9); var retried = await Run(vm, session, retry, cancelled.Receipt);
        Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); Assert.True(Read(vm, retried)); Assert.NotEmpty(vm.RecordedPasses!.Bodies);
        Assert.Null(cancelled.Response.Execution); Assert.Null(cancelled.Receipt.ObservationMetadata); Assert.NotEqual(cancelled.Response.AttemptId, retried.Response.AttemptId);
    }
}
