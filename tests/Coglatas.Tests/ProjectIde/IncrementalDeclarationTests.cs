using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class IncrementalDeclarationTests
{
    [Fact]
    public void RenameReusesOnlyUnchangedDeclarationsWhileAllBoundContextsAndResultsAreFresh()
    {
        var committed = ReferenceCompletionTests.CompletionInput(2); var cache = new PlanDeclarationReadCache();
        var first = ProjectSemanticBinder.Bind(committed, declarationCache: cache);
        var draft = ProjectDraftRename.Fork(first, DraftRenameTests.Proposal(committed));
        var before = ProjectSemanticBinder.Bind(draft, declarationCache: cache);
        var plan = ProjectDraftRename.Preview(before, DraftRenameTests.Target(draft), draft.InputDigest, "Renamed", DraftRenameTests.Next(draft)).Plan!;
        var after = ProjectSemanticBinder.Bind(plan.After, declarationCache: cache);
        Assert.Same(first.Declarations[0].Declaration, before.Declarations[0].Declaration);
        Assert.Same(before.Declarations[0].Declaration, after.Declarations[0].Declaration);
        Assert.NotSame(before.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(10))).Declaration,
            after.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(10))).Declaration);
        Assert.All(after.Symbols, item => Assert.Equal(plan.After.Source.Context, item.Context));
        AssertEquivalent(plan.After, cache);
        Assert.NotEqual(draft.InputDigest, after.Input.InputDigest);
    }

    [Theory]
    [InlineData("rules")]
    [InlineData("units")]
    [InlineData("calendars")]
    [InlineData("observations")]
    [InlineData("descriptors")]
    [InlineData("source-coverage")]
    [InlineData("budget")]
    [InlineData("package")]
    public void InputDependencyChangesAlwaysMatchFullCompilation(string dimension)
    {
        var baseline = ReferenceCompletionTests.CompletionInput(1); var cache = new PlanDeclarationReadCache();
        AssertEquivalent(baseline, cache);
        var settings = JsonNode.Parse(baseline.Settings.CanonicalText)!.AsObject();
        var source = baseline.Source;
        if (dimension == "rules") settings["rules"]![0]!["configuration"] = Obj(new { future = true });
        if (dimension is "units" or "calendars") settings[dimension] = new JsonArray(Artifact("example." + dimension));
        if (dimension == "observations") settings["observations"] = new JsonArray(Obj(new { snapshotId = Id(60), version = "1", context = Source(9).Context.Data.Value,
            digest = Source(9).Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        if (dimension == "descriptors") settings["descriptors"] = new JsonArray();
        if (dimension == "source-coverage") settings["sourceCoverage"] = "coglatas.partial";
        if (dimension == "budget") settings["executionProfile"]!["maximumOperations"] = 1;
        if (dimension == "package") source = ProjectSource.Create(source.Context, source.Documents, source.Declarations, source.Relations,
            packageLock: Json(new JsonArray(Artifact("example.unadopted-package"))));
        var changed = Input(settings, source); Assert.NotEqual(baseline.InputDigest, changed.InputDigest);
        AssertEquivalent(changed, cache);
        AssertEquivalent(baseline, cache);
    }

    [Fact]
    public void MaterializationLossAndRevocationNeverReuseResolvedBindings()
    {
        var baseline = ReferenceCompletionTests.CompletionInput(1); var external = Source(9);
        var declarations = JsonNode.Parse(baseline.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        port["type"] = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = "coglatas.project" });
        port["defaultValue"] = Obj(new { type = port["type"]!.DeepClone(), state = "coglatas.known", value = new {
            tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = external.Context.Data.Value,
            origin = DraftRenameTests.Target(baseline).Data.Value, manifest = new { version = "1", digest = external.Digest.Data.Value } } });
        var settings = Settings(); settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(60), version = "1", context = external.Context.Data.Value,
            digest = external.Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        var input = Input(settings, ProjectSource.Create(baseline.Source.Context, baseline.Source.Documents, Json(declarations)));
        var allowed = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(60)), "1", new(external, true, true, true));
        var denied = new CompilerMaterializedSnapshot(allowed.SnapshotId, "1", new(external, false, true, true));
        var cache = new PlanDeclarationReadCache();
        var before = ProjectSemanticBinder.Bind(input, [allowed], declarationCache: cache); Assert.True(before.BindingComplete);
        foreach (var supply in new[] { Array.Empty<CompilerMaterializedSnapshot>(), new[] { denied }, new[] { allowed } })
        {
            var actual = ProjectSemanticBinder.Bind(input, supply, declarationCache: cache);
            var full = ProjectSemanticBinder.Bind(input, supply);
            Assert.Equal(new CompilerSemanticInspection(full).Data, new CompilerSemanticInspection(actual).Data);
            Assert.Same(before.Declarations[1].Declaration, actual.Declarations[1].Declaration);
            if (supply.Length == 0 || !supply[0].Snapshot.IsAuthorized) Assert.DoesNotContain(actual.References, item => item.State == ReferenceResolutionState.Resolved);
            AssertEquivalent(input, cache, supply);
        }
    }

    [Fact]
    public void CapacityClearAndScopeChangesDoNotChangeDecodeSemanticsOrTenantChecks()
    {
        var source = ReferenceCompletionTests.CompletionInput(2).Source;
        var tiny = new PlanDeclarationReadCache(1, 1); Assert.Equal(PlanDeclarationReader.Read(source).Select(item => item.Status), tiny.Read(source).Select(item => item.Status));
        Assert.Equal(0, tiny.RetainedCount); Assert.Equal(0, tiny.RetainedCanonicalBytes);
        var cache = new PlanDeclarationReadCache(2, 1024); cache.Read(source);
        Assert.InRange(cache.RetainedCount, 0, 2); Assert.InRange(cache.RetainedCanonicalBytes, 0, 1024);
        cache.Clear(); Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.RetainedCanonicalBytes);
        var first = cache.Read(Source());
        var foreign = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(90)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(5))));
        var other = ProjectSource.Create(foreign, Source().Documents);
        Assert.NotSame(first[0].Declaration, cache.Read(other)[0].Declaration);
        var resource = Obj(new { entityId = Id(70), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1,
            payload = new { tenantId = Id(1), resourceId = Id(71), snapshotVersion = "1", snapshotDigest = new { algorithm = "sha-256", value = new string('a', 64) }, resourceKind = "coglatas.equipment" } });
        var localResource = ProjectSource.Create(Source().Context, Source().Documents, Json(new JsonArray(resource)));
        Assert.Equal(ValueDecodeStatus.Decoded, cache.Read(localResource)[1].Status);
        var foreignResource = ProjectSource.Create(foreign, Source().Documents, localResource.Declarations);
        Assert.Equal(ValueDecodeStatus.Invalid, cache.Read(foreignResource)[1].Status);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => cache.Read(source, cancelled.Token));
    }

    [Fact]
    public void DocumentIdentityAndCodecLimitsArePartOfDecodeReuseIdentity()
    {
        var source = Source(); var cache = new PlanDeclarationReadCache(); var initial = cache.Read(source)[0];
        var document = SourceDocument.Create(DocumentId.Parse(Id(91)), "other", source.Documents[0].Root.Data);
        var moved = ProjectSource.Create(source.Context, [document]); var changedDocument = cache.Read(moved)[0];
        Assert.NotSame(initial.Declaration, changedDocument.Declaration); Assert.Equal(document.DocumentId, changedDocument.Declaration!.DocumentId);
        var limited = ProjectSourceCodec.Decode(source.Data.ToCanonicalBytes(), new SourceCodecLimits(MaximumBytes: 1_000_000)).RequireSource();
        Assert.Equal(source.Data, limited.Data); Assert.NotSame(initial.Declaration, cache.Read(limited)[0].Declaration);
    }

    [Fact]
    public void AnEarlierCallerCannotMutateRetainedPortsOrNestedValuesAndPoisonLaterCompilations()
    {
        var baseline = ReferenceCompletionTests.CompletionInput(1);
        var nodes = JsonNode.Parse(baseline.Source.Declarations.CanonicalText)!.AsArray();
        var text = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = false });
        var list = Obj(new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = false, elementType = text.DeepClone() });
        var structure = Obj(new { kind = "coglatas.structure", schemaVersion = 1, nullable = false, acceptsUnknown = false,
            fields = new JsonArray(Obj(new { name = "items", required = true, type = list.DeepClone() })) });
        var port = nodes[0]!["payload"]!["ports"]![0]!; port["type"] = structure;
        port["defaultValue"] = Obj(new { type = structure.DeepClone(), state = "coglatas.known", value = new {
            items = new { type = list.DeepClone(), state = "coglatas.known", value = new JsonArray(Obj(new { type = text.DeepClone(), state = "coglatas.known", value = "kept" })) } } });
        var input = Input(source: ProjectSource.Create(baseline.Source.Context, baseline.Source.Documents, Json(nodes)));
        var cache = new PlanDeclarationReadCache(); var first = cache.Read(input.Source);
        var task = Assert.IsType<TaskPlanDeclaration>(first.Single(item => item.Declaration is TaskPlanDeclaration).Declaration);
        var definition = Assert.Single(task.Ports);
        var value = Assert.IsType<ProjectStructureValue>(definition.DefaultValue!.KnownValue);
        var collection = Assert.IsType<ProjectCollectionValue>(value.Fields["items"].KnownValue);
        Assert.Throws<NotSupportedException>(() => ((IList<DeclarationDecodeResult>)first).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PortDefinition>)task.Ports).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PortDefinition>)task.Ports)[0] = definition);
        Assert.Throws<NotSupportedException>(() => ((IList<StructureField>)definition.Type.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, TypedProjectValue>)value.Fields)["items"] = value.Fields["items"]);
        Assert.Throws<NotSupportedException>(() => ((IList<TypedProjectValue>)collection.Items).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ProjectMapEntry>)collection.Entries).Add(new(collection.Items[0], collection.Items[0])));
        var bytes = task.Data.ToCanonicalBytes(); bytes[0] = (byte)'!';
        var detached = JsonNode.Parse(task.Data.CanonicalText)!; detached["displayName"] = "Changed by caller";
        var next = Assert.IsType<TaskPlanDeclaration>(cache.Read(input.Source).Single(item => item.Declaration is TaskPlanDeclaration).Declaration);
        Assert.Same(task, next); Assert.Equal("Task 0", next.DisplayName);
        Assert.Equal("kept", collection.Items[0].KnownValue);
        AssertEquivalent(input, cache);
    }

    [Fact]
    public async Task ConcurrentLocalHostRequestsAndClearPreserveExactResponsesForWindowsPreparation()
    {
        var inputs = Enumerable.Range(0, 12).Select(index => Input(source: Source(100 + index))).ToArray();
        var input = inputs[0]; CompilerArtifactIdentity ArtifactAt(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, ArtifactAt("schema"), ArtifactAt("ruleset"), ArtifactAt("semantics"), ArtifactAt("ir"), ArtifactAt("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var cache = new PlanDeclarationReadCache(); var host = new LocalNetCompilerHost(profile, cache);
        var requests = inputs.Select((item, index) => new CompilerHostRequest(new(item), index)).ToArray();
        var responses = await Task.WhenAll(requests.Select((request, index) => { if (index % 3 == 0) cache.Clear(); return host.CompileAsync(request); }));
        for (var index = 0; index < inputs.Length; index++)
        {
            Assert.Equal(CompilerHostStatus.Completed, responses[index].Status); Assert.Equal(index, responses[index].ClientGeneration);
            Assert.Equal(PureCoreCompiler.Compile(inputs[index], inputs[index].Compiler).Result.Data, responses[index].Compilation!.Result.Data);
            Assert.All(responses[index].Compilation!.Model!.Symbols, symbol => Assert.Equal(inputs[index].Source.Context, symbol.Context));
        }
        Assert.Equal(1, cache.RetainedCount);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(requests[^1]);
        Assert.True(view.TryAccept(responses[^1])); Assert.False(view.TryAccept(responses[0]));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(CompilerHostStatus.Cancelled, (await host.CompileAsync(requests[0], cancelled.Token)).Status); Assert.Equal(1, cache.RetainedCount);
    }

    private static void AssertEquivalent(CompilerInputManifest input, PlanDeclarationReadCache cache, CompilerMaterializedSnapshot[]? snapshots = null)
    {
        var full = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
        var actual = PureCoreCompiler.Compile(input, input.Compiler, snapshots, declarationCache: cache);
        Assert.Equal(full.Result.Data, actual.Result.Data);
        Assert.Equal(full.Result.ResultDigest, actual.Result.ResultDigest);
        if (full.Model is { } model) Assert.Equal(new CompilerSemanticInspection(model).Data, new CompilerSemanticInspection(actual.Model!).Data);
    }
}
