using System.Collections;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Xunit;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent synthetic retained-storage controls; no host authorization or runtime acceptance.</summary>
public sealed class SecuritySymbolCacheCandidateTests
{
    private static readonly DocumentId Document = new(Id(1001));
    private static readonly EntityId RootEntity = new(Id(1002));
    private static readonly EntityId TaskEntity = new(Id(1003));
    private static readonly EntityId ResourceEntity = new(Id(1004));
    private static readonly PortId OriginalPort = new(Id(1005));

    [Fact]
    public void ColdAndWarmBindsPreserveCanonicalDeclarationsAndSymbols()
    {
        var input = Input(Source());
        var cache = new ProjectSymbolBindingCache();
        var uncached = Bind(input);
        var cold = Bind(input, cache);
        var original = Fingerprint(cold);
        var warm = Bind(input, cache);

        Assert.True(cold.BindingComplete);
        Assert.Equal(Fingerprint(uncached), original);
        Assert.Equal(original, Fingerprint(warm));
        Assert.Equal(2, cache.RetainedCount);
        Assert.True(cache.RetainedCanonicalBytes > 0);
        Assert.Same(TaskResult(cold), TaskResult(warm));
        Assert.Same(TaskSymbol(cold), TaskSymbol(warm));
        Assert.Same(input, warm.Input);
        AssertNested(cold, "retained-alpha");
        AssertNested(warm, "retained-alpha");
    }

    [Fact]
    public void CallerOwnedBuffersAndDocumentArraysCannotRewriteRetainedModels()
    {
        var documents = new[] { RootDocument() };
        var declarationBytes = Encoding.UTF8.GetBytes(Json(new[] { TaskBody("caller-alpha") }).CanonicalText);
        var declarations = SourceJson.Parse(declarationBytes);
        var source = ProjectSource.Create(Context(), documents, declarations);
        var cache = new ProjectSymbolBindingCache();
        var input = Input(source);
        var model = Bind(input, cache);
        var original = Fingerprint(model);

        Array.Fill(declarationBytes, (byte)'x');
        documents[0] = SourceDocument.Create(new DocumentId(Id(1101)), "replacement", Json(new
        {
            entityId = Id(1102).ToString(), kind = "coglatas.project", schemaVersion = 1,
            payload = new { roots = Array.Empty<string>() }
        }));
        var sourceCopy = source.Data.ToCanonicalBytes();
        Array.Clear(sourceCopy);

        Assert.Equal(Document, Assert.Single(source.Documents).DocumentId);
        Assert.Equal(original, Fingerprint(model));
        Assert.Equal(original, Fingerprint(Bind(input, cache)));
        AssertNested(model, "caller-alpha");
    }

    [Fact]
    public void PublishedModelCollectionsRejectWritableInterfaceSurfaces()
    {
        var model = Bind(Input(Source()), new ProjectSymbolBindingCache());
        var original = Fingerprint(model);
        RejectList(model.Declarations);
        RejectList(model.Symbols);
        RejectList(model.References);
        RejectList(model.Relations);
        RejectList(model.Findings);
        RejectList(model.BoundSnapshots);
        RejectList(model.Input.Rules);
        RejectList(model.Input.Source.Documents);
        RejectList(model.Input.Source.Manifest.DocumentIds);
        RejectList(Assert.IsType<ProjectPlanDeclaration>(model.Declarations[0].Declaration).Roots);
        RejectList(TaskDeclaration(model).Ports);
        Assert.Equal(original, Fingerprint(model));
        AssertNested(model, "retained-alpha");
    }

    [Fact]
    public void NestedStructureAndMapCollectionsKeepPrivateStorage()
    {
        var cache = new ProjectSymbolBindingCache();
        var input = Input(Source());
        var cold = Bind(input, cache);
        var port = Assert.Single(TaskDeclaration(cold).Ports);
        var structure = Assert.IsType<ProjectStructureValue>(port.DefaultValue!.KnownValue);
        var rows = Assert.IsType<ProjectCollectionValue>(structure.Fields["rows"].KnownValue);
        var map = Assert.IsType<ProjectCollectionValue>(structure.Fields["lookup"].KnownValue);
        var mapRows = Assert.IsType<ProjectCollectionValue>(Assert.Single(map.Entries).Value.KnownValue);
        var original = Fingerprint(cold);

        RejectList(port.Type.Fields);
        RejectMap(structure.Fields);
        RejectList(rows.Items);
        RejectList(rows.Entries);
        RejectList(map.Items);
        RejectList(map.Entries);
        RejectList(mapRows.Items);
        AssertNested(cold, "retained-alpha");
        AssertNested(Bind(input, cache), "retained-alpha");
        Assert.Equal(original, Fingerprint(cold));
    }

