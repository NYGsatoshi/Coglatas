using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ReferenceSearchPageTests
{
    private static CompilerInputManifest ReferencesInput(int count, bool resolved = true)
    {
        var original = Source();
        var declarations = new JsonArray(Obj(new
        {
            entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
            payload = new
            {
                duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
                ports = new[] { new { portId = Id(8), direction = "coglatas.input",
                    type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                    minimumConnections = 0, maximumConnections = 1 } }
            }
        }));
        for (var ordinal = 0; ordinal < count; ordinal++) declarations.Add(Obj(new
        {
            entityId = Id(10 + ordinal), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
            payload = new { assertions = new[] { new { key = "example.reference", kind = "coglatas.assert-reference-resolved",
                target = CompilerAnalysisFixtures.Reference(resolved ? 7 : 9999, original.Context), portId = Id(8) } } }
        }));
        var settings = Settings();
        if (count > 100) settings["executionProfile"]!["maximumOperations"] = 1000000;
        return Input(settings, ProjectSource.Create(original.Context, original.Documents, Json(declarations)));
    }
    [Fact]
    public void PagesRetainEveryActualReferenceOnceWithoutChangingTheOriginalIndexBytes()
    {
        var input = ReferencesInput(70); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var symbol = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))); var original = index.FindReferences(symbol.Symbol.Location);
        var data = index.Data; var digest = index.Digest; var collected = new List<CompilerBoundReference>();
        var page = index.FindReferencesPage(symbol.Symbol.Location, input.InputDigest); var pages = 0;
        while (true)
        {
            pages++; Assert.InRange(pages, 1, 71); Assert.Equal(ReferenceSearchState.Ready, page.State); Assert.True(page.IndexCoverageComplete);
            Assert.Same(input.InputDigest, page.InputDigest); Assert.Same(symbol.Symbol.Location, page.Target);
            Assert.InRange(page.References.Count, 1, CompilerSemanticInspection.MaximumReferenceSearchPageSize);
            Assert.InRange(page.ScannedReferences, 1, CompilerSemanticInspection.MaximumReferenceSearchScan);
            Assert.Equal(page.NextCursor is null, page.ScanComplete); collected.AddRange(page.References);
            if (page.NextCursor is null) break;
            page = index.FindReferencesPage(symbol.Symbol.Location, input.InputDigest, cursor: page.NextCursor);
        }
        Assert.Equal(3, pages); Assert.Equal(70, collected.Count); Assert.Equal(original.Count, collected.Count);
        for (var ordinal = 0; ordinal < original.Count; ordinal++) Assert.Same(original[ordinal], collected[ordinal]);
        Assert.Same(data, index.Data); Assert.Same(digest, index.Digest);
        var again = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input)); Assert.Equal(data, again.Data); Assert.Equal(digest, again.Digest);
    }
    [Fact]
    public void AnEmptyBoundedPrefixRetainsContinuationAndPartialBindingNeverClaimsCompleteCoverage()
    {
        var input = ReferencesInput(257, false); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var symbol = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))); Assert.False(index.BindingComplete);
        var first = index.FindReferencesPage(symbol.Symbol.Location, input.InputDigest);
        Assert.Equal(ReferenceSearchState.Ready, first.State); Assert.Empty(first.References);
        Assert.Equal(256, first.ScannedReferences); Assert.False(first.IndexCoverageComplete); Assert.False(first.ScanComplete); Assert.NotNull(first.NextCursor);
        var last = index.FindReferencesPage(symbol.Symbol.Location, input.InputDigest, cursor: first.NextCursor);
        Assert.Empty(last.References); Assert.Equal(1, last.ScannedReferences); Assert.Null(last.NextCursor); Assert.True(last.ScanComplete); Assert.False(last.IndexCoverageComplete);
        var emptyInput = ReferencesInput(0); var empty = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(emptyInput));
        var noReferences = empty.FindReferencesPage(Assert.Single(empty.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))).Symbol.Location, emptyInput.InputDigest);
        Assert.Empty(noReferences.References); Assert.True(noReferences.IndexCoverageComplete); Assert.True(noReferences.ScanComplete);
    }
    [Fact]
    public void ForeignOrOldInputAndCursorNeverReturnAnotherInspectionsFacts()
    {
        var input = ReferencesInput(3); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input)); var target = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))).Symbol.Location;
        var first = index.FindReferencesPage(target, input.InputDigest, 1); Assert.NotNull(first.NextCursor);
        var wrongSymbol = index.FindReferencesPage(Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(6))).Symbol.Location,
            input.InputDigest, cursor: first.NextCursor);
        Assert.Equal(ReferenceSearchState.InvalidCursor, wrongSymbol.State); Assert.Empty(wrongSymbol.References);
        Assert.Null(wrongSymbol.InputDigest); Assert.Null(wrongSymbol.Target); Assert.Null(wrongSymbol.NextCursor);
        var other = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var refused = other.FindReferencesPage(target, input.InputDigest, cursor: first.NextCursor);
        Assert.Equal(ReferenceSearchState.InvalidCursor, refused.State); Assert.Empty(refused.References); Assert.Null(refused.InputDigest); Assert.Null(refused.Target);
        var stale = index.FindReferencesPage(target, Input().InputDigest);
        Assert.Equal(ReferenceSearchState.StaleInput, stale.State); Assert.Empty(stale.References); Assert.Null(stale.InputDigest); Assert.Null(stale.Target);
        var foreign = ProjectLocation.Create(target.DocumentId, Source(9).Context, target.PropertyPath, entityId: target.EntityId);
        var wrongScope = index.FindReferencesPage(foreign, input.InputDigest);
        Assert.Equal(ReferenceSearchState.OutOfScope, wrongScope.State); Assert.Empty(wrongScope.References);
        Assert.Equal(ReferenceSearchState.Unavailable, index.FindReferencesPage(ProjectLocation.Create(target.DocumentId, input.Source.Context, "/missing", entityId: target.EntityId), input.InputDigest).State);
    }
    [Fact]
    public void PageLimitsAndCancellationRemainExplicit()
    {
        var input = ReferencesInput(2); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input)); var target = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))).Symbol.Location;
        Assert.Throws<ArgumentOutOfRangeException>(() => index.FindReferencesPage(target, input.InputDigest, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.FindReferencesPage(target, input.InputDigest, 33));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledToken = cancelled.Token;
        Assert.Throws<OperationCanceledException>(() => index.FindReferencesPage(target, input.InputDigest, cancellationToken: cancelledToken));
        var page = index.FindReferencesPage(target, input.InputDigest, 1); Assert.Single(page.References); Assert.NotNull(page.NextCursor);
        var last = index.FindReferencesPage(target, input.InputDigest, 2, page.NextCursor); Assert.Single(last.References); Assert.Null(last.NextCursor);
    }
    [Fact]
    public void ARealMatchingReferenceBeyondTheScanCeilingRemainsReachable()
    {
        var original = ReferencesInput(257, false);
        var previous = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(original));
        var finalOrigin = previous.References[^1].Origin;
        var declarations = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        var lastDeclaration = declarations.Single(item => item!["entityId"]!.GetValue<string>() == finalOrigin.EntityId!.Value.ToString())!;
        lastDeclaration["payload"]!["assertions"]![0]!["target"] = CompilerAnalysisFixtures.Reference(7, original.Source.Context);
        var input = Input(JsonNode.Parse(original.Settings.CanonicalText)!.AsObject(),
            ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations)));
        var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var target = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))).Symbol.Location;
        var actualReference = Assert.Single(index.FindReferences(target));
        Assert.Same(index.References[^1], actualReference);
        var first = index.FindReferencesPage(target, input.InputDigest);
        Assert.Empty(first.References); Assert.Equal(256, first.ScannedReferences); Assert.NotNull(first.NextCursor);
        Assert.False(first.IndexCoverageComplete); Assert.False(first.ScanComplete);
        var last = index.FindReferencesPage(target, input.InputDigest, cursor: first.NextCursor);
        Assert.Same(actualReference, Assert.Single(last.References)); Assert.Equal(1, last.ScannedReferences);
        Assert.True(last.ScanComplete); Assert.False(last.IndexCoverageComplete);
    }
    [Theory]
    [InlineData("tenantId")]
    [InlineData("projectId")]
    [InlineData("branchId")]
    [InlineData("revisionId")]
    public void EveryForeignContextDimensionReturnsNoReferenceFacts(string dimension)
    {
        var input = ReferencesInput(3); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var target = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))).Symbol.Location;
        var location = JsonNode.Parse(target.Data.CanonicalText)!;
        if (dimension == "revisionId") location["context"]![dimension] = Id(9998);
        else location["context"]!["branch"]![dimension] = Id(9998);
        var page = index.FindReferencesPage(ProjectLocation.Parse(Json(location)), input.InputDigest);
        Assert.Equal(ReferenceSearchState.OutOfScope, page.State); Assert.Empty(page.References);
        Assert.Null(page.InputDigest); Assert.Null(page.Target); Assert.Null(page.NextCursor);
        Assert.False(page.IndexCoverageComplete); Assert.False(page.ScanComplete); Assert.Equal(0, page.ScannedReferences);
    }
}
