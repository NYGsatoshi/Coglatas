using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticProjectTestPanelTests
{
    private static CompilerInputManifest Input(string key)
    {
        using var item = JsonDocument.Parse(WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == key));
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.RootElement.GetProperty("sourceText").GetString()!)).RequireSource();
        return CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.RootElement.GetProperty("manifest").GetRawText()));
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
    [InlineData("tests-pass")]
    [InlineData("tests-fail")]
    [InlineData("tests-unknown")]
    [InlineData("tests-empty")]
    [InlineData("tests-unsupported")]
    [InlineData("tests-cancelled")]
    [InlineData("tests-evaluator-mismatch")]
    [InlineData("tests-error")]
    [InlineData("tests-exhausted")]
    [InlineData("tests-unicode")]
    [InlineData("tests-many")]
    public async Task FixedLocalHostReproducesActualCoreExportWithoutChangingCompilerVerdict(string key)
    {
        using var view = await Ready(key); var compilerResult = view.Result;
        using var cancellation = new CancellationTokenSource();
        if (key == "tests-cancelled") cancellation.Cancel();
        Assert.True(view.RunSyntheticTests(cancellation.Token, key == "tests-exhausted" ? new(256, 1) : null));
        using var fixture = JsonDocument.Parse(WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == key));
        var expected = SourceJson.Parse(fixture.RootElement.GetProperty("localProjectTests").GetProperty("result").GetProperty("text").GetString()!);
        Assert.Equal(expected, view.TestRun!.Data); Assert.Same(compilerResult, view.Result);
        foreach (var assertion in view.TestRun.Tests.SelectMany(test => test.Assertions))
        {
            Assert.True(view.SelectTestAssertion(assertion)); Assert.True(view.Navigate()); Assert.Equal(assertion.Location, view.SourceTarget);
        }
    }

    [Fact]
    public async Task RejectsAnEarlierAssertionInstanceAndClearsResultsOnContextReplacement()
    {
        using var view = await Ready(); Assert.True(view.RunSyntheticTests());
        var assertion = Assert.Single(Assert.Single(view.TestRun!.Tests).Assertions);
        Assert.True(view.SelectTestAssertion(assertion)); Assert.True(view.Navigate());
        Assert.True(view.RunSyntheticTests()); Assert.False(view.SelectTestAssertion(assertion)); Assert.Null(view.SourceTarget);
        var input = Input("tests-pass"); var newer = new CompilerHostRequest(new(input), 2);
        view.BeginSynthetic(newer); Assert.Null(view.TestRun); Assert.Null(view.SelectedTestAssertion);
        Assert.False(view.RunSyntheticTests()); Assert.Null(view.TestRun);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(newer))); Assert.True(view.RunSyntheticTests());
        view.Reset(); Assert.Null(view.TestRun); Assert.Null(view.SelectedTestAssertion); Assert.Null(view.SourceTarget);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("dispose")]
    [InlineData("begin")]
    [InlineData("nested-run")]
    public async Task NotificationReentryCannotPublishAnInterruptedTestRun(string mode)
    {
        var view = await Ready();
        try
        {
            var entered = false; var input = view.Result!.Input;
            view.PropertyChanged += (sender, _) =>
            {
                if (entered) return; entered = true; var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                if (mode == "reset") current.Reset();
                if (mode == "dispose") current.Dispose();
                if (mode == "begin") current.BeginSynthetic(new(new(input), 2));
                if (mode == "nested-run") Assert.True(current.RunSyntheticTests());
            };
            Assert.False(view.RunSyntheticTests()); Assert.True(entered);
            if (mode == "nested-run") Assert.NotNull(view.TestRun); else Assert.Null(view.TestRun);
            Assert.Null(view.SelectedTestAssertion); Assert.Null(view.SourceTarget);
        }
        finally { view.Dispose(); }
    }

    [Fact]
    public async Task SelectionNotificationCannotPreserveAnAssertionAfterReset()
    {
        var view = await Ready();
        try
        {
            Assert.True(view.RunSyntheticTests()); var assertion = Assert.Single(Assert.Single(view.TestRun!.Tests).Assertions);
            var entered = false;
            view.PropertyChanged += (sender, _) => { if (entered) return; entered = true; Assert.IsType<SyntheticWorkbenchViewModel>(sender).Reset(); };
            Assert.False(view.SelectTestAssertion(assertion)); Assert.Null(view.TestRun); Assert.Null(view.SelectedTestAssertion); Assert.False(view.Navigate());
        }
        finally { view.Dispose(); }
    }
}
