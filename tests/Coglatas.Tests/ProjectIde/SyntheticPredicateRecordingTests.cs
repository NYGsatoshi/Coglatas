using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticPredicateRecordingTests
{
    private static (CoreCompilation Control, PredicateCompilerHostRequest Request, CompilerHostProfile Profile) Input(string key = "fail", long generation = 7,
        CompilerPredicateCapturePolicy? policy = null)
    {
        var text = RootSourceAdmissionTests.Cases().Select(row => (string)row[0]).Single(text => {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.GetProperty("key").GetString() == key;
        });
        var (control, core, _) = DiagnosticPremiseSnapshotTests.Prepare(text); var input = control.Input;
        CompilerArtifactIdentity At(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        return (control, new(new(core.Input, generation, core.Snapshots, core.Units, core.Calendars), policy ?? new(CompilerPredicateCaptureMode.OriginalRootArrays)), profile);
    }
    private static async Task<PredicateCompilerRecordedInvocation> Run(SyntheticWorkbenchViewModel vm, LocalPredicateCompilerRecordingSession session,
        PredicateCompilerHostRequest request, LocalPredicateCompilerRecordingReceipt? origin = null)
    {
        var manifest = session.Prepare(request, Guid.NewGuid(), origin); vm.BeginPredicateSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(vm.TryAcceptPredicate(actual)); return actual;
    }
    private static void Select(SyntheticWorkbenchViewModel vm, PredicateCompilerRecordedInvocation actual)
    {
        Assert.True(vm.LoadSyntheticRootSource(actual.Response.Compilation!.Result.ResultDigest));
        var diagnostic = actual.Response.Compilation.Result.Diagnostics.First(); Assert.True(vm.SelectDiagnostic(diagnostic));
    }
    private static void ReadBodies(SyntheticWorkbenchViewModel vm, PredicateCompilerRecordedInvocation actual) =>
        Assert.True(vm.ReadRecordedPredicatePremises(actual.Response.Compilation!.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));

    [Theory]
    [InlineData("fail")] [InlineData("pass")] [InlineData("unknown")] [InlineData("test-assertion-navigation")]
    public async Task PreselectedManifestRefusesAWholeGenuineOlderInvocationWithIdenticalRequestGenerationResultAndRecord(string key)
    {
        var (control, request, profile) = Input(key);
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var older = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var intended = session.Prepare(request, Guid.NewGuid()); vm.BeginPredicateSynthetic(intended, request);
        var actual = await session.ExecuteAsync(intended, request);
        Assert.Equal(older.Response.Compilation!.Result.ResultDigest, actual.Response.Compilation!.Result.ResultDigest);
        Assert.Equal(older.Receipt.PredicateRecordDigest, actual.Receipt.PredicateRecordDigest);
        Assert.NotEqual(older.Receipt.AttemptId, actual.Receipt.AttemptId); Assert.NotSame(older.Receipt.Manifest, actual.Receipt.Manifest);
        Assert.True(older.Response.IsFor(request)); Assert.True(older.Receipt.IsFor(older.Response));
        Assert.False(vm.TryAcceptPredicate(older)); Assert.Equal("Waiting", vm.State); Assert.Null(vm.Result);
        Assert.True(vm.TryAcceptPredicate(actual)); Assert.Same(actual.Response.Compilation.Result, vm.Result);
        Assert.Equal(control.Result.Data, vm.Result!.Data); Assert.Equal(control.LogicalOperationsConsumed, actual.Response.Compilation.LogicalOperationsConsumed);
    }

    [Theory]
    [InlineData("fail")] [InlineData("pass")] [InlineData("unknown")] [InlineData("test-assertion-navigation")]
    public async Task ExplicitSourceAndSelectionGateDefaultManifestAndOriginalSelectedBodiesWithoutChangingOldReads(string key)
    {
        var (_, request, profile) = Input(key);
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var result = actual.Response.Compilation!.Result;
        Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.PredicateRecordMetadata); Assert.Null(vm.RecordedPredicatePremises);
        Assert.False(vm.ReadPredicateRecordingMetadata()); Assert.False(vm.ReadRecordedPredicatePremises(result.ResultDigest));
        Assert.True(vm.LoadSyntheticRootSource(result.ResultDigest)); Assert.True(vm.ReadPredicateRecordingMetadata());
        Assert.Same(actual.Receipt.Data, vm.PredicateReceiptMetadata); Assert.Same(actual.Receipt.PredicateMetadata, vm.PredicateRecordMetadata);
        Assert.False(vm.ReadRecordedPredicatePremises(result.ResultDigest)); Assert.Null(vm.RecordedPredicatePremises);
        foreach (var diagnostic in result.Diagnostics)
        {
            Assert.True(vm.SelectDiagnostic(diagnostic)); Assert.True(vm.ReadRecordedPredicatePremises(result.ResultDigest));
            Assert.Equal(DiagnosticPremiseSnapshotMode.Manifest, vm.RecordedPredicatePremises!.Mode); Assert.Empty(vm.RecordedPredicatePremises.Bodies);
            ReadBodies(vm, actual); var snapshot = vm.RecordedPredicatePremises!;
            foreach (var pair in snapshot.Bodies)
            {
                Assert.Contains(pair.Key, diagnostic.Evidence.Premises.Select(premise => premise.Value));
                Assert.Same(actual.Receipt.Bodies[pair.Key], pair.Value);
            }
            Assert.Equal(key == "fail" ? 1 : 0, snapshot.Bodies.Count);
            Assert.True(vm.ReadSyntheticDiagnosticPremises(result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));
            Assert.All(vm.DiagnosticPremises!.Bodies.Keys, digest => Assert.DoesNotContain(digest, snapshot.Bodies.Keys));
            Assert.Equal(result.Data, vm.Result!.Data);
        }
    }

    [Fact]
    public async Task ForeignEqualRequestTokensSessionsAndOldProtocolCannotCrossTheDeclaredRun()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginPredicateSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); var old = await new LocalNetCompilerHost(profile).CompileAsync(request.Core);
        Assert.False(vm.TryAccept(old)); Assert.Equal("Waiting", vm.State);
        using var equalRequestVm = new SyntheticWorkbenchViewModel();
        var equalRequest = new PredicateCompilerHostRequest(request.Core, request.CapturePolicy); equalRequestVm.BeginPredicateSynthetic(manifest, equalRequest);
        Assert.Equal(request.ExecutionInputDigest, equalRequest.ExecutionInputDigest); Assert.False(equalRequestVm.TryAcceptPredicate(actual));
        using var foreignSession = new LocalPredicateCompilerRecordingSession(profile);
        var foreign = await foreignSession.ExecuteAsync(foreignSession.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(actual.Response.Compilation!.Result.ResultDigest, foreign.Response.Compilation!.Result.ResultDigest); Assert.False(vm.TryAcceptPredicate(foreign));
        Assert.True(vm.TryAcceptPredicate(actual));
        using var oldVm = new SyntheticWorkbenchViewModel();
        oldVm.BeginSynthetic(request.Core); Assert.False(oldVm.TryAcceptPredicate(actual)); Assert.True(oldVm.TryAccept(old));
        var nextCore = new CompilerHostRequest(request.Core.Input, 8, request.Core.Snapshots, request.Core.Units, request.Core.Calendars);
        vm.BeginSynthetic(nextCore); Assert.False(vm.TryAcceptPredicate(actual));
        Assert.True(vm.TryAccept(await new LocalNetCompilerHost(profile).CompileAsync(nextCore))); Assert.Null(vm.PredicateReceiptMetadata);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task MetadataNotificationReentryWithdrawsBothBeforeAndAfterPublication(int notification)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Assert.True(vm.LoadSyntheticRootSource(actual.Response.Compilation!.Result.ResultDigest));
        var events = 0; vm.PropertyChanged += (sender, _) => { if (++events == notification) Assert.IsType<SyntheticWorkbenchViewModel>(sender).Reset(); };
        Assert.False(vm.ReadPredicateRecordingMetadata()); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.PredicateRecordMetadata); Assert.Null(vm.RecordedPredicatePremises);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task SelectedBodyNotificationReentryWithdrawsBothBeforeAndAfterPublication(int notification)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual);
        var events = 0; vm.PropertyChanged += (sender, _) => { if (++events == notification) Assert.IsType<SyntheticWorkbenchViewModel>(sender).Reset(); };
        Assert.False(vm.ReadRecordedPredicatePremises(actual.Response.Compilation!.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));
        Assert.Null(vm.RecordedPredicatePremises); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.SelectedDiagnostic);
    }

    [Theory]
    [InlineData("Empty")] [InlineData("Waiting")] [InlineData("Ready")]
    public async Task BeginAndAcceptanceNotificationsCannotReviveAResetLifecycle(string trigger)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); var reset = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (!reset && current.State == trigger) { reset = true; current.Reset(); }
        };
        vm.BeginPredicateSynthetic(manifest, request); var actual = await session.ExecuteAsync(manifest, request);
        Assert.False(vm.TryAcceptPredicate(actual)); Assert.True(reset); Assert.Equal("Empty", vm.State);
        Assert.Null(vm.Result); Assert.Null(vm.Source); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises);
    }

    [Fact]
    public async Task CancelledReadAndIncorrectSourceReadWithdrawDisplayWithoutPhysicallyErasingCallerOwnedFrames()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual); Assert.True(vm.ReadPredicateRecordingMetadata()); ReadBodies(vm, actual);
        var retained = vm.RecordedPredicatePremises!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => vm.ReadRecordedPredicatePremises(actual.Response.Compilation!.Result.ResultDigest,
            DiagnosticPremiseSnapshotMode.AvailableBodies, token: cancellation.Token));
        Assert.Null(vm.RecordedPredicatePremises); Assert.Single(retained.Bodies);
        Assert.False(vm.LoadSyntheticRootSource(actual.Response.Compilation!.Input.InputDigest)); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises);
        Assert.True(vm.LoadSyntheticRootSource(actual.Response.Compilation.Result.ResultDigest)); Assert.True(vm.ReadPredicateRecordingMetadata());
        Assert.Same(actual.Receipt.Data, vm.PredicateReceiptMetadata); Assert.Equal(CompilerVerdict.Fail, vm.Result!.Verdict);
    }

    [Fact]
    public async Task NewGenerationResetAndDisposalWithdrawIntentAndAllCurrentDisplays()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        var vm = new SyntheticWorkbenchViewModel();
        try
        {
            var actual = await Run(vm, session, request); Select(vm, actual); Assert.True(vm.ReadPredicateRecordingMetadata()); ReadBodies(vm, actual);
            var retained = vm.RecordedPredicatePremises!;
            var (_, next, _) = Input(generation: 8); var manifest = session.Prepare(next, Guid.NewGuid(), actual.Receipt); vm.BeginPredicateSynthetic(manifest, next);
            Assert.False(vm.TryAcceptPredicate(actual)); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises); Assert.Single(retained.Bodies);
            var newer = await session.ExecuteAsync(manifest, next); Assert.True(vm.TryAcceptPredicate(newer)); Select(vm, newer); ReadBodies(vm, newer);
            vm.Reset(); Assert.False(vm.TryAcceptPredicate(newer)); Assert.Null(vm.RecordedPredicatePremises); Assert.Null(vm.PredicateReceiptMetadata);
            var (_, latest, _) = Input(generation: 9); var final = await Run(vm, session, latest, newer.Receipt); Select(vm, final); ReadBodies(vm, final);
            var original = Assert.Single(vm.RecordedPredicatePremises!.Bodies).Value; vm.Dispose();
            Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises); Assert.Equal(134, original.ToCanonicalBytes().Length);
            Assert.Throws<ObjectDisposedException>(() => vm.ReadPredicateRecordingMetadata());
        }
        finally { vm.Dispose(); }
    }

    [Fact]
    public async Task ResourceInterruptedHostCompletionPreservesCompilerAxesAndDoesNotRequireASemanticModel()
    {
        var (control, _, _) = Input(); var settings = JsonNode.Parse(control.Input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = 1;
        var input = new CompilerInputManifest(control.Input.Source, SourceJson.Parse(settings.ToJsonString())); var (_, _, profile) = Input();
        var request = new PredicateCompilerHostRequest(new(new(input), 7), new(CompilerPredicateCaptureMode.OriginalRootArrays));
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var compilation = actual.Response.Compilation!;
        Assert.Equal(CompilerHostStatus.Completed, actual.Response.Status); Assert.Equal(CompilerExecutionState.ResourceExhausted, vm.Result!.Execution);
        Assert.Equal("not-executed", vm.LocalEvidenceState); Assert.False(vm.HasCompleteLocalPass);
        Assert.Equal(compilation.Model is null, vm.Inspection is null); Assert.True(vm.LoadSyntheticRootSource(vm.Result.ResultDigest)); Assert.True(vm.ReadPredicateRecordingMetadata());
        Assert.Equal("CaptureInterrupted", vm.PredicateRecordMetadata!.Value.GetProperty("state").GetString());
        Assert.NotEmpty(compilation.Result.Diagnostics);
        foreach (var diagnostic in compilation.Result.Diagnostics)
        {
            Assert.True(vm.SelectDiagnostic(diagnostic)); ReadBodies(vm, actual);
            Assert.Equal(PredicatePremiseRecordState.CaptureInterrupted, vm.RecordedPredicatePremises!.OriginalCaptureState); Assert.Empty(vm.RecordedPredicatePremises.Bodies);
        }
    }

    [Fact]
    public async Task CancelledHostHasNoInventedResultAndDiagnosticRetryHasItsOwnRunAttemptAndActualOrigin()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginPredicateSynthetic(manifest, request);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel(); var cancelled = await session.ExecuteAsync(manifest, request, cancellation.Token);
        Assert.False(vm.TryAcceptPredicate(cancelled)); Assert.Equal("Cancelled", vm.State); Assert.Null(vm.Result); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises);
        var (_, retry, _) = Input(generation: 8); var actual = await Run(vm, session, retry, cancelled.Receipt);
        Assert.NotEqual(cancelled.Receipt.Manifest.RunId, actual.Receipt.Manifest.RunId); Assert.NotEqual(cancelled.Receipt.AttemptId, actual.Receipt.AttemptId);
        Assert.Equal(cancelled.Receipt.Manifest.RunId.ToString(), actual.Receipt.Manifest.Data.Value.GetProperty("origin").GetProperty("runId").GetString());
        Select(vm, actual); ReadBodies(vm, actual); Assert.Single(vm.RecordedPredicatePremises!.Bodies);
        Assert.Null(cancelled.Response.Compilation); Assert.Null(cancelled.Receipt.PredicateMetadata); Assert.Empty(cancelled.Receipt.Bodies);
    }

    [Fact]
    public async Task EqualIdentityForeignSelectedFindingCannotReopenARecordedArray()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual); ReadBodies(vm, actual);
        var foreign = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(actual.Response.Compilation!.Result.Diagnostics.Single().DiagnosticId, foreign.Response.Compilation!.Result.Diagnostics.Single().DiagnosticId);
        Assert.False(vm.SelectDiagnostic(foreign.Response.Compilation.Result.Diagnostics.Single())); Assert.Null(vm.RecordedPredicatePremises);
        Assert.False(vm.ReadRecordedPredicatePremises(actual.Response.Compilation.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies)); Assert.Null(vm.RecordedPredicatePremises);
    }

    [Fact]
    public async Task SourceReloadWithdrawsDisplayButRetainsTheActualAcceptedPairForExplicitReadmission()
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual); Assert.True(vm.ReadPredicateRecordingMetadata()); ReadBodies(vm, actual);
        Assert.True(vm.LoadSyntheticRootSource(actual.Response.Compilation!.Result.ResultDigest)); Assert.Null(vm.PredicateReceiptMetadata); Assert.Null(vm.RecordedPredicatePremises);
        Assert.True(vm.ReadPredicateRecordingMetadata()); Select(vm, actual); ReadBodies(vm, actual);
        Assert.True(vm.ReadPredicateRecordingMetadata()); Assert.Null(vm.RecordedPredicatePremises); Assert.Same(actual.Receipt.Data, vm.PredicateReceiptMetadata);
    }

    [Theory]
    [InlineData("fail", "3a65e7a723d074602765ccedbd3f7942fbdab1bfdb360fb86ba153448c03be20", "3f64abfa4d6e47c0a9191a3bafef2dbc46d94bef80020f6047191b08201478dd")]
    [InlineData("pass", "66df7f9ec303eb0fcc80a71306bd22b5f9729e728b20ff3f25243dbae21b9ee5", "7cf0b32bebfa348c4d76818d45e8fe96772dea913b2862f0f05fc6fd14fac471")]
    [InlineData("unknown", "3821d206c48afd22761e66fa7064db60709350e0db391cb61df160f296f69f6a", "ec75129138334d06ff06ae07923821f5b12044e9ee788bf6305771dd4b9c0abf")]
    [InlineData("test-assertion-navigation", "1d851d0e73f1fd90f6badfe31a6acce1f87a4e2673b7c8bc0df7f35ee696c7ca", "713577569cc914a04d2534b923461e68c2c1fcaf9a062b140335b0f910c8f7e0")]
    public void OriginalRequestSerializerBytesAndVersionOneHashInputRemainUnchanged(string key, string originalSerializationHash, string originalExecutionDigest)
    {
        // Captured from qualified pre-addition App source9f143f93 and original actual DLL b46be981; no derived name/hash normalization.
        var (_, request, _) = Input(key); var core = request.Core; var bytes = JsonSerializer.SerializeToUtf8Bytes(core);
        Assert.Equal(1503, bytes.Length); Assert.Equal(originalSerializationHash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        Assert.Equal(originalExecutionDigest, core.ExecutionInputDigest.Value);
        Assert.Equal(core.ExecutionInputDigest, ContentDigest.Compute("coglatas.compiler-host-input/1", core.ExecutionInputData.ToCanonicalBytes()));
        Assert.Equal(2, core.ExecutionInputData.Value.EnumerateObject().Count());
        Assert.Equal(core.Input.ExpectedInputDigest.Data, SourceJson.Parse(core.ExecutionInputData.Value.GetProperty("declaredInputDigest").GetRawText()));
        Assert.Equal(core.MaterializationDigest.Data, SourceJson.Parse(core.ExecutionInputData.Value.GetProperty("materializationsDigest").GetRawText()));
    }

    [Theory]
    [InlineData(CompilerPredicateCaptureMode.Manifest, 1048576, PredicatePremiseRecordState.ManifestOnly, RecordedPredicateSnapshotState.NoAvailableOriginalArrays)]
    [InlineData(CompilerPredicateCaptureMode.OriginalRootArrays, 1, PredicatePremiseRecordState.BodyBudgetExceeded, RecordedPredicateSnapshotState.NoAvailableOriginalArrays)]
    [InlineData(CompilerPredicateCaptureMode.OriginalRootArrays, 1048576, PredicatePremiseRecordState.CapturedOriginalArrays, RecordedPredicateSnapshotState.BodyBudgetExceeded)]
    public async Task OriginalCaptureGapAndViewerByteLimitRemainSeparateWithoutChangingFailedTruth(CompilerPredicateCaptureMode mode, int captureBytes,
        PredicatePremiseRecordState originalState, RecordedPredicateSnapshotState viewState)
    {
        var (_, request, profile) = Input(policy: new(mode, captureBytes));
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual);
        Assert.True(vm.ReadRecordedPredicatePremises(actual.Response.Compilation!.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies, 1));
        Assert.Equal(originalState, vm.RecordedPredicatePremises!.OriginalCaptureState); Assert.Equal(viewState, vm.RecordedPredicatePremises.State);
        Assert.Empty(vm.RecordedPredicatePremises.Bodies); Assert.Equal(CompilerVerdict.Fail, vm.Result!.Verdict);
        Assert.Equal(actual.Response.Compilation.Result.Data, vm.Result.Data);
    }

    private sealed class CancelPublishedRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly ContentDigest _expectedResult;
        private readonly bool _replace;
        private readonly InvalidOperationException? _publicationFailure;
        private readonly InvalidOperationException? _withdrawalFailure;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public int WithdrawalNotifications { get; private set; }
        public int DeliveredWithdrawals { get; private set; }
        public ProjectRecordedPredicatePremiseSnapshot? Replacement { get; private set; }
        public CancelPublishedRead(SyntheticWorkbenchViewModel vm, bool replace, InvalidOperationException? publicationFailure,
            InvalidOperationException? withdrawalFailure)
        {
            _vm = vm;
            _expectedResult = vm.Result!.ResultDigest;
            _replace = replace;
            _publicationFailure = publicationFailure;
            _withdrawalFailure = withdrawalFailure;
            vm.PropertyChanged += Changed;
            vm.PropertyChanged += RecordWithdrawal;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ReadRecordedPredicatePremises(_expectedResult, DiagnosticPremiseSnapshotMode.AvailableBodies, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired)
            {
                if (Token.IsCancellationRequested)
                {
                    WithdrawalNotifications++;
                    if (_vm.RecordedPredicatePremises is null && _withdrawalFailure is not null) throw _withdrawalFailure;
                }
                return;
            }
            if (_vm.RecordedPredicatePremises is null) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.ReadRecordedPredicatePremises(_expectedResult, DiagnosticPremiseSnapshotMode.AvailableBodies);
                Replacement = _vm.RecordedPredicatePremises;
            }
            _source.Cancel();
            if (_publicationFailure is not null) throw _publicationFailure;
        }

        private void RecordWithdrawal(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Token.IsCancellationRequested && _vm.RecordedPredicatePremises is null)
            {
                DeliveredWithdrawals++;
            }
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
    public Task CancellationAtRecordingPublicationWithdrawsOnlyItsOwnedView(bool replace) =>
        VerifyRecordingOwnership(replace, null);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task ThrowingRecordingObserverWithdrawsOnlyItsCancelledView(bool replace) =>
        VerifyRecordingOwnership(replace, new InvalidOperationException("Publication observer failed."),
            new InvalidOperationException("Withdrawal observer failed."));

    [Fact]
    public Task ThrowingRecordingWithdrawalObserverPreservesCancellation() =>
        VerifyRecordingOwnership(false, null, new InvalidOperationException("Withdrawal observer failed."));

    private static async Task VerifyRecordingOwnership(bool replace, InvalidOperationException? publicationFailure,
        InvalidOperationException? withdrawalFailure = null)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); Select(vm, actual);
        var compilation = actual.Response.Compilation!; var result = compilation.Result; var resultData = result.Data;
        var receiptData = actual.Receipt.Data;
        ReadBodies(vm, actual);
        var previous = vm.RecordedPredicatePremises!; var previousData = previous.Data; Assert.NotEmpty(previous.Bodies);
        using var cancellation = new CancelPublishedRead(vm, replace, publicationFailure, withdrawalFailure); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data); Assert.Equal(receiptData, actual.Receipt.Data);
        Assert.Equal(previousData, previous.Data); Assert.NotEmpty(previous.Bodies);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, vm.RecordedPredicatePremises); Assert.NotEmpty(vm.RecordedPredicatePremises!.Bodies);
        }
        else Assert.Null(vm.RecordedPredicatePremises);
        Assert.Equal(replace ? 0 : 1, cancellation.WithdrawalNotifications);
        Assert.Equal(replace ? 0 : 1, cancellation.DeliveredWithdrawals);
        if (publicationFailure is null)
        {
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(withdrawalFailure, cancelled.InnerException);
        }
        else Assert.Same(publicationFailure, error);
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
        public ProjectRecordedPredicatePremiseSnapshot Initial { get; }
        public ProjectRecordedPredicatePremiseSnapshot? Replacement { get; private set; }
        public CancelInitialRecordedRead(SyntheticWorkbenchViewModel view, bool admitted)
        {
            _view = view; _admitted = admitted;
            _expected = view.Result!.ResultDigest; _refused = view.Result.Input.InputDigest;
            Assert.True(ReadCurrent(_expected, default));
            Initial = view.RecordedPredicatePremises!;
            view.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => ReadCurrent(_expected, Token);
        private bool ReadCurrent(ContentDigest expected, CancellationToken token) =>
            _view.ReadRecordedPredicatePremises(expected, DiagnosticPremiseSnapshotMode.AvailableBodies, token: token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || _view.RecordedPredicatePremises is not null) return;
            Fired = true;
            ReplacementSucceeded = ReadCurrent(_admitted ? _expected : _refused, default);
            Replacement = _view.RecordedPredicatePremises;
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtInitialRecordedReadNotificationReportsCancellationWithoutErasingNewerRead(bool admitted)
    {
        var (_, request, profile) = Input();
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var view = new SyntheticWorkbenchViewModel();
        var actual = await Run(view, session, request);
        Select(view, actual);
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
        Assert.Same(latest, view.RecordedPredicatePremises);
        if (admitted)
        {
            Assert.NotNull(latest);
            Assert.NotSame(previous, latest);
            Assert.NotEmpty(latest.Bodies);
        }
        else
        {
            Assert.NotNull(latest);
            Assert.Equal(RecordedPredicateSnapshotState.RefusedWrongResult, latest.State);
            Assert.Empty(latest.Bodies);
        }
        Assert.Same(result, view.Result);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(previousData, previous.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
