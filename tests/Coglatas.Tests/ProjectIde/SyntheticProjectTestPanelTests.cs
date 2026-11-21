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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicationCancellationWithdrawsTheOwnedRunAndItsAssertion(bool throwOnPublication, bool throwOnWithdrawal)
    {
        using var view = await Ready("tests-pass");
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        var publicationFailure = new InvalidOperationException("publication failed");
        var withdrawalFailure = new InvalidOperationException("withdrawal failed");
        var published = false;
        var withdrawalNotifications = 0;
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (!published && subject.TestRun is { Execution: CompilerExecutionState.Completed } run)
            {
                published = true;
                Assert.Equal(CompilerVerdict.Pass, run.Verdict);
                Assert.True(subject.SelectTestAssertion(Assert.Single(Assert.Single(run.Tests).Assertions)));
                Assert.True(subject.Navigate());
                cancel();
                if (throwOnPublication) throw publicationFailure;
            }
            else if (published && subject.TestRun is null && throwOnWithdrawal)
            {
                throw withdrawalFailure;
            }
        };
        System.ComponentModel.PropertyChangedEventHandler laterObserver = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published && subject.TestRun is null) withdrawalNotifications++;
        };
        view.PropertyChanged += observer;
        view.PropertyChanged += laterObserver;
        Exception? failure;
        try { failure = RunAndCapturePublicationError(view, token); }
        finally
        {
            view.PropertyChanged -= observer;
            view.PropertyChanged -= laterObserver;
        }

        Assert.True(published);
        Assert.Null(view.TestRun);
        Assert.Null(view.SelectedTestAssertion);
        Assert.Null(view.SourceTarget);
        Assert.Equal(1, withdrawalNotifications);
        if (throwOnPublication)
        {
            Assert.Same(publicationFailure, failure);
        }
        else
        {
            var cancelled = Assert.IsType<OperationCanceledException>(failure);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(throwOnWithdrawal ? withdrawalFailure : null, cancelled.InnerException);
        }
    }

    [Fact]
    public async Task PublicationCancellationKeepsAReentrantReplacementRun()
    {
        using var view = await Ready("tests-pass");
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        ProjectTestRun? replacement = null;
        var replaced = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (replaced || subject.TestRun is null) return;
            replaced = true;
            cancel();
            Assert.True(subject.RunSyntheticTests());
            replacement = subject.TestRun;
        };
        view.PropertyChanged += observer;
        OperationCanceledException cancelled;
        try { cancelled = Assert.IsType<OperationCanceledException>(RunAndCapturePublicationError(view, token)); }
        finally { view.PropertyChanged -= observer; }
        Assert.Equal(token, cancelled.CancellationToken);
        Assert.NotNull(replacement);
        Assert.Same(replacement, view.TestRun);
        Assert.Equal(CompilerVerdict.Pass, replacement.Verdict);
    }

    private static Exception? RunAndCapturePublicationError(SyntheticWorkbenchViewModel view, CancellationToken token)
    {
        try
        {
            _ = view.RunSyntheticTests(token);
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
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