    [Fact]
    public void DetachedArraysAndCopiedRecordsCannotRewriteRetainedBindings()
    {
        var input = Input(Source());
        var cache = new ProjectSymbolBindingCache();
        var model = Bind(input, cache);
        var original = Fingerprint(model);
        var symbol = TaskSymbol(model);
        var foreign = Context(project: 9);
        var detachedSymbols = model.Symbols.ToArray();
        detachedSymbols[0] = symbol with
        {
            Key = "detached-forged-key", Kind = "coglatas.forged", Context = foreign,
            Location = ProjectLocation.Create(Document, foreign, entityId: TaskEntity)
        };
        var reference = Assert.Single(model.References);
        var detachedReference = reference with { State = ReferenceResolutionState.OutOfScope, SymbolKey = null };
        Assert.NotEqual(reference, detachedReference);

        var port = Assert.Single(TaskDeclaration(model).Ports);
        var fieldCopy = port.Type.Fields[0] with { Name = "detached-forged-field" };
        Assert.DoesNotContain(port.Type.Fields, item => item.Name == fieldCopy.Name);
        var structure = Assert.IsType<ProjectStructureValue>(port.DefaultValue!.KnownValue);
        var entries = Assert.IsType<ProjectCollectionValue>(structure.Fields["lookup"].KnownValue).Entries;
        var entryCopy = entries[0] with { Key = TextValue("detached-forged-key") };
        var entryArray = entries.ToArray();
        entryArray[0] = entryCopy;
        var copiedFields = structure.Fields.ToDictionary(item => item.Key, item => item.Value);
        copiedFields.Clear();
        var copiedSymbols = new CompilerSymbol[model.Symbols.Count];
        Assert.IsAssignableFrom<ICollection<CompilerSymbol>>(model.Symbols).CopyTo(copiedSymbols, 0);
        Array.Clear(copiedSymbols);

        Assert.Equal(original, Fingerprint(model));
        Assert.Equal(original, Fingerprint(Bind(input, cache)));
        AssertNested(model, "retained-alpha");
        Assert.Equal("lookup-key", entries[0].Key.KnownValue);
    }

    [Fact]
    public void ContextAndLocationByteCopiesCannotRetargetSymbols()
    {
        var input = Input(Source());
        var cache = new ProjectSymbolBindingCache();
        var model = Bind(input, cache);
        var original = Fingerprint(model);
        foreach (var symbol in model.Symbols)
        {
            var contextBytes = symbol.Context.Data.ToCanonicalBytes();
            var locationBytes = symbol.Location.ToCanonicalBytes();
            Array.Clear(contextBytes);
            Array.Clear(locationBytes);
            Assert.Equal(input.Source.Context, symbol.Context);
            Assert.Equal(input.Source.Context, symbol.Location.Context);
            Assert.Equal(Document, symbol.Location.DocumentId);
            Assert.Equal(symbol.EntityId, symbol.Location.EntityId);
        }
        var port = Assert.Single(model.Symbols, item => item.PortId is not null);
        Assert.Equal("/payload/ports/" + OriginalPort, port.Location.PropertyPath);
        Assert.Equal(original, Fingerprint(model));
        Assert.Equal(original, Fingerprint(Bind(input, cache)));
    }

    [Fact]
    public void EqualReparsedContextsReuseExactPartitions()
    {
        var firstSource = Source();
        var reparsed = ProjectSourceCodec.Decode(firstSource.Data.ToCanonicalBytes()).RequireSource();
        Assert.NotSame(firstSource.Context, reparsed.Context);
        Assert.Equal(firstSource.Context, reparsed.Context);
        var cache = new ProjectSymbolBindingCache();
        var first = Bind(Input(firstSource), cache);
        var nextInput = Input(reparsed);
        var next = Bind(nextInput, cache);

        Assert.Same(nextInput, next.Input);
        Assert.Same(TaskResult(first), TaskResult(next));
        Assert.Same(TaskSymbol(first), TaskSymbol(next));
        Assert.Equal(Fingerprint(first), Fingerprint(next));
        Assert.Equal(2, cache.RetainedCount);
        AssertNested(next, "retained-alpha");
    }

