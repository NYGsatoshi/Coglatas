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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ThrowingObserverWithdrawsOnlyItsCancelledPassView(bool readBoundary, bool replace) =>
        VerifyPassViewWithdrawal(readBoundary, replace, throwOnPublication: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThrowingPassViewWithdrawalObserverPreservesCancellation(bool readBoundary) =>
        VerifyPassViewWithdrawal(readBoundary, replace: false, throwOnPublication: false);

    private static async Task VerifyPassViewWithdrawal(bool readBoundary, bool replace, bool throwOnPublication)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var view = new SyntheticWorkbenchViewModel();
        var actual = await Prepare(view, session);
        Assert.True(Read(view));
        var previousBoundary = view.RecordedPassBoundary!;
        Assert.True(view.ReadPassBoundarySourcePage(maximumTargets: 1));
        var previousPage = view.PassBoundarySourcePage!;
        var previousTarget = Assert.Single(previousPage.Targets);
        Assert.True(previousPage.TotalTargets > 1);
        Assert.True(view.NavigatePassBoundarySource(previousPage, previousTarget));
        var root = view.RootSource;
        var result = view.Result!;
        var receiptData = actual.Receipt.Data;

        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        Action cancel = cancellation.Cancel;
        var publicationError = new InvalidOperationException("Pass view publication observer failed");
        var withdrawalError = new InvalidOperationException("Pass view withdrawal observer failed");
        var published = false;
        var withdrawalCount = 0;
        var deliveredWithdrawals = new List<(string? PropertyName, ProjectCompilerPassBoundarySnapshot? Boundary,
            ProjectLocation? Target, string NavigationState)>();
        ProjectCompilerPassBoundarySnapshot? observedBoundary = null;
        SyntheticPassSourcePage? observedPage = null;
        SyntheticPassSourceTarget? observedTarget = null;

        void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            var publishedView = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (token.IsCancellationRequested)
            {
                withdrawalCount++;
                if (publishedView.PassBoundarySourcePage is null) throw withdrawalError;
                return;
            }
            if (published || (readBoundary ? publishedView.RecordedPassBoundary is null : publishedView.PassBoundarySourcePage is null)) return;
            published = true;
            if (readBoundary && replace) Assert.True(Read(publishedView));
            observedBoundary = publishedView.RecordedPassBoundary;
            if (readBoundary || replace)
                Assert.True(publishedView.ReadPassBoundarySourcePage(replace ? 1 : 0, 1));
            observedPage = publishedView.PassBoundarySourcePage!;
            observedTarget = Assert.Single(observedPage.Targets);
            Assert.True(publishedView.NavigatePassBoundarySource(observedPage, observedTarget));
            cancel();
            if (throwOnPublication) throw publicationError;
        }

        void RecordWithdrawal(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (token.IsCancellationRequested && sender is SyntheticWorkbenchViewModel publishedView &&
                publishedView.PassBoundarySourcePage is null)
            {
                deliveredWithdrawals.Add((args.PropertyName, publishedView.RecordedPassBoundary,
                    publishedView.SourceTarget, publishedView.NavigationState));
            }
        }

        view.PropertyChanged += Observe;
        view.PropertyChanged += RecordWithdrawal;
        Exception? error;
        try
        {
            error = Record.Exception(() =>
            {
                if (readBoundary)
                    view.ReadRecordedPassBoundary(result.ResultDigest, 5, CompilerPassObservationSnapshotMode.AvailableOriginalJson, token: token);
                else
                    view.ReadPassBoundarySourcePage(maximumTargets: 1, token: token);
            });
        }
        finally
        {
            view.PropertyChanged -= Observe;
            view.PropertyChanged -= RecordWithdrawal;
        }

        if (throwOnPublication) Assert.Same(publicationError, error);
        else
        {
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            Assert.Same(withdrawalError, cancelled.InnerException);
        }
        Assert.True(published);
        Assert.Equal(replace ? 0 : 1, deliveredWithdrawals.Count);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Same(root, view.RootSource);
        Assert.Same(result, view.Result);
        Assert.Equal(receiptData, actual.Receipt.Data);
        Assert.NotEmpty(previousBoundary.Bodies);
        Assert.Same(previousTarget, Assert.Single(previousPage.Targets));
        if (replace)
        {
            Assert.Same(observedBoundary, view.RecordedPassBoundary);
            Assert.Same(observedPage, view.PassBoundarySourcePage);
            Assert.Equal(1, observedPage!.Offset);
            Assert.Same(observedTarget!.Location, view.SourceTarget);
            Assert.Equal("ready", view.NavigationState);
            Assert.Equal(0, withdrawalCount);
        }
        else
        {
            var withdrawal = Assert.Single(deliveredWithdrawals);
            Assert.Null(withdrawal.PropertyName);
            Assert.Same(readBoundary ? null : previousBoundary, withdrawal.Boundary);
            Assert.Null(withdrawal.Target);
            Assert.Empty(withdrawal.NavigationState);
            if (readBoundary) Assert.True(view.RecordedPassBoundary is null, "The cancelled pass boundary remained visible.");
            else Assert.Same(previousBoundary, view.RecordedPassBoundary);
            Assert.True(view.PassBoundarySourcePage is null, "The cancelled pass Source page remained visible.");
            Assert.Null(view.SourceTarget);
            Assert.Empty(view.NavigationState);
            Assert.Equal(1, withdrawalCount);
        }
    }

    private sealed class CancelPagePublication : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _replace;
        private int _notifications;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public bool PageOpened { get; private set; }
        public SyntheticPassSourcePage? Published { get; private set; }
        public CancelPagePublication(SyntheticWorkbenchViewModel vm, bool replace)
        { _vm = vm; _replace = replace; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ReadPassBoundarySourcePage(maximumTargets: 1, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace) ReplacementSucceeded = _vm.ReadPassBoundarySourcePage(1, 1);
            Published = _vm.PassBoundarySourcePage;
            if (Published is { } page && page.Targets.Count == 1) PageOpened = _vm.NavigatePassBoundarySource(page, page.Targets[0]);
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAtSourcePagePublicationWithdrawsOnlyItsOwnedPageAndTarget(bool replace)
    {
        var (_, _, profile) = SyntheticOperatorRecordingTests.Input("pass");
        using var session = new LocalPassObservationCompilerRecordingSession(profile);
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Prepare(vm, session); Assert.True(Read(vm));
        var result = vm.Result!; var resultData = result.Data; var receiptData = actual.Receipt.Data;
        var boundary = vm.RecordedPassBoundary!; var boundaryData = boundary.Data; var root = vm.RootSource!; var rootData = root.Data;
        Assert.True(vm.ReadPassBoundarySourcePage(maximumTargets: 1));
        var previous = vm.PassBoundarySourcePage!; var previousTarget = Assert.Single(previous.Targets);
        Assert.True(previous.TotalTargets > 1);
        using var cancellation = new CancelPagePublication(vm, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested); Assert.True(cancellation.PageOpened);
        Assert.Same(result, vm.Result); Assert.Equal(resultData, result.Data); Assert.Equal(receiptData, actual.Receipt.Data);
        Assert.Same(boundary, vm.RecordedPassBoundary); Assert.Equal(boundaryData, boundary.Data);
        Assert.Same(root, vm.RootSource); Assert.Equal(rootData, root.Data); Assert.Same(previousTarget, Assert.Single(previous.Targets));
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Published);
            Assert.Same(cancellation.Published, vm.PassBoundarySourcePage); Assert.Equal(1, vm.PassBoundarySourcePage!.Offset);
            Assert.Same(vm.PassBoundarySourcePage.Targets[0].Location, vm.SourceTarget); Assert.Equal("ready", vm.NavigationState);
        }
        else { Assert.Null(vm.PassBoundarySourcePage); Assert.Null(vm.SourceTarget); }
        Assert.IsType<OperationCanceledException>(error);
    }
}
