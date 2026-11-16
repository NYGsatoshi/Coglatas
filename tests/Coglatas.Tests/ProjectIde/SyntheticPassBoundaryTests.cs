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
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("fail");
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
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("fail");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        await Prepare(vm, session); var digest = vm.Result!.ResultDigest; Assert.True(vm.LoadSyntheticRootSource(digest));
        var notifications = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            if (fired || ++notifications != notification) return;
            fired = true; Assert.True(Read(Assert.IsType<SyntheticWorkbenchViewModel>(sender), digest, 1));
        };
        Assert.False(Read(vm, digest, 0)); Assert.True(fired); Assert.Equal(1, vm.RecordedPassBoundary!.Ordinal);
        Assert.Equal(1, vm.RecordedPassBoundary.OriginalBoundaryMetadata!.Value.GetProperty("ordinal").GetInt32());
    }

    [Fact]
    public async Task WrongResultCancellationAndUnavailableSelectorsWithdrawOnlyCurrentDisplay()
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("fail");
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
}
