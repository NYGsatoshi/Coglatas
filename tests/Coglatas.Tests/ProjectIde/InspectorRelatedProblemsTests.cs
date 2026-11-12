using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Developer regressions for the pure Windows Inspector; no native UI acceptance is claimed.</summary>
public sealed class InspectorRelatedProblemsTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }

    private static async Task<SyntheticWorkbenchViewModel> Ready()
    {
        var input = CompilerAnalysisFixtures.Constraint(false, requirement: true);
        var request = new CompilerHostRequest(new(input), 1);
        var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        return view;
    }

    [Fact]
    public async Task RelatedFindingSelectsAndNavigatesTheExactAcceptedPrimaryLocation()
    {
        using var view = await Ready();
        Assert.True(view.Problems.Count > 1);
        var constraint = view.Inspection!.Symbols.Single(item => item.Symbol.Location.EntityId == EntityId.Parse(Id(7)) &&
            item.Symbol.Location.PropertyPath.Length == 0);
        Assert.True(view.Select(constraint.Symbol.Location));
        var diagnostic = Assert.Single(view.RelatedProblems);
        Assert.Equal("coglatas.analysis.constraint-false", diagnostic.Code);
        Assert.True(view.SelectDiagnostic(diagnostic));
        Assert.Same(diagnostic, Assert.Single(view.RelatedProblems));
        Assert.True(view.Navigate());
        Assert.Equal(diagnostic.Location, view.SourceTarget);
    }

    [Fact]
    public async Task UnrelatedRecordAndReferenceOriginHaveNoConstraintFindings()
    {
        using var view = await Ready();
        var project = view.Inspection!.Symbols.Single(item => item.Symbol.Location.EntityId == EntityId.Parse(Id(6)) &&
            item.Symbol.Location.PropertyPath.Length == 0);
        Assert.True(view.Select(project.Symbol.Location));
        Assert.Empty(view.RelatedProblems);
        var reference = view.Inspection.References.First(item => item.Origin.RelationId is not null);
        Assert.True(view.SelectReference(reference));
        Assert.Empty(view.RelatedProblems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetOrReplacementRejectsRetainedRelatedFinding(bool replace)
    {
        using var view = await Ready();
        var diagnostic = view.Problems.Single(item => item.Code == "coglatas.analysis.constraint-false");
        var input = view.Result!.Input;
        Assert.True(view.SelectDiagnostic(diagnostic));
        Assert.NotEmpty(view.RelatedProblems);
        if (replace) view.BeginSynthetic(new(new(input), 2));
        else view.Reset();
        Assert.Empty(view.RelatedProblems);
        if (replace) Assert.True(view.TryAccept(await Host(input).CompileAsync(new(new(input), 2))));
        Assert.False(view.SelectDiagnostic(diagnostic));
        Assert.Empty(view.RelatedProblems);
        Assert.Null(view.SourceTarget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousInvalidationCannotLeaveRelatedFindings(bool dispose)
    {
        using var view = await Ready();
        var diagnostic = view.Problems.Single(item => item.Code == "coglatas.analysis.constraint-false");
        var entered = false;
        view.PropertyChanged += (sender, _) =>
        {
            if (entered) return;
            entered = true;
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (dispose) current.Dispose();
            else current.Reset();
            Assert.Empty(current.RelatedProblems);
        };
        Assert.False(view.SelectDiagnostic(diagnostic));
        Assert.True(entered);
        Assert.Empty(view.RelatedProblems);
        Assert.Null(view.SourceTarget);
    }
}
