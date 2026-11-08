using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent candidate tests; copied only into an exact Product composition.</summary>
public sealed class LocalEvidenceDisplayCandidateTests
{
    [Fact]
    public async Task ResourceExhaustionHasANonExecutedDisplayEvenWhenTheHostReturnsAResult()
    {
        var settings = Settings();
        settings["executionProfile"]!["maximumOperations"] = 1;
        var input = Input(settings);
        var request = new CompilerHostRequest(new(input), 10);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, view.Result!.Execution);
        Assert.Equal("not-executed", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);
    }

    [Theory]
    [InlineData("pass", "complete-local-pass")]
    [InlineData("fail", "not-passed")]
    [InlineData("unknown", "partial-coverage")]
    [InlineData("unsupported", "partial-coverage")]
    [InlineData("branch-pass", "complete-local-pass")]
    [InlineData("other-project", "complete-local-pass")]
    public async Task SharedFixtureHasAnIndependentExpectedDisplayState(string key, string expected)
    {
        var fixture = WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(value => SourceJson.Parse(value).Value.GetProperty("key").GetString() == key);
        using var parsed = System.Text.Json.JsonDocument.Parse(fixture);
        var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var request = new CompilerHostRequest(new(input), 10);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        Assert.Equal(expected, view.LocalEvidenceState);
        Assert.Equal(expected == "complete-local-pass", view.HasCompleteLocalPass);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("dispose")]
    [InlineData("reselect")]
    public async Task ReentrantNotificationCannotRetainOrReportAnOldPositive(string operation)
    {
        var input = Input();
        var request = new CompilerHostRequest(new(input), 10);
        var view = new SyntheticWorkbenchViewModel();
        try
        {
            view.BeginSynthetic(request);
            var invalidated = false;
            view.PropertyChanged += (_, _) =>
            {
                if (invalidated || !view.HasCompleteLocalPass) return;
                invalidated = true;
                if (operation == "dispose") view.Dispose();
                else if (operation == "reselect") view.BeginSynthetic(new(new(Input(source: Source(11))), 11));
                else view.Reset();
            };
            Assert.False(view.TryAccept(await Host(input).CompileAsync(request)));
            Assert.True(invalidated);
            Assert.False(view.HasCompleteLocalPass);
            Assert.Equal("not-loaded", view.LocalEvidenceState);
            Assert.Null(view.Result);
        }
        finally { view.Dispose(); }
    }

    [Theory]
    [InlineData(CompilerHostStatus.Cancelled)]
    [InlineData(CompilerHostStatus.Unsupported)]
    [InlineData(CompilerHostStatus.InvalidInput)]
    [InlineData(CompilerHostStatus.InternalError)]
    public async Task FailureAfterAPreviousPassNeverKeepsPositiveEvidence(CompilerHostStatus status)
    {
        var input = Input();
        var request = new CompilerHostRequest(new(input), 10);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request)));
        Assert.True(view.HasCompleteLocalPass);
        view.BeginSynthetic(new(new(input), 11));
        Assert.False(view.TryAccept(new(status, "test.terminal", input.InputDigest, 11, Guid.NewGuid(), null)));
        Assert.Equal("not-loaded", view.LocalEvidenceState);
        Assert.False(view.HasCompleteLocalPass);
        Assert.Null(view.Result);
    }

    [Fact]
    public async Task SubstitutedSourceAndResultIdentityCannotEnterTheCurrentSelection()
    {
        var input = Input();
        var foreign = Input(source: Source(12));
        var desired = new CompilerHostRequest(new(input), 10);
        var foreignResponse = await Host(foreign).CompileAsync(new(new(foreign), 10));
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(desired);
        Assert.False(view.TryAccept(foreignResponse with { RequestedInputDigest = input.InputDigest }));
        Assert.False(view.HasCompleteLocalPass);
        Assert.Null(view.Result);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(desired)));
        Assert.True(view.HasCompleteLocalPass);
    }

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
        var expectedState = result.Execution != CompilerExecutionState.Completed ? "not-executed" :
            result.Coverage != CompilerCoverage.Complete ? "partial-coverage" :
            !result.EvidenceComplete ? "missing-evidence" :
            result.Verdict == CompilerVerdict.Pass ? "complete-local-pass" : "not-passed";
        Assert.Equal(expectedState, view.LocalEvidenceState);

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
        var view = new SyntheticWorkbenchViewModel();
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
