using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticProjectTestEditingTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }
    private static async Task<(SyntheticWorkbenchViewModel View, ProjectLocation Target, SourceJson Definition, SourceRevisionContext Next)> Ready()
    {
        using var data = JsonDocument.Parse(WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == "tests-fail"));
        var root = data.RootElement; var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(root.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(root.GetProperty("manifest").GetRawText()));
        var basis = source.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.Parse(Id(420)), CandidateRevisionId.Parse(Id(421)), basis, basis);
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(input), proposal);
        var request = new CompilerHostRequest(new(draft), 1); var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(draft).CompileAsync(request)));
        var target = ProjectLocation.Create(source.Documents[0].DocumentId, draft.Source.Context, entityId: EntityId.Parse(Id(10)));
        var export = view.ExportTestDefinition(target)!; Assert.Equal(ProjectTestEditState.Ready, export.State);
        var definition = JsonNode.Parse(export.Definition!.CanonicalText)!.AsObject(); definition["payload"]!["assertions"]![0]!["expected"]!["value"] = false;
        return (view, target, Json(definition), SourceRevisionContext.Candidate(new(proposal.ProposalId, CandidateRevisionId.Parse(Id(422)), basis, basis)));
    }
    [Fact]
    public async Task ApplyingOnlyTheCurrentPreviewClearsOldCompilationAndTestsAndRequestsAFreshCompile()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.RunSyntheticTests()); var oldRun = view.TestRun;
        Assert.Equal(CompilerVerdict.Fail, oldRun!.Verdict);
        var preview = view.PreviewTestDefinition(f.Target, f.Definition, f.Next)!; Assert.Equal(ProjectTestEditState.Ready, preview.State);
        Assert.Same(oldRun, view.TestRun); Assert.Null(view.ApplyPreviewedTestDefinition(preview.Plan!, 1));
        var request = view.ApplyPreviewedTestDefinition(preview.Plan!, 2)!; Assert.NotNull(request);
        Assert.Equal("Waiting", view.State); Assert.Null(view.TestRun); Assert.Null(view.Result); Assert.Null(view.TestEditPreview); Assert.Null(view.SourceTarget);
        Assert.Null(view.ApplyPreviewedTestDefinition(preview.Plan!, 3));
        Assert.True(view.TryAccept(await Host(request.Input.Materialize()).CompileAsync(request)));
        Assert.True(view.RunSyntheticTests()); Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
    }
    [Fact]
    public async Task ANewSelectionInvalidatesThePreparedDefinitionPlan()
    {
        var f = await Ready(); using var view = f.View; var preview = view.PreviewTestDefinition(f.Target, f.Definition, f.Next)!;
        Assert.True(view.Select(f.Target)); Assert.Null(view.TestEditPreview); Assert.Null(view.ApplyPreviewedTestDefinition(preview.Plan!, 2));
    }
    [Theory]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("dispose", false)]
    [InlineData("dispose", true)]
    [InlineData("begin", false)]
    [InlineData("begin", true)]
    [InlineData("select", false)]
    [InlineData("select", true)]
    [InlineData("navigate", false)]
    [InlineData("navigate", true)]
    public async Task NotificationReentryCannotRetainOrReturnAnInterruptedDefinitionPreview(string mode, bool existing)
    {
        var f = await Ready(); var view = f.View;
        try
        {
            if (existing) Assert.NotNull(view.PreviewTestDefinition(f.Target, f.Definition, f.Next));
            var entered = false; var input = view.Result!.Input;
            view.PropertyChanged += (sender, _) =>
            {
                if (entered) return; entered = true; var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                if (mode == "reset") current.Reset();
                if (mode == "dispose") current.Dispose();
                if (mode == "begin") current.BeginSynthetic(new(new(input), 2));
                if (mode == "select") current.Select(f.Target);
                if (mode == "navigate") current.Navigate();
            };
            Assert.Null(view.PreviewTestDefinition(f.Target, f.Definition, f.Next)); Assert.True(entered); Assert.Null(view.TestEditPreview);
        }
        finally { view.Dispose(); }
    }

    private sealed class CancelDraftPreview : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _replace;
        private readonly InvalidOperationException? _publicationError;
        private readonly InvalidOperationException? _withdrawalError;
        private readonly ProjectLocation _target;
        private readonly SourceJson _definition;
        private readonly SourceRevisionContext _next;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public int WithdrawalNotifications { get; private set; }
        public ProjectTestEditPreview? Published { get; private set; }
        public ProjectTestEditPreview? Replacement { get; private set; }
        public CancelDraftPreview(SyntheticWorkbenchViewModel vm, bool replace, ProjectLocation target, SourceJson definition, SourceRevisionContext next,
            InvalidOperationException? publicationError, InvalidOperationException? withdrawalError)
        {
            _vm = vm;
            _replace = replace;
            _publicationError = publicationError;
            _withdrawalError = withdrawalError;
            _target = target;
            _definition = definition;
            _next = next;
            vm.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.PreviewTestDefinition(_target, _definition, _next, Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired)
            {
                if (Token.IsCancellationRequested)
                {
                    WithdrawalNotifications++;
                    if (_vm.TestEditPreview is null && _withdrawalError is not null) throw _withdrawalError;
                }
                return;
            }
            if (_vm.TestEditPreview is not { } published) return;
            Fired = true; Published = published;
            if (_replace) Replacement = _vm.PreviewTestDefinition(_target, _definition, _next);
            _source.Cancel();
            if (_publicationError is not null) throw _publicationError;
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task CancellationAtDraftPreviewPublicationWithdrawsOnlyItsOwnedPlan(bool replace) =>
        VerifyDraftPublication(replace, null);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task ThrowingDraftObserverWithdrawsOnlyItsCancelledPlan(bool replace) =>
        VerifyDraftPublication(replace, new InvalidOperationException("Draft publication observer failed."),
            new InvalidOperationException("Draft withdrawal observer failed."));

    [Fact]
    public Task ThrowingDraftWithdrawalObserverPreservesCancellation() =>
        VerifyDraftPublication(false, null, new InvalidOperationException("Draft withdrawal observer failed."));

    private static async Task VerifyDraftPublication(bool replace, InvalidOperationException? publicationError,
        InvalidOperationException? withdrawalError = null)
    {
        var f = await Ready();
        using var view = f.View;
        Assert.True(view.RunSyntheticTests());
        var result = view.Result!; var resultData = result.Data; var source = view.Source!; var sourceData = source.Data;
        var selected = view.Selected; var tests = view.TestRun;
        var previous = view.PreviewTestDefinition(f.Target, f.Definition, f.Next)!; Assert.Equal(ProjectTestEditState.Ready, previous.State); Assert.NotNull(previous.Plan);
        var previousData = previous.Plan.After.Data; Assert.NotEqual(sourceData, previous.Plan.After.Source.Data);
        using var cancellation = new CancelDraftPreview(view, replace, f.Target, f.Definition, f.Next, publicationError, withdrawalError);
        var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.NotNull(cancellation.Published); Assert.Equal(ProjectTestEditState.Ready, cancellation.Published.State); Assert.NotNull(cancellation.Published.Plan);
        Assert.Same(result, view.Result); Assert.Equal(resultData, result.Data); Assert.Same(source, view.Source); Assert.Equal(sourceData, source.Data);
        Assert.Same(selected, view.Selected); Assert.Same(tests, view.TestRun); Assert.Equal(previousData, previous.Plan.After.Data);
        if (replace)
        {
            Assert.NotNull(cancellation.Replacement); Assert.Equal(ProjectTestEditState.Ready, cancellation.Replacement.State);
            Assert.NotSame(cancellation.Published, cancellation.Replacement); Assert.Same(cancellation.Replacement, view.TestEditPreview);
        }
        else Assert.Null(view.TestEditPreview);
        Assert.Equal(replace ? 0 : 1, cancellation.WithdrawalNotifications);
        if (publicationError is null)
        {
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(withdrawalError, cancelled.InnerException);
        }
        else Assert.Same(publicationError, error);
    }
    private sealed class CancelInitialDefinitionPreview : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _admitted;
        private readonly ProjectLocation _target;
        private readonly SourceJson _definition;
        private readonly SourceRevisionContext _next;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public ProjectTestEditPreview? Replacement { get; private set; }
        public CancelInitialDefinitionPreview(SyntheticWorkbenchViewModel view, bool admitted,
            ProjectLocation target, SourceJson definition, SourceRevisionContext next)
        { _view = view; _admitted = admitted; _target = target; _definition = definition; _next = next; view.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _view.PreviewTestDefinition(_target, _definition, _next, Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || _view.TestEditPreview is not null) return;
            Fired = true;
            Replacement = _view.PreviewTestDefinition(_target, _definition, _admitted ? _next : _target.Context);
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationDuringInitialDefinitionNotificationReportsCancellationAndPreservesTheNewerPlan(bool admitted)
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.RunSyntheticTests());
        var result = view.Result!; var resultData = result.Data; var source = view.Source!; var sourceData = source.Data;
        var selected = view.Selected; var tests = view.TestRun!; var testsData = tests.Data;
        var previous = view.PreviewTestDefinition(f.Target, f.Definition, f.Next)!;
        Assert.Equal(ProjectTestEditState.Ready, previous.State); Assert.NotNull(previous.Plan);
        var previousData = previous.Plan.After.Data;
        using var cancellation = new CancelInitialDefinitionPreview(view, admitted, f.Target, f.Definition, f.Next);
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.NotNull(cancellation.Replacement); Assert.NotSame(previous, cancellation.Replacement);
        Assert.Same(cancellation.Replacement, view.TestEditPreview);
        Assert.Equal(admitted ? ProjectTestEditState.Ready : ProjectTestEditState.InvalidContext, cancellation.Replacement.State);
        if (admitted) { Assert.NotNull(cancellation.Replacement.Plan); Assert.Equal(previousData, cancellation.Replacement.Plan.After.Data); }
        else Assert.Null(cancellation.Replacement.Plan);
        Assert.Same(result, view.Result); Assert.Equal(resultData, result.Data); Assert.Same(source, view.Source); Assert.Equal(sourceData, source.Data);
        Assert.Same(selected, view.Selected); Assert.Same(tests, view.TestRun); Assert.Equal(testsData, tests.Data);
        Assert.Equal(previousData, previous.Plan.After.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