    [Fact]
    public void CommittedCandidateAndScenarioContextsRemainDistinct()
    {
        var committed = Context();
        var candidate = SourceRevisionContext.Candidate(new ProposalContext(
            new ProposalId(Id(1201)), new CandidateRevisionId(Id(1202)),
            committed.CommittedRevision!, committed.CommittedRevision!));
        var scenario = SourceRevisionContext.Hypothetical(new ScenarioRef(
            new ScenarioId(Id(1203)), new ScenarioRevisionId(Id(1204)), candidate, Digest("overlay")));
        var cache = new ProjectSymbolBindingCache();
        var models = new[] { committed, candidate, scenario }
            .Select(context => Bind(Input(Source(context)), cache)).ToArray();
        var originals = models.Select(Fingerprint).ToArray();

        Assert.Equal(3, models.Select(model => TaskSymbol(model).Key).Distinct().Count());
        for (var index = 0; index < models.Length; index++)
        {
            Assert.True(models[index].BindingComplete);
            Assert.All(models[index].Symbols, symbol =>
            {
                Assert.Equal(models[index].Input.Source.Context, symbol.Context);
                Assert.Equal(symbol.Context, symbol.Location.Context);
            });
            AssertNested(models[index], "retained-alpha");
        }
        cache.Clear();
        Assert.Equal(originals, models.Select(Fingerprint).ToArray());
    }

    [Fact]
    public void EditedPortsCannotRewriteRetainedModels()
    {
        var cache = new ProjectSymbolBindingCache();
        var oldInput = Input(Source());
        var old = Bind(oldInput, cache);
        var original = Fingerprint(old);
        var replacementPort = new PortId(Id(1301));
        var edited = Bind(Input(Source(Context(revision: 2), "edited-beta", replacementPort)), cache);

        Assert.True(edited.BindingComplete);
        Assert.Equal(replacementPort, Assert.Single(TaskDeclaration(edited).Ports).PortId);
        Assert.DoesNotContain(edited.Symbols, symbol => symbol.PortId == OriginalPort);
        Assert.NotSame(TaskResult(old), TaskResult(edited));
        AssertNested(old, "retained-alpha");
        AssertNested(edited, "edited-beta");
        Assert.Equal(original, Fingerprint(old));
        Assert.Equal(original, Fingerprint(Bind(oldInput, cache)));
    }

    [Fact]
    public void ChangedDeclarationKindsDoNotReuseOldDecodeStatus()
    {
        var cache = new ProjectSymbolBindingCache();
        var valid = Bind(Input(Source()), cache);
        var original = Fingerprint(valid);
        var unsupportedInput = Input(Source(Context(revision: 2), taskKind: "coglatas.future-task"));
        var unsupported = Bind(unsupportedInput, cache);

        Assert.False(unsupported.BindingComplete);
        Assert.Equal(ValueDecodeStatus.Unsupported, TaskResult(unsupported).Status);
        Assert.Null(TaskResult(unsupported).Declaration);
        Assert.Equal(ValueDecodeStatus.Unsupported, TaskSymbol(unsupported).Status);
        Assert.DoesNotContain(unsupported.Symbols, symbol => symbol.PortId is not null);
        Assert.Equal(Fingerprint(unsupported), Fingerprint(Bind(unsupportedInput, cache)));
        var restored = Bind(Input(Source(Context(revision: 3))), cache);
        Assert.True(restored.BindingComplete);
        Assert.Equal(ValueDecodeStatus.Decoded, TaskSymbol(restored).Status);
        Assert.Equal(original, Fingerprint(valid));
        AssertNested(valid, "retained-alpha");
    }

    [Fact]
    public void EntryEvictionPreservesOldNestedModels()
    {
        var cache = new ProjectSymbolBindingCache(maximumEntries: 2);
        var firstInput = Input(Source());
        var first = Bind(firstInput, cache);
        var original = Fingerprint(first);
        for (var revision = 2; revision <= 5; revision++)
        {
            var changed = Bind(Input(Source(Context(revision), "revision-" + revision)), cache);
            Assert.True(changed.BindingComplete);
            Assert.Equal(2, cache.RetainedCount);
            AssertNested(changed, "revision-" + revision);
        }
        var rebound = Bind(firstInput, cache);
        Assert.NotSame(TaskResult(first), TaskResult(rebound));
        Assert.NotSame(TaskSymbol(first), TaskSymbol(rebound));
        Assert.Equal(original, Fingerprint(rebound));
        Assert.Equal(original, Fingerprint(first));
        AssertNested(first, "retained-alpha");
    }

    [Fact]
    public void CanonicalByteBudgetBypassKeepsBindingsUncached()
    {
        var input = Input(Source());
        var cache = new ProjectSymbolBindingCache(maximumCanonicalBytes: 1);
        var first = Bind(input, cache);
        var next = Bind(input, cache);
        Assert.True(first.BindingComplete);
        Assert.Equal(0, cache.RetainedCount);
        Assert.Equal(0, cache.RetainedCanonicalBytes);
        Assert.NotSame(TaskResult(first), TaskResult(next));
        Assert.NotSame(TaskSymbol(first), TaskSymbol(next));
        Assert.Equal(Fingerprint(first), Fingerprint(next));
        cache.Clear();
        AssertNested(first, "retained-alpha");
        Assert.Equal(Fingerprint(first), Fingerprint(Bind(input, cache)));
    }

