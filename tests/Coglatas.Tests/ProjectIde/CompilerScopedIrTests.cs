using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerBindingClosureTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerScopedIrTests
{
    private static CoreCompilation Compile(Fixture fixture, PlanDeclarationReadCache? cache = null) =>
        PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, declarationCache: cache);

    [Fact]
    public void ActualCompilationConsolidatesAllExactRegionsAndOriginalSourceMapsWithoutClaimingGlobalPass()
    {
        var fixture = Graph(backToRoot: true, withRelation: true); var compiled = Compile(fixture);
        var analysis = compiled.Dependencies!; var scoped = Assert.IsType<CompilerScopedIr>(analysis.ScopedIr);
        Assert.Same(fixture.Input, scoped.Input); Assert.Equal(3, scoped.Regions.Count);
        Assert.Equal(3, scoped.Data.Value.GetProperty("regions").GetArrayLength());
        Assert.Equal(3, scoped.SourceMap.Value.GetProperty("regions").GetArrayLength());
        Assert.Equal(scoped.Digest, ContentDigest.Compute(CompilerScopedIr.Format, scoped.Data.ToCanonicalBytes()));
        Assert.Equal(scoped.SourceMapDigest, ContentDigest.Compute(CompilerScopedIr.SourceMapFormat, scoped.SourceMap.ToCanonicalBytes()));
        Assert.Equal(scoped.Digest.Data.CanonicalText, SourceJson.Parse(analysis.Data.Value.GetProperty("scopedIrDigest").GetRawText()).CanonicalText);
        foreach (var region in scoped.Regions)
            foreach (var node in region.Validation!.Ir!.Nodes)
                Assert.Equal(node.Origin, scoped.ResolveNode(region.Binding.Source.Context, node.NodeId));
        var owner = scoped.Regions.Single(region => region.Binding.Source.Context.Equals(fixture.A));
        var relation = Assert.Single(owner.Binding.Relations).Relation!;
        Assert.Equal(relation.Location, scoped.ResolveRelation(fixture.A, relation.RelationId));
        Assert.Null(scoped.ResolveRelation(fixture.Input.Source.Context, relation.RelationId));
        var edge = Assert.Single(owner.Validation!.Ir!.Relations.Value.EnumerateArray());
        Assert.Equal(fixture.A.Data, SourceJson.Parse(edge.GetProperty("source").GetProperty("context").GetRawText()));
        Assert.Equal(fixture.Input.Source.Context.Data, SourceJson.Parse(edge.GetProperty("target").GetProperty("context").GetRawText()));
        Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict); Assert.Null(compiled.Validation!.Ir);
        Assert.Empty(compiled.Result.Artifacts); Assert.False(compiled.Result.EvidenceComplete);
        Assert.Contains(compiled.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.semantic.external-closure-not-lowered");
    }
    [Fact]
    public void TwoVersionsKeepStableNodeIdsInSeparateContextSlotsAndRejectAnUnadmittedVersion()
    {
        var fixture = Graph(twoBVersions: true); var scoped = Compile(fixture).Dependencies!.ScopedIr!;
        var versions = scoped.Regions.Where(region => region.Binding.Source.ProjectId == fixture.B.Branch.ProjectId).ToArray();
        Assert.Equal(2, versions.Length);
        var first = versions[0].Validation!.Ir!.Nodes.Single(node => node.EntityId == EntityId.Parse(Id(77)));
        var second = versions[1].Validation!.Ir!.Nodes.Single(node => node.EntityId == first.EntityId);
        Assert.Equal(first.NodeId, second.NodeId);
        Assert.Equal(first.Origin, scoped.ResolveNode(versions[0].Binding.Source.Context, second.NodeId));
        Assert.Equal(second.Origin, scoped.ResolveNode(versions[1].Binding.Source.Context, first.NodeId));
        Assert.NotEqual(first.Origin, second.Origin);
        var absent = SourceRevisionContext.Committed(new(fixture.B.Branch, RevisionId.Parse(Id(400))));
        Assert.Null(scoped.ResolveNode(absent, first.NodeId));
        Assert.Equal(4, scoped.Regions.Count);
    }
    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")] [InlineData("partial-relations")]
    public void IncompleteDeclaredInputsCannotPublishAnAggregatePrefix(string admission)
    {
        var compiled = Compile(Graph(bAdmission: admission));
        Assert.Null(compiled.Dependencies?.ScopedIr); Assert.Empty(compiled.Result.Artifacts);
        Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
    }
    [Fact]
    public void UnavailableRegionalUnitsCannotBeHiddenByOtherValidRegions()
    {
        var fixture = Graph(transitive: false, typedTransitive: true);
        var original = fixture.Snapshots.Single(snapshot => snapshot.Snapshot.Source.Context.Equals(fixture.B));
        var declarations = JsonNode.Parse(original.Snapshot.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        port["type"] = Obj(new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = false,
            unitId = "example.unavailable", dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } });
        port.AsObject().Remove("defaultValue");
        var b = ProjectSource.Create(fixture.B, original.Snapshot.Source.Documents, Json(declarations));
        var aOriginal = fixture.Snapshots.Single(snapshot => snapshot.Snapshot.Source.Context.Equals(fixture.A));
        var aDeclarations = JsonNode.Parse(aOriginal.Snapshot.Source.Declarations.CanonicalText)!.AsArray();
        aDeclarations.Last()!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(b.Digest.Data.CanonicalText);
        var a = ProjectSource.Create(fixture.A, aOriginal.Snapshot.Source.Documents, Json(aDeclarations));
        var local = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        local[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(a.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]![0]!["digest"] = JsonNode.Parse(a.Digest.Data.CanonicalText);
        settings["authorizedExternalSnapshots"]![1]!["digest"] = JsonNode.Parse(b.Digest.Data.CanonicalText);
        var changed = fixture with { Input = new(ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(local)), Json(settings)),
            Snapshots = [new(aOriginal.SnapshotId, aOriginal.Version, new(a, true, true, true)), new(original.SnapshotId, original.Version, new(b, true, true, true))] };
        var result = Compile(changed); Assert.Null(result.Dependencies!.ScopedIr);
        Assert.Contains(result.Dependencies.Regions.SelectMany(region => region.Diagnostics), diagnostic => diagnostic.Code == "coglatas.semantic.unit-unavailable");
        Assert.NotEqual(CompilerVerdict.Pass, result.Result.Verdict);
    }
    [Fact]
    public void RootSourceChangesInvalidateTheAggregateAndItsForeignSourceMapBasis()
    {
        var fixture = Graph(); var before = Compile(fixture).Dependencies!.ScopedIr!;
        var changed = ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents.Select(document =>
            SourceDocument.Create(document.DocumentId, "changed-root-name", document.Root.Data)), fixture.Input.Source.Declarations, fixture.Input.Source.Relations);
        var after = Compile(fixture with { Input = new(changed, fixture.Input.Settings) }).Dependencies!.ScopedIr!;
        Assert.NotEqual(before.Digest, after.Digest); Assert.NotEqual(before.SourceMapDigest, after.SourceMapDigest);
        Assert.Equal(before.Regions.Single(region => region.Binding.Source.Context.Equals(fixture.B)).Binding.Source.Data,
            after.Regions.Single(region => region.Binding.Source.Context.Equals(fixture.B)).Binding.Source.Data);
    }
    [Fact]
    public void CacheSupplyOrderingAndImmutableViewsPreserveTheAggregate()
    {
        var fixture = Graph(backToRoot: true, twoBVersions: true); var direct = Compile(fixture).Dependencies!.ScopedIr!;
        var cached = Compile(fixture with { Snapshots = fixture.Snapshots.Reverse().ToArray() }, new()).Dependencies!.ScopedIr!;
        Assert.Equal(direct.Data, cached.Data); Assert.Equal(direct.SourceMap, cached.SourceMap);
        Assert.False(direct.Regions is CompilerDependencyRegion[]);
        var view = Assert.IsAssignableFrom<IList<CompilerDependencyRegion>>(direct.Regions);
        Assert.True(view.IsReadOnly); Assert.Throws<NotSupportedException>(() => view[0] = direct.Regions[1]);
        Assert.All(direct.Regions, region => Assert.False(region.Validation!.Ir!.Nodes is ProjectIrNode[]));
    }
    [Fact]
    public void AggregateWorkUsesTheRootBudgetAndNeverPublishesATruncatedArtifact()
    {
        var fixture = Graph(); var full = Compile(fixture); var scoped = full.Dependencies!.ScopedIr!;
        Assert.True(scoped.LogicalOperationsConsumed > 0);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = full.LogicalOperationsConsumed - 1;
        var bounded = Compile(fixture with { Input = new(fixture.Input.Source, Json(settings)) });
        Assert.Equal(CompilerExecutionState.ResourceExhausted, bounded.Result.Execution);
        Assert.Equal(CompilerDependencyAnalysisState.ResourceLimit, bounded.Dependencies!.State);
        Assert.Null(bounded.Dependencies.ScopedIr); Assert.False(bounded.Result.EvidenceComplete);
        Assert.True(bounded.LogicalOperationsConsumed <= bounded.Input.ExecutionProfile.MaximumOperations);
    }
    [Fact]
    public void AggregatePublicationBoundsDoNotBorrowAnUnboundedRegionalArtifact()
    {
        var fixture = Graph(twoBVersions: true); var full = Compile(fixture); var analysis = full.Dependencies!;
        var limit = Math.Max(analysis.Binding.Data.ToCanonicalBytes().Length, analysis.Regions.Max(region => region.Data.ToCanonicalBytes().Length)) + 100;
        Assert.True(analysis.ScopedIr!.Data.ToCanonicalBytes().Length > limit || analysis.ScopedIr.SourceMap.ToCanonicalBytes().Length > limit);
        var source = ProjectSourceCodec.Decode(fixture.Input.Source.Data.ToCanonicalBytes(), new(MaximumBytes: limit)).RequireSource();
        var bounded = Compile(fixture with { Input = new(source, fixture.Input.Settings) });
        Assert.Null(bounded.Dependencies!.ScopedIr); Assert.Equal(CompilerDependencyAnalysisState.ResourceLimit, bounded.Dependencies.State);
        Assert.NotEqual(CompilerVerdict.Pass, bounded.Result.Verdict);
    }
    [Fact]
    public async Task ActualLocalHostCarriesTheSameRootBoundAggregateAndCancellationPublishesNone()
    {
        var fixture = Graph(withRelation: true);
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(fixture.Input.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(fixture.Input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            fixture.Input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        var response = await host.CompileAsync(new(new(fixture.Input), 1, fixture.Snapshots));
        Assert.Equal(CompilerHostStatus.Completed, response.Status);
        Assert.Equal(Compile(fixture).Dependencies!.ScopedIr!.Data, response.Compilation!.Dependencies!.ScopedIr!.Data);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var stopped = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, cancellationToken: cancelled.Token);
        Assert.Null(stopped.Dependencies); Assert.Equal(CompilerExecutionState.Cancelled, stopped.Result.Execution);
    }

    public static IEnumerable<object[]> FailedDependencyCases() => CompilerDependencyAnalysisTests.BrowserDependencyCases();
    public static IEnumerable<object[]> InvalidGraphCases() => CompilerGraphClosureTests.BrowserGraphCases();
    private static (CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots) ReadFixture(string fixture)
    {
        using var document = System.Text.Json.JsonDocument.Parse(fixture); var item = document.RootElement;
        var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        return (input, WebPreviewContractTests.Snapshots(item, input).ToArray());
    }
    [Theory]
    [MemberData(nameof(FailedDependencyCases))]
    public void AStructurallyValidKnownRuleFailureCanHaveScopedIrButSurvivesItsLaterBudgetFailure(string fixture)
    {
        var data = ReadFixture(fixture); var full = PureCoreCompiler.Compile(data.Input, data.Input.Compiler, data.Snapshots);
        Assert.Equal(CompilerVerdict.Fail, full.Result.Verdict); Assert.NotNull(full.Dependencies!.ScopedIr);
        var settings = JsonNode.Parse(data.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = full.LogicalOperationsConsumed - 1;
        var limited = new CompilerInputManifest(data.Input.Source, Json(settings));
        var result = PureCoreCompiler.Compile(limited, limited.Compiler, data.Snapshots);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Result.Execution);
        Assert.Null(result.Dependencies!.ScopedIr); Assert.Equal(CompilerVerdict.Fail, result.Result.Verdict);
        Assert.Contains(result.Dependencies.KnownFailures, failure => failure.RuleId == CoreCompilerRules.Constraints);
    }
    [Theory]
    [MemberData(nameof(InvalidGraphCases))]
    public void AWholeGraphCycleCannotPublishIndividuallyBoundRegionArtifactsAsAConsolidatedIr(string fixture)
    {
        var data = ReadFixture(fixture); var result = PureCoreCompiler.Compile(data.Input, data.Input.Compiler, data.Snapshots);
        Assert.Equal(CompilerVerdict.Fail, result.Result.Verdict);
        Assert.False(result.Dependencies!.Graph!.StructuralValidated); Assert.Null(result.Dependencies.ScopedIr);
    }
}
