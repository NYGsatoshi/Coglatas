using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerBindingClosureTests;
namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerScopedPublicationTests
{
    internal static CompilerInputManifest Upgrade(CompilerInputManifest input)
    {
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["semantics"]!["version"] = "2"; settings["ir"]!["version"] = "2";
        return new(input.Source, Json(settings));
    }
    private static CompilerHostProfile Profile(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        return new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
    }
    private sealed class Transport(Func<CompilerHostRequest, CancellationToken, Task<CompilerHostResponse>> run) : IServerCompilerTransport
    { public Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken cancellationToken = default) => run(request, cancellationToken); }
    private static CoreCompilation Compile(CompilerInputManifest input, params CompilerMaterializedSnapshot[] snapshots) => PureCoreCompiler.Compile(input, input.Compiler, snapshots);

    [Fact]
    public void ALocalVersionTwoInputProducesActualCompleteNormalArtifactsAndPass()
    {
        var input = Upgrade(Input()); var compiled = Compile(input); var pub = Assert.IsType<CompilerScopedPublication>(compiled.Publication);
        Assert.Equal(CompilerVerdict.Pass, compiled.Result.Verdict); Assert.Equal(CompilerCoverage.Complete, compiled.Result.Coverage); Assert.True(compiled.Result.EvidenceComplete);
        Assert.True(compiled.Validation!.StructuralClosureValidated); Assert.Null(compiled.Validation.Ir); Assert.Same(pub, compiled.Validation.Publication);
        Assert.Single(pub.Scope.Regions); Assert.Equal(3, compiled.Result.Artifacts.Count); Assert.True(pub.IsDeclaredBy(compiled.Result));
        Assert.All(compiled.Result.Artifacts, artifact => Assert.Equal(2, artifact.Data.Value.GetProperty("version").GetInt32()));
        Assert.Equal(ContentDigest.Compute(CompilerScopedPublication.ModelFormat, pub.SemanticModel.ToCanonicalBytes()), pub.SemanticModelDigest);
        Assert.Equal(ContentDigest.Compute(CompilerScopedPublication.IrFormat, pub.Ir.ToCanonicalBytes()), pub.IrDigest);
        Assert.Equal(ContentDigest.Compute(CompilerScopedPublication.MapFormat, pub.SourceMap.ToCanonicalBytes()), pub.SourceMapDigest);
        Assert.All(compiled.Result.Stages, stage => Assert.Equal(CompilerCoverage.Complete, stage.Coverage));
        Assert.DoesNotContain(compiled.Result.Diagnostics, diagnostic => diagnostic.Code.Contains("not-lowered") || diagnostic.Code.Contains("publication-pending"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheWholeDeclaredGraphCanPassWithRepeatedIdsInSeparateSourceVersions(bool versions)
    {
        var fixture = Graph(withRelation: true, twoBVersions: versions); var input = Upgrade(fixture.Input); var compiled = Compile(input, fixture.Snapshots);
        Assert.Equal(CompilerVerdict.Pass, compiled.Result.Verdict); Assert.Equal(CompilerCoverage.Complete, compiled.Result.Coverage);
        var pub = compiled.Publication!; Assert.Equal(versions ? 4 : 3, pub.Scope.Regions.Count);
        Assert.Equal(pub.Scope.Regions.Count, pub.SemanticModel.Value.GetProperty("regions").GetArrayLength());
        Assert.Equal(pub.Scope.Regions.Count, pub.SourceMap.Value.GetProperty("regions").GetArrayLength());
        foreach (var region in pub.Scope.Regions)
        {
            var model = pub.SemanticModel.Value.GetProperty("regions").EnumerateArray().Single(row => SourceJson.Parse(row.GetProperty("context").GetRawText()).Equals(region.Binding.Source.Context.Data));
            Assert.Equal(region.Validation!.Ir!.SemanticModel, SourceJson.Parse(model.GetProperty("semanticModel").GetRawText()));
        }
        var cache = new PlanDeclarationReadCache(); var cached = PureCoreCompiler.Compile(input, input.Compiler, fixture.Snapshots.Reverse(), declarationCache: cache);
        Assert.Equal(compiled.Result.Data, cached.Result.Data); Assert.Equal(pub.Ir, cached.Publication!.Ir);
    }
    [Theory]
    [InlineData(true, CompilerVerdict.Pass, CompilerCoverage.Complete)]
    [InlineData(false, CompilerVerdict.Fail, CompilerCoverage.Complete)]
    [InlineData(null, CompilerVerdict.Inconclusive, CompilerCoverage.Partial)]
    public void ActualRequiredPredicateTruthControlsTheNormalVerdictWithoutTheTemporaryGuard(bool? truth, CompilerVerdict verdict, CompilerCoverage coverage)
    {
        var compiled = Compile(Upgrade(CompilerAnalysisFixtures.Constraint(truth)));
        Assert.Equal(verdict, compiled.Result.Verdict); Assert.Equal(coverage, compiled.Result.Coverage); Assert.NotNull(compiled.Publication);
        Assert.DoesNotContain(compiled.Result.Diagnostics, diagnostic => diagnostic.Code is "coglatas.semantic.scoped-publication-pending" or "coglatas.analysis.semantic-closure-unavailable");
        Assert.Equal(verdict, Assert.Single(compiled.Result.RuleOutcomes, rule => rule.RuleId == CoreCompilerRules.Constraints).Verdict);
    }
    [Fact]
    public void RequirementEvidenceAndResourceWorkDoNotBecomePassByPublishingStructuralArtifacts()
    {
        var compiled = Compile(Upgrade(CompilerAnalysisFixtures.Constraint(false, requirement: true)));
        Assert.Equal(CompilerVerdict.Fail, compiled.Result.Verdict); Assert.Equal(CompilerCoverage.Partial, compiled.Result.Coverage);
        Assert.False(compiled.Result.EvidenceComplete); Assert.NotNull(compiled.Publication);
        Assert.Contains(compiled.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.analysis.requirement-evidence-unavailable");
    }
    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")] [InlineData("partial-relations")]
    public void MissingDeniedAndPartialInputsCannotProduceNormalScopeArtifacts(string admission)
    {
        var fixture = Graph(bAdmission: admission); var compiled = Compile(Upgrade(fixture.Input), fixture.Snapshots);
        Assert.Null(compiled.Publication); Assert.Empty(compiled.Result.Artifacts); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
    }
    [Theory]
    [InlineData("1", "2")] [InlineData("2", "1")] [InlineData("3", "3")]
    public void MixedOrFutureTargetVersionsStayExplicitlyUnsupported(string semantic, string ir)
    {
        var input = Input(); var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["semantics"]!["version"] = semantic; settings["ir"]!["version"] = ir;
        var result = Compile(new(input.Source, Json(settings))); Assert.Null(result.Publication); Assert.Empty(result.Result.Artifacts);
        Assert.NotEqual(CompilerVerdict.Pass, result.Result.Verdict);
        Assert.Contains(result.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.semantic.version-compatibility");
    }
    [Fact]
    public void UnsupportedRequiredRulesCannotBorrowCompleteStructuralPublicationAsRulePass()
    {
        var input = Upgrade(Input()); var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
        settings["rules"]!.AsArray().Add(Obj(new { ruleId = "example.unsupported", version = 1, required = true, configuration = new { } }));
        var result = Compile(new(input.Source, Json(settings))); Assert.NotNull(result.Publication); Assert.Equal(CompilerVerdict.Inconclusive, result.Result.Verdict);
        Assert.Equal(CompilerVerdict.NotRun, Assert.Single(result.Result.RuleOutcomes, rule => rule.RuleId == "example.unsupported").Verdict);
    }
    [Fact]
    public void LegacyTargetBytesAndItsExternalGuardRemainVersionSpecific()
    {
        var local = Compile(Input()); Assert.Null(local.Publication); Assert.NotNull(local.Validation!.Ir);
        Assert.Equal("coglatas.project-ir/1", local.Validation.Ir.Data.Value.GetProperty("format").GetString());
        var fixture = Graph(); var external = Compile(fixture.Input, fixture.Snapshots); Assert.Null(external.Publication); Assert.Empty(external.Result.Artifacts);
        Assert.Contains(external.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.semantic.external-closure-not-lowered");
    }
    [Fact]
    public void LaterVersionTwoPublicationBudgetExhaustionRetainsAnAlreadyEvaluatedFalseRule()
    {
        var input = Upgrade(CompilerAnalysisFixtures.Constraint(false)); var full = Compile(input);
        Assert.Equal(CompilerVerdict.Fail, full.Result.Verdict); Assert.NotNull(full.Publication);
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = full.LogicalOperationsConsumed - 1;
        var limited = Compile(new(input.Source, Json(settings)));
        Assert.Equal(CompilerVerdict.Fail, limited.Result.Verdict); Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution);
        Assert.Null(limited.Publication); Assert.Empty(limited.Result.Artifacts); Assert.False(limited.Result.EvidenceComplete);
        Assert.Contains(limited.Dependencies!.KnownFailures, failure => failure.RuleId == CoreCompilerRules.Constraints);
    }
    [Fact]
    public void RootChangesInvalidateThePublishedModelMapAndIrTogether()
    {
        var fixture = Graph(); var input = Upgrade(fixture.Input); var before = Compile(input, fixture.Snapshots).Publication!;
        var source = ProjectSource.Create(input.Source.Context, input.Source.Documents.Select(document => SourceDocument.Create(document.DocumentId, "changed-root", document.Root.Data)), input.Source.Declarations, input.Source.Relations);
        var after = Compile(new(source, input.Settings), fixture.Snapshots).Publication!;
        Assert.NotEqual(before.SemanticModelDigest, after.SemanticModelDigest); Assert.NotEqual(before.SourceMapDigest, after.SourceMapDigest); Assert.NotEqual(before.IrDigest, after.IrDigest);
    }
    [Fact]
    public async Task LocalServerAndWindowsPureConsumersAgreeOnActualPublishedScope()
    {
        var fixture = Graph(withRelation: true); var input = Upgrade(fixture.Input); var profile = Profile(input); var local = new LocalNetCompilerHost(profile);
        var server = new ServerCompilerHost(profile, new Transport(local.CompileAsync)); var request = new CompilerHostRequest(new(input), 4, fixture.Snapshots);
        var a = await local.CompileAsync(request); var b = await server.CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, a.Status); Assert.Equal(CompilerHostStatus.Completed, b.Status);
        Assert.Equal(a.Compilation!.Result.Data, b.Compilation!.Result.Data); Assert.Equal(a.Compilation.Publication!.Ir, b.Compilation.Publication!.Ir);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(a)); Assert.True(view.HasCompleteLocalPass);
        Assert.True(view.LoadSyntheticScopedSources()); var record = PlanDeclarationReader.Read(input.Source).First();
        var location = ProjectLocation.Create(record.DocumentId, input.Source.Context, entityId: record.Declaration!.EntityId);
        Assert.True(view.Select(location)); Assert.True(view.Navigate()); Assert.Equal(location, view.SourceTarget);
        Assert.Equal(SyntheticScopedSourceProjection.FormatTwo, SyntheticScopedSourceProjection.Export(a.Compilation)!.Value.GetProperty("format").GetString());
    }
    [Theory]
    [InlineData("missing-payload")] [InlineData("wrong-schema")] [InlineData("missing-stage")] [InlineData("wrong-stage-version")] [InlineData("duplicate-stage")]
    public async Task ServerRefusesDeclaredVersionTwoArtifactsWithoutTheirExactPayloadSchemaAndProducingStage(string mode)
    {
        var input = Upgrade(Input()); var compiled = Compile(input); var profile = Profile(input);
        var server = new ServerCompilerHost(profile, new Transport((request, _) =>
        {
            var result = compiled.Result;
            if (mode == "wrong-schema") result = new(input, result.Execution, result.Coverage, result.RuleOutcomes, result.Diagnostics, result.Stages, result.EvidenceComplete,
                result.Artifacts.Select(artifact => new CompilerArtifactReference(artifact.Role, "example.wrong", 2, input.InputDigest, artifact.Digest)));
            if (mode == "missing-stage") result = new(input, result.Execution, result.Coverage, result.RuleOutcomes, result.Diagnostics,
                result.Stages.Where(stage => stage.StageId != "coglatas.scoped-publication"), result.EvidenceComplete, result.Artifacts);
            if (mode == "wrong-stage-version") result = new(input, result.Execution, result.Coverage, result.RuleOutcomes, result.Diagnostics,
                result.Stages.Select(stage => stage.StageId == "coglatas.scoped-publication" ? new CompilerStageOutcome(stage.StageId, 1, stage.Execution, stage.Coverage, input.InputDigest,
                    compiled.Publication!.Artifacts.Select(artifact => artifact.Digest)) : stage), result.EvidenceComplete, result.Artifacts);
            if (mode == "duplicate-stage") result = new(input, result.Execution, result.Coverage, result.RuleOutcomes, result.Diagnostics,
                result.Stages.Append(result.Stages.Single(stage => stage.StageId == "coglatas.scoped-publication")), result.EvidenceComplete, result.Artifacts);
            var constructor = typeof(CoreCompilation).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
            var forged = (CoreCompilation)constructor.Invoke(new object?[] { input, compiled.Model, compiled.Validation, result, compiled.LogicalOperationsConsumed, compiled.Dependencies,
                mode == "missing-payload" ? null : compiled.Publication });
            return Task.FromResult(new CompilerHostResponse(CompilerHostStatus.Completed, "example.transport", input.InputDigest, request.ClientGeneration, Guid.NewGuid(), new CompilerHostExecutionReceipt(request, forged)));
        }));
        var response = await server.CompileAsync(new(new(input), 1)); Assert.Equal(CompilerHostStatus.InvalidInput, response.Status); Assert.Null(response.Compilation);
    }
    [Fact]
    public async Task PublicTransportTransfersCoreOwnedGraphsAndFreshEquivalentCompilationsPreserveTheirAliases()
    {
        Assert.Empty(typeof(CoreCompilation).GetConstructors()); Assert.Empty(typeof(CompilerScopedPublication).GetConstructors());
        var input = Upgrade(Input()); var first = Compile(input); var second = Compile(input);
        Assert.NotSame(first.Publication, second.Publication); Assert.Equal(first.Result.Data, second.Result.Data);
        foreach (var compiled in new[] { first, second }) {
            Assert.Same(compiled.Publication, compiled.Validation!.Publication);
            Assert.Same(compiled.Publication!.Scope, compiled.Dependencies!.ScopedIr);
        }
        var server = new ServerCompilerHost(Profile(input), new Transport((request, _) => Task.FromResult(new CompilerHostResponse(
            CompilerHostStatus.Completed, "example.fresh-core-graph", input.InputDigest, request.ClientGeneration, Guid.NewGuid(), new CompilerHostExecutionReceipt(request, second)))));
        var response = await server.CompileAsync(new(new(input), 3));
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.Same(second, response.Compilation);
    }
    [Fact]
    public void OnlyPrivateRegionalProducerOutcomesReachPublicationAndCompleteRulesCarryActualTruth()
    {
        // The public outcome DTO permits this combination; it is not a global DTO invariant.
        var possible = new CompilerRuleOutcome(CoreCompilerRules.Constraints, 1, CompilerExecutionState.Completed, CompilerCoverage.Complete,
            CompilerVerdict.Inconclusive, new(CompilerEvidenceKind.Missing, [], "coglatas.missing-input"));
        Assert.Equal(CompilerVerdict.Inconclusive, possible.Verdict);
        foreach (bool? truth in new bool?[] { true, false, null }) {
            var compiled = Compile(Upgrade(CompilerAnalysisFixtures.Constraint(truth)));
            foreach (var rule in compiled.Dependencies!.Regions.SelectMany(region => region.Rules))
                if (rule.Execution == CompilerExecutionState.Completed && rule.Coverage == CompilerCoverage.Complete)
                    Assert.Contains(rule.Verdict, new[] { CompilerVerdict.Pass, CompilerVerdict.Fail });
            if (truth is null) Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict);
        }
    }
    [Fact]
    public void CancellationPublishesNoNewVersionTwoArtifacts()
    {
        var input = Upgrade(Input()); using var token = new CancellationTokenSource(); token.Cancel();
        var result = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: token.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, result.Result.Execution); Assert.Null(result.Publication); Assert.Empty(result.Result.Artifacts);
    }
}
