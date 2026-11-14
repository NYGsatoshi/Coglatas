using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerBindingClosureTests
{
    internal sealed record Fixture(CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots, SourceRevisionContext A, SourceRevisionContext B);
    private static JsonObject BooleanType => Obj(new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false });
    private static JsonObject Boolean(bool value) => Obj(new { type = BooleanType, state = "coglatas.known", value });
    private static JsonObject TaskNode(int entity = 77) => Obj(new { entityId = Id(entity), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
        payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = new[] { new { portId = Id(78), direction = "coglatas.input", type = BooleanType, minimumConnections = 0, maximumConnections = 1, defaultValue = Boolean(true) } } } });
    private static JsonObject TestNode(int entity, int task, PortId port, SourceRevisionContext context) => Obj(new { entityId = Id(entity), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
        payload = new { assertions = new[] { new { key = "example.target", kind = "coglatas.assert-reference-resolved", target = CompilerAnalysisFixtures.Reference(task, context), portId = port.ToString() } } } });
    internal static Fixture Graph(bool transitive = true, bool backToRoot = false, string bAdmission = "available", bool allConcepts = false,
        bool falseConstraint = false, bool opaqueA = false, bool twoBVersions = false, bool withRelation = false, int rootTarget = 7, bool typedTransitive = false, bool wrongTypedPin = false)
    {
        var original = ScenarioReferenceRebaseTests.Fixture(); var aContext = original.Snapshot.Snapshot.Source.Context;
        var bContext = SourceRevisionContext.Committed(new(new(original.Input.Source.TenantId, ProjectId.Parse(Id(42)), BranchId.Parse(Id(43))), RevisionId.Parse(Id(44))));
        var b2Context = SourceRevisionContext.Committed(new(bContext.Branch, RevisionId.Parse(Id(45))));
        var b = ProjectSource.Create(bContext, original.Input.Source.Documents, Json(new JsonArray(TaskNode())));
        var b2 = ProjectSource.Create(b2Context, original.Input.Source.Documents, Json(new JsonArray(TaskNode())));
        var declarations = JsonNode.Parse(original.Snapshot.Snapshot.Source.Declarations.CanonicalText)!.AsArray();
        if (transitive) declarations.Add(TestNode(90, 77, PortId.Parse(Id(78)), bContext));
        if (twoBVersions) declarations.Add(TestNode(91, 77, PortId.Parse(Id(78)), b2Context));
        if (backToRoot) declarations.Add(TestNode(92, 7, PortId.Parse(Id(8)), original.Input.Source.Context));
        if (opaqueA) declarations.Add(Obj(new { entityId = Id(99), documentId = Id(4), kind = "example.future", schemaVersion = 1, payload = new { privateMarker = "opaque-sentinel" } }));
        if (allConcepts)
        {
            declarations.Add(TaskNode());
            declarations.Add(Obj(new { entityId = Id(79), documentId = Id(4), kind = "coglatas.milestone", schemaVersion = 1, payload = new { target = new { type = new { kind = "coglatas.instant", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "2026-11-14T00:00:00.0000000Z" } } }));
            var requirement = CompilerAnalysisFixtures.RequirementDeclaration(aContext); requirement["entityId"] = Id(80); declarations.Add(requirement);
            declarations.Add(Obj(new { entityId = Id(81), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1, payload = new { tenantId = Id(1), resourceId = Id(82), snapshotVersion = "1", snapshotDigest = new { algorithm = "sha-256", value = new string('a', 64) }, resourceKind = "coglatas.human" } }));
            declarations.Add(Obj(new { entityId = Id(83), documentId = Id(4), kind = "coglatas.workflow", schemaVersion = 1, payload = new { states = new[] { Id(84) }, transitions = Array.Empty<object>(), initialState = Id(84), maximumTransitions = 1 } }));
            declarations.Add(Obj(new { entityId = Id(85), documentId = Id(4), kind = "coglatas.constraint", schemaVersion = 1, payload = new { predicate = CompilerAnalysisFixtures.Predicate(!falseConstraint), classification = "coglatas.required" } }));
        }
        var relations = new JsonArray();
        if (withRelation)
        {
            declarations.Add(TaskNode(7));
            relations.Add(Obj(new { relationId = Id(70), documentId = Id(4), kind = RelationKinds.RelatedTo, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.RelatedTo, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(7, aContext) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(rootTarget, original.Input.Source.Context) }, payload = new { } }));
        }
        if (typedTransitive)
        {
            var task = TaskNode(); var port = task["payload"]!["ports"]![0]!;
            var type = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.project" });
            port["type"] = type.DeepClone(); port["defaultValue"] = Obj(new { type, state = "coglatas.known", value = new { tenantId = Id(1), projectId = b.ProjectId.ToString(),
                entityId = Id(6), context = b.Context.Data.Value, origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), aContext, entityId: EntityId.Parse(Id(77))).Data.Value,
                manifest = new { version = wrongTypedPin ? "2" : "1", digest = b.Digest.Data.Value } } });
            declarations.Add(task);
        }
        var a = ProjectSource.Create(aContext, original.Input.Source.Documents, Json(declarations), Json(relations));
        var local = JsonNode.Parse(original.Input.Source.Declarations.CanonicalText)!.AsArray();
        local[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(a.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(original.Input.Settings.CanonicalText)!.AsObject();
        var pinA = settings["authorizedExternalSnapshots"]![0]!; pinA["digest"] = JsonNode.Parse(a.Digest.Data.CanonicalText);
        if (transitive || typedTransitive || twoBVersions) settings["authorizedExternalSnapshots"]!.AsArray().Add(Obj(new { snapshotId = Id(31), version = "1", digest = b.Digest.Data.Value, context = b.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        if (twoBVersions) settings["authorizedExternalSnapshots"]!.AsArray().Add(Obj(new { snapshotId = Id(32), version = "2", digest = b2.Digest.Data.Value, context = b2.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        var input = new CompilerInputManifest(ProjectSource.Create(original.Input.Source.Context, original.Input.Source.Documents, Json(local)), Json(settings));
        var snapshots = new List<CompilerMaterializedSnapshot> { new(ExternalSnapshotId.Parse(Id(30)), "1", new(a, true, true, true)) };
        if ((transitive || typedTransitive || twoBVersions) && bAdmission != "missing") snapshots.Add(new(ExternalSnapshotId.Parse(Id(31)), "1", new(b, bAdmission != "denied", bAdmission != "partial-declarations", bAdmission != "partial-relations")));
        if (twoBVersions) snapshots.Add(new(ExternalSnapshotId.Parse(Id(32)), "2", new(b2, true, true, true)));
        return new(input, snapshots.ToArray(), aContext, bContext);
    }
    private static CompilerBindingClosure Build(Fixture f, PlanDeclarationReadCache? cache = null) =>
        CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(f.Input, f.Snapshots, declarationCache: cache), f.Input.InputDigest, declarationCache: cache);

    [Fact]
    public void TransitiveScopesUseExactContextRatherThanEntityOrDocumentIdentity()
    {
        var f = Graph(); var root = ProjectSemanticBinder.Bind(f.Input, f.Snapshots); Assert.True(root.BindingComplete);
        var closure = CompilerBindingClosure.Build(root, f.Input.InputDigest); Assert.Equal(CompilerBindingClosureState.Complete, closure.State);
        Assert.Equal(3, closure.Regions.Count); Assert.Equal(3, closure.Regions.Select(x => x.Source.Context).Distinct().Count());
        Assert.All(closure.Regions, region => { Assert.True(region.BindingComplete); Assert.Same(f.Input, region.RootInput); Assert.Contains(region.Declarations, row => row.Declaration!.EntityId == EntityId.Parse(Id(6))); });
        var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A)); Assert.Contains(a.References, row => row.SymbolKey!.Contains(f.B.Data.CanonicalText, StringComparison.Ordinal));
        Assert.Equal(ContentDigest.Compute(CompilerBindingClosure.Format, closure.Data.ToCanonicalBytes()), closure.Digest);
        Assert.Equal(f.Input.InputDigest.Data, SourceJson.Parse(closure.Data.Value.GetProperty("inputDigest").GetRawText()));
        Assert.True(closure.LogicalOperationsConsumed > root.LogicalOperationsConsumed); Assert.True(closure.LogicalOperationsConsumed <= f.Input.ExecutionProfile.MaximumOperations);
    }
    [Fact]
    public void ABackReferenceReusesTheExactOriginalRootWithoutMintingASnapshotIdentity()
    {
        var f = Graph(backToRoot: true); var closure = Build(f); Assert.Equal(CompilerBindingClosureState.Complete, closure.State); Assert.Equal(3, closure.Regions.Count);
        var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A)); Assert.Contains(a.References, row => row.SymbolKey!.Contains(f.Input.Source.Context.Data.CanonicalText, StringComparison.Ordinal));
        var root = Assert.Single(closure.Regions, x => x.IsRoot); Assert.Equal(f.Input.Source.Digest, root.Source.Digest);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.Data.Value.GetProperty("snapshot").ValueKind);
    }
    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")] [InlineData("partial-relations")]
    public void MissingDeniedOrPartialTransitiveInputsCannotCloseTheGraph(string admission)
    {
        var f = Graph(bAdmission: admission); var root = ProjectSemanticBinder.Bind(f.Input, f.Snapshots);
        if (admission == "missing") Assert.True(root.BindingComplete);
        var closure = CompilerBindingClosure.Build(root, f.Input.InputDigest); Assert.Equal(CompilerBindingClosureState.Incomplete, closure.State);
        if (admission == "missing") Assert.Contains(closure.Regions.SelectMany(x => x.Findings), row => row.Diagnostic.Code == "coglatas.binding.reference-coverage-unknown");
        if (admission == "denied") { Assert.Single(closure.Regions); Assert.DoesNotContain(f.B.Data.CanonicalText, closure.Data.CanonicalText); }
    }
    [Fact]
    public void AllSevenConceptsAreRetainedButBindingIsNotSemanticOrCompilerPass()
    {
        var f = Graph(transitive: false, allConcepts: true, falseConstraint: true); var closure = Build(f);
        Assert.Equal(CompilerBindingClosureState.Complete, closure.State); var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A));
        var kinds = a.Declarations.Select(x => x.Declaration!.Kind).Distinct().ToHashSet(StringComparer.Ordinal);
        foreach (var kind in new[] { "coglatas.project", "coglatas.task", "coglatas.milestone", "coglatas.requirement", "coglatas.resource-binding", "coglatas.constraint", "coglatas.workflow" }) Assert.Contains(kind, kinds);
        Assert.Equal(PredicateTruth.False, a.Declarations.Select(x => x.Declaration).OfType<ConstraintPlanDeclaration>().Single().Predicate.Evaluate().Truth);
        var compiled = PureCoreCompiler.Compile(f.Input, f.Input.Compiler, f.Snapshots);
        Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict); Assert.Null(compiled.Validation!.Ir);
        Assert.Contains(compiled.Result.Diagnostics, row => row.Code == "coglatas.semantic.external-closure-not-lowered");
    }
    [Fact]
    public void AnUnreferencedOpaqueDeclarationInsideARequiredSourceIsNotDiscarded()
    {
        var f = Graph(transitive: false, opaqueA: true); Assert.True(ProjectSemanticBinder.Bind(f.Input, f.Snapshots).BindingComplete);
        var closure = Build(f); Assert.Equal(CompilerBindingClosureState.Incomplete, closure.State);
        var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A)); Assert.Contains(a.Declarations, row => row.Status == ValueDecodeStatus.Unsupported);
        Assert.Contains(a.Findings, row => row.Diagnostic.Code == "coglatas.binding.unsupported-declaration");
    }
    [Fact]
    public void TwoPinnedVersionsOfOneProjectRemainDistinctBindingRegions()
    {
        var f = Graph(twoBVersions: true); var closure = Build(f); Assert.Equal(CompilerBindingClosureState.Complete, closure.State); Assert.Equal(4, closure.Regions.Count);
        var versions = closure.Regions.Where(x => x.Source.ProjectId == f.B.Branch.ProjectId).ToArray(); Assert.Equal(2, versions.Length);
        Assert.Equal(versions[0].Declarations.Select(x => x.Declaration!.EntityId), versions[1].Declarations.Select(x => x.Declaration!.EntityId));
        Assert.NotEqual(versions[0].Source.Context, versions[1].Source.Context); Assert.NotEqual(versions[0].Digest, versions[1].Digest);
    }
    [Fact]
    public void InputSupplyPermutationAndTheExistingDeclarationCacheHaveEqualClosureBytes()
    {
        var f = Graph(backToRoot: true); var original = Build(f); var reordered = Build(f with { Snapshots = f.Snapshots.Reverse().ToArray() });
        var cache = new PlanDeclarationReadCache(); var first = Build(f, cache); var repeated = Build(f, cache);
        Assert.Equal(original.Data, reordered.Data); Assert.Equal(original.Data, first.Data); Assert.Equal(first.Data, repeated.Data); Assert.Equal(original.Digest, repeated.Digest);
    }
    [Fact]
    public void StaleInputsCancellationAndSharedLogicalBudgetDoNotPublishACompletePrefix()
    {
        var f = Graph(); var model = ProjectSemanticBinder.Bind(f.Input, f.Snapshots);
        var stale = CompilerBindingClosure.Build(model, ContentDigest.Compute("example.foreign-input", [])); Assert.Equal(CompilerBindingClosureState.StaleInput, stale.State); Assert.Empty(stale.Regions);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => CompilerBindingClosure.Build(model, f.Input.InputDigest, cancellation.Token));
        var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = model.LogicalOperationsConsumed + 1;
        var bounded = f with { Input = new(f.Input.Source, Json(settings)) }; var root = ProjectSemanticBinder.Bind(bounded.Input, bounded.Snapshots); Assert.True(root.BindingComplete);
        var limited = CompilerBindingClosure.Build(root, bounded.Input.InputDigest); Assert.Equal(CompilerBindingClosureState.ResourceLimit, limited.State);
        Assert.Equal(bounded.Input.ExecutionProfile.MaximumOperations, limited.LogicalOperationsConsumed);
    }
    [Fact]
    public void PublicSnapshotsAndReturnedCollectionsCannotChangeAnOwnedClosure()
    {
        var f = Graph(); var model = ProjectSemanticBinder.Bind(f.Input, f.Snapshots); var first = CompilerBindingClosure.Build(model, f.Input.InputDigest); var bytes = first.Data;
        f.Snapshots[0] = new(f.Snapshots[0].SnapshotId, f.Snapshots[0].Version, f.Snapshots[0].Snapshot with { IsAuthorized = false });
        var nodes = first.Regions.ToArray(); nodes[0] = nodes[1]; var json = JsonNode.Parse(first.Data.CanonicalText)!; json["regions"]!.AsArray().Clear();
        Assert.False(first.Regions is CompilerBindingRegion[]); Assert.Equal(bytes, first.Data); Assert.Equal(bytes, CompilerBindingClosure.Build(model, f.Input.InputDigest).Data);
    }
    [Fact]
    public async Task ActualLocalHostModelAndDirectCoreBindingsProduceTheSameClosure()
    {
        var f = Graph(backToRoot: true); CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(f.Input.Settings.Value.GetProperty(name).GetRawText()));
        var host = new LocalNetCompilerHost(new(f.Input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"), f.Input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(x => x.GetString())));
        var request = new CompilerHostRequest(new(f.Input), 1, f.Snapshots); var response = await host.CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status); var actual = CompilerBindingClosure.Build(response.Compilation!.Model!, f.Input.InputDigest);
        Assert.Equal(Build(f).Data, actual.Data); Assert.NotEqual(CompilerVerdict.Pass, response.Compilation.Result.Verdict);
    }
    [Fact]
    public void RelationEndpointsWithTheSameEntityIdBindTheirOwnExactContexts()
    {
        var f = Graph(transitive: false, withRelation: true); var closure = Build(f); Assert.Equal(CompilerBindingClosureState.Complete, closure.State);
        var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A)); var relation = Assert.Single(a.Relations).Relation!;
        Assert.Equal(f.A, relation.Location.Context);
        var bindings = a.References.Where(x => x.Origin.RelationId == relation.RelationId).ToArray(); Assert.Equal(2, bindings.Length);
        Assert.All(bindings, row => Assert.Equal(ReferenceResolutionState.Resolved, row.State)); Assert.NotEqual(bindings[0].SymbolKey, bindings[1].SymbolKey);
        Assert.Contains(bindings, row => row.SymbolKey!.Contains(f.Input.Source.Context.Data.CanonicalText, StringComparison.Ordinal));
        Assert.Contains(bindings, row => row.SymbolKey!.Contains(f.A.Data.CanonicalText, StringComparison.Ordinal));
    }
    [Fact]
    public void ARelationCannotResolveAMissingTargetFromAnotherContextByEntityOnly()
    {
        var f = Graph(transitive: false, withRelation: true, rootTarget: 999); var closure = Build(f); Assert.Equal(CompilerBindingClosureState.Incomplete, closure.State);
        Assert.Contains(closure.Regions.SelectMany(x => x.References), row => row.Origin.RelationId == RelationId.Parse(Id(70)) && row.State == ReferenceResolutionState.MissingWithinCompleteScope);
    }
    [Fact]
    public void CapturedRootChangesInvalidateEveryRegionCertificateEvenWhenTheDependencySourceIsIdentical()
    {
        var f = Graph(backToRoot: true); var before = Build(f);
        var source = ProjectSource.Create(f.Input.Source.Context, f.Input.Source.Documents.Select(doc => SourceDocument.Create(doc.DocumentId, "renamed-root", doc.Root.Data)), f.Input.Source.Declarations, f.Input.Source.Relations);
        var next = f with { Input = new(source, f.Input.Settings) }; var after = Build(next); Assert.Equal(CompilerBindingClosureState.Complete, after.State);
        var a = before.Regions.Single(x => x.Source.Context.Equals(f.A)); var b = after.Regions.Single(x => x.Source.Context.Equals(f.A));
        Assert.Equal(a.Source.Data, b.Source.Data); Assert.NotEqual(a.RootInput.InputDigest, b.RootInput.InputDigest); Assert.NotEqual(a.Digest, b.Digest);
        Assert.Equal(CompilerBindingClosureState.StaleInput, CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(next.Input, next.Snapshots), f.Input.InputDigest).State);
    }
    [Fact]
    public void AdmittedSnapshotIdentityIsNotGuessedFromEquivalentManifestAliases()
    {
        var f = Graph(transitive: false); var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject();
        var alias = settings["authorizedExternalSnapshots"]![0]!.DeepClone(); alias["snapshotId"] = Id(33); settings["authorizedExternalSnapshots"]!.AsArray().Add(alias);
        var closure = Build(f with { Input = new(f.Input.Source, Json(settings)) }); Assert.Equal(CompilerBindingClosureState.Complete, closure.State);
        var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A)); Assert.Equal(Id(30), a.Data.Value.GetProperty("snapshot").GetProperty("snapshotId").GetString());
        Assert.Equal("1", a.Data.Value.GetProperty("snapshot").GetProperty("version").GetString());
    }

    [Fact]
    public void AResolvedDocumentReferenceDiscoversItsExactSourceRegion()
    {
        var f = Graph(transitive: false); var local = JsonNode.Parse(f.Input.Source.Declarations.CanonicalText)!.AsArray();
        var port = local[0]!["payload"]!["ports"]![0]!; var value = port["defaultValue"]!;
        value["type"]!["kind"] = "coglatas.document-reference"; value["type"]!["expectedKind"] = SourceDocument.SchemaId; port["type"] = value["type"]!.DeepClone();
        value["value"]!.AsObject().Remove("entityId"); value["value"]!["documentId"] = Id(4);
        var input = new CompilerInputManifest(ProjectSource.Create(f.Input.Source.Context, f.Input.Source.Documents, Json(local)), f.Input.Settings);
        var closure = Build(f with { Input = input }); Assert.Equal(CompilerBindingClosureState.Complete, closure.State); Assert.Equal(2, closure.Regions.Count);
        var root = Assert.Single(closure.Regions, x => x.IsRoot);
        Assert.Contains(root.References, reference => SourceJson.Parse(reference.SymbolKey!).Value.TryGetProperty("documentId", out var id) && id.GetString() == Id(4));
    }
    [Fact]
    public void SourceEncodingBoundsAlsoLimitTheCompleteClosurePublication()
    {
        var f = Graph(backToRoot: true); var closure = Build(f);
        var sourceBytes = f.Input.Source.Data.ToCanonicalBytes(); Assert.True(closure.Data.ToCanonicalBytes().Length > sourceBytes.Length);
        var source = ProjectSourceCodec.Decode(sourceBytes, new(MaximumBytes: sourceBytes.Length)).RequireSource();
        var input = new CompilerInputManifest(source, f.Input.Settings); var limited = Build(f with { Input = input });
        Assert.Equal(CompilerBindingClosureState.ResourceLimit, limited.State); Assert.Empty(limited.Regions);
        Assert.Equal(input.InputDigest, limited.Input.InputDigest);
    }

    [Theory]
    [InlineData("scenario")] [InlineData("candidate")]
    public void ExactAppliedDependencyContextsAreBoundWithoutReplayingTheirOverlayIntent(string kind)
    {
        var f = Graph(transitive: false, allConcepts: true); var original = f.Snapshots[0].Snapshot.Source;
        var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject(); settings["authorizedExternalSnapshots"] = new JsonArray();
        var basis = new CompilerInputManifest(original, Json(settings)); var model = ProjectSemanticBinder.Bind(basis); Assert.True(model.BindingComplete);
        var change = new ProjectScenarioOverride(ProjectLocation.Create(DocumentId.Parse(Id(4)), original.Context, entityId: EntityId.Parse(Id(77))),
            ProjectScenarioOverrideKind.TaskInputDefault, Json(Boolean(false)), PortId.Parse(Id(78)));
        var scenario = ProjectScenarios.Prepare(model, basis.InputDigest, ScenarioId.Parse(Id(1500)), ScenarioRevisionId.Parse(Id(1501)), [change]).Plan!;
        Assert.NotNull(scenario); var source = scenario.Input.Source;
        if (kind == "candidate")
        {
            var committed = original.Context.CommittedRevision!;
            source = ProjectScenarios.ToDraft(scenario, model, basis.InputDigest, new(ProposalId.Parse(Id(1502)), CandidateRevisionId.Parse(Id(1503)), committed, committed)).Plan!.After.Source;
        }
        var root = JsonNode.Parse(f.Input.Source.Declarations.CanonicalText)!.AsArray(); var reference = root[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!;
        reference["context"] = JsonNode.Parse(source.Context.Data.CanonicalText); reference["manifest"]!["digest"] = JsonNode.Parse(source.Digest.Data.CanonicalText);
        var rootSettings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject(); var pin = rootSettings["authorizedExternalSnapshots"]![0]!;
        pin["context"] = JsonNode.Parse(source.Context.Data.CanonicalText); pin["digest"] = JsonNode.Parse(source.Digest.Data.CanonicalText);
        var input = new CompilerInputManifest(ProjectSource.Create(f.Input.Source.Context, f.Input.Source.Documents, Json(root)), Json(rootSettings));
        var snapshot = new CompilerMaterializedSnapshot(f.Snapshots[0].SnapshotId, "1", new(source, true, true, true));
        var closure = CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(input, [snapshot]), input.InputDigest);
        Assert.Equal(CompilerBindingClosureState.Complete, closure.State);
        var dependency = Assert.Single(closure.Regions, region => !region.IsRoot); Assert.Equal(source.Data, dependency.Source.Data); Assert.Equal(source.Context, dependency.Source.Context);
        Assert.Equal(source.Digest.Data, SourceJson.Parse(dependency.Data.Value.GetProperty("sourceDigest").GetRawText()));
        Assert.NotEqual(CompilerVerdict.Pass, PureCoreCompiler.Compile(input, input.Compiler, [snapshot]).Result.Verdict);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TransitiveTypedReferencesRequireTheirExactExistingVersionAndDigest(bool wrongVersion)
    {
        var f = Graph(transitive: false, typedTransitive: true, wrongTypedPin: wrongVersion); var root = ProjectSemanticBinder.Bind(f.Input, f.Snapshots); Assert.True(root.BindingComplete);
        var closure = CompilerBindingClosure.Build(root, f.Input.InputDigest);
        Assert.Equal(wrongVersion ? CompilerBindingClosureState.Incomplete : CompilerBindingClosureState.Complete, closure.State);
        var a = closure.Regions.Single(x => x.Source.Context.Equals(f.A));
        if (wrongVersion) Assert.Contains(a.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.value-reference-manifest-mismatch");
        else { Assert.Equal(3, closure.Regions.Count); Assert.Contains(a.References, reference => reference.SymbolKey!.Contains(f.B.Data.CanonicalText, StringComparison.Ordinal)); }
    }

}
