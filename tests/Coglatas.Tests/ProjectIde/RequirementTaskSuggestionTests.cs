using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class RequirementTaskSuggestionTests
{
    private static async Task<SyntheticWorkbenchViewModel> AcceptedView(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var host = new LocalNetCompilerHost(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        var view = new SyntheticWorkbenchViewModel(); var request = new CompilerHostRequest(new(input), 1); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await host.CompileAsync(request))); Assert.True(view.Select(Origin(view.Inspection!))); return view;
    }
    [Fact]
    public async Task WindowsQueriesOpenOnlyRetainedCandidatesAndClearOnSelectionResetAndReentrantPublication()
    {
        var input = SuggestionInput(); using var view = await AcceptedView(input); var page = view.SuggestRequirementTasks()!;
        Assert.NotNull(page); Assert.Equal(3, page.Candidates.Count); Assert.True(view.NavigateTaskSuggestion(page, page.Candidates[0]));
        Assert.Equal(input.Source.Data, view.Source!.Data);
        Assert.False(view.NavigateTaskSuggestion(page, page.Candidates[0] with { DisplayName = "Forged" })); Assert.Null(view.SourceTarget);
        Assert.True(view.Select(Origin(view.Inspection!))); Assert.Null(view.TaskSuggestions); Assert.False(view.NavigateTaskSuggestion(page, page.Candidates[0]));
        view.PropertyChanged += (sender, _) => { if (sender is SyntheticWorkbenchViewModel current && current.TaskSuggestions is not null) current.Reset(); };
        Assert.Null(view.SuggestRequirementTasks()); Assert.Null(view.TaskSuggestions); Assert.Equal("Empty", view.State);
    }
    [Fact]
    public async Task WindowsClearNotifiesBeforeCancellationAndAnOlderRequestCannotReplaceANestedNewerPage()
    {
        var input = SuggestionInput(); using var view = await AcceptedView(input); Assert.NotNull(view.SuggestRequirementTasks());
        var cleared = false; view.PropertyChanged += (sender, _) => { if (sender is SyntheticWorkbenchViewModel { TaskSuggestions: null }) cleared = true; };
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.SuggestRequirementTasks(cancellationToken: cancellation.Token)); Assert.True(cleared); Assert.Null(view.TaskSuggestions);
        Assert.NotNull(view.SuggestRequirementTasks()); RequirementTaskSuggestionPage? nested = null; var called = false;
        view.PropertyChanged += (sender, _) => { if (!called && sender is SyntheticWorkbenchViewModel { TaskSuggestions: null } current) { called = true; nested = current.SuggestRequirementTasks(1); } };
        Assert.Null(view.SuggestRequirementTasks()); Assert.NotNull(nested); Assert.Same(nested, view.TaskSuggestions); Assert.Single(nested.Candidates);
    }
    [Theory]
    [InlineData("symbols")]
    [InlineData("references")]
    [InlineData("find-references")]
    public void InspectionViewsCannotExposeMutableSymbolOrReferenceArrays(string view)
    {
        var input = SuggestionInput(); var index = Index(input);
        var target = index.Symbols.First(item => item.Symbol.Kind == "coglatas.project").Symbol.Location;
        if (view == "symbols") Check(index.Symbols); else if (view == "references") Check(index.References); else Check(index.FindReferences(target));
        static void Check<T>(IReadOnlyList<T> values)
        {
            Assert.NotEmpty(values); var before = values.ToArray();
            if (values is System.Collections.ICollection collection && collection.SyncRoot is Array storage) storage.SetValue(null, 0);
            Assert.Equal(before, values);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateCollectionsCannotExposeTheirMutableBackingThroughSyncRoot(bool existingReferenceQuery)
    {
        var input = SuggestionInput(); var index = Index(input);
        var suggestions = index.SuggestRequirementTasks(Origin(index), input.InputDigest).Candidates;
        var port = index.Symbols.First(item => item.Symbol.PortId is not null).Symbol.Location;
        var references = index.CompleteEntityReference(port, input.InputDigest).Candidates;
        if (existingReferenceQuery) Check(references); else Check(suggestions);
        static void Check<T>(IReadOnlyList<T> items)
        {
            Assert.NotEmpty(items); var original = items.ToArray();
            if (items is System.Collections.ICollection collection && collection.SyncRoot is IList<T> backing) backing.Clear();
            Assert.Equal(original, items);
            var list = Assert.IsAssignableFrom<IList<T>>(items); Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list.Clear());
        }
    }
    internal static CompilerInputManifest SuggestionInput(int tasks = 3, bool partial = false)
    {
        var input = ReferenceCompletionTests.CompletionInput(tasks, partial: partial);
        var declarations = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        foreach (var number in new[] { 30000, 30001 }) declarations.Add(Obj(new
        {
            entityId = Id(number), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1, displayName = "Requirement " + number,
            payload = new { target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = input.Source.Context.Data.Value },
                condition = new { kind = "coglatas.literal", value = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true } },
                evidenceKind = "coglatas.model-proof" }
        }));
        return new(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations)), input.Settings);
    }
    private static CompilerSemanticInspection Index(CompilerInputManifest input) => new(ProjectSemanticBinder.Bind(input));
    private static ProjectLocation Origin(CompilerSemanticInspection index) => index.Symbols.First(item => item.Symbol.Kind == "coglatas.requirement").Symbol.Location;

    [Fact]
    public void CandidatesRetainTypedIdentityStructuralReasonAndExactInputWithoutChangingSource()
    {
        var input = SuggestionInput(); var source = input.Source.Data; var index = Index(input); var origin = Origin(index);
        var page = index.SuggestRequirementTasks(origin, input.InputDigest);
        Assert.Equal(RequirementTaskSuggestionState.Ready, page.State); Assert.True(page.CandidateOnly); Assert.True(page.CoverageComplete);
        Assert.Equal(input.InputDigest, page.InputDigest); Assert.Equal(origin, page.Origin); Assert.Equal(3, page.Candidates.Count); Assert.Null(page.NextCursor);
        Assert.All(page.Candidates, candidate =>
        {
            Assert.Equal("coglatas.task", candidate.Symbol.Symbol.Kind); Assert.Null(candidate.Symbol.Symbol.PortId);
            Assert.Equal(input.Source.Context, candidate.Symbol.Symbol.Context); Assert.Equal(RequirementTaskSuggestionReason.DeclaredTaskInSnapshot, candidate.Reason);
            Assert.StartsWith("Task ", candidate.DisplayName);
        });
        Assert.Equal(source, input.Source.Data); Assert.Equal(source, index.Input.Source.Data);
    }
    [Fact]
    public void PaginationIsBoundToInspectionAndRequirementAndRetainsAllCandidatesExactlyOnce()
    {
        var input = SuggestionInput(); var index = Index(input); var requirements = index.Symbols.Where(item => item.Symbol.Kind == "coglatas.requirement").ToArray();
        var first = index.SuggestRequirementTasks(requirements[0].Symbol.Location, input.InputDigest, 1); Assert.NotNull(first.NextCursor);
        Assert.Equal(RequirementTaskSuggestionState.InvalidCursor, index.SuggestRequirementTasks(requirements[1].Symbol.Location, input.InputDigest, cursor: first.NextCursor).State);
        Assert.Equal(RequirementTaskSuggestionState.InvalidCursor, Index(input).SuggestRequirementTasks(requirements[0].Symbol.Location, input.InputDigest, cursor: first.NextCursor).State);
        var keys = first.Candidates.Select(item => item.Symbol.Symbol.Key).ToList(); var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = index.SuggestRequirementTasks(requirements[0].Symbol.Location, input.InputDigest, 1, cursor);
            Assert.InRange(page.ScannedSymbols, 0, CompilerSemanticInspection.MaximumCompletionScan);
            keys.AddRange(page.Candidates.Select(item => item.Symbol.Symbol.Key)); cursor = page.NextCursor;
        }
        Assert.Equal(3, keys.Count); Assert.Equal(3, keys.Distinct().Count());
    }
    [Fact]
    public void StaleForeignAndWrongKindRequestsRevealNoCandidatesAndCancellationThrows()
    {
        var input = SuggestionInput(); var index = Index(input); var origin = Origin(index);
        var stale = index.SuggestRequirementTasks(origin, Input().InputDigest); Assert.Equal(RequirementTaskSuggestionState.StaleInput, stale.State); Assert.Empty(stale.Candidates);
        var foreign = ProjectLocation.Create(origin.DocumentId, Source(99).Context, entityId: origin.EntityId);
        var denied = index.SuggestRequirementTasks(foreign, input.InputDigest); Assert.Equal(RequirementTaskSuggestionState.OutOfScope, denied.State); Assert.Empty(denied.Candidates);
        var task = index.Symbols.First(item => item.Symbol.Kind == "coglatas.task").Symbol.Location;
        Assert.Equal(RequirementTaskSuggestionState.Unavailable, index.SuggestRequirementTasks(task, input.InputDigest).State);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => index.SuggestRequirementTasks(origin, input.InputDigest, cancellationToken: cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SuggestRequirementTasks(origin, input.InputDigest, 33));
    }
    [Fact]
    public void PartialBindingAndAnEmptyLocalSetNeverClaimGlobalAbsenceOrSatisfaction()
    {
        var input = SuggestionInput(0, partial: true); var index = Index(input); var page = index.SuggestRequirementTasks(Origin(index), input.InputDigest);
        Assert.Equal(RequirementTaskSuggestionState.Ready, page.State); Assert.Empty(page.Candidates); Assert.False(page.CoverageComplete); Assert.True(page.CandidateOnly);
    }
    [Theory]
    [InlineData("tenantId")]
    [InlineData("projectId")]
    [InlineData("branchId")]
    [InlineData("revisionId")]
    [InlineData("candidate")]
    public void EveryForeignContextDimensionIsRefusedWithoutCandidateNamesOrCounts(string dimension)
    {
        var input = SuggestionInput(); var inspection = Index(input); var origin = Origin(inspection);
        var context = JsonNode.Parse(input.Source.Context.Data.CanonicalText)!.AsObject();
        if (dimension == "revisionId") context[dimension] = Id(99); else if (dimension != "candidate") context["branch"]![dimension] = Id(99);
        var foreign = dimension == "candidate" ? SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(90)), CandidateRevisionId.Parse(Id(91)),
            input.Source.Context.CommittedRevision!, input.Source.Context.CommittedRevision!)) : SourceRevisionContext.Parse(Json(context));
        var page = inspection.SuggestRequirementTasks(ProjectLocation.Create(origin.DocumentId, foreign, entityId: origin.EntityId), input.InputDigest);
        Assert.Equal(RequirementTaskSuggestionState.OutOfScope, page.State); Assert.Empty(page.Candidates);
        Assert.Equal(0, page.ScannedSymbols); Assert.Null(page.NextCursor); Assert.False(page.CoverageComplete);
    }
    [Fact]
    public void KnownTasksRemainCandidatesUnderPartialBindingWithoutCompleteCoverageCredit()
    {
        var input = SuggestionInput(partial: true); var inspection = Index(input); var page = inspection.SuggestRequirementTasks(Origin(inspection), input.InputDigest);
        Assert.Equal(3, page.Candidates.Count); Assert.False(page.CoverageComplete); Assert.True(page.CandidateOnly);
    }
    [Fact]
    public void AFullNonMatchingScanRetainsItsContinuationUntilTheLaterTaskIsReached()
    {
        var input = SuggestionInput(1); var declarations = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Single(item => item!["kind"]!.GetValue<string>() == "coglatas.task")!["entityId"] = Id(50000);
        for (var index = 0; index < 300; index++) declarations.Add(Obj(new
        {
            entityId = Id(10 + index), documentId = Id(4), kind = "coglatas.milestone", schemaVersion = 1,
            payload = new { target = new { type = new { kind = "coglatas.instant", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                state = "coglatas.known", value = "2026-11-01T00:00:00.0000000Z" } }
        }));
        input = new(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations)), input.Settings);
        var inspection = Index(input); Assert.True(inspection.BindingComplete); var origin = Origin(inspection);
        var page = inspection.SuggestRequirementTasks(origin, input.InputDigest, 1); Assert.Empty(page.Candidates);
        Assert.Equal(CompilerSemanticInspection.MaximumCompletionScan, page.ScannedSymbols); Assert.NotNull(page.NextCursor);
        var candidates = new List<RequirementTaskSuggestion>(); var steps = 0;
        while (page.NextCursor is { } cursor)
        {
            Assert.InRange(++steps, 1, 10); page = inspection.SuggestRequirementTasks(origin, input.InputDigest, 1, cursor);
            Assert.InRange(page.ScannedSymbols, 0, CompilerSemanticInspection.MaximumCompletionScan); candidates.AddRange(page.Candidates);
        }
        Assert.Equal(EntityId.Parse(Id(50000)), Assert.Single(candidates).Symbol.Symbol.EntityId);
    }
    [Fact]
    public void ExportedTaskCandidatePagesReproduceActualCoreQueriesWithoutImplyingSatisfiedRequirements()
    {
        var fixture = WebPreviewContractTests.Cases().Select(row => SourceJson.Parse((string)row[0]).Value)
            .Single(item => item.GetProperty("key").GetString() == "requirement-candidates");
        var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(fixture.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(fixture.GetProperty("manifest").GetRawText())); var index = Index(input);
        var pages = fixture.GetProperty("requirementTaskSuggestions").EnumerateArray().ToArray(); Assert.Equal(2, pages.Length);
        foreach (var encoded in pages)
        {
            var origin = index.Symbols.Single(item => item.Symbol.Key == encoded.GetProperty("originKey").GetString()).Symbol.Location;
            var page = index.SuggestRequirementTasks(origin, input.InputDigest, 32); Assert.True(page.CandidateOnly); Assert.Single(page.Candidates);
            Assert.Equal(page.State.ToString(), encoded.GetProperty("state").GetString()); Assert.Equal(page.InputDigest.Data, SourceJson.Parse(encoded.GetProperty("inputDigest").GetRawText()));
            Assert.Equal(page.CoverageComplete, encoded.GetProperty("coverageComplete").GetBoolean()); Assert.Equal(page.NextCursor is not null, encoded.GetProperty("truncated").GetBoolean());
            var candidate = encoded.GetProperty("candidates")[0]; Assert.Equal(page.Candidates[0].Symbol.Symbol.Key, candidate.GetProperty("symbolKey").GetString());
            Assert.Equal(page.Candidates[0].DisplayName, candidate.GetProperty("displayName").GetString()); Assert.Equal(page.Candidates[0].Reason.ToString(), candidate.GetProperty("reason").GetString());
        }
        Assert.Equal("coglatas.inconclusive", fixture.GetProperty("result").GetProperty("verdict").GetString());
    }
}