    [Fact]
    public void ClearDropsRetentionAndRebindsFreshObjects()
    {
        var input = Input(Source());
        var cache = new ProjectSymbolBindingCache();
        var first = Bind(input, cache);
        var original = Fingerprint(first);
        var retainedBytes = cache.RetainedCanonicalBytes;
        Assert.True(retainedBytes > 0);
        cache.Clear();
        Assert.Equal(0, cache.RetainedCount);
        Assert.Equal(0, cache.RetainedCanonicalBytes);
        var rebound = Bind(input, cache);
        Assert.NotSame(TaskResult(first), TaskResult(rebound));
        Assert.NotSame(TaskSymbol(first), TaskSymbol(rebound));
        Assert.Equal(retainedBytes, cache.RetainedCanonicalBytes);
        Assert.Equal(original, Fingerprint(rebound));
        Assert.Equal(original, Fingerprint(first));
        AssertNested(first, "retained-alpha");
    }

    [Fact]
    public void BranchProjectAndTenantSwitchesCannotRetainAnotherOwner()
    {
        var input = Input(Source());
        var cache = new ProjectSymbolBindingCache();
        var first = Bind(input, cache);
        var original = Fingerprint(first);
        foreach (var context in new[] { Context(branch: 2), Context(project: 2), Context(tenant: 2) })
        {
            var other = Bind(Input(Source(context, "different-owner")), cache);
            Assert.True(other.BindingComplete);
            Assert.Equal(2, cache.RetainedCount);
            Assert.All(other.Symbols, symbol => Assert.Equal(context, symbol.Context));
            Assert.DoesNotContain(other.Symbols, symbol => symbol.Key == TaskSymbol(first).Key);
            AssertNested(other, "different-owner");
        }
        var restored = Bind(input, cache);
        Assert.NotSame(TaskResult(first), TaskResult(restored));
        Assert.Equal(original, Fingerprint(restored));
        Assert.Equal(original, Fingerprint(first));
        AssertNested(first, "retained-alpha");
    }

