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
        using var view = await Ready(); var diagnostic = Assert.Single(view.Problems);
        if (mode.StartsWith("navigate", StringComparison.Ordinal)) Assert.True(view.SelectDiagnostic(diagnostic));
        var entered = false;
        view.PropertyChanged += (_, _) =>
        {
            if (entered) return; entered = true;
            if (mode.EndsWith("dispose", StringComparison.Ordinal)) view.Dispose();
            else if (mode.EndsWith("begin", StringComparison.Ordinal)) view.BeginSynthetic(new(new(view.Result!.Input), 2));
            else if (mode.EndsWith("select", StringComparison.Ordinal)) view.Select(view.Inspection!.Symbols[0].Symbol.Location);
            else view.Reset();
        };
        Assert.False(mode.StartsWith("select", StringComparison.Ordinal) ? view.SelectDiagnostic(diagnostic) : view.Navigate());
        Assert.True(entered); Assert.Null(view.SourceTarget); Assert.Null(view.SelectedDiagnostic);
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
}
