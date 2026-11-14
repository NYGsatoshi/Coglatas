using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using System.Text.Json;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerBindingClosureTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerDependencyAnalysisTests
{
    private static Fixture Graph(bool transitive = true, bool backToRoot = false, string bAdmission = "available",
        bool allConcepts = false, bool twoBVersions = false, bool withRelation = false)
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: transitive, backToRoot: backToRoot, bAdmission: bAdmission,
            allConcepts: allConcepts, twoBVersions: twoBVersions, withRelation: withRelation);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["rules"] = CompilerAnalysisFixtures.AllRulesSettings()["rules"]!.DeepClone();
        return fixture with { Input = new(fixture.Input.Source, Json(settings)) };
    }
    private static SourceJson Data(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static CoreCompilation Compile(Fixture fixture) => PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
    private static Fixture ReplaceA(Fixture fixture, Func<ProjectSource, ProjectSource> replace)
    {
        var snapshot = fixture.Snapshots.Single(item => item.Snapshot.Source.Context.Equals(fixture.A));
        var source = replace(snapshot.Snapshot.Source);
        var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(source.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        foreach (var pin in settings["authorizedExternalSnapshots"]!.AsArray().Where(pin => pin!["context"]!.ToJsonString() == JsonNode.Parse(fixture.A.Data.CanonicalText)!.ToJsonString()))
            pin!["digest"] = JsonNode.Parse(source.Digest.Data.CanonicalText);
        return fixture with
        {
            Input = new(ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations)), Json(settings)),
            Snapshots = fixture.Snapshots.Select(item => item == snapshot ? new CompilerMaterializedSnapshot(item.SnapshotId, item.Version, new(source, item.Snapshot.IsAuthorized, item.Snapshot.DeclarationsComplete, item.Snapshot.RelationsComplete)) : item).ToArray()
        };
    }
    private static Fixture Constraint(bool? truth, bool advisory = false, bool attached = true)
    {
        var fixture = Graph(transitive: false);
        return ReplaceA(fixture, source =>
        {
            var original = CompilerAnalysisFixtures.Constraint(truth, advisory: advisory, attached: attached).Source;
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            var constraint = JsonNode.Parse(original.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "coglatas.constraint").GetRawText())!;
            declarations.Add(constraint); var predicate = constraint["payload"]!["predicate"]!;
            var relations = new JsonArray();
            if (attached) relations.Add(Obj(new
            {
                relationId = Id(20), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = CompilerAnalysisFixtures.Reference(7, source.Context) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = CompilerAnalysisFixtures.Reference(6, source.Context) },
                payload = new { predicateReference = CompilerAnalysisFixtures.Reference(7, source.Context), version = source.Context.CommittedRevision!.RevisionId.ToString(),
                    predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(predicate).ToCanonicalBytes()).Data.Value }
            }));
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), Json(relations));
        });
    }

    [Fact]
    public void AnAttachedRequiredFalseDependencyConstraintMakesTheActualRootCompilerFail()
    {
        var fixture = Constraint(false); var result = Compile(fixture);
        Assert.Equal(CompilerVerdict.Fail, result.Result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Result.Coverage);
        var analysis = Assert.IsType<CompilerDependencyAnalysis>(result.Dependencies); Assert.Equal(CompilerDependencyAnalysisState.Evaluated, analysis.State);
        var region = Assert.Single(analysis.Regions, item => !item.Binding.IsRoot);
        var diagnostic = Assert.Single(region.Diagnostics, item => item.Code == "coglatas.analysis.constraint-false");
        Assert.Equal(fixture.A, diagnostic.Location.Context); Assert.NotEqual(fixture.Input.InputId, diagnostic.InputId);
        var summary = Assert.Single(result.Result.Diagnostics, item => item.Discriminator == region.Digest.Value + ":" + diagnostic.DiagnosticId);
        Assert.Equal(fixture.Input.Source.Context, summary.Location.Context); Assert.Equal("coglatas.analysis.dependency-finding", summary.Code);
        Assert.Equal(CompilerEvidenceKind.ModelProof, summary.Evidence.Kind); Assert.Contains(region.Digest, summary.Evidence.Premises);
        Assert.Equal(CompilerVerdict.Fail, Assert.Single(result.Result.RuleOutcomes, rule => rule.RuleId == CoreCompilerRules.Constraints).Verdict);
        Assert.NotNull(region.Validation!.Ir); Assert.Null(result.Validation!.Ir); Assert.Empty(result.Result.Artifacts);
    }
    [Theory]
    [InlineData(true, false)] [InlineData(false, true)] [InlineData(null, false)]
    public void TrueAdvisoryAndUnknownPredicatesDoNotInventGlobalPass(bool? truth, bool advisory)
    {
        var result = Compile(Constraint(truth, advisory));
        Assert.Equal(CompilerVerdict.Inconclusive, result.Result.Verdict);
        var region = Assert.Single(result.Dependencies!.Regions, item => !item.Binding.IsRoot);
        if (advisory) Assert.Contains(region.Diagnostics, item => item.Code == "coglatas.analysis.constraint-false" && item.Severity == CompilerDiagnosticSeverity.Warning);
        Assert.Contains(result.Result.Diagnostics, item => item.Code == "coglatas.semantic.external-closure-not-lowered");
        Assert.Null(result.Validation!.Ir); Assert.False(result.Result.EvidenceComplete);
    }
    [Fact]
    public void AnUnattachedRequiredDependencyConstraintStaysUnknown()
    {
        var result = Compile(Constraint(false, attached: false)); Assert.Equal(CompilerVerdict.Inconclusive, result.Result.Verdict);
        Assert.Contains(result.Dependencies!.Regions.SelectMany(item => item.Diagnostics), item => item.Code == "coglatas.analysis.unattached-required-constraint");
    }
    [Fact]
    public void RequirementFailureSurvivesMissingEvidenceAndUnavailableResourceSupply()
    {
        var fixture = ReplaceA(Graph(transitive: false, allConcepts: true), source =>
        {
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            declarations.Single(item => item!["kind"]!.GetValue<string>() == "coglatas.requirement")!["payload"]!["condition"] = CompilerAnalysisFixtures.Predicate(false);
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), source.Relations);
        });
        var result = Compile(fixture); Assert.Equal(CompilerVerdict.Fail, result.Result.Verdict);
        var rows = result.Dependencies!.Regions.SelectMany(item => item.Diagnostics).ToArray();
        foreach (var code in new[] { "coglatas.analysis.requirement-condition-false", "coglatas.analysis.requirement-evidence-unavailable", "coglatas.analysis.resource-snapshot-unavailable" })
            Assert.Contains(rows, item => item.Code == code);
        Assert.False(result.Result.EvidenceComplete);
    }
    [Fact]
    public void RequiredDependencyRuleFamiliesCannotBeOmittedFromTheRootPolicy()
    {
        var fixture = Constraint(false); var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["rules"]!.AsArray().Remove(settings["rules"]!.AsArray().Single(rule => rule!["ruleId"]!.GetValue<string>() == CoreCompilerRules.Constraints));
        var result = Compile(fixture with { Input = new(fixture.Input.Source, Json(settings)) });
        Assert.NotEqual(CompilerVerdict.Pass, result.Result.Verdict);
        Assert.Contains(result.Dependencies!.Regions.SelectMany(item => item.Diagnostics), item => item.Code == "coglatas.analysis.required-family-missing");
    }
    [Fact]
    public void DependencyTypesAreActuallyCheckedRatherThanOnlyBound()
    {
        var fixture = ReplaceA(Graph(transitive: false, allConcepts: true), source =>
        {
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            var task = declarations.Single(item => item!["kind"]!.GetValue<string>() == "coglatas.task")!;
            task["payload"]!["ports"]![0]!["type"] = Obj(new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = false,
                unitId = "unit.unknown", dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } });
            task["payload"]!["ports"]![0]!.AsObject().Remove("defaultValue");
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), source.Relations);
        });
        var result = Compile(fixture); Assert.Equal(CompilerVerdict.Inconclusive, result.Result.Verdict);
        var region = Assert.Single(result.Dependencies!.Regions, item => !item.Binding.IsRoot);
        Assert.Contains(region.Diagnostics, item => item.Code == "coglatas.semantic.unit-unavailable");
        Assert.Null(region.Validation!.Ir);
    }
    [Fact]
    public void RegionalIrEndpointsCarryTheirActualContextWithRepeatedEntityIdentities()
    {
        var fixture = Graph(transitive: false, withRelation: true); var result = Compile(fixture);
        var region = result.Dependencies!.Regions.Single(item => item.Binding.Source.Context.Equals(fixture.A));
        var ir = Assert.IsType<DerivedProjectIr>(region.Validation!.Ir);
        Assert.Equal("coglatas.project-ir-region/1", ir.Data.Value.GetProperty("format").GetString());
        var edge = ir.Relations.Value.EnumerateArray().Single();
        Assert.Equal(fixture.A.Data, SourceJson.Parse(edge.GetProperty("source").GetProperty("context").GetRawText()));
        Assert.Equal(fixture.Input.Source.Context.Data, SourceJson.Parse(edge.GetProperty("target").GetProperty("context").GetRawText()));
        Assert.NotEqual(edge.GetProperty("source").GetProperty("irNodeId").GetString(), edge.GetProperty("target").GetProperty("irNodeId").GetString());
        Assert.All(ir.Nodes, item => Assert.Equal(fixture.A, item.Origin.Context));
        Assert.Equal(ContentDigest.Compute("coglatas.project-ir-region/1", ir.Data.ToCanonicalBytes()), ir.Digest);
    }
    [Fact]
    public void TwoVersionsRetainStableNodeIdsAndSeparateRegionalSourceMaps()
    {
        var fixture = Graph(twoBVersions: true); var result = Compile(fixture);
        var regions = result.Dependencies!.Regions.Where(item => item.Binding.Source.ProjectId == fixture.B.Branch.ProjectId).ToArray();
        Assert.Equal(2, regions.Length); var a = regions[0].Validation!.Ir!; var b = regions[1].Validation!.Ir!;
        Assert.Equal(a.Nodes.Select(item => item.NodeId), b.Nodes.Select(item => item.NodeId));
        Assert.NotEqual(a.SourceMapDigest, b.SourceMapDigest); Assert.NotEqual(a.Digest, b.Digest);
        Assert.NotEqual(regions[0].Binding.Source.Context, regions[1].Binding.Source.Context);
        Assert.NotEqual(CompilerVerdict.Pass, result.Result.Verdict);
    }
    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")] [InlineData("partial-relations")]
    public void IncompleteOrDeniedSupplyCannotBorrowRegionalProof(string supply)
    {
        var fixture = Graph(bAdmission: supply); var result = Compile(fixture);
        Assert.NotEqual(CompilerVerdict.Pass, result.Result.Verdict);
        if (result.Dependencies is { } analysis)
        {
            Assert.Equal(CompilerDependencyAnalysisState.IncompleteBinding, analysis.State); Assert.Empty(analysis.Regions);
            if (supply == "denied") Assert.DoesNotContain(fixture.B.Data.CanonicalText, analysis.Data.CanonicalText);
        }
        Assert.Null(result.Validation!.Ir);
    }
    [Fact]
    public void BindingSemanticIrAndRuleWorkShareOneRootBudget()
    {
        var fixture = Constraint(false); var full = Compile(fixture);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = full.LogicalOperationsConsumed - 1;
        var limited = Compile(fixture with { Input = new(fixture.Input.Source, Json(settings)) });
        Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution);
        Assert.True(limited.LogicalOperationsConsumed <= limited.Input.ExecutionProfile.MaximumOperations);
        Assert.NotEqual(CompilerVerdict.Pass, limited.Result.Verdict);
        Assert.Contains(limited.Result.Diagnostics, item => item.Code.Contains("budget", StringComparison.Ordinal));
        Assert.Equal(CompilerDependencyAnalysisState.ResourceLimit, limited.Dependencies!.State);
    }
    [Fact]
    public void SuppliesAreReadOnceAndCacheAndSupplyPermutationPreserveTheReceipt()
    {
        var fixture = Graph(backToRoot: true); var count = 0;
        IEnumerable<UnitDefinition?> Supply() { count++; if (count != 1) throw new InvalidOperationException("Repeated caller supply"); yield break; }
        var direct = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, units: Supply());
        Assert.Equal(1, count); var cache = new PlanDeclarationReadCache();
        var cached = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots.Reverse(), declarationCache: cache);
        Assert.Equal(direct.Result.Data, cached.Result.Data); Assert.Equal(direct.Dependencies!.Data, cached.Dependencies!.Data);
        Assert.False(direct.Dependencies.Regions is CompilerDependencyRegion[]);
        Assert.Equal(ContentDigest.Compute(CompilerDependencyAnalysis.Format, direct.Dependencies.Data.ToCanonicalBytes()), direct.Dependencies.Digest);
    }
    [Fact]
    public async Task ActualLocalHostReturnsTheSameDependencyFailureAndCancellationPublishesNoReceipt()
    {
        var fixture = Constraint(false);
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(fixture.Input.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(fixture.Input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            fixture.Input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        var response = await host.CompileAsync(new(new(fixture.Input), 1, fixture.Snapshots));
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.Equal(Compile(fixture).Result.Data, response.Compilation!.Result.Data);
        Assert.Equal(CompilerVerdict.Fail, response.Compilation.Result.Verdict);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, cancellationToken: cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Result.Execution); Assert.Null(cancelled.Dependencies); Assert.Null(cancelled.Model);
    }

    [Fact]
    public void InvalidDependencyDeclarationsRemainKnownFailuresWhenBindingCannotClose()
    {
        var fixture = ReplaceA(Graph(transitive: false, allConcepts: true), source =>
        {
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            declarations.Single(item => item!["kind"]!.GetValue<string>() == "coglatas.task")!["payload"]!["duration"]!["value"] = "invalid-duration";
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), source.Relations);
        });
        var result = Compile(fixture); Assert.Equal(CompilerVerdict.Fail, result.Result.Verdict);
        Assert.Equal(CompilerDependencyAnalysisState.IncompleteBinding, result.Dependencies!.State);
        Assert.Empty(result.Dependencies.Regions); Assert.Null(result.Validation!.Ir);
        Assert.Contains(result.Result.Diagnostics, item => item.Code == "coglatas.analysis.dependency-finding" && item.Evidence.Kind == CompilerEvidenceKind.ModelProof);
    }
    [Fact]
    public void RootChangesInvalidateUnchangedDependencyAnalysisCertificates()
    {
        var fixture = Constraint(false); var before = Compile(fixture);
        var changed = ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents.Select(document =>
            SourceDocument.Create(document.DocumentId, "changed-root-name", document.Root.Data)), fixture.Input.Source.Declarations, fixture.Input.Source.Relations);
        var after = Compile(fixture with { Input = new(changed, fixture.Input.Settings) });
        var a = before.Dependencies!.Regions.Single(item => !item.Binding.IsRoot); var b = after.Dependencies!.Regions.Single(item => !item.Binding.IsRoot);
        Assert.Equal(a.Binding.Source.Data, b.Binding.Source.Data); Assert.NotEqual(a.Digest, b.Digest);
        Assert.NotEqual(before.Dependencies.Digest, after.Dependencies.Digest);
        Assert.Equal(CompilerVerdict.Fail, after.Result.Verdict);
        Assert.NotEqual(before.Result.ResultId, after.Result.ResultId);
    }
    [Fact]
    public void PublicationBoundsNeverReturnACompleteRegionalPrefix()
    {
        var fixture = Graph(backToRoot: true); var full = Compile(fixture);
        var source = ProjectSourceCodec.Decode(fixture.Input.Source.Data.ToCanonicalBytes(),
            new(MaximumBytes: full.Dependencies!.Binding.Data.ToCanonicalBytes().Length + 100)).RequireSource();
        var bounded = Compile(fixture with { Input = new(source, fixture.Input.Settings) });
        Assert.Equal(CompilerExecutionState.ResourceExhausted, bounded.Result.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, bounded.Result.Verdict);
        Assert.Equal(CompilerDependencyAnalysisState.ResourceLimit, bounded.Dependencies!.State);
        Assert.Empty(bounded.Dependencies.Regions);
    }

    public static IEnumerable<object[]> BrowserDependencyCases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "dependency-analysis-v1.json"))) directory = directory.Parent;
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "frontend", "public", "assets", "projectide", "dependency-analysis-v1.json")));
        Assert.Equal(3, data.RootElement.GetProperty("cases").GetArrayLength());
        foreach (var item in data.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(BrowserDependencyCases))]
    public async Task ActualCoreFixtureAndWindowsPureViewPreserveDependencyFailureApartFromLocalTestPass(string fixture)
    {
        new WebPreviewContractTests().BrowserFixturesReproduceExactCoreInputsResultsAndSourceLocations(fixture);
        using var document = JsonDocument.Parse(fixture); var item = document.RootElement;
        var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString())));
        var request = new CompilerHostRequest(new(input), 1, WebPreviewContractTests.Snapshots(item, input));
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await host.CompileAsync(request)));
        Assert.Equal(CompilerVerdict.Fail, view.Result!.Verdict); Assert.False(view.HasCompleteLocalPass);
        var diagnostic = view.Problems.First(row => row.Code == "coglatas.analysis.dependency-finding");
        Assert.True(view.SelectDiagnostic(diagnostic)); Assert.Equal(input.Source.Context, view.SelectedDiagnostic!.Location.Context);
        Assert.Null(view.DiagnosticSymbol); Assert.True(view.RunSyntheticTests());
        Assert.Equal(item.GetProperty("key").GetString()!.EndsWith("-base", StringComparison.Ordinal) ? CompilerVerdict.Fail : CompilerVerdict.Pass, view.TestRun!.Verdict);
        Assert.Equal(CompilerVerdict.Fail, view.Result.Verdict); view.Reset(); Assert.Null(view.Result); Assert.Empty(view.Problems);
    }

    [Theory]
    [InlineData(true, CompilerVerdict.Inconclusive)]
    [InlineData(false, CompilerVerdict.Fail)]
    public void AnotherContextsConstraintWithTheSameEntityIdDoesNotAttachTheLocalRequiredPredicate(bool remoteTruth, CompilerVerdict expected)
    {
        var fixture = Constraint(false, attached: false);
        var remotePredicate = CompilerAnalysisFixtures.Predicate(remoteTruth);
        var b = ProjectSource.Create(fixture.B, fixture.Input.Source.Documents, Data(new[] { new
        {
            entityId = Id(7), documentId = Id(4), kind = "coglatas.constraint", schemaVersion = 1,
            payload = new { predicate = remotePredicate, classification = "coglatas.required" }
        } }));
        fixture = ReplaceA(fixture, source => ProjectSource.Create(source.Context, source.Documents, source.Declarations,
            Data(new[] { new
            {
                relationId = Id(20), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = CompilerAnalysisFixtures.Reference(7, b.Context) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = CompilerAnalysisFixtures.Reference(6, source.Context) },
                payload = new { predicateReference = CompilerAnalysisFixtures.Reference(7, b.Context), version = b.Context.CommittedRevision!.RevisionId.ToString(),
                    predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(remotePredicate).ToCanonicalBytes()).Data.Value }
            } })));
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]!.AsArray().Add(Obj(new { snapshotId = Id(31), version = "1", digest = b.Digest.Data.Value,
            context = b.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        fixture = fixture with { Input = new(fixture.Input.Source, Json(settings)), Snapshots = [..fixture.Snapshots, new(ExternalSnapshotId.Parse(Id(31)), "1", new(b, true, true, true))] };
        var result = Compile(fixture); Assert.Equal(expected, result.Result.Verdict);
        var remote = result.Dependencies!.Regions.Single(item => item.Binding.Source.Context.Equals(fixture.B));
        Assert.Equal(!remoteTruth, remote.Diagnostics.Any(item => item.Code == "coglatas.analysis.constraint-false"));
        var a = result.Dependencies!.Regions.Single(item => item.Binding.Source.Context.Equals(fixture.A));
        Assert.NotNull(a.Validation!.Ir);
        Assert.Contains(a.Diagnostics, item => item.Code == "coglatas.analysis.unattached-required-constraint");
        Assert.DoesNotContain(a.Diagnostics, item => item.Code == "coglatas.analysis.constraint-false");
        Assert.Equal(fixture.B.Data, SourceJson.Parse(a.Validation.Ir.Relations.Value.EnumerateArray().Single().GetProperty("source").GetProperty("context").GetRawText()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KnownRequiredDependencyFailuresSurviveRegionalAndAggregatePublicationLimits(bool aggregate)
    {
        var fixture = Constraint(false); var full = Compile(fixture); var proof = full.Dependencies!;
        var maxRegion = proof.Regions.Max(region => region.Data.ToCanonicalBytes().Length);
        var limit = aggregate ? Math.Max(maxRegion, proof.Binding.Data.ToCanonicalBytes().Length) + 100
            : proof.Binding.Data.ToCanonicalBytes().Length + 100;
        Assert.True(proof.Data.ToCanonicalBytes().Length > limit);
        if (aggregate) Assert.All(proof.Regions, region => Assert.True(region.Data.ToCanonicalBytes().Length <= limit));
        else Assert.Contains(proof.Regions, region => region.Data.ToCanonicalBytes().Length > limit);
        var source = ProjectSourceCodec.Decode(fixture.Input.Source.Data.ToCanonicalBytes(), new(MaximumBytes: limit)).RequireSource();
        var limited = Compile(fixture with { Input = new(source, fixture.Input.Settings) });
        Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution);
        Assert.Equal(CompilerVerdict.Fail, limited.Result.Verdict); Assert.Equal(CompilerCoverage.Partial, limited.Result.Coverage);
        var retained = limited.Dependencies!; Assert.Equal(CompilerDependencyAnalysisState.ResourceLimit, retained.State);
        Assert.Empty(retained.Regions);
        var failure = Assert.Single(retained.KnownFailures, item => item.RuleId == CoreCompilerRules.Constraints);
        Assert.Equal(fixture.A, failure.Context);
        Assert.Contains(limited.Result.Diagnostics, item => item.Code == "coglatas.analysis.dependency-known-failure" &&
            item.Evidence.Kind == CompilerEvidenceKind.ModelProof && item.Evidence.Premises.Contains(failure.Digest));
        Assert.False(limited.Result.EvidenceComplete);
    }

    [Fact]
    public void EstablishedForeignSemanticFailureSurvivesExhaustionBeforeRuleExecution()
    {
        var unitData = Data(new { unitId = "unit.mass", version = 1, dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } });
        var unit = new UnitDefinition(unitData, ContentDigest.Compute("coglatas.unit-definition/1", unitData.ToCanonicalBytes()));
        var fixture = ReplaceA(Graph(transitive: false, allConcepts: true), source =>
        {
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            var port = declarations.Single(item => item!["kind"]!.GetValue<string>() == "coglatas.task")!["payload"]!["ports"]![0]!;
            port["type"] = Obj(new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = false, unitId = "unit.mass",
                dimension = new Dictionary<string, int> { ["dimension.length"] = 1 } });
            port.AsObject().Remove("defaultValue");
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), source.Relations);
        });
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["units"] = new JsonArray(Obj(new { artifactId = unit.UnitId, version = "1", digest = unit.Digest.Data.Value }));
        fixture = fixture with { Input = new(fixture.Input.Source, Json(settings)) };
        var full = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, units: [unit]);
        Assert.Equal(CompilerVerdict.Fail, full.Result.Verdict);
        var semanticCost = full.Dependencies!.Regions.Sum(region => region.Validation!.LogicalOperationsConsumed - region.Validation.Model.LogicalOperationsConsumed);
        settings["executionProfile"]!["maximumOperations"] = full.Dependencies.Binding.LogicalOperationsConsumed + semanticCost - 1;
        var limitedInput = new CompilerInputManifest(fixture.Input.Source, Json(settings));
        var limited = PureCoreCompiler.Compile(limitedInput, limitedInput.Compiler, fixture.Snapshots, units: [unit]);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution); Assert.Equal(CompilerVerdict.Fail, limited.Result.Verdict);
        Assert.Empty(limited.Dependencies!.Regions);
        Assert.Contains(limited.Dependencies.KnownFailures, failure => failure.Context.Equals(fixture.A) &&
            failure.Diagnostic?.Code == "coglatas.semantic.unit-dimension");
        Assert.Contains(limited.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.analysis.dependency-known-failure" && diagnostic.Evidence.Kind == CompilerEvidenceKind.ModelProof);
    }
    [Fact]
    public void InnerResultPublicationLimitDoesNotSkipALaterRegionsRequiredFalseConstraint()
    {
        var fixture = Graph();
        fixture = ReplaceA(fixture, source =>
        {
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            declarations.Add(Obj(new { entityId = Id(501), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
            {
                duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
                ports = Enumerable.Range(10000, 3000).Select(id => new { portId = Id(id), direction = "coglatas.input", minimumConnections = 0, maximumConnections = 1,
                    type = new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = false, unitId = "unit.unavailable", dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } } }).ToArray()
            } }));
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), source.Relations);
        });
        var originalB = fixture.Snapshots.Single(snapshot => snapshot.Snapshot.Source.Context.Equals(fixture.B));
        var template = CompilerAnalysisFixtures.Constraint(false).Source;
        var constraint = template.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "coglatas.constraint");
        var relation = JsonNode.Parse(template.Relations.CanonicalText)!.AsArray()[0]!.DeepClone();
        relation["source"]!["reference"] = CompilerAnalysisFixtures.Reference(7, fixture.B);
        relation["target"]!["reference"] = CompilerAnalysisFixtures.Reference(6, fixture.B);
        relation["payload"]!["predicateReference"] = CompilerAnalysisFixtures.Reference(7, fixture.B);
        relation["payload"]!["version"] = fixture.B.CommittedRevision!.RevisionId.ToString();
        var b = ProjectSource.Create(fixture.B, originalB.Snapshot.Source.Documents,
            Data(originalB.Snapshot.Source.Declarations.Value.EnumerateArray().Append(constraint).ToArray()), Json(new JsonArray(relation)));
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = 10000000;
        settings["executionProfile"]!["maximumDeclarations"] = 10000;
        foreach (var pin in settings["authorizedExternalSnapshots"]!.AsArray())
            if (pin!["snapshotId"]!.GetValue<string>() == originalB.SnapshotId.ToString()) pin["digest"] = JsonNode.Parse(b.Digest.Data.CanonicalText);
        fixture = fixture with { Input = new(fixture.Input.Source, Json(settings)),
            Snapshots = fixture.Snapshots.Select(snapshot => snapshot == originalB ? new CompilerMaterializedSnapshot(snapshot.SnapshotId, snapshot.Version, new(b, true, true, true)) : snapshot).ToArray() };
        var result = Compile(fixture); Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Result.Execution);
        Assert.True(result.LogicalOperationsConsumed < fixture.Input.ExecutionProfile.MaximumOperations);
        var a = result.Dependencies!.Regions.Single(region => region.Binding.Source.Context.Equals(fixture.A));
        Assert.Contains(a.Diagnostics, diagnostic => diagnostic.Code == "coglatas.analysis.publication-budget");
        var later = result.Dependencies.Regions.Single(region => region.Binding.Source.Context.Equals(fixture.B));
        Assert.Contains(later.Diagnostics, diagnostic => diagnostic.Code == "coglatas.analysis.constraint-false");
        Assert.Equal(CompilerVerdict.Fail, result.Result.Verdict);
    }
}
