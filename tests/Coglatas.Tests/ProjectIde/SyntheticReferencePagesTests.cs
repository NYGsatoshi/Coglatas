using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticReferencePagesTests
{
    private static async Task<SyntheticWorkbenchViewModel> Ready(bool readRoot = true)
    {
        var original = Source(); var declarations = new JsonArray();
        for (var ordinal = 0; ordinal < 35; ordinal++)
        {
            var requirement = CompilerAnalysisFixtures.RequirementDeclaration(original.Context);
            requirement["entityId"] = Id(10 + ordinal); declarations.Add(requirement);
        }
        var input = Input(CompilerAnalysisFixtures.AllRulesSettings(),
            ProjectSource.Create(original.Context, original.Documents, Json(declarations)));
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"),
            Artifact("ir"), Artifact("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var request = new CompilerHostRequest(new(input), 1); var vm = new SyntheticWorkbenchViewModel();
        try
        {
            vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await new LocalNetCompilerHost(profile).CompileAsync(request)));
            if (readRoot) Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest));
            var root = Assert.Single(vm.Inspection!.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(6)));
            Assert.True(vm.Select(root.Symbol.Location)); return vm;
        }
        catch { vm.Dispose(); throw; }
    }

    [Fact]
    public async Task PagesRetainTheSelectedSymbolAndOnlyNavigateOwnedOriginalReferences()
    {
        using var vm = await Ready();

        var symbol = vm.Selected!; var actual = vm.Inspection!.FindReferences(symbol.Symbol.Location);
        var first = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage());
        Assert.Equal(32, first.References.Count); Assert.NotNull(first.NextCursor); Assert.False(first.ScanComplete);
        var last = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage(cursor: first.NextCursor));
        Assert.Equal(3, last.References.Count); Assert.True(last.ScanComplete); Assert.True(last.IndexCoverageComplete);
        var rows = first.References.Concat(last.References).ToArray(); Assert.Equal(35, rows.Length);
        for (var ordinal = 0; ordinal < 35; ordinal++) Assert.Same(actual[ordinal], rows[ordinal]);
        Assert.False(vm.NavigateReferencePageOrigin(first, first.References[0])); Assert.Null(vm.SourceTarget);
        Assert.Same(last, vm.ReferencePage);
        Assert.True(vm.NavigateReferencePageOrigin(last, last.References[0]));
        Assert.Same(last.References[0].Origin, vm.SourceTarget); Assert.Same(symbol, vm.Selected); Assert.Same(last, vm.ReferencePage);
        Assert.False(vm.Result!.EvidenceComplete); Assert.NotEqual(CompilerVerdict.Pass, vm.Result.Verdict);
    }

    [Theory]
    [InlineData("root")] [InlineData("selection")] [InlineData("reset")] [InlineData("dispose")] [InlineData("cancel")]
    public async Task AChangedBoundaryOrCancellationWithdrawsThePageAndItsOwnedTarget(string action)
    {
        var vm = await Ready();
        try
        {
            var page = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage());
            Assert.True(vm.NavigateReferencePageOrigin(page, page.References[0]));
            switch (action)
            {
                case "root": Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); break;
                case "selection": Assert.True(vm.Select(vm.Inspection!.Symbols.First(item => item.Symbol.Kind == "coglatas.requirement").Symbol.Location)); break;
                case "reset": vm.Reset(); break;
                case "dispose": vm.Dispose(); break;
                default:
                    var token = new CancellationToken(true);
                    Assert.Throws<OperationCanceledException>(() => vm.ReadReferenceSearchPage(cancellationToken: token)); break;
            }
            Assert.Null(vm.ReferencePage); Assert.Null(vm.SourceTarget);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task ReentrantRootReloadBeforeOrAfterPublicationCannotReviveAnOldPage(int notification)
    {
        using var vm = await Ready();

        var digest = vm.Result!.ResultDigest; var count = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired || ++count != notification) return;
            fired = true; Assert.True(current.LoadSyntheticRootSource(digest));
        };
        Assert.Null(vm.ReadReferenceSearchPage()); Assert.True(fired); Assert.Null(vm.ReferencePage); Assert.Null(vm.SourceTarget);
    }

    [Fact]
    public async Task ANewerReentrantPageQueryWinsAfterTheOldQueryPublishes()
    {
        using var vm = await Ready();

        var first = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage(1)); var cursor = first.NextCursor;
        var expected = vm.Inspection!.FindReferences(vm.Selected!.Symbol.Location)[1]; var count = 0; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired || ++count != 2) return;
            fired = true; Assert.NotNull(current.ReadReferenceSearchPage(1, cursor));
        };
        Assert.Null(vm.ReadReferenceSearchPage(1)); Assert.True(fired);
        Assert.Same(expected, Assert.Single(vm.ReferencePage!.References));
    }

    [Theory]
    [InlineData("page")] [InlineData("root")] [InlineData("ordinary")]
    public async Task NavigationRechecksThePageAndPreservesANewerOrdinaryNavigation(string action)
    {
        using var vm = await Ready();

        var page = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage());
        var rootLocation = vm.Selected!.Symbol.Location; var digest = vm.Result!.ResultDigest; var fired = false;
        vm.PropertyChanged += (sender, _) => {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (fired) return;
            fired = true;
            switch (action)
            {
                case "page": Assert.NotNull(current.ReadReferenceSearchPage(cursor: page.NextCursor)); break;
                case "root": Assert.True(current.LoadSyntheticRootSource(digest)); break;
                default: Assert.True(current.Navigate()); break;
            }
        };
        Assert.False(vm.NavigateReferencePageOrigin(page, page.References[0])); Assert.True(fired);
        if (action == "ordinary") Assert.Same(rootLocation, vm.SourceTarget);
        else Assert.Null(vm.SourceTarget);
    }

    [Fact]
    public async Task ForeignPagesReferencesAndCursorsNeverBecomeCurrentOwnedFacts()
    {
        using var vm = await Ready();
        using var other = await Ready();

        var page = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage(1));
        var foreign = Assert.IsType<ReferenceSearchPage>(other.ReadReferenceSearchPage(1));
        Assert.False(vm.NavigateReferencePageOrigin(foreign, foreign.References[0]));
        Assert.False(vm.NavigateReferencePageOrigin(page, foreign.References[0])); Assert.Null(vm.SourceTarget);
        var rejected = Assert.IsType<ReferenceSearchPage>(vm.ReadReferenceSearchPage(cursor: foreign.NextCursor));
        Assert.Equal(ReferenceSearchState.InvalidCursor, rejected.State); Assert.Empty(rejected.References);
        Assert.Null(rejected.Target); Assert.Null(rejected.InputDigest); Assert.Null(rejected.NextCursor);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.ReadReferenceSearchPage(0)); Assert.Null(vm.ReferencePage);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.ReadReferenceSearchPage(33)); Assert.Null(vm.ReferencePage);
    }

    [Fact]
    public async Task RootSourceMustBeExplicitlyReadBeforeAnyPageIsPublished()
    {
        using var vm = await Ready(false);

        Assert.Null(vm.RootSource); Assert.Null(vm.ReadReferenceSearchPage()); Assert.Null(vm.ReferencePage);
        Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest));
        Assert.NotNull(vm.ReadReferenceSearchPage());
    }

    [Theory]
    [InlineData("page")] [InlineData("ordinary")]
    public async Task CancellationDuringPublicationWithdrawsTheOwnedPageAndPreservesNewerOrdinaryNavigation(string action)
    {
        using var vm = await Ready();
        using var cancellation = new PublicationCancellation(vm, action);

        var root = vm.Selected!.Symbol.Location; var result = vm.Result;
        Assert.Throws<OperationCanceledException>(() => vm.ReadReferenceSearchPage(cancellationToken: cancellation.Token));
        Assert.True(cancellation.Fired); Assert.Same(result, vm.Result);
        Assert.Null(vm.ReferencePage);
        if (action == "ordinary") Assert.Same(root, vm.SourceTarget);
        else Assert.Null(vm.SourceTarget);
    }

    [Theory]
    [InlineData("page")] [InlineData("ordinary")] [InlineData("replacement")]
    public async Task ThrowingObserverWithdrawsOnlyTheCancelledReferencePage(string action)
    {
        using var vm = await Ready();
        using var cancellation = new PublicationCancellation(vm, action, throwDuringPublication: true, throwDuringWithdrawal: true);
        var root = vm.RootSource;
        var result = vm.Result;
        var inspection = vm.Inspection;
        var symbol = vm.Selected!;
        var references = inspection!.FindReferences(symbol.Symbol.Location);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            vm.ReadReferenceSearchPage(cancellationToken: cancellation.Token));

        Assert.Same(cancellation.PublicationError, failure);
        Assert.True(cancellation.Token.IsCancellationRequested);
        var cancelled = Assert.IsType<ReferenceSearchPage>(cancellation.CancelledPage);
        Assert.Same(references[0], cancelled.References[0]);
        Assert.Same(root, vm.RootSource);
        Assert.Same(result, vm.Result);
        Assert.Same(inspection, vm.Inspection);
        Assert.Same(symbol, vm.Selected);
        if (action == "replacement")
        {
            var replacement = Assert.IsType<ReferenceSearchPage>(cancellation.ReplacementPage);
            Assert.Same(references[32], replacement.References[0]);
            Assert.Same(replacement, vm.ReferencePage);
            Assert.Same(replacement.References[0].Origin, vm.SourceTarget);
            Assert.False(cancellation.WithdrawalNotified);
            Assert.Equal(0, cancellation.NotificationsAfterCancellation);
        }
        else
        {
            Assert.Null(vm.ReferencePage);
            Assert.True(cancellation.WithdrawalNotified);
            if (action == "ordinary") Assert.Same(symbol.Symbol.Location, vm.SourceTarget);
            else Assert.Null(vm.SourceTarget);
        }
    }

    [Fact]
    public async Task ThrowingWithdrawalObserverPreservesCancellationAndItsOriginalToken()
    {
        using var vm = await Ready();
        using var cancellation = new PublicationCancellation(vm, "page", throwDuringWithdrawal: true);
        var result = vm.Result;
        var root = vm.RootSource;
        var symbol = vm.Selected;

        var error = Assert.Throws<OperationCanceledException>(() =>
            vm.ReadReferenceSearchPage(cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Same(cancellation.WithdrawalError, error.InnerException);
        Assert.Equal(1, cancellation.NotificationsAfterCancellation);
        Assert.True(cancellation.WithdrawalNotified);
        Assert.NotNull(cancellation.CancelledPage);
        Assert.Null(vm.ReferencePage);
        Assert.Null(vm.SourceTarget);
        Assert.Same(result, vm.Result);
        Assert.Same(root, vm.RootSource);
        Assert.Same(symbol, vm.Selected);
    }

    private sealed class PublicationCancellation : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _viewModel;
        private readonly CancellationTokenSource _source = new();
        private readonly string _action;
        private readonly bool _throwDuringPublication;
        private readonly bool _throwDuringWithdrawal;
        public bool Fired { get; private set; }
        public bool WithdrawalNotified { get; private set; }
        public int NotificationsAfterCancellation { get; private set; }
        public ReferenceSearchPage? CancelledPage { get; private set; }
        public ReferenceSearchPage? ReplacementPage { get; private set; }
        public InvalidOperationException PublicationError { get; } = new("Reference page publication failed.");
        public InvalidOperationException WithdrawalError { get; } = new("Reference page withdrawal failed.");
        public CancellationToken Token => _source.Token;
        internal PublicationCancellation(SyntheticWorkbenchViewModel viewModel, string action, bool throwDuringPublication = false,
            bool throwDuringWithdrawal = false)
        {
            _viewModel = viewModel;
            _action = action;
            _throwDuringPublication = throwDuringPublication;
            _throwDuringWithdrawal = throwDuringWithdrawal;
            _viewModel.PropertyChanged += Changed;
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (Fired)
            {
                if (!_source.IsCancellationRequested) return;
                NotificationsAfterCancellation++;
                if (current.ReferencePage is null)
                {
                    WithdrawalNotified = true;
                    if (_throwDuringWithdrawal) throw WithdrawalError;
                }
                return;
            }
            if (current.ReferencePage is not { } page) return;
            Fired = true;
            CancelledPage = page;
            Assert.True(current.NavigateReferencePageOrigin(page, page.References[0]));
            if (_action == "ordinary") Assert.True(current.Navigate());
            if (_action == "replacement")
            {
                var cursor = Assert.IsType<ReferenceSearchCursor>(page.NextCursor);
                var replacement = Assert.IsType<ReferenceSearchPage>(current.ReadReferenceSearchPage(cursor: cursor));
                Assert.True(current.NavigateReferencePageOrigin(replacement, replacement.References[0]));
                ReplacementPage = replacement;
            }
            _source.Cancel();
            if (_throwDuringPublication) throw PublicationError;
        }
        public void Dispose()
        {
            _viewModel.PropertyChanged -= Changed;
            _source.Dispose();
        }
    }

}