    [Fact]
    public async Task ConcurrentDifferentRootsKeepIndependentSymbolsAndValues()
    {
        var cache = new ProjectSymbolBindingCache(maximumEntries: 4);
        var inputs = Enumerable.Range(0, 4)
            .Select(index => Input(Source(Context(tenant: index + 1, project: index + 1), "parallel-" + index)))
            .ToArray();
        var expected = inputs.Select(input => Fingerprint(Bind(input))).ToArray();
        var retained = await Task.WhenAll(Enumerable.Range(0, inputs.Length).Select(index => Task.Run(() =>
        {
            PartialSemanticModel? last = null;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                last = Bind(inputs[index], cache);
                Assert.Equal(expected[index], Fingerprint(last));
                AssertNested(last, "parallel-" + index);
                Assert.All(last.Symbols, symbol =>
                {
                    Assert.Equal(inputs[index].Source.Context, symbol.Context);
                    Assert.Equal(symbol.Context, symbol.Location.Context);
                });
            }
            return Assert.IsType<PartialSemanticModel>(last);
        })));
        cache.Clear();
        Assert.Equal(0, cache.RetainedCount);
        for (var index = 0; index < retained.Length; index++)
        {
            Assert.Equal(expected[index], Fingerprint(retained[index]));
            AssertNested(retained[index], "parallel-" + index);
        }
    }

    [Fact]
    public void ResourceTenantChecksRemainInvalidUntilCorrected()
    {
        var owner = Context().Branch.TenantId;
        var foreign = Context(tenant: 2).Branch.TenantId;
        var cache = new ProjectSymbolBindingCache();
        var first = Bind(Input(Source(resourceTenant: owner)), cache);
        var original = Fingerprint(first);
        Assert.True(first.BindingComplete);
        Assert.Equal(ValueDecodeStatus.Decoded, ResourceResult(first).Status);
        var invalidInput = Input(Source(Context(revision: 2), resourceTenant: foreign));
        var invalid = Bind(invalidInput, cache);
        Assert.False(invalid.BindingComplete);
        Assert.Equal(ValueDecodeStatus.Invalid, ResourceResult(invalid).Status);
        Assert.Null(ResourceResult(invalid).Declaration);
        Assert.Equal(ValueDecodeStatus.Invalid,
            Assert.Single(invalid.Symbols, symbol => symbol.EntityId == ResourceEntity).Status);
        Assert.Equal(Fingerprint(invalid), Fingerprint(Bind(invalidInput, cache)));
        var restored = Bind(Input(Source(Context(revision: 3), resourceTenant: owner)), cache);
        Assert.True(restored.BindingComplete);
        Assert.Equal(ValueDecodeStatus.Decoded, ResourceResult(restored).Status);
        Assert.Equal(original, Fingerprint(first));
    }

    [Fact]
    public void WarmCacheDoesNotAuthorizeDeniedMaterializedSnapshots()
    {
        var external = Materialization(Source(Context(project: 2), "external-owned"));
        var input = Input(Source(), pin: external);
        var cache = new ProjectSymbolBindingCache();
        var first = Bind(input, cache, external);
        var original = Fingerprint(first);
        Assert.True(first.BindingComplete);
        Assert.Single(first.BoundSnapshots);
        var originalExternal = Assert.Single(first.Symbols, symbol =>
            symbol.Context.Equals(external.Snapshot.Source.Context) && symbol.EntityId == TaskEntity && symbol.PortId is null);
        var denied = new CompilerMaterializedSnapshot(external.SnapshotId, external.Version,
            external.Snapshot with { IsAuthorized = false });
        var unavailable = Bind(input, cache, denied);
        Assert.False(unavailable.BindingComplete);
        Assert.Empty(unavailable.BoundSnapshots);
        Assert.All(unavailable.Symbols, symbol => Assert.Equal(input.Source.Context, symbol.Context));
        Assert.Contains(unavailable.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.snapshot-unavailable");
        var restored = Bind(input, cache, external);
        Assert.True(restored.BindingComplete);
        Assert.Equal(original, Fingerprint(restored));
        Assert.Same(originalExternal, Assert.Single(restored.Symbols, symbol => symbol.Key == originalExternal.Key));
        Assert.True(Assert.Single(first.BoundSnapshots).IsAuthorized);
        Assert.Equal(original, Fingerprint(first));
    }

    [Fact]
    public void ChangedSnapshotPinsCannotReusePreviouslyAdmittedSymbols()
    {
        var external = Materialization(Source(Context(project: 2), "external-owned"));
        var input = Input(Source(), pin: external);
        var cache = new ProjectSymbolBindingCache();
        var admitted = Bind(input, cache, external);
        var original = Fingerprint(admitted);
        Assert.True(admitted.BindingComplete);
        var replacements = new[]
        {
            new CompilerMaterializedSnapshot(external.SnapshotId, "replacement-version", external.Snapshot),
            new CompilerMaterializedSnapshot(external.SnapshotId, external.Version,
                new RelationScopeSnapshot(Source(Context(revision: 2, project: 2), "changed-body"), true, true, true))
        };
        foreach (var replacement in replacements)
        {
            var rejected = Bind(input, cache, replacement);
            Assert.False(rejected.BindingComplete);
            Assert.Empty(rejected.BoundSnapshots);
            Assert.All(rejected.Symbols, symbol => Assert.Equal(input.Source.Context, symbol.Context));
            Assert.Contains(rejected.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.snapshot-pin-mismatch");
        }
        Assert.Equal(original, Fingerprint(Bind(input, cache, external)));
        Assert.Equal(original, Fingerprint(admitted));
    }

    [Fact]
    public void PartialCoverageNeverBecomesCompleteThroughCacheReuse()
    {
        var source = Source();
        var cache = new ProjectSymbolBindingCache();
        var completeInput = Input(source);
        var first = Bind(completeInput, cache);
        var original = Fingerprint(first);
        var partialInput = Input(source, complete: false);
        var partial = Bind(partialInput, cache);
        Assert.NotEqual(completeInput.InputDigest, partialInput.InputDigest);
        Assert.False(partial.BindingComplete);
        Assert.Same(partialInput, partial.Input);
        Assert.Same(TaskResult(first), TaskResult(partial));
        Assert.Contains(partial.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.incomplete-source");
        Assert.Equal(Fingerprint(Bind(partialInput)), Fingerprint(partial));
        Assert.Equal(original, Fingerprint(Bind(completeInput, cache)));
        Assert.Equal(original, Fingerprint(first));
    }

    [Fact]
    public void CancelledBindsPreservePreviouslyPublishedModels()
    {
        var cache = new ProjectSymbolBindingCache();
        var input = Input(Source());
        var first = Bind(input, cache);
        var original = Fingerprint(first);
        var count = cache.RetainedCount;
        var bytes = cache.RetainedCanonicalBytes;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(
            Input(Source(Context(tenant: 2), "cancelled-owner")),
            cancellationToken: cancellation.Token, symbolBindingCache: cache));
        Assert.Equal(count, cache.RetainedCount);
        Assert.Equal(bytes, cache.RetainedCanonicalBytes);
        Assert.Same(TaskSymbol(first), TaskSymbol(Bind(input, cache)));
        Assert.Equal(original, Fingerprint(first));
        AssertNested(first, "retained-alpha");
    }

    [Fact]
    public void WarmCacheDoesNotBypassLogicalOperationBudgets()
    {
        var source = Source();
        var cache = new ProjectSymbolBindingCache();
        var input = Input(source);
        var originalModel = Bind(input, cache);
        var original = Fingerprint(originalModel);
        var limitedInput = Input(source, maximumOperations: 1);
        var limited = Bind(limitedInput, cache);
        Assert.False(limited.BindingComplete);
        Assert.Empty(limited.Declarations);
        Assert.Empty(limited.Symbols);
        Assert.Equal(1, limited.LogicalOperationsConsumed);
        Assert.Contains(limited.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.budget-exhausted");
        Assert.Equal(Fingerprint(Bind(limitedInput)), Fingerprint(limited));
        Assert.Same(TaskResult(originalModel), TaskResult(Bind(input, cache)));
        Assert.Equal(original, Fingerprint(originalModel));
    }

    [Fact]
    public void FullCompilationMatchesColdWarmEvictedAndClearedBindings()
    {
        var input = Input(Source());
        var full = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(CompilerExecutionState.Completed, full.Result.Execution);
        Assert.Equal(CompilerVerdict.Pass, full.Result.Verdict);
        Assert.NotNull(full.Validation?.Ir);
        var cache = new ProjectSymbolBindingCache(maximumEntries: 2);
        var cold = PureCoreCompiler.Compile(input, input.Compiler, symbolBindingCache: cache);
        var warm = PureCoreCompiler.Compile(input, input.Compiler, symbolBindingCache: cache);
        Assert.Same(TaskResult(cold.Model!), TaskResult(warm.Model!));
        Bind(Input(Source(Context(revision: 2), "evicting-source")), cache);
        var afterEviction = PureCoreCompiler.Compile(input, input.Compiler, symbolBindingCache: cache);
        Assert.NotSame(TaskResult(cold.Model!), TaskResult(afterEviction.Model!));
        cache.Clear();
        var afterClear = PureCoreCompiler.Compile(input, input.Compiler, symbolBindingCache: cache);
        Assert.NotSame(TaskResult(afterEviction.Model!), TaskResult(afterClear.Model!));
        foreach (var actual in new[] { cold, warm, afterEviction, afterClear })
        {
            Assert.Equal(full.Result.Data.CanonicalText, actual.Result.Data.CanonicalText);
            Assert.Equal(full.Result.ResultDigest, actual.Result.ResultDigest);
            Assert.Equal(full.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
            Assert.Equal(full.Validation!.Ir!.Data.CanonicalText, actual.Validation!.Ir!.Data.CanonicalText);
            Assert.Equal(Fingerprint(full.Model!), Fingerprint(actual.Model!));
            AssertNested(actual.Model!, "retained-alpha");
        }
    }

    private static PartialSemanticModel Bind(CompilerInputManifest input, ProjectSymbolBindingCache? cache = null,
        params CompilerMaterializedSnapshot[] snapshots) =>
        ProjectSemanticBinder.Bind(input, snapshots, symbolBindingCache: cache);

    private static DeclarationDecodeResult TaskResult(PartialSemanticModel model) =>
        Assert.Single(model.Declarations, item => item.Data.Value.GetProperty("entityId").GetString() == TaskEntity.ToString());

    private static DeclarationDecodeResult ResourceResult(PartialSemanticModel model) =>
        Assert.Single(model.Declarations, item => item.Data.Value.GetProperty("entityId").GetString() == ResourceEntity.ToString());

    private static TaskPlanDeclaration TaskDeclaration(PartialSemanticModel model) =>
        Assert.IsType<TaskPlanDeclaration>(TaskResult(model).Declaration);

    private static CompilerSymbol TaskSymbol(PartialSemanticModel model) =>
        Assert.Single(model.Symbols, item => item.EntityId == TaskEntity && item.PortId is null
            && item.Context.Equals(model.Input.Source.Context));

    private static void AssertNested(PartialSemanticModel model, string marker)
    {
        var port = Assert.Single(TaskDeclaration(model).Ports);
        Assert.Equal(new[] { "rows", "lookup" }, port.Type.Fields.Select(field => field.Name).ToArray());
        var structure = Assert.IsType<ProjectStructureValue>(port.DefaultValue!.KnownValue);
        Assert.Equal(2, structure.Fields.Count);
        var rows = Assert.IsType<ProjectCollectionValue>(structure.Fields["rows"].KnownValue);
        Assert.Equal(new[] { marker, "tail-value" }, rows.Items.Select(item => Assert.IsType<string>(item.KnownValue)).ToArray());
        var map = Assert.IsType<ProjectCollectionValue>(structure.Fields["lookup"].KnownValue);
        var entry = Assert.Single(map.Entries);
        Assert.Equal("lookup-key", Assert.IsType<string>(entry.Key.KnownValue));
        var nestedRows = Assert.IsType<ProjectCollectionValue>(entry.Value.KnownValue);
        Assert.Equal(marker, Assert.IsType<string>(Assert.Single(nestedRows.Items).KnownValue));
    }

    private static void RejectList<T>(IReadOnlyList<T> values)
    {
        Assert.False(values is T[]);
        Assert.False(values is IList);
        Assert.False(values is ICollection);
        var list = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(default!));
        Assert.Throws<NotSupportedException>(() => list.Insert(0, default!));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => { list.Remove(default!); });
        if (values.Count != 0)
        {
            Assert.Throws<NotSupportedException>(() => list[0] = values[0]);
            Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        }
    }

    private static void RejectMap<T>(IReadOnlyDictionary<string, T> values)
    {
        Assert.False(values is IDictionary);
        Assert.False(values is ICollection);
        var map = Assert.IsAssignableFrom<IDictionary<string, T>>(values);
        Assert.True(map.IsReadOnly);
        var item = values.First();
        Assert.Throws<NotSupportedException>(() => map.Add("forged", item.Value));
        Assert.Throws<NotSupportedException>(() => map[item.Key] = item.Value);
        Assert.Throws<NotSupportedException>(() => { map.Remove(item.Key); });
        Assert.Throws<NotSupportedException>(() => map.Clear());
        RejectList(Assert.IsAssignableFrom<IReadOnlyList<string>>(map.Keys));
        RejectList(Assert.IsAssignableFrom<IReadOnlyList<T>>(map.Values));
        Assert.False(values.Keys is ICollection);
        Assert.False(values.Values is ICollection);
    }

    private static string Fingerprint(PartialSemanticModel model) => Json(new
    {
        input = model.Input.Data.Value,
        model.BindingComplete,
        model.LogicalOperationsConsumed,
        declarations = model.Declarations.Select(item => new { item.Status, data = item.Data.Value }).ToArray(),
        symbols = model.Symbols.Select(item => new
        {
            item.Key, item.Kind, entityId = item.EntityId.ToString(), portId = item.PortId?.ToString(),
            context = item.Context.Data.Value, location = item.Location.Data.Value, item.Status
        }).ToArray(),
        references = model.References.Select(item => new { origin = item.Origin.Data.Value, item.State, item.SymbolKey }).ToArray(),
        findings = model.Findings.Select(item => new { item.State, diagnostic = item.Diagnostic.Data.Value }).ToArray(),
        snapshots = model.BoundSnapshots.Select(item => new
        {
            source = item.Source.Data.Value, item.IsAuthorized, item.DeclarationsComplete, item.RelationsComplete
        }).ToArray()
    }).CanonicalText;

    private static ProjectSource Source(SourceRevisionContext? context = null, string marker = "retained-alpha",
        PortId? port = null, string taskKind = "coglatas.task", TenantId? resourceTenant = null)
    {
        var declarations = new List<object> { TaskBody(marker, port, taskKind) };
        if (resourceTenant is { } tenant)
            declarations.Add(new
            {
                entityId = ResourceEntity.ToString(), documentId = Document.ToString(),
                kind = "coglatas.resource-binding", schemaVersion = 1,
                payload = new
                {
                    tenantId = tenant.ToString(), resourceId = Id(1401).ToString(), snapshotVersion = "fixed-resource-v1",
                    snapshotDigest = Digest("resource").Data.Value, resourceKind = "coglatas.equipment"
                }
            });
        return ProjectSource.Create(context ?? Context(), [RootDocument()], Json(declarations));
    }

    private static SourceDocument RootDocument() => SourceDocument.Create(Document, "independent-symbol-storage", Json(new
    {
        entityId = RootEntity.ToString(), kind = "coglatas.project", schemaVersion = 1,
        payload = new { roots = new[] { TaskEntity.ToString() } }
    }));

    private static object TaskBody(string marker, PortId? port = null, string kind = "coglatas.task")
    {
        var text = ScalarType("coglatas.text");
        var list = new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = false, elementType = text };
        var map = new
        {
            kind = "coglatas.map", schemaVersion = 1, nullable = false, acceptsUnknown = false,
            keyType = text, valueType = list, identity = "coglatas.canonical-value"
        };
        var structure = new
        {
            kind = "coglatas.structure", schemaVersion = 1, nullable = false, acceptsUnknown = false,
            fields = new object[]
            {
                new { name = "rows", type = (object)list, required = true },
                new { name = "lookup", type = (object)map, required = true }
            }
        };
        return new
        {
            entityId = TaskEntity.ToString(), documentId = Document.ToString(), kind, schemaVersion = 1,
            payload = new
            {
                duration = Known(ScalarType("coglatas.elapsed-duration"), "7"),
                ports = new[]
                {
                    new
                    {
                        portId = (port ?? OriginalPort).ToString(), direction = "coglatas.input", type = structure,
                        minimumConnections = 0, maximumConnections = 1,
                        defaultValue = Known(structure, new
                        {
                            rows = Known(list, new[] { Known(text, marker), Known(text, "tail-value") }),
                            lookup = Known(map, new[]
                            {
                                new { key = Known(text, "lookup-key"), value = Known(list, new[] { Known(text, marker) }) }
                            })
                        })
                    }
                }
            }
        };
    }

    private static object ScalarType(string kind) => new { kind, schemaVersion = 1, nullable = false, acceptsUnknown = false };
    private static object Known(object type, object value) => new { type, state = "coglatas.known", value };
    private static TypedProjectValue TextValue(string value)
    {
        var decoded = TypedProjectValueCodec.Decode(Json(Known(ScalarType("coglatas.text"), value)));
        Assert.Equal(ValueDecodeStatus.Decoded, decoded.Status);
        return Assert.IsType<TypedProjectValue>(decoded.Value);
    }

    private static CompilerInputManifest Input(ProjectSource source, bool complete = true, int maximumOperations = 2_000_000,
        CompilerMaterializedSnapshot? pin = null)
    {
        object Artifact(string id) => new { artifactId = id, version = "1", digest = Digest(id).Data.Value };
        var none = new { kind = "coglatas.none" };
        object scenario = source.Context.Scenario is { } overlay
            ? new { baseContext = overlay.BaseContext.Data.Value, overlayDigest = overlay.OverlayDigest.Data.Value, assumptions = Array.Empty<object>() }
            : none;
        object[] snapshots = pin is null ? [] : [new
        {
            snapshotId = pin.SnapshotId.ToString(), version = pin.Version, digest = pin.Snapshot.Source.Digest.Data.Value,
            context = pin.Snapshot.Source.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null
        }];
        return new CompilerInputManifest(source, Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.independent-symbol-cache-compiler"),
            ruleset = Artifact("coglatas.independent-symbol-cache-rules"), semantics = Artifact("coglatas.semantic-model"),
            ir = Artifact("coglatas.project-ir"), evaluator = Artifact("coglatas.independent-predicate-evaluator"),
            authorizedExternalSnapshots = snapshots, resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(),
            observations = Array.Empty<object>(), evidence = Array.Empty<object>(), descriptors = Array.Empty<object>(),
            units = Array.Empty<object>(), calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            solver = none, stochastic = none, importer = none, scheduler = none, scenario,
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = new[] { "coglatas.core.structural" }, targets = new[] { "coglatas.semantic-model", "coglatas.project-ir" },
            analysisScope = new[] { "coglatas.source-structure" }, sourceCoverage = complete ? "coglatas.complete" : "coglatas.partial",
            executionProfile = new
            {
                purpose = "coglatas.normal", maximumOperations, maximumDeclarations = 64, maximumRelations = 64,
                maximumDigits = 128, maximumScale = 32, rounding = "coglatas.ties-to-even"
            },
            clock = new { kind = "coglatas.not-applicable" }
        }));
    }

    private static CompilerMaterializedSnapshot Materialization(ProjectSource source) => new(
        new ExternalSnapshotId(Id(1501)), "fixed-capture-v1", new RelationScopeSnapshot(source, true, true, true));

    private static SourceRevisionContext Context(int revision = 1, int tenant = 1, int project = 1, int branch = 1) =>
        SourceRevisionContext.Committed(new RevisionRef(
            new BranchRef(new TenantId(Id(2000 + tenant)), new ProjectId(Id(3000 + project)), new BranchId(Id(4000 + branch))),
            new RevisionId(Id(5000 + revision))));

    private static Guid Id(int value) => Guid.Parse($"19380000-0000-4000-8000-{value:x12}");
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ContentDigest Digest(string value) => ContentDigest.Compute(
        "coglatas.independent-symbol-cache-fixture/1", Encoding.UTF8.GetBytes(value));
}
