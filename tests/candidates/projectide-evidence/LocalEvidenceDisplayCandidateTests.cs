using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent candidate tests; copied only into an exact Product composition.</summary>
public sealed class LocalEvidenceDisplayCandidateTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) =>
            new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"),
            Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray()
                .Select(item => item.GetString())));
    }

    [Fact]
    public async Task NoLocalPassCanAppearBeforeTheExactCurrentResponse()
    {
        var input = Input();
        var request = new CompilerHostRequest(new(input), 1);
        using var view = new SyntheticWorkbenchViewModel();
        Assert.Equal("not-loaded", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);
        view.BeginSynthetic(request);
        Assert.Equal("not-loaded", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);

        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        Assert.Equal("Ready", view.State);
        var result = Assert.IsType<CompilerResult>(view.Result);
        Assert.Equal(result.Verdict == CompilerVerdict.Pass &&
                     result.Execution == CompilerExecutionState.Completed &&
                     result.Coverage == CompilerCoverage.Complete &&
                     result.EvidenceComplete, view.HasCompleteLocalPass);
        Assert.Equal(view.HasCompleteLocalPass ? "complete-local-pass" : "not-passed",
            view.LocalEvidenceState);

        view.Reset();
        Assert.Equal("not-loaded", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);
        Assert.Null(view.Result);
    }

    [Fact]
    public async Task CurrentIncompleteCoreResultDoesNotBecomeTrustedOrLocallyPassed()
    {
        var input = CompilerAnalysisFixtures.Constraint(null);
        var request = new CompilerHostRequest(new(input), 2);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        Assert.Equal(CompilerVerdict.Inconclusive, view.Result!.Verdict);
        Assert.False(view.HasCompleteLocalPass);
        Assert.NotEqual("complete-local-pass", view.LocalEvidenceState);
    }

    [Fact]
    public async Task WrongGenerationAndResetRejectOldResponseWithoutRestoringLocalPass()
    {
        var input = Input();
        var previous = new CompilerHostRequest(new(input), 4);
        var next = new CompilerHostRequest(new(input), 5);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(previous);
        var response = await Host(input).CompileAsync(previous);
        view.Reset();
        view.BeginSynthetic(next);
        Assert.False(view.TryAccept(response));
        Assert.Equal("not-loaded", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);
        view.Dispose();
        Assert.Equal("not-loaded", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);
    }
}
