using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ReferenceBindingCacheTests
{
    [Fact]
    public void UnchangedTargetPartitionsSurviveOtherDependencyEditsWhileChangedPortsAndKindsRebind()
    {
        var basis = ReferenceCompletionTests.CompletionInput();
        var external = ProjectSource.Create(Source(9).Context, basis.Source.Documents, basis.Source.Declarations);
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var fixture = SymbolBindingCacheTests.External(external);
        var first = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        var warm = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        Assert.NotEmpty(first.References);
        Assert.All(first.References, reference => Assert.Same(reference, At(warm, reference)));
        var declarations = JsonNode.Parse(external.Declarations.CanonicalText)!.AsArray();
        declarations[2]!["displayName"] = "Unrelated target edit";
        var other = SymbolBindingCacheTests.External(ProjectSource.Create(external.Context, external.Documents, Json(declarations)));
        var edited = Compile(other.Input, cache, symbols, [other.Snapshot]).Model!;
        var portOrigin = Assert.Single(first.References, row => row.Origin.EntityId == EntityId.Parse(Id(70)));
        var kindOrigin = Assert.Single(first.References, row => row.Origin.EntityId == EntityId.Parse(Id(71)));
        Assert.Same(portOrigin, At(edited, portOrigin)); Assert.Same(kindOrigin, At(edited, kindOrigin));
        Assert.NotEqual(first.Input.InputDigest, edited.Input.InputDigest);
        declarations[0]!["payload"]!["ports"] = new JsonArray();
        declarations[1]!["kind"] = "example.opaque-task";
        var changed = SymbolBindingCacheTests.External(ProjectSource.Create(external.Context, external.Documents, Json(declarations)));
        var invalidated = Compile(changed.Input, cache, symbols, [changed.Snapshot]).Model!;
        Assert.NotSame(portOrigin, At(invalidated, portOrigin)); Assert.NotSame(kindOrigin, At(invalidated, kindOrigin));
        Assert.Equal(ReferenceResolutionState.MissingWithinCompleteScope, At(invalidated, portOrigin).State);
        Assert.Equal(ReferenceResolutionState.Unsupported, At(invalidated, kindOrigin).State);
        var restored = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        Assert.Same(portOrigin, At(restored, portOrigin)); Assert.Same(kindOrigin, At(restored, kindOrigin));
        Assert.Equal(external.Data, first.BoundSnapshots.Single().Source.Data);
    }

    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial")]
    [InlineData("wrong-digest")] [InlineData("conflict")]
    public void WarmReferencesCannotSupplyMissingDeniedConflictingOrChangedCoverageDependencies(string change)
    {
        var fixture = SymbolBindingCacheTests.External(Source(9));
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var first = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        CompilerMaterializedSnapshot Replace(RelationScopeSnapshot snapshot) => new(fixture.Snapshot.SnapshotId, fixture.Snapshot.Version, snapshot);
        var external = fixture.Snapshot.Snapshot.Source;
        var supplied = change switch
        {
            "missing" => Array.Empty<CompilerMaterializedSnapshot>(),
            "denied" => [Replace(new(external, false, true, true))],
            "partial" => [Replace(new(external, true, false, false))],
            "wrong-digest" => [Replace(new(ProjectSource.Create(external.Context,
                [SourceDocument.Create(external.Documents[0].DocumentId, "changed", external.Documents[0].Root.Data)]), true, true, true))],
            _ => new[] { fixture.Snapshot, Replace(new(external, false, true, true)) }
        };
        var current = Compile(fixture.Input, cache, symbols, supplied).Model!;
        Assert.All(first.References, reference => Assert.NotSame(reference, At(current, reference)));
        if (change != "partial") Assert.DoesNotContain(current.References, row => row.State == ReferenceResolutionState.Resolved);
        var restored = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        Assert.All(first.References, reference => Assert.Same(reference, At(restored, reference)));
    }

    [Theory]
    [InlineData("scalar")] [InlineData("document")] [InlineData("list")] [InlineData("sequence")]
    [InlineData("set")] [InlineData("nullable")] [InlineData("optional")] [InlineData("structure")]
    [InlineData("map")] [InlineData("unknown")] [InlineData("null")] [InlineData("absent")]
    public void EveryKnownValueShapeAndOpaqueUnknownBasisKeepTheCompleteUncachedIndex(string shape)
    {
        var fixture = CompilerValueReferenceTests.Fixture(shape: shape);
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var first = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        var warm = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        Assert.All(first.References, reference => Assert.Same(reference, At(warm, reference)));
        if (shape is "unknown" or "null" or "absent") Assert.Empty(warm.References);
        else Assert.NotEmpty(warm.References);
    }

    [Theory]
    [InlineData("kind")] [InlineData("version")] [InlineData("digest")]
    [InlineData("origin-document")] [InlineData("origin-context")] [InlineData("origin-entity")]
    [InlineData("missing")] [InlineData("revision")] [InlineData("branch")] [InlineData("tenant")]
    [InlineData("unauthorized")] [InlineData("opaque")] [InlineData("resource")] [InlineData("snapshot")]
    public void TypedOriginKindManifestAndContextChangesNeverBorrowTheEarlierResolution(string change)
    {
        var basis = CompilerValueReferenceTests.Fixture(); var changed = CompilerValueReferenceTests.Fixture(change);
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var first = Compile(basis.Input, cache, symbols, [basis.Snapshot]).Model!;
        var current = Compile(changed.Input, cache, symbols, [changed.Snapshot]).Model!;
        Assert.DoesNotContain(current.References, row => row.State == ReferenceResolutionState.Resolved);
        Assert.All(current.References, row => Assert.DoesNotContain(first.References, earlier => ReferenceEquals(earlier, row)));
        Compile(basis.Input, cache, symbols, [basis.Snapshot]);
    }

    [Fact]
    public void AReusedDocumentReferenceRepublishesItsSymbolAfterTheEarlierFirstReferenceIsRemoved()
    {
        var fixture = CompilerValueReferenceTests.Fixture(shape: "document");
        var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        var ports = declarations[0]!["payload"]!["ports"]!.AsArray();
        var second = ports[0]!.DeepClone(); second["portId"] = Id(10); ports.Add(second);
        CompilerInputManifest Current() => Input(JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject(),
            ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations)));
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var first = Compile(Current(), cache, symbols, [fixture.Snapshot]).Model!;
        var retained = Assert.Single(first.References, row => row.Origin.PropertyPath.Contains(Id(10), StringComparison.Ordinal));
        ports.RemoveAt(0);
        var current = Compile(Current(), cache, symbols, [fixture.Snapshot]).Model!;
        Assert.Same(retained, Assert.Single(current.References));
        Assert.Contains(current.Symbols, symbol => symbol.Key == retained.SymbolKey && symbol.Kind == SourceDocument.SchemaId);
    }

    [Theory]
    [InlineData("rules")] [InlineData("units")] [InlineData("calendars")] [InlineData("observations")]
    [InlineData("descriptors")] [InlineData("source-coverage")] [InlineData("budget")] [InlineData("package")]
    public void ReferenceReuseDoesNotBypassGlobalValidationOrLogicalCharges(string dimension)
    {
        var fixture = SymbolBindingCacheTests.External(Source(9)); var source = fixture.Input.Source;
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        Compile(fixture.Input, cache, symbols, [fixture.Snapshot]);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        if (dimension == "rules") settings["rules"]![0]!["configuration"] = Obj(new { future = true });
        if (dimension is "units" or "calendars") settings[dimension] = new JsonArray(Artifact("example." + dimension));
        if (dimension == "observations") settings["observations"] = new JsonArray(Obj(new { snapshotId = Id(61), version = "1", context = Source(9).Context.Data.Value,
            digest = Source(9).Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        if (dimension == "descriptors") settings["descriptors"] = new JsonArray();
        if (dimension == "source-coverage") settings["sourceCoverage"] = "coglatas.partial";
        if (dimension == "budget") settings["executionProfile"]!["maximumOperations"] = 1;
        if (dimension == "package") source = ProjectSource.Create(source.Context, source.Documents, source.Declarations, source.Relations,
            packageLock: Json(new JsonArray(Artifact("example.unadopted-package"))));
        Compile(Input(settings, source), cache, symbols, [fixture.Snapshot]);
        Compile(fixture.Input, cache, symbols, [fixture.Snapshot]);
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(1, 4096)] [InlineData(2, 4096)]
    public void EntryAndExactDependencyByteLimitsEvictOrDeclineWithoutChangingResults(int entries, int bytes)
    {
        var fixture = SymbolBindingCacheTests.External(ReferenceCompletionTests.CompletionInput().Source);
        var cache = new ProjectReferenceBindingCache(entries, bytes); var symbols = new ProjectSymbolBindingCache();
        Compile(fixture.Input, cache, symbols, [fixture.Snapshot]); Compile(fixture.Input, cache, symbols, [fixture.Snapshot]);
        Assert.InRange(cache.RetainedCount, 0, entries); Assert.InRange(cache.RetainedCanonicalBytes, 0, bytes);
        if (bytes == 1) Assert.Equal(0, cache.RetainedCount);
        else Assert.True(cache.RetainedCount > 0);
        cache.Clear(); Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.RetainedCanonicalBytes);
        Compile(fixture.Input, cache, symbols, [fixture.Snapshot]);
    }

    [Theory]
    [InlineData("tenant")] [InlineData("project")] [InlineData("branch")]
    public void AReferenceFreeDifferentOwnerClearsTheSessionAndCancellationDoesNotPublish(string dimension)
    {
        var fixture = CompilerValueReferenceTests.Fixture();
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var first = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        var branch = fixture.Input.Source.Context.Branch;
        var context = SourceRevisionContext.Committed(new(new(dimension == "tenant" ? TenantId.Parse(Id(90)) : branch.TenantId,
            dimension == "project" ? ProjectId.Parse(Id(90)) : branch.ProjectId,
            dimension == "branch" ? BranchId.Parse(Id(90)) : branch.BranchId), RevisionId.Parse(Id(5))));
        var foreign = Input(source: ProjectSource.Create(context, Source().Documents));
        Compile(foreign, cache, symbols); Assert.Equal(0, cache.RetainedCount);
        var restored = Compile(fixture.Input, cache, symbols, [fixture.Snapshot]).Model!;
        Assert.NotSame(Assert.Single(first.References), Assert.Single(restored.References));
        var count = cache.RetainedCount;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(fixture.Input, [fixture.Snapshot], cancelled.Token, referenceBindingCache: cache));
        Assert.Equal(count, cache.RetainedCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompleteRegionalAndExecutionPlanPublicationsKeepFreshModelsAndOwnedCursors(bool plan)
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true, twoBVersions: true);
        var input = plan ? CompilerExecutionPlanTests.Request(fixture.Input) : CompilerScopedPublicationTests.Upgrade(fixture.Input);
        var cache = new ProjectReferenceBindingCache(); var symbols = new ProjectSymbolBindingCache();
        var first = Compile(input, cache, symbols, fixture.Snapshots);
        var warm = Compile(input, cache, symbols, fixture.Snapshots);
        var a = first.Dependencies!.Binding; var b = warm.Dependencies!.Binding;
        Assert.All(a.Regions.SelectMany(region => region.References), reference => Assert.Contains(b.Regions.SelectMany(region => region.References),
            current => ReferenceEquals(current, reference)));
        var oldIndex = CompilerSemanticIndex.Build(a, input.InputDigest);
        var index = CompilerSemanticIndex.Build(b, input.InputDigest);
        var origin = oldIndex.Symbols.First(row => row.Symbol.PortId is not null).Symbol.Location;
        var completion = oldIndex.CompleteEntityReference(origin, input.InputDigest, 1);
        Assert.NotNull(completion.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, index.CompleteEntityReference(origin, input.InputDigest, 1, completion.NextCursor).State);
        Assert.Equal(oldIndex.Data, index.Data); Assert.NotSame(oldIndex, index);
    }

    [Fact]
    public async Task ConcurrentHostGenerationsAndClearingKeepOriginalInputsAndCancelledRequestsInert()
    {
        var fixtures = Enumerable.Range(0, 12).Select(index => SymbolBindingCacheTests.External(Source(9), Source(100 + index).Context)).ToArray();
        var input = fixtures[0].Input;
        CompilerArtifactIdentity Identity(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Identity("schema"), Identity("ruleset"), Identity("semantics"), Identity("ir"), Identity("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var cache = new ProjectReferenceBindingCache(); var host = new LocalNetCompilerHost(profile, referenceBindingCache: cache);
        var requests = fixtures.Select((fixture, index) => new CompilerHostRequest(new(fixture.Input), index, [fixture.Snapshot])).ToArray();
        var results = await Task.WhenAll(requests.Select((request, index) => { if (index % 3 == 0) cache.Clear(); return host.CompileAsync(request); }));
        for (var index = 0; index < fixtures.Length; index++)
        {
            Assert.Equal(CompilerHostStatus.Completed, results[index].Status); Assert.Equal(index, results[index].ClientGeneration);
            var expected = PureCoreCompiler.Compile(fixtures[index].Input, input.Compiler, [fixtures[index].Snapshot]);
            Assert.Equal(expected.Result.Data, results[index].Compilation!.Result.Data);
            Assert.All(results[index].Compilation!.Model!.References, row => Assert.Equal(fixtures[index].Input.Source.Context, row.Origin.Context));
        }
        var count = cache.RetainedCount;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(CompilerHostStatus.Cancelled, (await host.CompileAsync(requests[0], cancelled.Token)).Status);
        Assert.Equal(count, cache.RetainedCount);
    }

    private static CoreCompilation Compile(CompilerInputManifest input, ProjectReferenceBindingCache cache,
        ProjectSymbolBindingCache symbols, CompilerMaterializedSnapshot[]? snapshots = null) =>
        SymbolBindingCacheTests.AssertEquivalent(input, symbols, snapshots, cache);

    private static CompilerBoundReference At(PartialSemanticModel model, CompilerBoundReference reference) =>
        Assert.Single(model.References, row => row.Origin.Data.Equals(reference.Origin.Data));
}
