using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticDiagnosticNavigationTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }
    private static async Task<SyntheticWorkbenchViewModel> Ready(CompilerInputManifest? input = null)
    {
        input ??= CompilerAnalysisFixtures.Constraint(false); var request = new CompilerHostRequest(new(input), 1);
        var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request))); return view;
    }

    [Fact]
    public async Task DiagnosticIdentityStaysSeparateFromSymbolAndNavigatesItsActualPrimaryLocation()
    {
        using var view = await Ready(); var diagnostic = Assert.Single(view.Problems);
        Assert.True(view.SelectDiagnostic(diagnostic)); Assert.Same(diagnostic, view.SelectedDiagnostic);
        Assert.Null(view.Selected); Assert.Null(view.SourceTarget); Assert.True(view.Navigate());
        Assert.Equal(diagnostic.Location, view.SourceTarget); Assert.Null(view.SourceTarget!.TextSpan);
        var symbol = view.Inspection!.Symbols[0]; Assert.True(view.Select(symbol.Symbol.Location));
        Assert.Null(view.SelectedDiagnostic); Assert.Null(view.SourceTarget); Assert.True(view.Navigate());
        Assert.Equal(symbol.Symbol.Location, view.SourceTarget);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("revision")]
    [InlineData("same-identity-different-content")]
    public async Task UnacceptedDiagnosticCannotRetainASelectionOrDiscloseASourceTarget(string mode)
    {
        using var view = await Ready(); var diagnostic = Assert.Single(view.Problems);
        Assert.True(view.SelectDiagnostic(diagnostic)); Assert.True(view.Navigate());
        var input = mode == "revision" ? Input(source: Source(99)) : view.Result!.Input;
        var rejected = new CompilerDiagnostic(input, input.Rules[0].RuleId, 1, 0, "test.missing",
            ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context, entityId: EntityId.Parse(Id(6))), Proof(input));
        if (mode == "same-identity-different-content") rejected = new CompilerDiagnostic(view.Result!.Input, diagnostic.RuleId,
            diagnostic.RuleVersion, diagnostic.StageOrdinal, diagnostic.Code, diagnostic.Location, diagnostic.Evidence,
            diagnostic.Severity, diagnostic.Discriminator, "test.different-message");
        Assert.False(view.SelectDiagnostic(rejected)); Assert.Null(view.SelectedDiagnostic); Assert.Null(view.Selected);
        Assert.Null(view.SourceTarget); Assert.False(view.Navigate()); Assert.Equal("unavailable", view.NavigationState);
    }

    [Theory]
    [InlineData("document", "unavailable")]
    [InlineData("entity", "unavailable")]
    [InlineData("relation", "unavailable")]
    [InlineData("both", "invalid-location")]
    [InlineData("neither", "unsupported")]
    [InlineData("revision", "wrong-scope")]
    [InlineData("path", "invalid-location")]
    [InlineData("span", "invalid-location")]
    public async Task InvalidSymbolLocationsClearAnEarlierDiagnosticTarget(string mode, string failure)
    {
        using var view = await Ready(); var diagnostic = Assert.Single(view.Problems);
        Assert.True(view.SelectDiagnostic(diagnostic)); Assert.True(view.Navigate());
        var location = diagnostic.Location;
        var invalid = ProjectLocation.Create(mode == "document" ? DocumentId.Parse(Id(99)) : location.DocumentId,
            mode == "revision" ? Source(99).Context : location.Context, mode == "path" ? "/payload/missing" : "",
            mode is "neither" or "relation" ? null : mode == "entity" ? EntityId.Parse(Id(99)) : location.EntityId,
            mode is "both" or "relation" ? RelationId.Parse(Id(99)) : null,
            mode == "span" ? new SourceTextSpan(int.MaxValue, 0) : null);
        Assert.False(view.Select(invalid)); Assert.Equal(failure, view.NavigationState);
        Assert.Null(view.SourceTarget); Assert.Null(view.SelectedDiagnostic); Assert.Null(view.Selected);
    }

    [Theory]
    [InlineData("select-reset")]
    [InlineData("select-dispose")]
    [InlineData("navigate-reset")]
    [InlineData("navigate-begin")]
    [InlineData("navigate-select")]
    public async Task NotificationReentryCannotReportInterruptedNavigationAsSuccessful(string mode)
    {
        var view = await Ready();
        try
        {
            var diagnostic = Assert.Single(view.Problems);
            if (mode.StartsWith("navigate", StringComparison.Ordinal)) Assert.True(view.SelectDiagnostic(diagnostic));
            var entered = false;
            view.PropertyChanged += (sender, _) =>
            {
                if (entered) return; entered = true;
                var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                if (mode.EndsWith("dispose", StringComparison.Ordinal)) current.Dispose();
                else if (mode.EndsWith("begin", StringComparison.Ordinal)) current.BeginSynthetic(new(new(current.Result!.Input), 2));
                else if (mode.EndsWith("select", StringComparison.Ordinal)) current.Select(current.Inspection!.Symbols[0].Symbol.Location);
                else current.Reset();
            };
            Assert.False(mode.StartsWith("select", StringComparison.Ordinal) ? view.SelectDiagnostic(diagnostic) : view.Navigate());
            Assert.True(entered); Assert.Null(view.SourceTarget); Assert.Null(view.SelectedDiagnostic);
        }
        finally
        {
            view.Dispose();
        }
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("unsupported")]
    public async Task UsesTheExactCoreExportedDiagnosticLocations(string key)
    {
        using var fixture = JsonDocument.Parse(WebPreviewContractTests.Cases().Select(items => (string)items[0])
            .Single(item => JsonSerializer.Deserialize<JsonElement>(item).GetProperty("key").GetString() == key));
        var data = fixture.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(data.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(data.GetProperty("manifest").GetRawText()));
        using var view = await Ready(input);
        Assert.Equal(SourceJson.Parse(data.GetProperty("result").GetRawText()), view.Result!.Data);
        Assert.NotEmpty(view.Problems);
        foreach (var diagnostic in view.Problems)
        {
            Assert.True(view.SelectDiagnostic(diagnostic));
            if (key == "fail")
            {
                Assert.Equal(diagnostic.Location, view.DiagnosticSymbol?.Symbol.Location);
                Assert.True(view.NavigateDiagnosticSymbol());
                Assert.Equal(diagnostic.Location, view.SourceTarget);
            }
            else
            {
                Assert.Null(view.DiagnosticSymbol);
                Assert.False(view.NavigateDiagnosticSymbol());
                Assert.Null(view.SourceTarget);
            }
            var location = diagnostic.Location;
            if (location.EntityId is null && location.RelationId is null)
            {
                Assert.False(view.Navigate());
                Assert.Equal("unsupported", view.NavigationState);
                Assert.Null(view.SourceTarget);
            }
            else
            {
                Assert.True(view.Navigate());
                Assert.Equal(location, view.SourceTarget);
            }
        }

        if (key == "unsupported")
        {
            var reference = view.Inspection!.References.First(item => item.State == ReferenceResolutionState.Resolved);
            Assert.False(view.SelectReference(reference));
            Assert.False(view.NavigateReferenceDefinition());
            Assert.Null(view.SourceTarget);
        }
    }

    [Fact]
    public async Task ResetInvalidatesTheExactDiagnosticSymbolAndItsTarget()
    {
        using var view = await Ready();
        var diagnostic = Assert.Single(view.Problems);
        Assert.True(view.SelectDiagnostic(diagnostic));
        Assert.True(view.NavigateDiagnosticSymbol());
        view.Reset();
        Assert.Null(view.DiagnosticSymbol);
        Assert.False(view.NavigateDiagnosticSymbol());
        Assert.Null(view.SourceTarget);
    }

    [Fact]
    public async Task DisposalRejectsSelectionAndNavigationAndRepeatedDisposalIsSafe()
    {
        var view = await Ready(); var diagnostic = Assert.Single(view.Problems);
        Assert.True(view.SelectDiagnostic(diagnostic)); Assert.True(view.Navigate());
        view.Dispose(); view.Dispose(); Assert.Null(view.SourceTarget); Assert.Null(view.SelectedDiagnostic);
        Assert.Throws<ObjectDisposedException>(() => view.SelectDiagnostic(diagnostic));
        Assert.Throws<ObjectDisposedException>(() => view.Navigate());
    }

    [Fact]
    public async Task LocalReferenceNavigatesOriginAndExactDefinitionSeparately()
    {
        using var view = await Ready();
        var inspection = view.Inspection!;
        var references = inspection.References.Where(item => item.Origin.RelationId is not null).ToArray();
        Assert.Equal(new[] { "/source", "/target" }, references.Select(item => item.Origin.PropertyPath));
        Assert.Equal(2, references.Select(item => item.SymbolKey).Distinct().Count());
        foreach (var reference in references)
        {
            Assert.True(view.SelectDiagnostic(Assert.Single(view.Problems)));
            Assert.True(view.Navigate());
            Assert.True(view.SelectReference(reference));
            Assert.Same(reference, view.SelectedReference);
            Assert.Null(view.SelectedDiagnostic); Assert.Null(view.Selected); Assert.Null(view.SourceTarget);
            var symbol = Assert.Single(inspection.Symbols, item => item.Symbol.Key == reference.SymbolKey);
            Assert.Same(symbol, view.ReferenceSymbol);
            Assert.True(view.NavigateReferenceOrigin());
            Assert.Equal(reference.Origin, view.SourceTarget);
            Assert.True(view.NavigateReferenceDefinition());
            Assert.Equal(symbol.Symbol.Location, view.SourceTarget);
            Assert.NotEqual(reference.Origin, view.SourceTarget);
        }
    }

    [Theory]
    [InlineData(ReferenceResolutionState.MissingWithinCompleteScope)]
    [InlineData(ReferenceResolutionState.UnknownDueToCoverage)]
    [InlineData(ReferenceResolutionState.Ambiguous)]
    [InlineData(ReferenceResolutionState.Unsupported)]
    [InlineData(ReferenceResolutionState.OutOfScope)]
    [InlineData(ReferenceResolutionState.Resolved)]
    public async Task UnacceptedReferenceCannotRetainASelectedDefinition(ReferenceResolutionState state)
    {
        using var view = await Ready();
        var reference = view.Inspection!.References[0];
        Assert.True(view.SelectReference(reference)); Assert.True(view.NavigateReferenceDefinition());
        Assert.False(view.SelectReference(reference with { State = state, SymbolKey = null }));
        Assert.Null(view.SelectedReference); Assert.Null(view.ReferenceSymbol); Assert.Null(view.SourceTarget);
        Assert.False(view.NavigateReferenceDefinition());
        Assert.False(view.SelectReference(reference with { }));
    }

    [Theory]
    [InlineData(false, "reset")]
    [InlineData(false, "dispose")]
    [InlineData(false, "select")]
    [InlineData(false, "begin")]
    [InlineData(true, "reset")]
    [InlineData(true, "dispose")]
    [InlineData(true, "select")]
    [InlineData(true, "begin")]
    public async Task ReferenceOperationsRejectSynchronousSelectionAndLifecycleReplacement(bool navigate, string interruption)
    {
        var view = await Ready();
        try
        {
            var input = view.Result!.Input;
            var reference = view.Inspection!.References[0];
            if (navigate) Assert.True(view.SelectReference(reference));
            var entered = false;
            view.PropertyChanged += (sender, _) =>
            {
                if (entered) return;
                entered = true;
                var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                if (interruption == "dispose") { current.Dispose(); current.Dispose(); }
                else if (interruption == "select") Assert.True(current.Select(current.Inspection!.Symbols[0].Symbol.Location));
                else if (interruption == "begin") current.BeginSynthetic(new(new(input), 2));
                else current.Reset();
            };
            Assert.False(navigate ? view.NavigateReferenceDefinition() : view.SelectReference(reference));
            Assert.True(entered); Assert.Null(view.SelectedReference); Assert.Null(view.ReferenceSymbol); Assert.Null(view.SourceTarget);
        }
        finally
        {
            view.Dispose();
        }
    }

    [Fact]
    public async Task ReferenceCannotSurviveCancellationOrAnOldResponse()
    {
        var input = CompilerAnalysisFixtures.Constraint(false);
        using var view = await Ready(input);
        var reference = view.Inspection!.References[0];
        Assert.True(view.SelectReference(reference)); Assert.True(view.NavigateReferenceDefinition());
        var previous = new CompilerHostRequest(new(input), 1);
        var newer = new CompilerHostRequest(new(input), 2);
        view.BeginSynthetic(newer);
        Assert.Null(view.SourceTarget);
        Assert.False(view.TryAccept(await Host(input).CompileAsync(previous)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False(view.TryAccept(await Host(input).CompileAsync(newer, cancelled.Token)));
        Assert.False(view.SelectReference(reference));
        Assert.False(view.NavigateReferenceOrigin());
    }

    [Fact]
    public async Task ExhaustedCompilationCannotNavigateAnEarlierReference()
    {
        var input = CompilerAnalysisFixtures.Constraint(false);
        using var view = await Ready(input);
        var reference = view.Inspection!.References[0];
        Assert.True(view.SelectReference(reference));
        var settings = System.Text.Json.Nodes.JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = 1;
        var limited = Input(settings, input.Source);
        var request = new CompilerHostRequest(new(limited), 2);
        view.BeginSynthetic(request);
        var response = await Host(limited).CompileAsync(request);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, response.Compilation!.Result.Execution);
        view.TryAccept(response);
        Assert.False(view.SelectReference(reference));
        Assert.False(view.NavigateReferenceDefinition());
        Assert.Null(view.SourceTarget);
    }

    [Fact]
    public async Task FindReferencesUsesTheSelectedExactSymbolAndReturnsNavigableOrigins()
    {
        using var view = await Ready();
        var inspection = view.Inspection!;
        var reference = inspection.References.Single(item => item.Origin.PropertyPath == "/target");
        var symbol = inspection.Symbols.Single(item => item.Symbol.Key == reference.SymbolKey);
        Assert.True(view.Select(symbol.Symbol.Location));
        var result = Assert.IsType<SyntheticWorkbenchViewModel.ReferenceSearchResult>(view.FindReferences());
        Assert.False(result.Truncated);
        Assert.Same(reference, Assert.Single(result.References));
        Assert.True(view.SelectReference(reference));
        Assert.True(view.NavigateReferenceOrigin());
        Assert.Equal(reference.Origin, view.SourceTarget);
        view.Reset();
        Assert.Null(view.FindReferences());
        Assert.False(view.SelectReference(reference));
    }

    [Fact]
    public async Task FindReferencesObservesCancellationBeforeReturningCoverage()
    {
        using var view = await Ready();
        var location = view.Inspection!.Symbols[0].Symbol.Location;
        Assert.True(view.Select(location));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.FindReferences(cancelled.Token));
        Assert.Null(view.SourceTarget);
    }
}
