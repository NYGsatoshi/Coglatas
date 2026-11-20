using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Isolated synthetic acceptance candidates; never native UI or production evidence.</summary>
public sealed class InspectorIdentityCandidateTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }

    private static async Task<SyntheticWorkbenchViewModel> Ready()
    {
        var input = CompilerAnalysisFixtures.Constraint(false);
        var request = new CompilerHostRequest(new(input), 1);
        var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        Assert.True(view.SelectDiagnostic(Assert.Single(view.Problems)));
        Assert.Single(view.RelatedProblems);
        Assert.True(view.Navigate());
        return view;
    }

    /// <summary>Each location dimension independently invalidates the previous Inspector and Source target.</summary>
    [Theory]
    [InlineData("tenantId", "wrong-scope")]
    [InlineData("projectId", "wrong-scope")]
    [InlineData("branchId", "wrong-scope")]
    [InlineData("revisionId", "wrong-scope")]
    [InlineData("documentId", "unavailable")]
    [InlineData("entityId", "unavailable")]
    [InlineData("relationId", "unavailable")]
    public async Task ForeignLocationCannotReuseTheAcceptedFinding(string dimension, string expected)
    {
        using var view = await Ready();
        var location = JsonNode.Parse(Assert.Single(view.RelatedProblems).Location.Data.CanonicalText)!.AsObject();
        if (dimension is "tenantId" or "projectId" or "branchId") location["context"]!["branch"]![dimension] = Id(91);
        else if (dimension == "revisionId") location["context"]![dimension] = Id(91);
        else if (dimension == "relationId")
        {
            // Reuse the entity's UUID under the opposite typed identity.
            location["relationId"] = location["entityId"]!.GetValue<string>();
            location.Remove("entityId");
        }
        else location[dimension] = Id(91);
        Assert.False(view.Select(ProjectLocation.Parse(Json(location))));
        Assert.Equal(expected, view.NavigationState);
        Assert.Null(view.SelectedDiagnostic);
        Assert.Empty(view.RelatedProblems);
        Assert.Null(view.SourceTarget);
    }

    /// <summary>Content and diagnostic ID equality do not authorize a newly constructed object.</summary>
    [Fact]
    public async Task EqualDiagnosticCloneIsRejectedWhileTheOriginalRemainsSelectable()
    {
        using var view = await Ready();
        var original = Assert.Single(view.RelatedProblems);
        var clone = new CompilerDiagnostic(view.Result!.Input, original.RuleId, original.RuleVersion,
            original.StageOrdinal, original.Code, original.Location, original.Evidence, original.Severity,
            original.Discriminator, original.MessageKey);
        Assert.Equal(original.DiagnosticId, clone.DiagnosticId);
        Assert.Equal(original.Data.CanonicalText, clone.Data.CanonicalText);
        Assert.NotSame(original, clone);
        Assert.False(view.SelectDiagnostic(clone));
        Assert.Empty(view.RelatedProblems);
        Assert.Null(view.SourceTarget);
        Assert.True(view.SelectDiagnostic(original));
        Assert.Same(original, Assert.Single(view.RelatedProblems));
        Assert.True(view.Navigate());
        Assert.Equal(original.Location, view.SourceTarget);
    }

    /// <summary>A terminal refusal and a delayed old response cannot restore the old Inspector.</summary>
    [Theory]
    [InlineData(CompilerHostStatus.Cancelled)]
    [InlineData(CompilerHostStatus.Unsupported)]
    [InlineData(CompilerHostStatus.InternalError)]
    public async Task TerminalRequestRejectsOldFindingsAndLateCompletion(CompilerHostStatus status)
    {
        using var view = await Ready();
        var original = Assert.Single(view.RelatedProblems);
        var input = view.Result!.Input;
        var previous = await Host(input).CompileAsync(new(new(input), 1));
        view.BeginSynthetic(new(new(input), 2));
        Assert.False(view.TryAccept(new(status, "test.inspector-terminal", input.InputDigest, 2, Guid.NewGuid(), null)));
        Assert.Equal(status.ToString(), view.State);
        Assert.False(view.TryAccept(previous));
        Assert.False(view.SelectDiagnostic(original));
        Assert.Empty(view.RelatedProblems);
        Assert.Null(view.SourceTarget);
        Assert.Null(view.Result);
    }

    /// <summary>A synchronous new generation wins over a related-finding selection and its navigation.</summary>
    [Fact]
    public async Task ReentrantNewRequestCannotPublishTheOldRelatedSelection()
    {
        using var view = await Ready();
        var original = Assert.Single(view.RelatedProblems);
        var input = view.Result!.Input;
        var entered = false;
        view.PropertyChanged += (sender, _) =>
        {
            if (entered) return;
            entered = true;
            Assert.IsType<SyntheticWorkbenchViewModel>(sender).BeginSynthetic(new(new(input), 2));
        };
        Assert.False(view.SelectDiagnostic(original));
        Assert.True(entered);
        Assert.Equal("Waiting", view.State);
        Assert.Empty(view.RelatedProblems);
        Assert.Null(view.SourceTarget);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(new(new(input), 2))));
        Assert.False(view.SelectDiagnostic(original));
        var replacement = Assert.Single(view.Problems);
        Assert.Equal(original.DiagnosticId, replacement.DiagnosticId);
        Assert.NotSame(original, replacement);
        Assert.True(view.SelectDiagnostic(replacement));
        Assert.Same(replacement, Assert.Single(view.RelatedProblems));
    }
}
