using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerSemanticIndexTests
{
    private static (CoreCompilation Compilation, CompilerSemanticIndex Index) Build(CompilerBindingClosureTests.Fixture fixture)
    {
        var compilation = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        Assert.NotNull(compilation.Model);
        var binding = compilation.Dependencies?.Binding ?? CompilerBindingClosure.Build(compilation.Model, fixture.Input.InputDigest);
        return (compilation, CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest));
    }
    private static CompilerIndexedSymbol RootPort(CompilerSemanticIndex index) => Assert.Single(index.Symbols,
        item => item.Symbol.Context.Equals(index.Input.Source.Context) && item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8)));

    [Fact]
    public void CompletionContinuesAfterAnEmptyBoundedScanToActualLateRegionalCandidates()
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: false);
        var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        var template = declarations.Single(row => row!["entityId"]!.GetValue<string>() == Id(7))!;
        for (var i = 0; i < 270; i++)
        {
            var copy = template.DeepClone(); copy["entityId"] = Id(10000 + i);
            copy["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["origin"]!["entityId"] = Id(10000 + i);
            declarations.Add(copy);
        }
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = 3000000;
        var input = Input(settings, ProjectSource.Create(fixture.Input.Source.Context,
            fixture.Input.Source.Documents, Json(declarations), fixture.Input.Source.Relations));
        var (compilation, index) = Build(fixture with { Input = input }); Assert.Equal(CompilerSemanticIndexState.Complete, index.State); Assert.True(index.IndexCoverageComplete);
        var originalResult = compilation.Result.Data;
        var binding = compilation.Dependencies!.Binding;
        var tiny = CompilerSemanticIndex.Build(binding, input.InputDigest, maximumOperations: 2);
        Assert.Equal(CompilerSemanticIndexState.ResourceLimit, tiny.State);
        Assert.Equal(2, tiny.LogicalOperationsConsumed); Assert.Empty(tiny.Symbols); Assert.Empty(tiny.Regions);
        Assert.Same(originalResult, compilation.Result.Data);
        Assert.Equal(index.Data, CompilerSemanticIndex.Build(binding, input.InputDigest).Data);
        var found = new List<CompilerIndexedSymbol>(); CompilerIndexCursor? cursor = null; var emptyContinuation = false;
        do
        {
            var page = index.CompleteEntityReference(RootPort(index).Symbol.Location, input.InputDigest, 1, cursor);
            Assert.Equal(CompilerIndexQueryState.Ready, page.State); Assert.InRange(page.ScannedSymbols, 0, 256);
            emptyContinuation |= page.Candidates.Count == 0 && !page.ScanComplete;
            found.AddRange(page.Candidates); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.True(emptyContinuation);
        Assert.Equal(index.Symbols.Where(row => !row.IsDocument && row.Symbol.PortId is null && row.Symbol.Kind == "coglatas.project"), found);
        Assert.Contains(found, row => row.Symbol.Context.Equals(fixture.A));
    }

    [Fact]
    public void FullIndexIncludesActualTransitiveAndBackReferencesWithoutChangingLegacyInspectionOrCompiler()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true);
        var (compilation, index) = Build(fixture); var binding = compilation.Dependencies!.Binding;
        Assert.Equal(CompilerSemanticIndexState.Complete, index.State); Assert.True(index.IndexCoverageComplete);
        Assert.Equal(3, index.Regions.Count); Assert.Equal(3, index.Symbols.Select(row => row.Symbol.Context).Distinct().Count());
        var original = binding.Regions.SelectMany(region => region.References).OrderBy(row => row.Origin.Data.CanonicalText, StringComparer.Ordinal).ToArray();
        Assert.Equal(6, original.Length); Assert.Equal(original, index.References);
        Assert.Equal(2, new CompilerSemanticInspection(compilation.Model!).References.Count);
        var result = compilation.Result.Data; var source = fixture.Input.Source.Data; var operations = compilation.LogicalOperationsConsumed;
        var legacyInspectionBytes = new CompilerSemanticInspection(compilation.Model!).Data.ToCanonicalBytes();
        foreach (var reference in original)
        {
            var definition = index.FindDefinition(reference.Origin, fixture.Input.InputDigest);
            Assert.Equal(CompilerIndexQueryState.Ready, definition.State); Assert.Equal(ReferenceResolutionState.Resolved, definition.Resolution);
            Assert.Equal(reference.SymbolKey, definition.Target!.Symbol.Key);
            Assert.Contains(index.Symbols, item => ReferenceEquals(item, definition.Target));
        }
        foreach (var symbol in index.Symbols)
        {
            var expected = original.Where(row => row.State == ReferenceResolutionState.Resolved && row.SymbolKey == symbol.Symbol.Key).ToArray();
            var rows = new List<CompilerBoundReference>(); CompilerIndexCursor? cursor = null;
            do
            {
                var page = index.FindReferences(symbol.Symbol.Location, fixture.Input.InputDigest, 1, cursor);
                Assert.Equal(CompilerIndexQueryState.Ready, page.State); Assert.True(page.IndexCoverageComplete);
                Assert.InRange(page.ScannedReferences, 0, CompilerSemanticIndex.MaximumScan); rows.AddRange(page.References); cursor = page.NextCursor;
                Assert.Equal(cursor is null, page.ScanComplete);
            } while (cursor is not null);
            Assert.Equal(expected, rows);
        }
        Assert.Equal(result, compilation.Result.Data); Assert.Equal(source, fixture.Input.Source.Data); Assert.Equal(operations, compilation.LogicalOperationsConsumed);
        Assert.Equal(legacyInspectionBytes, new CompilerSemanticInspection(compilation.Model!).Data.ToCanonicalBytes());
        Assert.Equal(ContentDigest.Compute(CompilerSemanticIndex.Format, index.Data.ToCanonicalBytes()), index.Digest);
        Assert.Equal(fixture.Input.InputDigest.Data, SourceJson.Parse(index.Data.Value.GetProperty("rootInputDigest").GetRawText()));
    }

    [Fact]
    public void TypedCompletionUsesActualDeclaredKindsAcrossAllAdmittedRegionsWithDistinctIdentity()
    {
        var fixture = CompilerBindingClosureTests.Graph(); var (_, index) = Build(fixture); var origin = RootPort(index);
        var expected = index.Regions.SelectMany(region => region.Declarations.Where(row => row.Declaration is ProjectPlanDeclaration)
            .Select(row => (region.Source.Context, Entity: row.Declaration!.EntityId))).ToHashSet();
        var found = new List<CompilerIndexedSymbol>(); CompilerIndexCursor? cursor = null;
        do
        {
            var page = index.CompleteEntityReference(origin.Symbol.Location, fixture.Input.InputDigest, 1, cursor);
            Assert.Equal(CompilerIndexQueryState.Ready, page.State); Assert.Equal("coglatas.project", page.ExpectedKind);
            Assert.True(page.IndexCoverageComplete); Assert.InRange(page.ScannedSymbols, 0, CompilerSemanticIndex.MaximumScan);
            found.AddRange(page.Candidates); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(expected.Count, found.Count); Assert.True(expected.SetEquals(found.Select(item => (item.Symbol.Context, item.Symbol.EntityId))));
        Assert.All(found, item => { Assert.Null(item.Symbol.PortId); Assert.False(item.IsDocument); Assert.Equal("coglatas.project", item.Symbol.Kind); });
        Assert.Equal(3, found.Count(item => item.Symbol.EntityId == EntityId.Parse(Id(6))));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("partial-declarations")]
    [InlineData("partial-relations")]
    public void IncompleteClosureNeverClaimsGlobalAbsenceOrBorrowsMissingDependencyDefinitions(string admission)
    {
        var fixture = CompilerBindingClosureTests.Graph(bAdmission: admission); var (compilation, index) = Build(fixture);
        Assert.Equal(CompilerSemanticIndexState.Incomplete, index.State); Assert.False(index.IndexCoverageComplete);
        var target = Assert.Single(index.Symbols, item => item.Symbol.Context.Equals(fixture.Input.Source.Context) && item.Symbol.EntityId == EntityId.Parse(Id(6)) && !item.IsDocument);
        var page = index.FindReferences(target.Symbol.Location, fixture.Input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Ready, page.State); Assert.False(page.IndexCoverageComplete); Assert.True(page.ScanComplete);
        if (admission is "missing" or "denied") Assert.DoesNotContain(index.Symbols, item => item.Symbol.Context.Equals(fixture.B));
        if (admission == "denied")
        {
            Assert.Null(compilation.Dependencies); Assert.True(Assert.Single(index.Regions).IsRoot);
            Assert.DoesNotContain(index.Symbols, item => item.Symbol.Context.Equals(fixture.A));
            var original = Assert.Single(index.References, row => row.Origin.EntityId == EntityId.Parse(Id(7)));
            Assert.Equal(ReferenceResolutionState.Resolved, original.State);
            var unavailable = index.FindDefinition(original.Origin, fixture.Input.InputDigest);
            Assert.Equal(CompilerIndexQueryState.Unavailable, unavailable.State); Assert.Null(unavailable.Target);
            return;
        }
        var a = compilation.Dependencies!.Binding.Regions.Single(region => region.Source.Context.Equals(fixture.A));
        var reference = Assert.Single(a.References, row => row.Origin.EntityId == EntityId.Parse(Id(90)));
        var definition = index.FindDefinition(reference.Origin, fixture.Input.InputDigest);
        Assert.Equal(reference.State, definition.Resolution);
        if (admission is "missing" or "denied")
        { Assert.Equal(CompilerIndexQueryState.Unresolved, definition.State); Assert.Null(definition.Target); }
        else
        { Assert.Equal(CompilerIndexQueryState.Ready, definition.State); Assert.Equal(reference.SymbolKey, definition.Target!.Symbol.Key); }
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("scenario")]
    public void EqualEntityAndDocumentIdsRemainBoundToTheExactAppliedDependencyContext(string kind)
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: false); var original = fixture.Snapshots[0].Snapshot.Source;
        var committed = original.Context.CommittedRevision!;
        var context = kind == "candidate" ? SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(1500)), CandidateRevisionId.Parse(Id(1501)), committed, committed))
            : SourceRevisionContext.Hypothetical(new(ScenarioId.Parse(Id(1502)), ScenarioRevisionId.Parse(Id(1503)), original.Context, original.Digest));
        var replacement = ProjectSource.Create(context, original.Documents, original.Declarations, original.Relations);
        var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        var value = declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!;
        value["context"] = JsonNode.Parse(context.Data.CanonicalText); value["manifest"]!["digest"] = JsonNode.Parse(replacement.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject(); var pin = settings["authorizedExternalSnapshots"]![0]!;
        pin["context"] = JsonNode.Parse(context.Data.CanonicalText); pin["digest"] = JsonNode.Parse(replacement.Digest.Data.CanonicalText);
        var input = Input(settings, ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations)));
        var body = new CompilerMaterializedSnapshot(fixture.Snapshots[0].SnapshotId, "1", new(replacement, true, true, true));
        var (_, index) = Build(fixture with { Input = input, Snapshots = [body], A = context });
        Assert.True(index.IndexCoverageComplete);
        var reference = Assert.Single(index.References, row => row.Origin.EntityId == EntityId.Parse(Id(7)));
        var definition = index.FindDefinition(reference.Origin, input.InputDigest);
        Assert.Equal(context, definition.Target!.Symbol.Context); Assert.Equal(EntityId.Parse(Id(6)), definition.Target.Symbol.EntityId);
        Assert.DoesNotContain(index.Symbols, row => row.Symbol.Context.Equals(original.Context));
        Assert.Equal(CompilerIndexQueryState.OutOfScope, index.FindReferences(ProjectLocation.Create(DocumentId.Parse(Id(4)), original.Context, entityId: EntityId.Parse(Id(6))), input.InputDigest).State);
    }

    [Fact]
    public void DocumentReferenceDefinitionUsesItsDocumentIdentityRatherThanItsRootEntity()
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: false); var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        var type = Obj(new { kind = "coglatas.document-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = SourceDocument.SchemaId });
        port["type"] = type.DeepClone(); port["defaultValue"]!["type"] = type.DeepClone();
        var value = port["defaultValue"]!["value"]!.AsObject(); value.Remove("entityId"); value["documentId"] = Id(4);
        var input = Input(JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject(), ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations)));
        var (_, index) = Build(fixture with { Input = input });
        var reference = Assert.Single(index.References, row => row.Origin.EntityId == EntityId.Parse(Id(7)));
        var definition = index.FindDefinition(reference.Origin, input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Ready, definition.State); Assert.True(definition.Target!.IsDocument);
        Assert.Null(definition.Target.Symbol.Location.EntityId); Assert.Equal(fixture.A, definition.Target.Symbol.Context);
        Assert.Same(reference, Assert.Single(index.FindReferences(definition.Target.Symbol.Location, input.InputDigest).References));
    }

    [Fact]
    public void CursorOwnershipIncludesIndexQueryKindAndExactAnchorAndStaleRefusalsExposeNoScanFacts()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true); var (_, index) = Build(fixture); var origin = RootPort(index);
        var completion = index.CompleteEntityReference(origin.Symbol.Location, fixture.Input.InputDigest, 1); Assert.NotNull(completion.NextCursor);
        var (_, repeated) = Build(fixture);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, repeated.CompleteEntityReference(origin.Symbol.Location, fixture.Input.InputDigest, cursor: completion.NextCursor).State);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, index.FindReferences(origin.Symbol.Location, fixture.Input.InputDigest, cursor: completion.NextCursor).State);
        var root = Assert.Single(index.Symbols, row => row.Symbol.Context.Equals(fixture.Input.Source.Context) && row.Symbol.EntityId == EntityId.Parse(Id(6)) && !row.IsDocument);
        var references = index.FindReferences(origin.Symbol.Location, fixture.Input.InputDigest, 1); Assert.NotNull(references.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, index.FindReferences(root.Symbol.Location, fixture.Input.InputDigest, cursor: references.NextCursor).State);
        var stale = index.FindReferences(root.Symbol.Location, Source(99).Digest, cursor: references.NextCursor);
        Assert.Equal(CompilerIndexQueryState.StaleInput, stale.State); Assert.Null(stale.InputDigest); Assert.Null(stale.Target);
        Assert.Empty(stale.References); Assert.Equal(0, stale.ScannedReferences); Assert.False(stale.IndexCoverageComplete); Assert.Null(stale.NextCursor);
    }

    [Fact]
    public void IndependentConstructionBudgetAndCancellationDoNotAlterOriginalCompilerOrPublishAPrefix()
    {
        var fixture = CompilerBindingClosureTests.Graph(); var (compilation, index) = Build(fixture); var data = compilation.Result.Data;
        var limited = CompilerSemanticIndex.Build(compilation.Dependencies!.Binding, fixture.Input.InputDigest, maximumOperations: 1);
        Assert.Equal(CompilerSemanticIndexState.ResourceLimit, limited.State); Assert.Empty(limited.Symbols); Assert.Empty(limited.References); Assert.Empty(limited.Regions);
        Assert.Equal(CompilerIndexQueryState.ResourceLimit, limited.FindReferences(RootPort(index).Symbol.Location, fixture.Input.InputDigest).State);
        Assert.Throws<ArgumentOutOfRangeException>(() => CompilerSemanticIndex.Build(compilation.Dependencies.Binding, fixture.Input.InputDigest, maximumOperations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompilerSemanticIndex.Build(compilation.Dependencies.Binding, fixture.Input.InputDigest, maximumOperations: fixture.Input.ExecutionProfile.MaximumOperations + 1));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompilerSemanticIndex.Build(compilation.Dependencies.Binding, fixture.Input.InputDigest, cancellationToken: cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => index.CompleteEntityReference(RootPort(index).Symbol.Location, fixture.Input.InputDigest, cancellationToken: cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => index.FindDefinition(index.References[0].Origin, fixture.Input.InputDigest, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => index.FindReferences(RootPort(index).Symbol.Location, fixture.Input.InputDigest, cancellationToken: cancelled.Token));
        Assert.Equal(data, compilation.Result.Data); Assert.InRange(index.LogicalOperationsConsumed, 1, fixture.Input.ExecutionProfile.MaximumOperations);
    }

    [Fact]
    public void ReachableOnlyAndStaleClosuresNeverBecomeACompleteDeclaredScope()
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: false);
        var model = ProjectSemanticBinder.Bind(fixture.Input, fixture.Snapshots);
        var reachable = CompilerBindingClosure.Build(model, fixture.Input.InputDigest);
        Assert.Equal(CompilerBindingClosureState.Complete, reachable.State);
        var index = CompilerSemanticIndex.Build(reachable, fixture.Input.InputDigest);
        Assert.Equal(CompilerSemanticIndexState.Incomplete, index.State); Assert.False(index.IncludesDeclaredSnapshots); Assert.False(index.IndexCoverageComplete);
        var staleClosure = CompilerBindingClosure.Build(model, Source(99).Digest);
        var stale = CompilerSemanticIndex.Build(staleClosure, fixture.Input.InputDigest);
        Assert.Equal(CompilerSemanticIndexState.StaleInput, stale.State); Assert.Empty(stale.Symbols); Assert.Empty(stale.References);
        var wrongExpected = CompilerSemanticIndex.Build(reachable, Source(99).Digest);
        Assert.Equal(CompilerSemanticIndexState.StaleInput, wrongExpected.State); Assert.Empty(wrongExpected.Regions);
    }

    [Fact]
    public void IndexRostersAndReturnedPagesPreserveOriginalImmutableMembers()
    {
        var fixture = CompilerBindingClosureTests.Graph(); var (_, index) = Build(fixture); var original = index.Data;
        Assert.False(index.Symbols is CompilerIndexedSymbol[]); Assert.False(index.References is CompilerBoundReference[]);
        var copies = index.Symbols.ToArray(); copies[0] = copies[^1] with { DisplayName = "changed copy" };
        if (index.Symbols is IList mutable) Assert.ThrowsAny<Exception>(() => mutable.Clear());
        var page = index.CompleteEntityReference(RootPort(index).Symbol.Location, fixture.Input.InputDigest);
        Assert.All(page.Candidates, candidate => Assert.Contains(index.Symbols, row => ReferenceEquals(row, candidate)));
        Assert.Equal(original, index.Data); Assert.Equal(original, CompilerSemanticIndex.Build(PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots).Dependencies!.Binding, fixture.Input.InputDigest).Data);
    }
}
