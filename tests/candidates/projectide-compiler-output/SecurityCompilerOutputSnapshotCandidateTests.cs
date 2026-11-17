using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>
/// Security-owned independent local Compiler output-snapshot tests for P0 #1966.
/// Neither a local snapshot nor its content digest confers read/export/Trust authority.
/// </summary>
public sealed class SecurityCompilerOutputSnapshotCandidateTests
{
    private static CoreCompilation ActualCompilation()
    {
        var input = ProjectSimulationTests.Plan().Input;
        return PureCoreCompiler.Compile(input, input.Compiler);
    }

    private static ProjectCompilerOutputSnapshot Capture(CoreCompilation compilation,
        int budget = ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes)
        => ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, budget);

    [Fact]
    public void DefaultManifestRetainsRealInputAndResultAxesWithoutPrivateOutputBodies()
    {
        var compiled = ActualCompilation();
        var snapshot = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, snapshot.State);
        Assert.Equal(CompilerOutputSnapshotMode.Manifest, snapshot.Mode);
        Assert.Empty(snapshot.Bodies);
        Assert.Equal(0, snapshot.RetainedBodyBytes);
        Assert.Equal(compiled.Input.InputDigest, snapshot.InputDigest);
        Assert.Equal(compiled.Input.Source.Digest, snapshot.SourceDigest);
        Assert.Equal(compiled.Input.Source.Context, snapshot.Context);
        Assert.Equal(compiled.Result.ResultDigest, snapshot.ResultDigest);
        Assert.Equal(compiled.Result.Verdict.ToString(), snapshot.Data.Value.GetProperty("result").GetProperty("verdict").GetString());
        Assert.All(snapshot.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("NotCaptured", item.GetProperty("body").GetString()));
        Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectCompilerOutputSnapshot.Format, snapshot.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void ActualCorePublicationOnlyCapturesThreeDeclaredBoundOutputObjects()
    {
        var compiled = ActualCompilation();
        var capture = Capture(compiled);
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, capture.State);
        Assert.Equal(3, capture.Bodies.Count);
        Assert.NotNull(compiled.Publication);
        Assert.Same(compiled.Publication!.SemanticModel, capture.Bodies["coglatas.semantic-model"]);
        Assert.Same(compiled.Publication.Ir, capture.Bodies["coglatas.ir"]);
        Assert.Same(compiled.Publication.SourceMap, capture.Bodies["coglatas.source-map"]);
        Assert.Equal(compiled.Result.ResultDigest, capture.ResultDigest);
        Assert.Equal(compiled.Result.Artifacts.Select(item => item.Role).Order(StringComparer.Ordinal),
            capture.Bodies.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(capture.Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText)),
            capture.RetainedBodyBytes);
        Assert.All(capture.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("Captured", item.GetProperty("body").GetString()));
    }

    [Fact]
    public void WrongExpectedResultRefusesAllSourceResultAndOutputExistenceDetails()
    {
        var compiled = ActualCompilation();
        var refused = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Input.InputDigest,
            CompilerOutputSnapshotMode.AvailableOutputs);
        Assert.Equal(CompilerOutputSnapshotState.RefusedWrongResult, refused.State);
        Assert.Null(refused.InputDigest);
        Assert.Null(refused.ResultDigest);
        Assert.Null(refused.SourceDigest);
        Assert.Null(refused.Context);
        Assert.Empty(refused.Bodies);
        Assert.Empty(refused.ChangedDimensions);
        foreach (var field in new[] { "basis", "result", "desired", "freshness", "stages", "outputs" })
            Assert.Equal(JsonValueKind.Null, refused.Data.Value.GetProperty(field).ValueKind);
        Assert.NotEqual(Capture(compiled).Digest, refused.Digest);
    }

    [Fact]
    public void ExactByteBudgetSucceedsButLastMissingByteAtomicallyRefusesAllBodies()
    {
        var compiled = ActualCompilation();
        var entire = Capture(compiled);
        Assert.True(entire.RetainedBodyBytes > 1);
        var exact = Capture(compiled, entire.RetainedBodyBytes);
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, exact.State);
        Assert.Equal(3, exact.Bodies.Count);
        var shortByOne = Capture(compiled, entire.RetainedBodyBytes - 1);
        Assert.Equal(CompilerOutputSnapshotState.BodyBudgetExceeded, shortByOne.State);
        Assert.Empty(shortByOne.Bodies);
        Assert.Equal(0, shortByOne.RetainedBodyBytes);
        Assert.Equal(compiled.Result.ResultDigest, shortByOne.ResultDigest);
        Assert.Equal(entire.Data.Value.GetProperty("result").GetRawText(),
            shortByOne.Data.Value.GetProperty("result").GetRawText());
        Assert.All(shortByOne.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("NotCaptured", item.GetProperty("body").GetString()));
    }

    [Fact]
    public void PublicBodyMapKeysAndValuesDoNotOfferGenericOrLegacyMutation()
    {
        var snapshot = Capture(ActualCompilation());
        Assert.False(snapshot.Bodies is ICollection);
        Assert.False(snapshot.Bodies.Keys is ICollection);
        Assert.False(snapshot.Bodies.Values is ICollection);
        Assert.False(snapshot.ChangedDimensions is ICollection);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(snapshot.Bodies);
        var resultBytes = snapshot.Data.ToCanonicalBytes();
        var digest = snapshot.Digest;
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary.Remove("coglatas.ir"));
        Assert.Throws<NotSupportedException>(() => dictionary.Keys.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary.Values.Clear());
        var copy = new KeyValuePair<string, SourceJson>[dictionary.Count];
        dictionary.CopyTo(copy, 0);
        copy[0] = default;
        var mutable = dictionary.ToDictionary(pair => pair.Key, pair => pair.Value);
        mutable.Clear();
        Assert.Equal(3, snapshot.Bodies.Count);
        Assert.Equal(resultBytes, snapshot.Data.ToCanonicalBytes());
        Assert.Equal(digest, snapshot.Digest);
    }

    [Fact]
    public void NestedOutputJsonAndCopiedCanonicalBytesCannotRewriteOwnedEvidence()
    {
        var snapshot = Capture(ActualCompilation());
        var ir = snapshot.Bodies["coglatas.ir"];
        var original = ir.ToCanonicalBytes();
        var copy = ir.ToCanonicalBytes();
        Assert.NotEmpty(copy);
        copy[0] = (byte)(copy[0] == 123 ? 91 : 123);
        var parsed = JsonNode.Parse(ir.CanonicalText)!;
        if (parsed is JsonObject map) map["example.synthetic"] = true;
        Assert.Equal(original, ir.ToCanonicalBytes());
        Assert.Equal(ir.CanonicalText, snapshot.Bodies["coglatas.ir"].CanonicalText);
        Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectCompilerOutputSnapshot.Format, snapshot.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void DesiredFreshnessNeverOverwritesActualCompilerAxesOrSourceScope()
    {
        var compiled = ActualCompilation();
        var current = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
            desiredInput: compiled.Input);
        var changed = Input(source: Source(79));
        var stale = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
            desiredInput: changed);
        Assert.Equal(CompilerFreshness.Current, current.Freshness);
        Assert.Empty(current.ChangedDimensions);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        Assert.NotEmpty(stale.ChangedDimensions);
        Assert.Equal(compiled.Input.InputDigest, stale.InputDigest);
        Assert.Equal(compiled.Result.ResultDigest, stale.ResultDigest);
        Assert.Equal(compiled.Input.Source.Context, stale.Context);
        Assert.Equal(current.Data.Value.GetProperty("result").GetRawText(),
            stale.Data.Value.GetProperty("result").GetRawText());
        Assert.NotEqual(current.Digest, stale.Digest);
    }

    [Fact]
    public void LegacyCompilerArtifactsNeverBecomeInventedCapturedBodies()
    {
        var input = Input();
        var compiled = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.NotEmpty(compiled.Result.Artifacts);
        var result = Capture(compiled);
        Assert.Equal(CompilerOutputSnapshotState.UnavailableBodyProvider, result.State);
        Assert.Empty(result.Bodies);
        Assert.Equal(0, result.RetainedBodyBytes);
        Assert.Equal(compiled.Result.ResultDigest, result.ResultDigest);
        Assert.All(result.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("NotCaptured", item.GetProperty("body").GetString()));
    }

    [Fact]
    public void CancellationRejectsCaptureWithoutChangingTheRealResult()
    {
        var compiled = ActualCompilation();
        var unchanged = compiled.Result.Data;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
                CompilerOutputSnapshotMode.AvailableOutputs, cancellationToken: cancellation.Token));
        Assert.Same(unchanged, compiled.Result.Data);
        Assert.Equal(compiled.Result.ResultDigest,
            ContentDigest.Compute("coglatas.compiler-result/1", compiled.Result.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void CancelledCompilationDoesNotFabricateEarlierBodiesOrCompletedVerdict()
    {
        var input = ProjectSimulationTests.Plan().Input;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var compiled = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: cancellation.Token);
        var snapshot = Capture(compiled);
        Assert.Equal(CompilerOutputSnapshotState.NoOutputsProduced, snapshot.State);
        Assert.Empty(snapshot.Bodies);
        Assert.Equal("Cancelled", snapshot.Data.Value.GetProperty("result").GetProperty("execution").GetString());
        Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty("intermediateStates").GetString());
        Assert.Equal(compiled.Result.ResultDigest, snapshot.ResultDigest);
    }

    [Fact]
    public void InvalidLimitsUnsupportedModeAndUnallocatedAuthorityNeverQualify()
    {
        var compiled = ActualCompilation();
        foreach (var budget in new[] { 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => Capture(compiled, budget));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
                (CompilerOutputSnapshotMode)999));
        var capture = Capture(compiled);
        Assert.Equal("NotAllocated", capture.Data.Value.GetProperty("runIdentity").GetString());
        Assert.Equal("NotRecorded", capture.Data.Value.GetProperty("intermediateStates").GetString());
        foreach (var name in new[] { "authority", "producerTrust", "eligibility" })
            Assert.Equal("NotEvaluated", capture.Data.Value.GetProperty(name).GetString());
        Assert.Equal("NotAdopted", capture.Data.Value.GetProperty("retentionPolicy").GetString());
        Assert.Equal(4, capture.Data.Value.GetProperty("gaps").GetArrayLength());
    }

    [Fact]
    public async Task ViewModelNeedsExplicitAdmissionAndKeepsOriginalCompilerPlanAndSimulation()
    {
        using var fixture = await LifecycleFixture.Create(admitSources: false);
        var view = fixture.View;
        var result = fixture.Response.Compilation!.Result;
        Assert.False(view.ReadSyntheticCompilerOutputs(result.ResultDigest));
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.Same(result, view.Result);

        Assert.True(view.LoadSyntheticScopedSources());
        var plan = view.ScopedExecutionPlan;
        Assert.NotNull(plan);
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(LifecycleId(901))));
        var simulation = view.SimulationRun;
        Assert.NotNull(simulation);
        Assert.True(view.ReadSyntheticCompilerOutputs(result.ResultDigest));
        var manifest = Assert.IsType<ProjectCompilerOutputSnapshot>(view.CompilerOutputSnapshot);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, manifest.State);
        Assert.Empty(manifest.Bodies);
        Assert.Equal(CompilerFreshness.Current, manifest.Freshness);
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        Assert.Same(simulation, view.SimulationRun);

        Assert.True(view.ReadSyntheticCompilerOutputs(result.ResultDigest));
        Assert.Empty(view.CompilerOutputSnapshot!.Bodies);
        Assert.NotSame(retained.Snapshot, view.CompilerOutputSnapshot);
        Assert.Same(result, view.Result);
        Assert.Same(simulation, view.SimulationRun);
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task RejectedAndInvalidQueriesWithdrawTheOldViewWithoutChangingItsCompiler()
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var result = view.Result!;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        Assert.False(view.ReadSyntheticCompilerOutputs(fixture.Request.Input.ExpectedInputDigest,
            CompilerOutputSnapshotMode.AvailableOutputs));
        AssertUnboundLifecycleRefusal(Assert.IsType<ProjectCompilerOutputSnapshot>(view.CompilerOutputSnapshot));
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);

        ReadLifecycleBodies(view);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            view.ReadSyntheticCompilerOutputs(result.ResultDigest, maximumBodyBytes: 0));
        Assert.Null(view.CompilerOutputSnapshot);
        ReadLifecycleBodies(view);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            view.ReadSyntheticCompilerOutputs(result.ResultDigest, (CompilerOutputSnapshotMode)999));
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task ResetRequiresANewGenerationAndReadmissionWithoutErasingReturnedBodies()
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        var expected = view.Result!.ResultDigest;
        view.Reset();
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.Null(view.Result);
        Assert.False(view.ReadSyntheticCompilerOutputs(expected));
        Assert.Throws<ArgumentException>(() => view.BeginSynthetic(fixture.Request));

        var next = new CompilerHostRequest(fixture.Request.Input, 2);
        var response = await CompileLifecycleRequest(next);
        view.BeginSynthetic(next);
        Assert.True(view.TryAccept(response));
        Assert.False(view.ReadSyntheticCompilerOutputs(response.Compilation!.Result.ResultDigest));
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.NotSame(retained.Snapshot, ReadLifecycleBodies(view));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task DisposalWithdrawsCurrentBodiesAndRejectsLaterQueries()
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        var expected = view.Result!.ResultDigest;
        view.Dispose();
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.Null(view.Result);
        Assert.False(view.TryAccept(fixture.Response));
        Assert.Throws<ObjectDisposedException>(() => view.ReadSyntheticCompilerOutputs(expected));
        Assert.Throws<ObjectDisposedException>(() => view.LoadSyntheticScopedSources());
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task NewGenerationRejectsTheEarlierGenuineSameContentResponse()
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        var next = new CompilerHostRequest(fixture.Request.Input, 2);
        var response = await CompileLifecycleRequest(next);
        var previousCompilation = Assert.IsType<CoreCompilation>(fixture.Response.Compilation);
        var nextCompilation = Assert.IsType<CoreCompilation>(response.Compilation);
        Assert.Equal(previousCompilation.Result.Data, nextCompilation.Result.Data);
        Assert.NotSame(previousCompilation, nextCompilation);
        Assert.Equal(fixture.Request.Input.ExpectedInputDigest, next.Input.ExpectedInputDigest);
        Assert.NotEqual(fixture.Response.ClientGeneration, response.ClientGeneration);

        view.BeginSynthetic(next);
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.False(view.TryAccept(fixture.Response));
        Assert.Equal("Waiting", view.State);
        Assert.Null(view.Result);
        Assert.True(view.TryAccept(response));
        Assert.Same(nextCompilation.Result, view.Result);
        Assert.False(view.ReadSyntheticCompilerOutputs(nextCompilation.Result.ResultDigest));
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.NotSame(retained.Snapshot, ReadLifecycleBodies(view));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task ScopedReadmissionWithdrawsOnlyTheCurrentViewAndNeedsANewQuery()
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var result = view.Result;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        var next = ReadLifecycleBodies(view);
        Assert.NotSame(retained.Snapshot, next);
        Assert.Equal(retained.Snapshot.Data, next.Data);
        retained.AssertUnchanged();
    }

    [Fact]
    public Task ResetDuringInitialQueryNotificationPreventsOldPublication()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.Reset, afterPublication: false);

    [Fact]
    public Task ResetDuringPublishedQueryNotificationWithdrawsOldPublication()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.Reset, afterPublication: true);

    [Fact]
    public Task DisposalDuringInitialQueryNotificationPreventsOldPublication()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.Dispose, afterPublication: false);

    [Fact]
    public Task DisposalDuringPublishedQueryNotificationWithdrawsOldPublication()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.Dispose, afterPublication: true);

    [Fact]
    public Task NewInputDuringInitialQueryNotificationCannotBorrowOldPublication()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.NewInput, afterPublication: false);

    [Fact]
    public Task NewInputDuringPublishedQueryNotificationCannotBorrowOldPublication()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.NewInput, afterPublication: true);

    [Fact]
    public Task ReadmissionDuringInitialQueryNotificationPreservesItsNewerQuery()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.Readmit, afterPublication: false);

    [Fact]
    public Task ReadmissionDuringPublishedQueryNotificationPreservesItsNewerQuery()
        => VerifyInterruptedLifecycleQuery(LifecycleChange.Readmit, afterPublication: true);

    [Fact]
    public Task CancellationDuringInitialNotificationKeepsTheNewerAdmittedQuery()
        => VerifyCancelledLifecycleQuery(afterPublication: false, replacement: "admitted");

    [Fact]
    public Task CancellationDuringPublicationKeepsTheNewerAdmittedQuery()
        => VerifyCancelledLifecycleQuery(afterPublication: true, replacement: "admitted");

    [Fact]
    public Task CancellationDuringInitialNotificationKeepsTheNewerRefusal()
        => VerifyCancelledLifecycleQuery(afterPublication: false, replacement: "refused");

    [Fact]
    public Task CancellationDuringPublicationKeepsTheNewerRefusal()
        => VerifyCancelledLifecycleQuery(afterPublication: true, replacement: "refused");

    [Fact]
    public async Task CancellationWithoutReplacementWithdrawsOnlyTheInterruptedSnapshot()
    {
        await VerifyCancelledLifecycleQuery(afterPublication: false, replacement: "none");
        await VerifyCancelledLifecycleQuery(afterPublication: true, replacement: "none");
    }

    [Fact]
    public Task NewerAdmittedQueryDuringInitialNotificationOwnsTheVisibleSnapshot()
        => VerifyNewerLifecycleQuery(afterPublication: false, admitted: true);

    [Fact]
    public Task NewerAdmittedQueryDuringPublicationOwnsTheVisibleSnapshot()
        => VerifyNewerLifecycleQuery(afterPublication: true, admitted: true);

    [Fact]
    public Task NewerRefusedQueryDuringInitialNotificationOwnsTheVisibleSnapshot()
        => VerifyNewerLifecycleQuery(afterPublication: false, admitted: false);

    [Fact]
    public Task NewerRefusedQueryDuringPublicationOwnsTheVisibleSnapshot()
        => VerifyNewerLifecycleQuery(afterPublication: true, admitted: false);

    [Fact]
    public async Task PreCancelledViewModelQueryWithdrawsThePriorSnapshotBeforeThrowing()
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var result = view.Result!;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.ReadSyntheticCompilerOutputs(result.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, token: cancellation.Token));
        Assert.Null(view.CompilerOutputSnapshot);
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        retained.AssertUnchanged();
    }

    private enum LifecycleChange { Reset, Dispose, NewInput, Readmit }

    private static async Task VerifyNewerLifecycleQuery(bool afterPublication, bool admitted)
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var result = view.Result!;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        ProjectCompilerOutputSnapshot? newer = null;
        using var notification = new OutputNotification(view, afterPublication, () =>
        {
            if (admitted) newer = ReadLifecycleBodies(view);
            else
            {
                Assert.False(view.ReadSyntheticCompilerOutputs(fixture.Request.Input.ExpectedInputDigest,
                    CompilerOutputSnapshotMode.AvailableOutputs));
                newer = Assert.IsType<ProjectCompilerOutputSnapshot>(view.CompilerOutputSnapshot);
                AssertUnboundLifecycleRefusal(newer);
            }
        });
        // The context and token never change: only the nested query owns the newer generation.
        Assert.False(view.ReadSyntheticCompilerOutputs(result.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes));
        Assert.True(notification.Fired);
        Assert.NotNull(newer);
        Assert.NotSame(retained.Snapshot, newer);
        Assert.Same(newer, view.CompilerOutputSnapshot);
        if (admitted)
        {
            Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, newer.State);
            Assert.Equal(retained.Snapshot.Data, newer.Data);
        }
        else AssertUnboundLifecycleRefusal(newer);
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        retained.AssertUnchanged();
    }

    private static async Task VerifyInterruptedLifecycleQuery(LifecycleChange change, bool afterPublication)
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var originalResult = view.Result!;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        CompilerHostRequest? nextRequest = null;
        CompilerHostResponse? nextResponse = null;
        if (change == LifecycleChange.NewInput)
        {
            nextRequest = new CompilerHostRequest(new CompilerInputTransfer(LifecycleInput(102)), 2);
            Assert.NotEqual(fixture.Request.Input.ExpectedInputDigest, nextRequest.Input.ExpectedInputDigest);
            nextResponse = await CompileLifecycleRequest(nextRequest);
        }
        ProjectCompilerOutputSnapshot? newer = null;
        using var notification = new OutputNotification(view, afterPublication, () =>
        {
            switch (change)
            {
                case LifecycleChange.Reset: view.Reset(); break;
                case LifecycleChange.Dispose: view.Dispose(); break;
                case LifecycleChange.NewInput:
                    view.BeginSynthetic(nextRequest!);
                    Assert.False(view.TryAccept(fixture.Response));
                    Assert.True(view.TryAccept(nextResponse!));
                    Assert.Null(view.CompilerOutputSnapshot);
                    Assert.True(view.LoadSyntheticScopedSources());
                    newer = ReadLifecycleBodies(view);
                    break;
                case LifecycleChange.Readmit:
                    Assert.True(view.LoadSyntheticScopedSources());
                    Assert.Null(view.CompilerOutputSnapshot);
                    newer = ReadLifecycleBodies(view);
                    break;
                default: throw new InvalidOperationException("Unknown independent lifecycle case.");
            }
        });
        Assert.False(view.ReadSyntheticCompilerOutputs(originalResult.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes));
        Assert.True(notification.Fired);
        if (change is LifecycleChange.Reset or LifecycleChange.Dispose)
        {
            Assert.Null(view.CompilerOutputSnapshot);
            Assert.Null(view.Result);
        }
        else
        {
            Assert.NotNull(newer);
            Assert.NotSame(retained.Snapshot, newer);
            Assert.Same(newer, view.CompilerOutputSnapshot);
            Assert.Same(change == LifecycleChange.NewInput ? nextResponse!.Compilation!.Result : originalResult, view.Result);
            Assert.Equal(view.Result!.Input.InputDigest, newer.InputDigest);
            if (change == LifecycleChange.NewInput)
                Assert.NotEqual(retained.Snapshot.InputDigest, newer.InputDigest);
        }
        retained.AssertUnchanged();
    }

    private static async Task VerifyCancelledLifecycleQuery(bool afterPublication, string replacement)
    {
        using var fixture = await LifecycleFixture.Create();
        var view = fixture.View;
        var result = view.Result!;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedOutput(ReadLifecycleBodies(view));
        using var cancellation = new CancellationTokenSource();
        ProjectCompilerOutputSnapshot? newer = null;
        using var notification = new OutputNotification(view, afterPublication, () =>
        {
            var interrupted = view.CompilerOutputSnapshot;
            if (replacement == "admitted") newer = ReadLifecycleBodies(view);
            else if (replacement == "refused")
            {
                Assert.False(view.ReadSyntheticCompilerOutputs(fixture.Request.Input.ExpectedInputDigest,
                    CompilerOutputSnapshotMode.AvailableOutputs));
                newer = Assert.IsType<ProjectCompilerOutputSnapshot>(view.CompilerOutputSnapshot);
                AssertUnboundLifecycleRefusal(newer);
            }
            else Assert.Equal("none", replacement);
            if (newer is not null)
            {
                Assert.NotSame(retained.Snapshot, newer);
                Assert.NotSame(interrupted, newer);
            }
            cancellation.Cancel();
        });
        var error = Record.Exception(() => view.ReadSyntheticCompilerOutputs(result.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes,
            cancellation.Token));
        Assert.True(notification.Fired);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.IsType<OperationCanceledException>(error);
        if (replacement == "none") Assert.Null(view.CompilerOutputSnapshot);
        else
        {
            Assert.NotNull(newer);
            Assert.Same(newer, view.CompilerOutputSnapshot);
            if (replacement == "refused") AssertUnboundLifecycleRefusal(newer);
            else Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, newer.State);
        }
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        retained.AssertUnchanged();
    }

    private static void AssertUnboundLifecycleRefusal(ProjectCompilerOutputSnapshot snapshot)
    {
        Assert.Equal(CompilerOutputSnapshotState.RefusedWrongResult, snapshot.State);
        Assert.Empty(snapshot.Bodies);
        Assert.Null(snapshot.InputDigest);
        Assert.Null(snapshot.SourceDigest);
        Assert.Null(snapshot.ResultDigest);
        Assert.Null(snapshot.Context);
        foreach (var field in new[] { "basis", "result", "desired", "freshness", "stages", "outputs" })
            Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty(field).ValueKind);
    }

    private static ProjectCompilerOutputSnapshot ReadLifecycleBodies(SyntheticWorkbenchViewModel view)
    {
        var result = Assert.IsType<CompilerResult>(view.Result);
        Assert.True(view.ReadSyntheticCompilerOutputs(result.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes));
        var snapshot = Assert.IsType<ProjectCompilerOutputSnapshot>(view.CompilerOutputSnapshot);
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, snapshot.State);
        Assert.Equal(new[] { "coglatas.ir", "coglatas.semantic-model", "coglatas.source-map" },
            snapshot.Bodies.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(result.ResultDigest, snapshot.ResultDigest);
        Assert.True(snapshot.RetainedBodyBytes > 0);
        return snapshot;
    }

    private sealed class RetainedOutput(ProjectCompilerOutputSnapshot snapshot)
    {
        public ProjectCompilerOutputSnapshot Snapshot { get; } = snapshot;
        private readonly byte[] _data = snapshot.Data.ToCanonicalBytes();
        private readonly ContentDigest _digest = snapshot.Digest;
        private readonly Dictionary<string, byte[]> _bodies = snapshot.Bodies.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToCanonicalBytes(), StringComparer.Ordinal);
        public void AssertUnchanged()
        {
            Assert.Equal(_data, Snapshot.Data.ToCanonicalBytes());
            Assert.Equal(_digest, Snapshot.Digest);
            Assert.Equal(3, Snapshot.Bodies.Count);
            foreach (var pair in _bodies) Assert.Equal(pair.Value, Snapshot.Bodies[pair.Key].ToCanonicalBytes());
            Assert.Equal("NotAdopted", Snapshot.Data.Value.GetProperty("retentionPolicy").GetString());
        }
    }

    private sealed class OutputNotification : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly bool _afterPublication;
        private readonly Action _action;
        public bool Fired { get; private set; }
        public OutputNotification(SyntheticWorkbenchViewModel view, bool afterPublication, Action action)
        {
            _view = view; _afterPublication = afterPublication; _action = action;
            view.PropertyChanged += Changed;
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || (_view.CompilerOutputSnapshot is not null) != _afterPublication) return;
            Assert.Same(_view, sender);
            Fired = true;
            _action();
        }
        public void Dispose() => _view.PropertyChanged -= Changed;
    }

    private sealed class LifecycleFixture(SyntheticWorkbenchViewModel view, CompilerHostRequest request,
        CompilerHostResponse response) : IDisposable
    {
        public SyntheticWorkbenchViewModel View { get; } = view;
        public CompilerHostRequest Request { get; } = request;
        public CompilerHostResponse Response { get; } = response;
        public static async Task<LifecycleFixture> Create(bool admitSources = true)
        {
            var request = new CompilerHostRequest(new CompilerInputTransfer(LifecycleInput(101)), 1);
            var response = await CompileLifecycleRequest(request);
            var compilation = Assert.IsType<CoreCompilation>(response.Compilation);
            var view = new SyntheticWorkbenchViewModel();
            view.BeginSynthetic(request);
            Assert.True(view.TryAccept(response));
            Assert.Same(compilation.Result, view.Result);
            if (admitSources)
            {
                Assert.True(view.LoadSyntheticScopedSources());
                Assert.Same(compilation.Publication!.ExecutionPlan, view.ScopedExecutionPlan);
            }
            return new(view, request, response);
        }
        public void Dispose() => View.Dispose();
    }

    private static async Task<CompilerHostResponse> CompileLifecycleRequest(CompilerHostRequest request)
    {
        var input = request.Input.Materialize();
        CompilerArtifactIdentity Contract(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Contract("schema"), Contract("ruleset"),
            Contract("semantics"), Contract("ir"), Contract("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()),
            CompilerExecutionPlan.Engine);
        var response = await new LocalNetCompilerHost(profile).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status);
        Assert.NotNull(response.Execution);
        var compilation = Assert.IsType<CoreCompilation>(response.Compilation);
        Assert.Equal(CompilerExecutionState.Completed, compilation.Result.Execution);
        Assert.NotNull(compilation.Publication);
        Assert.True(compilation.Publication!.IsDeclaredBy(compilation.Result));
        Assert.NotNull(compilation.Publication.ExecutionPlan);
        return response;
    }

    private static SourceJson LifecycleJson(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static string LifecycleId(int value) => new Guid(value, 0, 0x4000, 0x90, 0, 0, 0, 0, 0, 0, 9).ToString();

    private static CompilerInputManifest LifecycleInput(int revision)
    {
        // Independent public-API fixture: one declared Task, no imported Product test setup or authored results.
        var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(LifecycleId(1)),
            ProjectId.Parse(LifecycleId(2)), BranchId.Parse(LifecycleId(3))), RevisionId.Parse(LifecycleId(revision))));
        var document = SourceDocument.Create(DocumentId.Parse(LifecycleId(20)), "security-output-lifecycle",
            LifecycleJson(new { entityId = LifecycleId(40), kind = "coglatas.project", schemaVersion = 1, payload = new { } }));
        var declarations = LifecycleJson(new[] { new { entityId = LifecycleId(30), documentId = LifecycleId(20),
            kind = "coglatas.task", schemaVersion = 1, payload = new {
                duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                    state = "coglatas.known", value = "11" }, ports = Array.Empty<object>() } } });
        var source = ProjectSource.Create(context, [document], declarations, LifecycleJson(Array.Empty<object>()));
        object Artifact(string id, string version = "1") => new { artifactId = id, version,
            digest = new { algorithm = "sha-256", value = new string('c', 64) } };
        var settings = LifecycleJson(new {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.compiler"),
            ruleset = Artifact("coglatas.ruleset"), semantics = Artifact("coglatas.semantic-model", "2"),
            ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.evaluator"),
            units = Array.Empty<object>(),
            scheduler = new { kind = "coglatas.plan-lowering", artifact = CompilerExecutionPlan.Engine.Data.Value },
            solver = new { kind = "coglatas.none" }, stochastic = new { kind = "coglatas.none" },
            importer = new { kind = "coglatas.none" }, scenario = new { kind = "coglatas.none" },
            authorizedExternalSnapshots = Array.Empty<object>(), resourceSnapshots = Array.Empty<object>(),
            allocationSnapshots = Array.Empty<object>(), observations = Array.Empty<object>(), evidence = Array.Empty<object>(),
            calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }).ToArray(),
            rules = new[] { "coglatas.core.schema", "coglatas.core.declarations", "coglatas.core.relations" }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities)
                .Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model", CompilerExecutionPlan.Target },
            analysisScope = new[] { "coglatas.source-structure" }, sourceCoverage = "coglatas.complete",
            clock = new { kind = "coglatas.not-applicable" },
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = 200000, maximumDeclarations = 10000,
                maximumRelations = 20000, maximumDigits = 256, maximumScale = 256, rounding = "coglatas.ties-to-even" }
        });
        return new(source, settings);
    }
}
