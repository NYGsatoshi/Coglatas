using System.ComponentModel;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticContextStartCancellationTests
{
    private static readonly string[] Roles = ["rename", "revert", "test-definition", "scenario-load", "scenario-draft"];
    public static IEnumerable<object[]> InitialCases() =>
        from role in Roles
        from mode in new[] { "cancel-only", "new-ready", "new-waiting", "new-refused", "reset", "dispose" }
        select new object[] { role, mode };
    public static IEnumerable<object[]> CommittedCases() =>
        from role in Roles from lateCancellation in new[] { false, true }
        select new object[] { role, lateCancellation };

    private static CompilerInputManifest Committed()
    {
        var root = Source();
        var type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true };
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, displayName = "Task",
            payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1,
                nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
                ports = new[] { new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0,
                    maximumConnections = 1, defaultValue = new { type, state = "coglatas.known", value = false } } } } });
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
            payload = new { assertions = new[] { new { key = "example.expected", kind = "coglatas.assert-boolean",
                target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(7), context = root.Context.Data.Value },
                portId = Id(8), expected = new { type, state = "coglatas.known", value = true } } } } });
        return Input(source: ProjectSource.Create(root.Context, root.Documents, Json(new JsonArray(task, test))));
    }

    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity At(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }

    private sealed class Prepared(SyntheticWorkbenchViewModel view, CompilerInputManifest before, CompilerInputManifest after,
        string role, object plan, long generation, CompilerInputManifest? currentBase = null) : IDisposable
    {
        internal SyntheticWorkbenchViewModel View { get; } = view;
        internal CompilerInputManifest Before { get; } = before;
        internal CompilerInputManifest After { get; } = after;
        internal long Generation { get; } = generation;
        internal CompilerHostRequest? Apply(CancellationToken token) => role switch
        {
            "rename" => View.ApplyPreviewedRename((ProjectDraftRenamePlan)plan, Generation, token),
            "revert" => View.ApplyPreviewedRevert((ProjectDraftRevertPlan)plan, Generation, token),
            "test-definition" => View.ApplyPreviewedTestDefinition((ProjectTestEditPlan)plan, Generation, token),
            "scenario-load" => View.LoadScenario((ProjectScenarioPlan)plan, Generation, token),
            "scenario-draft" => View.ApplyScenarioDraft((ProjectScenarioDraftPlan)plan, currentBase!, Generation, token),
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        public void Dispose() => View.Dispose();
    }

    private static ProjectSource Revision(int revision, string first, string second)
    {
        var root = Source(revision);
        object Task(int id, string name) => new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task",
            schemaVersion = 1, displayName = name, payload = new { duration = new
            { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                state = "coglatas.known", value = "1" }, ports = Array.Empty<object>() } };
        return ProjectSource.Create(root.Context, root.Documents, Json(new JsonArray(Obj(Task(7, first)), Obj(Task(8, second)))));
    }

    private static async Task<Prepared> Prepare(string role)
    {
        var baseline = Committed();
        var before = role is "rename" or "test-definition"
            ? ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(baseline), DraftRenameTests.Proposal(baseline))
            : role == "revert" ? Input(source: Revision(62, "Changed", "Later")) : baseline;
        var view = new SyntheticWorkbenchViewModel();
        try
        {
            var request = new CompilerHostRequest(new(before), 1);
            view.BeginSynthetic(request); Assert.True(view.TryAccept(await Host(before).CompileAsync(request)));
            if (role == "rename")
            {
                var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), before.Source.Context, entityId: EntityId.Parse(Id(7)));
                Assert.True(view.Select(target));
                var preview = view.PreviewRename("Changed name", DraftRenameTests.Next(before));
                Assert.Equal(DraftRenameState.Ready, preview!.State);
                var plan = Assert.IsType<ProjectDraftRenamePlan>(preview.Plan);
                return new(view, before, plan.After, role, plan, 2);
            }
            if (role == "test-definition")
            {
                var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), before.Source.Context, entityId: EntityId.Parse(Id(9)));
                var exported = view.ExportTestDefinition(target);
                Assert.Equal(ProjectTestEditState.Ready, exported!.State);
                var definition = JsonNode.Parse(exported.Definition!.CanonicalText)!.AsObject();
                definition["payload"]!["assertions"]![0]!["expected"]!["value"] = false;
                var preview = view.PreviewTestDefinition(target, Json(definition), DraftRenameTests.Next(before));
                Assert.Equal(ProjectTestEditState.Ready, preview!.State);
                var plan = Assert.IsType<ProjectTestEditPlan>(preview.Plan);
                return new(view, before, plan.After, role, plan, 2);
            }
            if (role == "revert")
            {
                var parent = Input(source: Revision(60, "Original", "Other"));
                var reverted = Input(source: Revision(61, "Changed", "Other"));
                var history = new[] { ProjectHistoryRevision.Checkpoint(parent.Source, 0),
                    ProjectHistoryRevision.Checkpoint(reverted.Source, 1, [parent.Source.Context.CommittedRevision!]),
                    ProjectHistoryRevision.Checkpoint(before.Source, 2, [reverted.Source.Context.CommittedRevision!]) };
                var preview = view.PreviewRevert(ProjectSemanticBinder.Bind(parent), ProjectSemanticBinder.Bind(reverted),
                    parent.InputDigest, reverted.InputDigest, DraftRenameTests.Proposal(before, 80), history);
                Assert.Equal(DraftRevertState.Ready, preview!.State);
                var plan = Assert.IsType<ProjectDraftRevertPlan>(preview.Plan);
                return new(view, before, plan.After, role, plan, 2);
            }
            var value = Json(Obj(new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                state = "coglatas.known", value = true }));
            var change = new ProjectScenarioOverride(ProjectLocation.Create(DocumentId.Parse(Id(4)), before.Source.Context,
                entityId: EntityId.Parse(Id(7))), ProjectScenarioOverrideKind.TaskInputDefault, value, PortId.Parse(Id(8)));
            var scenario = view.PreviewScenario(ScenarioId.Parse(Id(700)), ScenarioRevisionId.Parse(Id(701)), [change]);
            Assert.Equal(ProjectScenarioState.Ready, scenario!.State);
            var scenarioPlan = Assert.IsType<ProjectScenarioPlan>(scenario.Plan);
            if (role == "scenario-load") return new(view, before, scenarioPlan.Input, role, scenarioPlan, 2);
            Assert.Equal("scenario-draft", role);
            var loaded = Assert.IsType<CompilerHostRequest>(view.LoadScenario(scenarioPlan, 2));
            Assert.True(view.TryAccept(await Host(scenarioPlan.Input).CompileAsync(loaded)));
            Assert.Same(scenarioPlan, view.ActiveScenario);
            var basis = baseline.Source.Context.CommittedRevision!;
            var proposal = new ProposalContext(ProposalId.Parse(Id(710)), CandidateRevisionId.Parse(Id(711)), basis, basis);
            var converted = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(baseline), baseline.InputDigest, proposal);
            Assert.Equal(ProjectScenarioState.Ready, converted!.State);
            var draftPlan = Assert.IsType<ProjectScenarioDraftPlan>(converted.Plan);
            return new(view, scenarioPlan.Input, draftPlan.After, role, draftPlan, 3, baseline);
        }
        catch { view.Dispose(); throw; }
    }

    private sealed class CancelAtNotification : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly CancellationTokenSource _source = new();
        private readonly CompilerHostRequest _newer;
        private readonly CompilerHostResponse _response;
        private readonly string _mode;
        private readonly SourceJson? _published;
        internal bool Fired { get; private set; }
        internal CancellationToken Token => _source.Token;
        internal CancelAtNotification(SyntheticWorkbenchViewModel view, CompilerHostRequest newer,
            CompilerHostResponse response, string mode, SourceJson? published = null)
        { _view = view; _newer = newer; _response = response; _mode = mode; _published = published; view.PropertyChanged += Changed; }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (Fired || (_published is null ? current.State != "Empty" : current.State != "Waiting" || !Equals(current.Source?.Data, _published))) return;
            Fired = true; _source.Cancel();
            switch (_mode)
            {
                case "new-ready": current.BeginSynthetic(_newer); Assert.True(current.TryAccept(_response)); break;
                case "new-waiting": current.BeginSynthetic(_newer); break;
                case "new-refused": current.BeginSynthetic(_newer); Assert.False(current.TryAccept(_response)); break;
                case "reset": current.Reset(); break;
                case "dispose": current.Dispose(); break;
            }
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [MemberData(nameof(InitialCases))]
    public async Task CancellationAtInitialContextNotificationAbortsOnlyTheOlderLocalReplacement(string role, string mode)
    {
        using var prepared = await Prepare(role);
        var originalSource = prepared.Before.Source.Data; var originalInput = prepared.Before.Data;
        var result = prepared.View.Result!; var originalResult = result.Data;
        var newer = new CompilerHostRequest(new(prepared.Before), prepared.Generation + 1);
        using var cancelled = new CancellationTokenSource();
        if (mode == "new-refused") cancelled.Cancel();
        var response = await Host(prepared.Before).CompileAsync(newer, cancelled.Token);
        using var boundary = new CancelAtNotification(prepared.View, newer, response, mode);
        var token = boundary.Token; var apply = prepared.Apply; CompilerHostRequest? returned = null;
        var error = Record.Exception(() => returned = apply(token));
        Assert.True(boundary.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Equal(originalSource, prepared.Before.Source.Data); Assert.Equal(originalInput, prepared.Before.Data);
        Assert.Equal(originalResult, result.Data);
        if (mode == "new-ready") { Assert.Equal("Ready", prepared.View.State); Assert.Same(response.Compilation!.Result, prepared.View.Result); }
        if (mode == "new-waiting") Assert.Equal("Waiting", prepared.View.State);
        if (mode == "new-refused") Assert.Equal("Cancelled", prepared.View.State);
        if (mode is "reset" or "dispose") Assert.Equal("Empty", prepared.View.State);
        Assert.IsType<OperationCanceledException>(error);
        Assert.Null(returned);
        if (mode is "cancel-only" or "reset" or "dispose")
        { Assert.Equal("Empty", prepared.View.State); Assert.Null(prepared.View.Source); }
        else Assert.Equal(originalSource, prepared.View.Source!.Data);
        if (mode != "new-ready")
        { Assert.Null(prepared.View.Result); Assert.Null(prepared.View.Inspection); Assert.Empty(prepared.View.Problems); }
    }

    [Theory]
    [MemberData(nameof(CommittedCases))]
    public async Task AdmittedContextPublicationRetainsTheExistingCommitPointAndFreshCompile(string role, bool lateCancellation)
    {
        using var prepared = await Prepare(role);
        var originalSource = prepared.Before.Source.Data; var originalInput = prepared.Before.Data;
        var result = prepared.View.Result!; var originalResult = result.Data;
        var newer = new CompilerHostRequest(new(prepared.Before), prepared.Generation + 1);
        var response = await Host(prepared.Before).CompileAsync(newer);
        using var boundary = lateCancellation
            ? new CancelAtNotification(prepared.View, newer, response, "cancel-only", prepared.After.Source.Data) : null;
        var token = boundary?.Token ?? CancellationToken.None;
        var request = Assert.IsType<CompilerHostRequest>(prepared.Apply(token));
        Assert.Equal(prepared.Generation, request.ClientGeneration); Assert.Equal("Waiting", prepared.View.State);
        Assert.Equal(prepared.After.Source.Data, prepared.View.Source!.Data); Assert.Null(prepared.View.Result);
        Assert.Null(prepared.View.Inspection); Assert.Null(prepared.View.Selected); Assert.Null(prepared.View.SourceTarget);
        if (lateCancellation) { Assert.True(boundary!.Fired); Assert.True(token.IsCancellationRequested); }
        Assert.True(prepared.View.TryAccept(await Host(prepared.After).CompileAsync(request)));
        Assert.Equal("Ready", prepared.View.State); Assert.Equal(prepared.After.Data, prepared.View.Result!.Input.Data);
        Assert.Equal(originalSource, prepared.Before.Source.Data); Assert.Equal(originalInput, prepared.Before.Data);
        Assert.Equal(originalResult, result.Data);
    }
}
