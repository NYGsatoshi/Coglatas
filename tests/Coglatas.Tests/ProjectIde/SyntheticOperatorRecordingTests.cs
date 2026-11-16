using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticOperatorRecordingTests
{
    internal static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend/public/assets/projectide/operator-recordings-v1.json"))) directory = directory.Parent;
        return directory is null ? throw new InvalidOperationException("The actual Product operator fixture is required.") :
            Path.Combine(directory.FullName, "frontend/public/assets/projectide/operator-recordings-v1.json");
    }
    public static IEnumerable<object[]> Keys() => new[] { "fail", "pass", "unknown", "test-assertion-navigation", "operator-and-short-circuit",
        "operator-nested-duplicates", "operator-or-unknown", "operator-not-pass", "operator-requirement-false-nested", "operator-requirement-true-short-circuit", "operator-requirement-unknown" }.Select(key => new object[] { key });
    internal static (CoreCompilation Control, PredicateObservationCompilerHostRequest Request, CompilerHostProfile Profile) Input(string key = "fail",
        long generation = 7, CompilerPredicateObservationPolicy? policy = null)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(FixturePath()));
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var control = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(SourceJson.Parse(row.GetProperty("result").GetRawText()), control.Result.Data);
        return (control, new(new(new(input), generation), policy ?? new(PredicateEvaluationCaptureMode.ObservedOperators)), PredicateCompilerHostTests.Profile(input));
    }
    private static async Task<PredicateObservationCompilerRecordedInvocation> Run(SyntheticWorkbenchViewModel vm,
        LocalPredicateObservationCompilerRecordingSession session, PredicateObservationCompilerHostRequest request,
        LocalPredicateObservationCompilerRecordingReceipt? origin = null)
    {
        var manifest = session.Prepare(request, Guid.NewGuid(), origin); vm.BeginOperatorSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(vm.TryAcceptOperator(actual)); return actual;
    }
    private static void Select(SyntheticWorkbenchViewModel vm, PredicateObservationCompilerRecordedInvocation actual)
    {
        Assert.True(vm.LoadSyntheticRootSource(actual.Response.Execution!.Compilation.Result.ResultDigest));
        Assert.True(vm.SelectDiagnostic(actual.Response.Execution.Compilation.Result.Diagnostics.First()));
    }
    private static bool Read(SyntheticWorkbenchViewModel vm, PredicateObservationCompilerRecordedInvocation actual,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) => vm.ReadRecordedOperators(actual.Response.Execution!.Compilation.Result.ResultDigest,
            CompilerPredicateObservationSnapshotMode.AvailableOperators, maximumBodyBytes);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task ExactIntendedManifestRefusesWholeGenuineOlderPairWithEqualRequestResultAndObservation(string key)
    {
        var (control, request, profile) = Input(key);
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var old = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var intended = session.Prepare(request, Guid.NewGuid()); vm.BeginOperatorSynthetic(intended, request);
        var actual = await session.ExecuteAsync(intended, request);
        Assert.Equal(old.Response.Execution!.Compilation.Result.ResultDigest, actual.Response.Execution!.Compilation.Result.ResultDigest);
        Assert.Equal(old.Receipt.ObservationRecordDigest, actual.Receipt.ObservationRecordDigest);
        Assert.True(old.Response.IsFor(request)); Assert.False(vm.TryAcceptOperator(old)); Assert.Equal("Waiting", vm.State); Assert.Null(vm.Result);
        Assert.True(vm.TryAcceptOperator(actual)); Assert.Same(actual.Response.Execution.Compilation.Result, vm.Result);
        Assert.Equal(control.Result.Data, vm.Result!.Data); Assert.Equal(control.LogicalOperationsConsumed, actual.Response.Execution.Compilation.LogicalOperationsConsumed);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task ExplicitRootSourceAndActualSelectedDiagnosticGateManifestAndOriginalOperatorFrames(string key)
    {
        var (_, request, profile) = Input(key);
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var execution = actual.Response.Execution!; var result = execution.Compilation.Result;
        Assert.Null(vm.OperatorReceiptMetadata); Assert.Null(vm.OperatorRecordMetadata); Assert.Null(vm.RecordedOperators);
        Assert.False(vm.ReadOperatorRecordingMetadata()); Assert.False(vm.ReadRecordedOperators(result.ResultDigest));
        Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest)); Assert.True(vm.ReadOperatorRecordingMetadata());
        Assert.Same(actual.Receipt.Data, vm.OperatorReceiptMetadata); Assert.Same(actual.Receipt.ObservationMetadata, vm.OperatorRecordMetadata);
        Assert.False(vm.ReadRecordedOperators(result.ResultDigest)); Assert.Null(vm.RecordedOperators);
        Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises);
        foreach (var diagnostic in result.Diagnostics)
        {
            Assert.True(vm.SelectDiagnostic(diagnostic)); Assert.True(vm.ReadRecordedOperators(result.ResultDigest));
            Assert.Equal(CompilerPredicateObservationSnapshotMode.Manifest, vm.RecordedOperators!.Mode);
            Assert.Empty(vm.RecordedOperators.Bodies); Assert.Empty(vm.RecordedOperators.EvaluationFrames);
            Assert.True(Read(vm, actual)); var snapshot = vm.RecordedOperators!;
            var direct = ProjectCompilerPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation, diagnostic,
                CompilerPredicateObservationSnapshotMode.AvailableOperators);
            Assert.Equal(direct.Data, snapshot.Data); Assert.Equal(direct.Digest, snapshot.Digest);
            foreach (var body in snapshot.Bodies) Assert.Same(direct.Bodies[body.Key], body.Value);
            foreach (var frame in snapshot.EvaluationFrames) Assert.Same(direct.EvaluationFrames[frame.Key], frame.Value);
            Assert.Equal(key switch { "fail" or "unknown" => 1, "operator-and-short-circuit" => 2, "operator-nested-duplicates" => 6,
                "operator-or-unknown" => 3, "operator-requirement-false-nested" when diagnostic.Code == "coglatas.analysis.requirement-condition-false" => 6,
                _ => 0 }, snapshot.Bodies.Count);
            if (key.StartsWith("operator-requirement-", StringComparison.Ordinal))
            {
                Assert.False(result.EvidenceComplete);
                Assert.Equal(diagnostic.Code == "coglatas.analysis.requirement-condition-false" ? 1 : 0, snapshot.EvaluationFrames.Count);
                if (diagnostic.Code == "coglatas.analysis.requirement-evidence-unavailable")
                    Assert.Equal(CompilerPredicateObservationSnapshotState.NoObservedPublishedDiagnostic, snapshot.State);
            }
            Assert.True(Read(vm, actual, 1)); Assert.Empty(vm.RecordedOperators!.Bodies); Assert.Empty(vm.RecordedOperators.EvaluationFrames);
            Assert.Equal(snapshot.OriginalCaptureState, vm.RecordedOperators.OriginalCaptureState);
            if (snapshot.Bodies.Count != 0) Assert.Equal(CompilerPredicateObservationSnapshotState.BodyBudgetExceeded, vm.RecordedOperators.State);
            Assert.Equal(result.Data, vm.Result!.Data);
            Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));
            Assert.Null(vm.RecordedPredicatePremises);
        }
    }

    [Fact]
    public async Task EqualRequestForeignSessionAndOldProtocolsCannotCrossTheExactOperatorIntent()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginOperatorSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request);
        var ordinary = await new LocalNetCompilerHost(profile).CompileAsync(request.Core);
        var equal = new PredicateObservationCompilerHostRequest(request.Core, request.ObservationPolicy);
        using var otherVm = new SyntheticWorkbenchViewModel();
        Assert.Throws<ArgumentException>(() => otherVm.BeginOperatorSynthetic(manifest, equal)); Assert.Equal("Empty", otherVm.State);
        Assert.False(vm.TryAccept(ordinary)); Assert.Equal("Waiting", vm.State);
        using var foreign = new LocalPredicateObservationCompilerRecordingSession(profile);
        var foreignPair = await foreign.ExecuteAsync(foreign.Prepare(request, Guid.NewGuid()), request);
        Assert.False(vm.TryAcceptOperator(foreignPair)); Assert.True(vm.TryAcceptOperator(actual));
        using var oldVm = new SyntheticWorkbenchViewModel(); oldVm.BeginSynthetic(request.Core);
        Assert.False(oldVm.TryAcceptOperator(actual)); Assert.True(oldVm.TryAccept(ordinary));
        var arrays = new PredicateCompilerHostRequest(request.Core, new(CompilerPredicateCaptureMode.OriginalRootArrays));
        using var arraySession = new LocalPredicateCompilerRecordingSession(profile);
        using var arrayVm = new SyntheticWorkbenchViewModel();
        var arrayManifest = arraySession.Prepare(arrays, Guid.NewGuid()); arrayVm.BeginPredicateSynthetic(arrayManifest, arrays);
        var arrayPair = await arraySession.ExecuteAsync(arrayManifest, arrays);
        Assert.False(arrayVm.TryAcceptOperator(actual)); Assert.True(arrayVm.TryAcceptPredicate(arrayPair)); Assert.Null(arrayVm.OperatorReceiptMetadata);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task MetadataNotificationsCannotPublishAcrossResetBeforeOrAfterTheRead(int notification)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Assert.True(vm.LoadSyntheticRootSource(actual.Response.Execution!.Compilation.Result.ResultDigest));
        var events = 0; vm.PropertyChanged += (sender, _) => { if (++events == notification) Assert.IsType<SyntheticWorkbenchViewModel>(sender).Reset(); };
        Assert.False(vm.ReadOperatorRecordingMetadata()); Assert.Null(vm.OperatorReceiptMetadata); Assert.Null(vm.OperatorRecordMetadata); Assert.Null(vm.RecordedOperators);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task SelectedOperatorNotificationsCannotPublishAcrossSelectionChanges(int notification)
    {
        var (_, request, profile) = Input("operator-nested-duplicates");
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual);
        var events = 0; var diagnostic = vm.SelectedDiagnostic!;
        vm.PropertyChanged += (sender, _) => { if (++events == notification) Assert.True(Assert.IsType<SyntheticWorkbenchViewModel>(sender).SelectDiagnostic(diagnostic)); };
        Assert.False(Read(vm, actual)); Assert.Null(vm.RecordedOperators); Assert.Same(diagnostic, vm.SelectedDiagnostic);
    }

    [Theory]
    [InlineData("Empty")] [InlineData("Waiting")] [InlineData("Ready")]
    public async Task BeginAndAcceptanceNotificationsCannotReviveAResetLifecycle(string trigger)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); var reset = false;
        vm.PropertyChanged += (sender, _) => { var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender); if (!reset && current.State == trigger) { reset = true; current.Reset(); } };
        vm.BeginOperatorSynthetic(manifest, request); var actual = await session.ExecuteAsync(manifest, request);
        Assert.False(vm.TryAcceptOperator(actual)); Assert.True(reset); Assert.Equal("Empty", vm.State);
        Assert.Null(vm.Result); Assert.Null(vm.OperatorReceiptMetadata); Assert.Null(vm.RecordedOperators);
    }

    [Fact]
    public async Task WrongResultCancellationSourceReloadAndForeignFindingWithdrawCurrentViewsWithoutErasingCallerFrames()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual); Assert.True(vm.ReadOperatorRecordingMetadata()); Assert.True(Read(vm, actual));
        var retained = vm.RecordedOperators!; var execution = actual.Response.Execution!;
        Assert.False(vm.ReadRecordedOperators(execution.Compilation.Input.InputDigest, CompilerPredicateObservationSnapshotMode.AvailableOperators)); Assert.Null(vm.RecordedOperators);
        Assert.True(Read(vm, actual));
        using var source = new CancellationTokenSource();
        source.Cancel(); Assert.Throws<OperationCanceledException>(() => vm.ReadRecordedOperators(execution.Compilation.Result.ResultDigest,
            CompilerPredicateObservationSnapshotMode.AvailableOperators, token: source.Token));
        Assert.Null(vm.RecordedOperators); Assert.Single(retained.Bodies);
        Assert.False(vm.LoadSyntheticRootSource(execution.Compilation.Input.InputDigest)); Assert.Null(vm.OperatorReceiptMetadata); Assert.Null(vm.RecordedOperators);
        Assert.True(vm.LoadSyntheticRootSource(execution.Compilation.Result.ResultDigest)); Assert.True(vm.ReadOperatorRecordingMetadata()); Assert.True(Read(vm, actual));
        var foreign = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var foreignDiagnostic = foreign.Response.Execution!.Compilation.Result.Diagnostics.First();
        Assert.Equal(vm.SelectedDiagnostic!.DiagnosticId, foreignDiagnostic.DiagnosticId);
        Assert.False(vm.SelectDiagnostic(foreignDiagnostic)); Assert.Null(vm.RecordedOperators);
    }

    [Fact]
    public async Task NewGenerationResetAndDisposeWithdrawActualIntentAndCurrentFrames()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        var vm = new SyntheticWorkbenchViewModel();
        try
        {
            var actual = await Run(vm, session, request); Select(vm, actual); Assert.True(Read(vm, actual)); var retained = vm.RecordedOperators!;
            var (_, next, _) = Input(generation: 8); var manifest = session.Prepare(next, Guid.NewGuid(), actual.Receipt); vm.BeginOperatorSynthetic(manifest, next);
            Assert.False(vm.TryAcceptOperator(actual)); Assert.Null(vm.RecordedOperators); Assert.Null(vm.OperatorReceiptMetadata); Assert.Single(retained.Bodies);
            var current = await session.ExecuteAsync(manifest, next); Assert.True(vm.TryAcceptOperator(current)); Select(vm, current); Assert.True(Read(vm, current));
            vm.Reset(); Assert.False(vm.TryAcceptOperator(current)); Assert.Null(vm.RecordedOperators);
            var (_, latest, _) = Input(generation: 9); var final = await Run(vm, session, latest, current.Receipt); Select(vm, final); Assert.True(Read(vm, final));
            vm.Dispose(); Assert.Null(vm.RecordedOperators); Assert.Null(vm.OperatorReceiptMetadata); Assert.Single(retained.Bodies);
            Assert.Throws<ObjectDisposedException>(() => vm.ReadOperatorRecordingMetadata());
        }
        finally { vm.Dispose(); }
    }

    [Fact]
    public async Task ResourceCompletedEnvelopeAndPreCancelledHostOutcomePreserveIndependentAxes()
    {
        var (control, _, profile) = Input(); var settings = JsonNode.Parse(control.Input.Settings.CanonicalText)!;
        settings["executionProfile"]!["maximumOperations"] = 1;
        var input = new CompilerInputManifest(control.Input.Source, SourceJson.Parse(settings.ToJsonString()));
        var request = new PredicateObservationCompilerHostRequest(new(new(input), 7), new(PredicateEvaluationCaptureMode.ObservedOperators));
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Assert.Equal(CompilerExecutionState.ResourceExhausted, vm.Result!.Execution);
        Assert.False(vm.HasCompleteLocalPass); Assert.True(vm.LoadSyntheticRootSource(vm.Result.ResultDigest)); Assert.True(vm.ReadOperatorRecordingMetadata());
        Assert.Equal("CaptureInterrupted", vm.OperatorRecordMetadata!.Value.GetProperty("state").GetString());
        foreach (var diagnostic in vm.Problems) { Assert.True(vm.SelectDiagnostic(diagnostic)); Assert.True(Read(vm, actual)); Assert.Empty(vm.RecordedOperators!.Bodies); }
        var cancelledRequest = new PredicateObservationCompilerHostRequest(new(new(control.Input), 8), new(PredicateEvaluationCaptureMode.ObservedOperators));
        var manifest = session.Prepare(cancelledRequest, Guid.NewGuid()); vm.BeginOperatorSynthetic(manifest, cancelledRequest);
        using var token = new CancellationTokenSource();
        token.Cancel(); var cancelled = await session.ExecuteAsync(manifest, cancelledRequest, token.Token);
        Assert.False(vm.TryAcceptOperator(cancelled)); Assert.Equal("Cancelled", vm.State); Assert.Null(vm.Result); Assert.Null(vm.RecordedOperators); Assert.Null(vm.OperatorReceiptMetadata);
        var (_, retry, _) = Input(generation: 9); var retried = await Run(vm, session, retry, cancelled.Receipt); Select(vm, retried); Assert.True(Read(vm, retried));
        Assert.Single(vm.RecordedOperators!.Bodies); Assert.Null(cancelled.Response.Execution);
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
        public ProjectCompilerPredicateObservationSnapshot? Replacement { get; private set; }
        public CancelPublishedRead(SyntheticWorkbenchViewModel vm, bool replace)
        { _vm = vm; _expectedResult = vm.Result!.ResultDigest; _replace = replace; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ReadRecordedOperators(_expectedResult, CompilerPredicateObservationSnapshotMode.AvailableOperators, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.ReadRecordedOperators(_expectedResult, CompilerPredicateObservationSnapshotMode.AvailableOperators);
                Replacement = _vm.RecordedOperators;
            }
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAtRecordingPublicationWithdrawsOnlyItsOwnedView(bool replace)
    {
        var (_, request, profile) = Input("fail");
        using var session = new LocalPredicateObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual);
        var compilation = actual.Response.Execution!.Compilation; var result = compilation.Result; var resultData = result.Data;
        var receiptData = actual.Receipt.Data;
        Assert.True(Read(vm, actual));
        var previous = vm.RecordedOperators!; var previousData = previous.Data; Assert.NotEmpty(previous.Bodies);
        using var cancellation = new CancelPublishedRead(vm, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data); Assert.Equal(receiptData, actual.Receipt.Data);
        Assert.Equal(previousData, previous.Data); Assert.NotEmpty(previous.Bodies);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, vm.RecordedOperators); Assert.NotEmpty(vm.RecordedOperators!.Bodies);
        }
        else Assert.Null(vm.RecordedOperators);
        Assert.IsType<OperationCanceledException>(error);
    }
}
