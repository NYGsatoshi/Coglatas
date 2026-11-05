using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticWorkbenchReentrancyTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }

    [Fact]
    public async Task ANewerLifecycleStartedByResetNotificationCannotBeOverwrittenByTheOuterBegin()
    {
        var input = Input(); var host = Host(input); var old = new CompilerHostRequest(new(input), 1); var next = new CompilerHostRequest(new(input), 2);
        using var view = new SyntheticWorkbenchViewModel(); var nested = false;
        view.PropertyChanged += (sender, _) =>
        {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (nested || current.State != "Empty") return;
            nested = true;
            current.BeginSynthetic(next);
        };
        view.BeginSynthetic(old);
        Assert.True(nested); Assert.False(view.TryAccept(await host.CompileAsync(old)));
        Assert.True(view.TryAccept(await host.CompileAsync(next))); Assert.Equal("Ready", view.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrDisposeDuringBeginNotificationPreventsOuterPublication(bool dispose)
    {
        var view = new SyntheticWorkbenchViewModel();
        var nested = false;
        try
        {
            view.PropertyChanged += (sender, _) =>
            {
                if (nested) return;
                nested = true;
                var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                if (dispose) current.Dispose(); else current.Reset();
            };
            view.BeginSynthetic(new(new(Input()), 1));
            Assert.Equal("Empty", view.State);
            Assert.Null(view.Source);
            Assert.Null(view.Result);
            Assert.Null(view.Inspection);
        }
        finally
        {
            view.Dispose();
        }
    }

    [Fact]
    public void DisposalBlocksReentrantBeginBeforeNotifyingSubscribers()
    {
        var view = new SyntheticWorkbenchViewModel();
        var nested = false;
        var blocked = false;
        try
        {
            view.BeginSynthetic(new(new(Input()), 1));
            view.PropertyChanged += (sender, _) =>
            {
                if (nested) return;
                nested = true;
                var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                try { current.BeginSynthetic(new(new(Input()), 2)); }
                catch (ObjectDisposedException) { blocked = true; }
            };
        }
        finally
        {
            view.Dispose();
        }
        Assert.True(blocked); Assert.Equal("Empty", view.State); Assert.Null(view.Source); Assert.Null(view.Result);
    }

    [Fact]
    public void ReentrantDisposeIsIdempotentWithoutRecursiveNotifications()
    {
        var view = new SyntheticWorkbenchViewModel();
        var notifications = 0;
        view.PropertyChanged += (sender, _) =>
        {
            notifications++;
            // Bound the old implementation's recursion so the negative control
            // reproduces the defect without crashing the test host.
            if (notifications < 3) Assert.IsType<SyntheticWorkbenchViewModel>(sender).Dispose();
        };
        view.Dispose();
        Assert.Equal(1, notifications); Assert.Equal("Empty", view.State); Assert.Null(view.Source);
    }
}
