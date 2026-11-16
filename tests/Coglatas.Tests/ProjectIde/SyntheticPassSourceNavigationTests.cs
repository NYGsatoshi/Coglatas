using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticPassSourceNavigationTests
{
    public static IEnumerable<object[]> Keys() => SyntheticOperatorRecordingTests.Keys();
    private static async Task<PassObservationCompilerRecordedInvocation> Prepare(SyntheticWorkbenchViewModel vm,
        LocalPassObservationCompilerRecordingSession session, string key = "pass")
    {
        var (_, previous, _) = SyntheticOperatorRecordingTests.Input(key);
        var request = new PassObservationCompilerHostRequest(previous.Core, new(CompilerPassObservationMode.AvailableOriginalJson));
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginPassSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(vm.TryAcceptPass(actual));
        Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); return actual;
    }
    private static bool Read(SyntheticWorkbenchViewModel vm, int ordinal = 5) =>
        vm.ReadRecordedPassBoundary(vm.Result!.ResultDigest, ordinal, CompilerPassObservationSnapshotMode.AvailableOriginalJson);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task OnlyAnExplicitlyReadOriginalMapProducesBoundedOriginalOrderTargets(string key)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input(key);
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel(); var actual = await Prepare(vm, session, key);
        Assert.False(vm.ReadPassBoundarySourcePage()); Assert.Null(vm.PassBoundarySourcePage);
        Assert.True(vm.ReadRecordedPassBoundary(vm.Result!.ResultDigest, 5)); Assert.False(vm.ReadPassBoundarySourcePage());
        Assert.True(Read(vm));
        if (key == "test-assertion-navigation")
        {
            Assert.Null(actual.Response.Execution!.Compilation.Validation!.Ir);
            Assert.False(vm.ReadPassBoundarySourcePage()); Assert.Null(vm.PassBoundarySourcePage); return;
        }
        var ir = Assert.IsType<DerivedProjectIr>(actual.Response.Execution!.Compilation.Validation!.Ir);
        Assert.True(vm.ReadPassBoundarySourcePage(maximumTargets: 1)); var first = vm.PassBoundarySourcePage!;
        Assert.Single(first.Targets); Assert.Equal(0, first.Offset); Assert.Equal(1, first.MaximumTargets);
        var nodes = ir.SourceMap.Value.GetProperty("nodes"); var relations = ir.SourceMap.Value.GetProperty("relations");
        Assert.Equal(nodes.GetArrayLength() + relations.GetArrayLength(), first.TotalTargets);
        Assert.Equal(ir.SourceMapDigest, first.OriginalMapDigest); Assert.Equal(vm.RecordedPassBoundary!.OriginalRecordDigest, first.OriginalRecordDigest);
        Assert.Equal(nodes[0].GetProperty("irNodeId").GetString(), first.Targets[0].Identity);
        Assert.Equal(SourceJson.Parse(nodes[0].GetProperty("location").GetRawText()), first.Targets[0].Location.Data);
        Assert.True(vm.NavigatePassBoundarySource(first, first.Targets[0])); Assert.Same(first.Targets[0].Location, vm.SourceTarget);
        Assert.True(vm.ReadPassBoundarySourcePage(nodes.GetArrayLength(), 1)); var relation = vm.PassBoundarySourcePage!;
        Assert.Null(vm.SourceTarget);
        if (relations.GetArrayLength() == 0) Assert.Empty(relation.Targets);
        else
        {
            Assert.Single(relation.Targets); Assert.Equal("relation", relation.Targets[0].Kind);
            Assert.Equal(relations[0].GetProperty("relationId").GetString(), relation.Targets[0].Identity);
            Assert.True(vm.NavigatePassBoundarySource(relation, relation.Targets[0])); Assert.Same(relation.Targets[0].Location, vm.SourceTarget);
        }
        Assert.False(vm.NavigatePassBoundarySource(first, first.Targets[0])); Assert.Null(vm.SourceTarget);
        Assert.False(vm.NavigatePassBoundarySource(relation, first.Targets[0])); Assert.Null(vm.SourceTarget);
        Assert.True(vm.ReadPassBoundarySourcePage(int.MaxValue, 1)); Assert.Empty(vm.PassBoundarySourcePage!.Targets);
        Assert.NotEmpty(first.Targets); Assert.True(Read(vm, 0)); Assert.False(vm.ReadPassBoundarySourcePage());
    }

    [Theory]
    [InlineData(1, "source")] [InlineData(2, "source")]
    [InlineData(1, "body")] [InlineData(2, "body")]
    [InlineData(1, "reset")] [InlineData(2, "reset")]
    [InlineData(1, "dispose")] [InlineData(2, "dispose")]
    public async Task PagePublicationCannotSurviveAReentrantSourceOrBoundaryChange(int notification, string action)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel(); await Prepare(vm, session); Assert.True(Read(vm));
        var digest = vm.Result!.ResultDigest; var count = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired || ++count != notification) return; fired = true;
            switch (action) { case "source": Assert.True(current.LoadSyntheticRootSource(digest)); break;
                case "body": Assert.True(Read(current, 0)); break; case "reset": current.Reset(); break; default: current.Dispose(); break; }
        };
        Assert.False(vm.ReadPassBoundarySourcePage()); Assert.True(fired); Assert.Null(vm.PassBoundarySourcePage); Assert.Null(vm.SourceTarget);
    }

    [Theory]
    [InlineData("source")] [InlineData("body")] [InlineData("page")] [InlineData("reset")] [InlineData("dispose")]
    public async Task NavigationPublicationRechecksTheOriginalPageAndClearsOnlyItsOwnTarget(string action)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel(); await Prepare(vm, session); Assert.True(Read(vm));
        Assert.True(vm.ReadPassBoundarySourcePage()); var page = vm.PassBoundarySourcePage!; var digest = vm.Result!.ResultDigest;
        var fired = false; vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired) return; fired = true;
            switch (action) { case "source": Assert.True(current.LoadSyntheticRootSource(digest)); break;
                case "body": Assert.True(Read(current, 0)); break; case "page": Assert.True(current.ReadPassBoundarySourcePage(1)); break;
                case "reset": current.Reset(); break; default: current.Dispose(); break; }
        };
        Assert.False(vm.NavigatePassBoundarySource(page, page.Targets[0])); Assert.True(fired); Assert.Null(vm.SourceTarget);
        Assert.NotEmpty(page.Targets);
    }

    [Fact]
    public async Task BodyGapsInvalidBoundsCancellationAndForeignPagesNeverCreateSourceNavigation()
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        using var other = new SyntheticWorkbenchViewModel();
        await Prepare(vm, session);
        await Prepare(other, session);
        Assert.True(Read(vm)); Assert.True(Read(other));
        Assert.True(vm.ReadPassBoundarySourcePage()); var page = vm.PassBoundarySourcePage!;
        Assert.False(other.NavigatePassBoundarySource(page, page.Targets[0])); Assert.Null(other.SourceTarget);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => vm.ReadPassBoundarySourcePage(token: cancelled.Token)); Assert.Null(vm.PassBoundarySourcePage);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.ReadPassBoundarySourcePage(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.ReadPassBoundarySourcePage(maximumTargets: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.ReadPassBoundarySourcePage(maximumTargets: 257));
        Assert.True(vm.ReadRecordedPassBoundary(vm.Result!.ResultDigest, 5, CompilerPassObservationSnapshotMode.AvailableOriginalJson, 1));
        Assert.False(vm.ReadPassBoundarySourcePage()); Assert.Null(vm.PassBoundarySourcePage);
        Assert.True(Read(vm)); Assert.True(vm.ReadPassBoundarySourcePage()); var current = vm.PassBoundarySourcePage!;
        Assert.True(vm.NavigatePassBoundarySource(current, current.Targets[0]));
        Assert.True(vm.ReadPassRecordingMetadata()); Assert.Null(vm.SourceTarget); Assert.Null(vm.PassBoundarySourcePage);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task NewerReentrantPageWinsWithoutRevivingTheOlderPage(int notification)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel(); await Prepare(vm, session); Assert.True(Read(vm));
        var count = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired || ++count != notification) return; fired = true; Assert.True(current.ReadPassBoundarySourcePage(1, 1));
        };
        Assert.False(vm.ReadPassBoundarySourcePage()); Assert.True(fired); Assert.Equal(1, vm.PassBoundarySourcePage!.Offset);
        Assert.Single(vm.PassBoundarySourcePage.Targets);
    }

    [Fact]
    public async Task ANewerNavigationOwnsTheTargetAfterTheOlderPublicationCallback()
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel(); await Prepare(vm, session); Assert.True(Read(vm)); Assert.True(vm.ReadPassBoundarySourcePage());
        var page = vm.PassBoundarySourcePage!; Assert.True(page.Targets.Count > 1); var fired = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired) return; fired = true; Assert.True(current.NavigatePassBoundarySource(page, page.Targets[1]));
        };
        Assert.False(vm.NavigatePassBoundarySource(page, page.Targets[0])); Assert.Same(page.Targets[1].Location, vm.SourceTarget);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.ReadPassBoundarySourcePage(-1)); Assert.Null(vm.SourceTarget); Assert.Null(vm.PassBoundarySourcePage);
    }

    private static void AttemptLegacyTargetWrite(SyntheticPassSourcePage page, SyntheticPassSourceTarget replacement)
    {
        if (page.Targets is System.Collections.ICollection view &&
            view.SyncRoot is IList<SyntheticPassSourceTarget> storage && storage.Count > 0)
        {
            try { storage[0] = replacement; }
            catch (NotSupportedException) { }
        }
    }

    [Fact]
    public async Task PassTargetCollectionsKeepTheirOriginalRosterAfterLegacyStorageWrites()
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        using var other = new SyntheticWorkbenchViewModel();

        await Prepare(vm, session); await Prepare(other, session); Assert.True(Read(vm)); Assert.True(Read(other));
        Assert.True(vm.ReadPassBoundarySourcePage(maximumTargets: 1));
        Assert.True(other.ReadPassBoundarySourcePage(maximumTargets: 1));
        var page = vm.PassBoundarySourcePage!; var original = Assert.Single(page.Targets);
        var foreign = Assert.Single(other.PassBoundarySourcePage!.Targets); Assert.NotSame(original, foreign);
        if (page.Targets is IList<SyntheticPassSourceTarget> direct)
            Assert.Throws<NotSupportedException>(() => direct[0] = foreign);
        AttemptLegacyTargetWrite(page, foreign);
        Assert.Same(original, Assert.Single(page.Targets));
    }

    [Fact]
    public async Task LegacyStorageCannotMakeAForeignRunTargetBelongToTheCurrentPage()
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        using var other = new SyntheticWorkbenchViewModel();

        await Prepare(vm, session); await Prepare(other, session); Assert.True(Read(vm)); Assert.True(Read(other));
        Assert.True(vm.ReadPassBoundarySourcePage(maximumTargets: 1));
        Assert.True(other.ReadPassBoundarySourcePage(maximumTargets: 1));
        var page = vm.PassBoundarySourcePage!; var original = Assert.Single(page.Targets);
        var foreign = Assert.Single(other.PassBoundarySourcePage!.Targets);
        Assert.False(vm.NavigatePassBoundarySource(page, foreign)); Assert.Null(vm.SourceTarget);
        Assert.Same(page, vm.PassBoundarySourcePage);
        AttemptLegacyTargetWrite(page, foreign);
        Assert.False(vm.NavigatePassBoundarySource(page, foreign)); Assert.Null(vm.SourceTarget);
        Assert.True(vm.NavigatePassBoundarySource(page, original)); Assert.Same(original.Location, vm.SourceTarget);
    }
}
