using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticPassBoundaryTests
{
    public static IEnumerable<object[]> Keys() => SyntheticOperatorRecordingTests.Keys();
    private static async Task<(PassObservationCompilerRecordedInvocation Actual, PassObservationCompilerHostRequest Request)> Prepare(
        SyntheticWorkbenchViewModel vm, LocalPassObservationCompilerRecordingSession session, string key = "fail")
    {
        var (_, previous, _) = SyntheticOperatorRecordingTests.Input(key);
        var request = new PassObservationCompilerHostRequest(previous.Core, new(CompilerPassObservationMode.AvailableOriginalJson));
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginPassSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(vm.TryAcceptPass(actual)); return (actual, request);
    }
    private static bool Read(SyntheticWorkbenchViewModel vm, ContentDigest expected, int ordinal = 0) =>
        vm.ReadRecordedPassBoundary(expected, ordinal, CompilerPassObservationSnapshotMode.AvailableOriginalJson);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task SelectedBoundaryRequiresExplicitRootAndPreservesOriginalBodiesWithoutADiagnostic(string key)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input(key);
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var (actual, _) = await Prepare(vm, session, key); var execution = actual.Response.Execution!; var digest = vm.Result!.ResultDigest;
        Assert.False(Read(vm, digest)); Assert.Null(vm.RecordedPassBoundary);
        Assert.True(vm.LoadSyntheticRootSource(digest)); Assert.Null(vm.SelectedDiagnostic);
        Assert.True(vm.ReadRecordedPassBoundary(digest, 0)); Assert.Empty(vm.RecordedPassBoundary!.Bodies);
        Assert.Equal(CompilerPassBoundarySnapshotState.ManifestOnly, vm.RecordedPassBoundary.State);
        Assert.True(Read(vm, digest)); var selected = vm.RecordedPassBoundary!;
        var direct = ProjectCompilerPassBoundarySnapshot.Read(execution, execution.Compilation, execution.Observation,
            execution.Compilation.Input.InputDigest, digest, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
        Assert.Equal(direct.Data, selected.Data); Assert.Equal(direct.Digest, selected.Digest);
        Assert.Same(direct.OriginalBoundaryMetadata, selected.OriginalBoundaryMetadata);
        foreach (var body in selected.Bodies) Assert.Same(direct.Bodies[body.Key], body.Value);
        foreach (var problem in vm.Problems) { Assert.True(vm.SelectDiagnostic(problem)); Assert.Same(selected, vm.RecordedPassBoundary); }
        Assert.True(vm.ReadPassRecordingMetadata()); Assert.Null(vm.RecordedPassBoundary); Assert.NotEmpty(selected.Bodies);
        Assert.Same(actual.Receipt.ObservationMetadata, vm.PassRecordMetadata);
        Assert.True(Read(vm, digest)); Assert.True(vm.ReadRecordedPasses(digest)); Assert.Null(vm.RecordedPassBoundary);
    }

    [Theory]
    [InlineData(1, "reset")] [InlineData(2, "reset")]
    [InlineData(1, "dispose")] [InlineData(2, "dispose")]
    [InlineData(1, "source")] [InlineData(2, "source")]
    [InlineData(1, "context")] [InlineData(2, "context")]
    public async Task BeforeAndAfterPublicationNotificationsCannotReviveOldSelectedBodies(int notification, string action)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var (actual, request) = await Prepare(vm, session); var digest = vm.Result!.ResultDigest;
        Assert.True(vm.LoadSyntheticRootSource(digest)); Assert.True(Read(vm, digest)); var callerOwned = vm.RecordedPassBoundary!;
        var next = new PassObservationCompilerHostRequest(new(request.Core.Input, request.Core.ClientGeneration + 1), request.ObservationPolicy);
        var manifest = session.Prepare(next, Guid.NewGuid(), actual.Receipt);
        var notifications = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            if (fired || ++notifications != notification) return;
            fired = true; var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            switch (action) { case "reset": current.Reset(); break; case "dispose": current.Dispose(); break;
                case "source": Assert.True(current.LoadSyntheticRootSource(digest)); break; default: current.BeginPassSynthetic(manifest, next); break; }
        };
        Assert.False(Read(vm, digest)); Assert.True(fired); Assert.Null(vm.RecordedPassBoundary); Assert.NotEmpty(callerOwned.Bodies);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task NewerSelectedReadWinsEvenWhenItStartsInsideAPublicationNotification(int notification)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        await Prepare(vm, session); var digest = vm.Result!.ResultDigest; Assert.True(vm.LoadSyntheticRootSource(digest));
        var notifications = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            if (fired || ++notifications != notification) return;
            fired = true; Assert.True(Read(Assert.IsType<SyntheticWorkbenchViewModel>(sender), digest, 1));
        };
        Assert.False(Read(vm, digest)); Assert.True(fired); Assert.Equal(1, vm.RecordedPassBoundary!.Ordinal);
        Assert.Equal(1, vm.RecordedPassBoundary.OriginalBoundaryMetadata!.Value.GetProperty("ordinal").GetInt32());
    }

    [Fact]
    public async Task WrongResultCancellationAndUnavailableSelectorsWithdrawOnlyCurrentDisplay()
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        var vm = new SyntheticWorkbenchViewModel();
        try
        {
            var (actual, _) = await Prepare(vm, session); var digest = vm.Result!.ResultDigest;
            Assert.True(vm.LoadSyntheticRootSource(digest)); Assert.True(Read(vm, digest)); var original = vm.RecordedPassBoundary!;
            Assert.False(Read(vm, actual.Response.Execution!.Compilation.Input.InputDigest)); Assert.Null(vm.RecordedPassBoundary);
            Assert.True(Read(vm, digest)); using var source = new CancellationTokenSource(); source.Cancel();
            Assert.Throws<OperationCanceledException>(() => vm.ReadRecordedPassBoundary(digest, 0, token: source.Token)); Assert.Null(vm.RecordedPassBoundary);
            Assert.True(Read(vm, digest)); Assert.False(Read(vm, digest, int.MaxValue));
            Assert.Equal(CompilerPassBoundarySnapshotState.BoundaryNotRecorded, vm.RecordedPassBoundary!.State); Assert.Empty(vm.RecordedPassBoundary.Bodies);
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(vm, digest, -1)); Assert.Null(vm.RecordedPassBoundary);
            Assert.True(Read(vm, digest)); Assert.False(vm.LoadSyntheticRootSource(actual.Response.Execution.Compilation.Input.InputDigest)); Assert.Null(vm.RecordedPassBoundary);
            Assert.NotEmpty(original.Bodies); vm.Dispose();
            Assert.Throws<ObjectDisposedException>(() => Read(vm, digest)); Assert.Null(vm.RecordedPassBoundary);
        }
        finally { vm.Dispose(); }
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
        public ProjectCompilerPassBoundarySnapshot? Replacement { get; private set; }
        public CancelPublishedRead(SyntheticWorkbenchViewModel vm, bool replace)
        { _vm = vm; _expectedResult = vm.Result!.ResultDigest; _replace = replace; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ReadRecordedPassBoundary(_expectedResult, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.ReadRecordedPassBoundary(_expectedResult, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
                Replacement = _vm.RecordedPassBoundary;
            }
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAtAdjacentSnapshotPublicationWithdrawsOnlyItsOwnedView(bool replace)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var (actual, _) = await Prepare(vm, session);
        Assert.True(vm.LoadSyntheticRootSource(actual.Response.Execution!.Compilation.Result.ResultDigest));
        var result = vm.Result!; var resultData = result.Data; var receiptData = actual.Receipt.Data;
        Assert.True(Read(vm, result.ResultDigest));
        var previous = vm.RecordedPassBoundary!; var previousData = previous.Data; Assert.NotEmpty(previous.Bodies);
        using var cancellation = new CancelPublishedRead(vm, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data); Assert.Equal(receiptData, actual.Receipt.Data);
        Assert.Equal(previousData, previous.Data); Assert.NotEmpty(previous.Bodies);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, vm.RecordedPassBoundary); Assert.NotEmpty(vm.RecordedPassBoundary!.Bodies);
        }
        else Assert.Null(vm.RecordedPassBoundary);
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
        public ProjectCompilerPassBoundarySnapshot Initial { get; }
        public ProjectCompilerPassBoundarySnapshot? Replacement { get; private set; }
        public CancelInitialRecordedRead(SyntheticWorkbenchViewModel view, bool admitted)
        {
            _view = view; _admitted = admitted;
            _expected = view.Result!.ResultDigest; _refused = view.Result.Input.InputDigest;
            Assert.True(ReadCurrent(_expected, default));
            Initial = view.RecordedPassBoundary!;
            view.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => ReadCurrent(_expected, Token);
        private bool ReadCurrent(ContentDigest expected, CancellationToken token) =>
            _view.ReadRecordedPassBoundary(expected, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson, token: token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || _view.RecordedPassBoundary is not null) return;
            Fired = true;
            ReplacementSucceeded = ReadCurrent(_admitted ? _expected : _refused, default);
            Replacement = _view.RecordedPassBoundary;
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtInitialRecordedReadNotificationReportsCancellationWithoutErasingNewerRead(bool admitted)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input();
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var view = new SyntheticWorkbenchViewModel();
        await Prepare(view, session);
        Assert.True(view.LoadSyntheticRootSource(view.Result!.ResultDigest));
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
        Assert.Same(latest, view.RecordedPassBoundary);
        if (admitted)
        {
            Assert.NotNull(latest);
            Assert.NotSame(previous, latest);
            Assert.NotEmpty(latest.Bodies);
        }
        else
        {
            Assert.Null(latest);
        }
        Assert.Same(result, view.Result);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(previousData, previous.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
