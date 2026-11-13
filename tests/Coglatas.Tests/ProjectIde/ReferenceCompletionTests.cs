using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ReferenceCompletionTests
{
    internal static CompilerInputManifest CompletionInput(int tasks = 3, string expectedKind = "coglatas.task", bool partial = false)
    {
        var original = Source();
        var declarations = new JsonArray();
        for (var i = 0; i < tasks; i++) declarations.Add(Obj(new
        {
            entityId = Id(10 + i), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, displayName = "Task " + i,
            payload = new
            {
                duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
                ports = new[] { new { portId = Id(10000 + i), direction = "coglatas.input",
                    type = new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind },
                    minimumConnections = 0, maximumConnections = 1 } }
            }
        }));
        var settings = Settings(); if (partial) settings["sourceCoverage"] = "coglatas.partial";
        // Large scan fixtures must first admit a complete model under the existing compiler budget.
        if (tasks > 100) settings["executionProfile"]!["maximumOperations"] = 1000000;
        return Input(settings, ProjectSource.Create(original.Context, original.Documents, Json(declarations)));
    }

    [Fact]
    public void CanonicalPortTypeFiltersKnownLocalDeclarationsAndPreservesInput()
    {
        var input = CompletionInput(); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var origin = index.Symbols.First(item => item.Symbol.PortId is not null).Symbol.Location;
        var page = index.CompleteEntityReference(origin, input.InputDigest);
        Assert.Equal(ReferenceCompletionState.Ready, page.State); Assert.Equal("coglatas.task", page.ExpectedKind);
        Assert.True(page.CoverageComplete); Assert.Null(page.NextCursor); Assert.Equal(3, page.Candidates.Count);
        Assert.All(page.Candidates, candidate => { Assert.Null(candidate.Symbol.Symbol.PortId); Assert.Equal("coglatas.task", candidate.Symbol.Symbol.Kind); Assert.StartsWith("Task ", candidate.DisplayName); });
        Assert.Equal(input.Source.Data, index.Input.Source.Data);
        Assert.Equal(page.Candidates.Select(item => item.Symbol.Symbol.Key), index.CompleteEntityReference(origin, input.InputDigest).Candidates.Select(item => item.Symbol.Symbol.Key));
    }

    [Fact]
    public void PaginationIsBoundToInspectionAndOriginAndDoesNotClaimMissingCandidates()
    {
        var input = CompletionInput(); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var ports = index.Symbols.Where(item => item.Symbol.PortId is not null).ToArray();
        var page = index.CompleteEntityReference(ports[0].Symbol.Location, input.InputDigest, 1);
        Assert.NotNull(page.NextCursor);
        var keys = page.Candidates.Select(item => item.Symbol.Symbol.Key).ToList();
        var cursor = page.NextCursor;
        while (cursor is not null)
        {
            page = index.CompleteEntityReference(ports[0].Symbol.Location, input.InputDigest, 1, cursor);
            Assert.InRange(page.ScannedSymbols, 0, CompilerSemanticInspection.MaximumCompletionScan);
            keys.AddRange(page.Candidates.Select(item => item.Symbol.Symbol.Key)); cursor = page.NextCursor;
        }
        Assert.Equal(3, keys.Count); Assert.Equal(3, keys.Distinct().Count());
        var first = index.CompleteEntityReference(ports[0].Symbol.Location, input.InputDigest, 1);
        Assert.Equal(ReferenceCompletionState.InvalidCursor, index.CompleteEntityReference(ports[1].Symbol.Location, input.InputDigest, cursor: first.NextCursor).State);
        var other = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        Assert.Equal(ReferenceCompletionState.InvalidCursor, other.CompleteEntityReference(ports[0].Symbol.Location, input.InputDigest, cursor: first.NextCursor).State);
    }

    [Fact]
    public void RefusesStaleForeignAndNonPortOriginsAndHonorsCancellationAndBounds()
    {
        var input = CompletionInput(); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var port = index.Symbols.First(item => item.Symbol.PortId is not null).Symbol;
        Assert.Equal(ReferenceCompletionState.StaleInput, index.CompleteEntityReference(port.Location, Input().InputDigest).State);
        var foreign = ProjectLocation.Create(port.Location.DocumentId, Source(99).Context, port.Location.PropertyPath, entityId: port.EntityId);
        Assert.Equal(ReferenceCompletionState.OutOfScope, index.CompleteEntityReference(foreign, input.InputDigest).State);
        Assert.Equal(ReferenceCompletionState.Unavailable, index.CompleteEntityReference(index.Symbols.First(item => item.Symbol.PortId is null).Symbol.Location, input.InputDigest).State);
        Assert.Throws<ArgumentOutOfRangeException>(() => index.CompleteEntityReference(port.Location, input.InputDigest, 33));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => index.CompleteEntityReference(port.Location, input.InputDigest, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void PartialCoverageAndWrongExpectedKindNeverBecomeGlobalAbsenceOrUntypedMatches()
    {
        var input = CompletionInput(expectedKind: "coglatas.milestone", partial: true);
        var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var port = index.Symbols.First(item => item.Symbol.PortId is not null);
        var page = index.CompleteEntityReference(port.Symbol.Location, input.InputDigest);
        Assert.Equal(ReferenceCompletionState.Ready, page.State); Assert.Empty(page.Candidates); Assert.False(page.CoverageComplete);
    }

    [Fact]
    public void ScanBudgetRetainsAContinuationEvenWhenNoCandidateMatches()
    {
        var input = CompletionInput(150, expectedKind: "coglatas.milestone");
        var model = ProjectSemanticBinder.Bind(input);
        Assert.True(model.BindingComplete, string.Join(",", model.Findings.Select(finding => finding.Diagnostic.Code)));
        var index = new CompilerSemanticInspection(model);
        var origin = index.Symbols.First(item => item.Symbol.PortId is not null).Symbol.Location;
        var first = index.CompleteEntityReference(origin, input.InputDigest);
        Assert.Equal(ReferenceCompletionState.Ready, first.State);
        Assert.Equal(256, first.ScannedSymbols); Assert.Empty(first.Candidates); Assert.NotNull(first.NextCursor);
        var last = index.CompleteEntityReference(origin, input.InputDigest, cursor: first.NextCursor);
        Assert.Null(last.NextCursor); Assert.Empty(last.Candidates);
        Assert.Equal(index.Symbols.Count, first.ScannedSymbols + last.ScannedSymbols);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("projectId")]
    [InlineData("branchId")]
    [InlineData("revisionId")]
    public void EveryForeignContextDimensionIsRefused(string dimension)
    {
        var input = CompletionInput(); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var location = JsonNode.Parse(index.Symbols.First(item => item.Symbol.PortId is not null).Symbol.Location.Data.CanonicalText)!;
        if (dimension == "revisionId") location["context"]![dimension] = Id(99);
        else location["context"]!["branch"]![dimension] = Id(99);
        var page = index.CompleteEntityReference(ProjectLocation.Parse(Json(location)), input.InputDigest);
        Assert.Equal(ReferenceCompletionState.OutOfScope, page.State); Assert.Empty(page.Candidates); Assert.False(page.CoverageComplete);
    }

    [Fact]
    public void DuplicateDeclarationIdentityIsRejectedBeforeIndexing()
    {
        var original = CompletionInput();
        var declarations = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        declarations[2]!["entityId"] = Id(11);
        Assert.Throws<FormatException>(() => ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations)));
    }

    [Fact]
    public void AScalarPortDoesNotBecomeAnUntypedReferenceQuery()
    {
        var original = CompletionInput(1);
        var declarations = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["ports"]![0]!["type"] = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = true });
        var input = Input(source: ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations)));
        var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var origin = index.Symbols.Single(item => item.Symbol.PortId is not null);
        var page = index.CompleteEntityReference(origin.Symbol.Location, input.InputDigest);
        Assert.Equal(ReferenceCompletionState.Unsupported, page.State); Assert.Empty(page.Candidates); Assert.False(page.CoverageComplete);
    }

    [Fact]
    public async Task WindowsPreparationRechecksCanonicalCandidatesAndClearsOnSelectionAndLifecycleBoundaries()
    {
        var input = CompletionInput();
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var request = new CompilerHostRequest(new(input), 1);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await new LocalNetCompilerHost(profile).CompileAsync(request)));
        var port = view.Inspection!.Symbols.First(item => item.Symbol.PortId is not null);
        Assert.True(view.Select(port.Symbol.Location)); var page = view.CompleteEntityReference()!;
        Assert.NotNull(page); Assert.Equal(3, page.Candidates.Count);
        Assert.True(view.NavigateCompletion(page, page.Candidates[0]));
        Assert.False(view.NavigateCompletion(page, page.Candidates[0] with { DisplayName = "injected" })); Assert.Null(view.SourceTarget);
        Assert.Equal(input.Source.Data, view.Source!.Data);
        Assert.True(view.Select(port.Symbol.Location)); Assert.Null(view.ReferenceCompletion);
        Assert.False(view.NavigateCompletion(page, page.Candidates[0]));
        view.Reset(); Assert.Null(view.ReferenceCompletion); Assert.Null(view.SourceTarget);
    }
}
