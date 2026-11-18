using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticProjectTestDiscoveryTests
{
    private static CompilerInputManifest Input(string key)
    {
        using var item = JsonDocument.Parse((key == "tests-discovery-unavailable" ? UnavailableCase() : WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == key)));
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.RootElement.GetProperty("sourceText").GetString()!)).RequireSource();
        return CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.RootElement.GetProperty("manifest").GetRawText()));
    }
    private static string UnavailableCase()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "test-discovery-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The actual Core discovery fixture is required.");
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "test-discovery-v1.json")));
        return data.RootElement.GetProperty("cases")[0].GetRawText();
    }
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }
    private static async Task<SyntheticWorkbenchViewModel> Ready(string key = "tests-fail")
    {
        var input = Input(key); var request = new CompilerHostRequest(new(input), 1);
        var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request))); return view;
    }

    [Theory]
    [InlineData("tests-pass")] [InlineData("tests-fail")] [InlineData("tests-unknown")]
    [InlineData("tests-empty")] [InlineData("tests-unsupported")] [InlineData("tests-cancelled")]
    [InlineData("tests-evaluator-mismatch")] [InlineData("tests-error")] [InlineData("tests-exhausted")]
    [InlineData("tests-unicode")] [InlineData("tests-many")] [InlineData("tests-discovery-unavailable")]
    public async Task CurrentLocalInventoryUsesCoreWithoutExecutingOrChangingTheCompiler(string key)
    {
        using var view = await Ready(key); var compilation = view.Result;
        var limits = key == "tests-exhausted" ? new ProjectTestLimits(256, 1) : null;
        var inventory = Assert.IsType<ProjectTestDiscovery>(view.DiscoverSyntheticTests(limits: limits));
        Assert.Same(inventory, view.TestDiscovery); Assert.Null(view.TestRun); Assert.Same(compilation, view.Result);
        if (key is "tests-discovery-unavailable" or "tests-exhausted")
        {
            Assert.Equal(key == "tests-exhausted" ? ProjectTestDiscoveryState.ResourceLimit : ProjectTestDiscoveryState.Unavailable, inventory.State);
            Assert.Empty(inventory.Tests); Assert.Null(inventory.InputDigest); Assert.Null(inventory.PlanDigest);
            Assert.Equal(0, inventory.DiscoveredTests); Assert.False(inventory.BindingComplete); Assert.False(inventory.EvaluatorMatches);
        }
        var input = compilation!.Input;
        var expected = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), SyntheticProjectTestConfiguration.Evaluator, limits)
            .Discover(input.InputDigest);
        Assert.Equal(expected.State, inventory.State); Assert.Equal(expected.InputDigest, inventory.InputDigest);
        Assert.Equal(expected.PlanDigest, inventory.PlanDigest); Assert.Equal(expected.BindingComplete, inventory.BindingComplete);
        Assert.Equal(expected.EvaluatorMatches, inventory.EvaluatorMatches); Assert.Equal(expected.DiscoveredTests, inventory.DiscoveredTests);
        Assert.Equal(expected.Tests.Select(row => (row.TestId, row.Location.Data, row.DisplayName, row.DecodeStatus, row.AssertionCount)),
            inventory.Tests.Select(row => (row.TestId, row.Location.Data, row.DisplayName, row.DecodeStatus, row.AssertionCount)));
        foreach (var row in inventory.Tests)
        {
            Assert.True(view.SelectDiscoveredTest(row)); Assert.True(view.Navigate()); Assert.Equal(row.Location, view.SourceTarget);
        }
    }

    [Fact]
    public async Task InventoryPreservesAnExistingKnownFailureAndRunningAgainClearsTheInventory()
    {
        using var view = await Ready(); Assert.True(view.RunSyntheticTests());
        var run = view.TestRun; var compilation = view.Result; Assert.Equal(CompilerVerdict.Fail, run!.Verdict);
        var inventory = Assert.IsType<ProjectTestDiscovery>(view.DiscoverSyntheticTests());
        Assert.Same(run, view.TestRun); Assert.Same(compilation, view.Result);
        var row = Assert.Single(inventory.Tests); Assert.True(view.SelectDiscoveredTest(row)); Assert.True(view.Navigate());
        Assert.Same(row, view.SelectedDiscoveredTest); Assert.NotNull(view.SourceTarget);
        var entered = false;
        view.PropertyChanged += (sender, _) => { if (entered) return; entered = true; var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender); Assert.Null(current.TestDiscovery); Assert.Null(current.SelectedDiscoveredTest); Assert.Null(current.SourceTarget); };
        Assert.True(view.RunSyntheticTests()); Assert.True(entered); Assert.Null(view.TestDiscovery);
        Assert.Equal(CompilerVerdict.Fail, view.TestRun!.Verdict); Assert.Same(compilation, view.Result);
    }

    [Fact]
    public async Task EarlierAndCopiedRowsCannotNavigateAfterReloadOrContextReplacement()
    {
        using var view = await Ready(); var first = view.DiscoverSyntheticTests()!; var row = Assert.Single(first.Tests);
        Assert.True(view.SelectDiscoveredTest(row)); Assert.True(view.Navigate());
        Assert.NotNull(view.DiscoverSyntheticTests()); Assert.Null(view.SelectedDiscoveredTest); Assert.Null(view.SourceTarget);
        Assert.False(view.SelectDiscoveredTest(row)); Assert.False(view.Navigate());
        var current = Assert.Single(view.TestDiscovery!.Tests);
        Assert.False(view.SelectDiscoveredTest(current with { })); Assert.Null(view.SourceTarget);
        Assert.True(view.SelectDiscoveredTest(current)); Assert.True(view.Navigate());
        view.BeginSynthetic(new(new(view.Result!.Input), 2));
        Assert.Null(view.TestDiscovery); Assert.Null(view.SelectedDiscoveredTest); Assert.Null(view.SourceTarget);
        Assert.Null(view.DiscoverSyntheticTests()); Assert.False(view.SelectDiscoveredTest(current));
    }

    [Fact]
    public async Task PrecancelledDiscoveryDoesNotWithdrawAnEarlierOwnedInventoryOrRun()
    {
        using var view = await Ready(); Assert.True(view.RunSyntheticTests());
        var run = view.TestRun; var inventory = view.DiscoverSyntheticTests(); Assert.NotNull(run); Assert.NotNull(inventory);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.DiscoverSyntheticTests(cancellation.Token));
        Assert.Same(inventory, view.TestDiscovery); Assert.Same(run, view.TestRun);
    }

    [Theory]
    [InlineData("reset", 1)] [InlineData("reset", 2)]
    [InlineData("dispose", 1)] [InlineData("dispose", 2)]
    [InlineData("begin", 1)] [InlineData("begin", 2)]
    [InlineData("nested-run", 1)] [InlineData("nested-run", 2)]
    [InlineData("nested-discovery", 1)] [InlineData("nested-discovery", 2)]
    [InlineData("cancel", 1)] [InlineData("cancel", 2)]
    [InlineData("cancel-nested-discovery", 1)] [InlineData("cancel-nested-discovery", 2)]
    public async Task InitialAndPublicationNotificationsCannotPublishInterruptedInventories(string mode, int boundary)
    {
        var view = await Ready();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var entered = false; var observedPublication = false; ProjectTestDiscovery? nested = null; var input = view.Result!.Input;
            Action requestCancellation = cancellation.Cancel;
            PropertyChangedEventHandler onChange = (sender, _) =>
            {
                if (entered) return;
                var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                var publishing = current.TestDiscovery is not null;
                if (publishing != (boundary == 2)) return;
                entered = true; observedPublication = publishing;
                if (mode == "reset") current.Reset();
                if (mode == "dispose") current.Dispose();
                if (mode == "begin") current.BeginSynthetic(new(new(input), 2));
                if (mode == "nested-run") Assert.True(current.RunSyntheticTests());
                if (mode is "nested-discovery" or "cancel-nested-discovery") nested = Assert.IsType<ProjectTestDiscovery>(current.DiscoverSyntheticTests());
                if (mode is "cancel" or "cancel-nested-discovery") requestCancellation();
            };
            view.PropertyChanged += onChange;
            try
            {
                if (mode is "cancel" or "cancel-nested-discovery")
                    Assert.Throws<OperationCanceledException>(() => view.DiscoverSyntheticTests(cancellation.Token));
                else Assert.Null(view.DiscoverSyntheticTests(cancellation.Token));
            }
            finally { view.PropertyChanged -= onChange; }
            Assert.True(entered); Assert.Equal(boundary == 2, observedPublication);
            Assert.Null(view.SelectedDiscoveredTest); Assert.Null(view.SourceTarget);
            if (mode is "nested-discovery" or "cancel-nested-discovery") Assert.Same(nested, view.TestDiscovery);
            else Assert.Null(view.TestDiscovery);
            if (mode == "nested-run") Assert.Equal(CompilerVerdict.Fail, view.TestRun!.Verdict);
        }
        finally { view.Dispose(); }
    }

    [Fact]
    public async Task SelectionNotificationCannotRetainAnOriginalRowAfterReset()
    {
        using var view = await Ready(); var row = Assert.Single(view.DiscoverSyntheticTests()!.Tests); var entered = false;
        view.PropertyChanged += (sender, _) => { if (entered) return; entered = true; Assert.IsType<SyntheticWorkbenchViewModel>(sender).Reset(); };
        Assert.False(view.SelectDiscoveredTest(row)); Assert.Null(view.TestDiscovery); Assert.Null(view.SelectedDiscoveredTest);
        Assert.False(view.Navigate()); Assert.Null(view.SourceTarget);
    }

    [Fact]
    public async Task DefinitionReadPreservesAnUnrelatedRecordedDiagnosticAndItsSource()
    {
        var raw = RootSourceAdmissionTests.Cases().Select(row => (string)row[0]).Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == "fail");
        var (control, core, _) = DiagnosticPremiseSnapshotTests.Prepare(raw); var input = control.Input;
        CompilerArtifactIdentity At(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        var request = new PredicateCompilerHostRequest(new(core.Input, 1, core.Snapshots, core.Units, core.Calendars), new(CompilerPredicateCaptureMode.OriginalRootArrays));
        using var session = new LocalPredicateCompilerRecordingSession(profile);
        using var view = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); view.BeginPredicateSynthetic(manifest, request);
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(view.TryAcceptPredicate(actual));
        var result = actual.Response.Compilation!.Result; Assert.True(view.LoadSyntheticRootSource(result.ResultDigest));
        var diagnostic = result.Diagnostics.First(); Assert.True(view.SelectDiagnostic(diagnostic)); Assert.True(view.Navigate());
        Assert.True(view.ReadRecordedPredicatePremises(result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies));
        var snapshot = view.RecordedPredicatePremises; var target = view.SourceTarget; Assert.NotNull(snapshot); Assert.NotNull(target);
        Assert.NotNull(view.DiscoverSyntheticTests());
        Assert.Same(diagnostic, view.SelectedDiagnostic); Assert.Same(snapshot, view.RecordedPredicatePremises); Assert.Same(target, view.SourceTarget);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelledPublicationWithdrawsOnlyTheSelectionOwnedByItsInventory(bool selectDiscovery)
    {
        using var view = await Ready();
        using var cancellation = new CancellationTokenSource();
        var symbol = view.Inspection!.Symbols.First(item => item.Symbol.Status == ValueDecodeStatus.Decoded);
        var entered = false; Action cancel = cancellation.Cancel;
        PropertyChangedEventHandler onChange = (sender, _) =>
        {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || current.TestDiscovery is not { } inventory) return; entered = true;
            if (selectDiscovery) Assert.True(current.SelectDiscoveredTest(Assert.Single(inventory.Tests)));
            else Assert.True(current.Select(symbol.Symbol.Location));
            Assert.True(current.Navigate()); cancel();
        };
        view.PropertyChanged += onChange;
        try { Assert.Throws<OperationCanceledException>(() => view.DiscoverSyntheticTests(cancellation.Token)); }
        finally { view.PropertyChanged -= onChange; }
        Assert.True(entered); Assert.Null(view.TestDiscovery); Assert.Null(view.SelectedDiscoveredTest);
        if (selectDiscovery) { Assert.Null(view.Selected); Assert.Null(view.SourceTarget); }
        else { Assert.Same(symbol, view.Selected); Assert.Equal(symbol.Symbol.Location, view.SourceTarget); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ObsoleteAndCopiedRowsCannotWithdrawTheCurrentSourceSelection(bool copied)
    {
        using var view = await Ready();
        var old = Assert.Single(Assert.IsType<ProjectTestDiscovery>(view.DiscoverSyntheticTests()).Tests);
        var inventory = Assert.IsType<ProjectTestDiscovery>(view.DiscoverSyntheticTests());
        var current = Assert.Single(inventory.Tests);
        Assert.True(view.SelectDiscoveredTest(current)); Assert.True(view.Navigate()); var target = view.SourceTarget;
        Assert.False(view.SelectDiscoveredTest(copied ? current with { } : old));
        Assert.Same(inventory, view.TestDiscovery); Assert.Same(current, view.SelectedDiscoveredTest); Assert.Same(target, view.SourceTarget);
        Assert.True(view.Navigate()); Assert.Same(target, view.SourceTarget);
    }

    [Theory]
    [InlineData("discovery")] [InlineData("unrelated")] [InlineData("nested")]
    public async Task CancelledThrowingPublicationWithdrawsOnlyItsOwnInventory(string mode)
    {
        using var view = await Ready();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token; Action cancel = cancellation.Cancel; var entered = false;
        var symbol = view.Inspection!.Symbols.First(item => item.Symbol.Status == ValueDecodeStatus.Decoded);
        ProjectTestDiscovery? nested = null; ProjectDiscoveredTest? selected = null;
        PropertyChangedEventHandler onChange = (sender, _) =>
        {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || current.TestDiscovery is not { } inventory) return;
            entered = true;
            if (mode == "nested") nested = inventory = Assert.IsType<ProjectTestDiscovery>(current.DiscoverSyntheticTests());
            if (mode != "unrelated")
            {
                selected = Assert.Single(inventory.Tests); Assert.True(current.SelectDiscoveredTest(selected));
            }
            else Assert.True(current.Select(symbol.Symbol.Location));
            Assert.True(current.Navigate()); cancel(); token.ThrowIfCancellationRequested();
        };
        view.PropertyChanged += onChange;
        try { Assert.Throws<OperationCanceledException>(() => view.DiscoverSyntheticTests(token)); }
        finally { view.PropertyChanged -= onChange; }
        Assert.True(entered);
        if (mode == "nested")
        {
            Assert.NotNull(nested); Assert.Same(nested, view.TestDiscovery); Assert.Same(selected, view.SelectedDiscoveredTest);
            Assert.Equal(selected!.Location, view.SourceTarget);
        }
        else
        {
            Assert.Null(view.TestDiscovery); Assert.Null(view.SelectedDiscoveredTest);
            if (mode == "unrelated") { Assert.Same(symbol, view.Selected); Assert.Equal(symbol.Symbol.Location, view.SourceTarget); }
            else Assert.Null(view.SourceTarget);
        }
    }
}
