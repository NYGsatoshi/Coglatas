using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SemanticDiffTests
{
    internal static ProjectSemanticDiff Compare(CompilerInputManifest before, CompilerInputManifest after, int maximum = 256) =>
        ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(before), ProjectSemanticBinder.Bind(after), before.InputDigest, after.InputDigest, maximum);

    [Fact]
    public void RenameKeepsIdentityDirectionAndExactPairWithoutRebasingNoise()
    {
        var before = DraftRenameTests.Draft();
        var plan = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(before), DraftRenameTests.Target(before), before.InputDigest, "Changed", DraftRenameTests.Next(before)).Plan!;
        var diff = Compare(before, plan.After); Assert.Equal(SemanticDiffState.Complete, diff.State); Assert.False(diff.NoChanges);
        Assert.Equal(before.InputDigest, diff.BeforeInputDigest); Assert.Equal(plan.After.InputDigest, diff.AfterInputDigest);
        var change = Assert.Single(diff.Changes); Assert.Equal(SemanticChangeKind.DisplayName, change.Kind);
        Assert.Equal(Id(10), change.Identity); Assert.Equal("/displayName", change.Path);
        Assert.Equal("Task 0", change.Before!.Value.GetString()); Assert.Equal("Changed", change.After!.Value.GetString());
        Assert.Equal(before.Source.Context, change.BeforeLocation!.Context); Assert.Equal(plan.After.Source.Context, change.AfterLocation!.Context);
        var reverse = Assert.Single(Compare(plan.After, before).Changes); Assert.Equal(change.Before, reverse.After); Assert.Equal(change.After, reverse.Before);
    }

    [Fact]
    public void ANewContextAloneDoesNotChangeLocalReferencesButFuturePinnedReferencesNeverAliasLocal()
    {
        var before = DraftRenameTests.Committed();
        var after = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(before), DraftRenameTests.Proposal(before));
        var diff = Compare(before, after); Assert.True(diff.NoChanges); Assert.Empty(diff.Changes);
        var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Single(item => item!["entityId"]!.GetValue<string>() == Id(30))!["payload"]!["target"]!["context"] = JsonNode.Parse(after.Source.Context.Data.CanonicalText);
        var pinned = With(before, declarations);
        var changed = Compare(pinned, after); Assert.False(changed.NoChanges);
        Assert.Contains(changed.Changes, item => item.Identity == Id(30) && item.Kind == SemanticChangeKind.Reference);
    }

    [Fact]
    public void EqualNamesWithNewIdsAreRemovalAndAdditionWithoutRenameGuessing()
    {
        var before = DraftRenameTests.Draft(); var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Single(item => item!["entityId"]!.GetValue<string>() == Id(11))!["entityId"] = Id(99);
        var changes = Compare(before, With(before, declarations)).Changes;
        Assert.Contains(changes, item => item.Identity == Id(11) && item.Kind == SemanticChangeKind.Removed);
        Assert.Contains(changes, item => item.Identity == Id(99) && item.Kind == SemanticChangeKind.Added);
        Assert.DoesNotContain(changes, item => item.Kind == SemanticChangeKind.DisplayName);
    }

    [Fact]
    public void TypedPropertyAndRelationEndpointChangesHaveDistinctCategories()
    {
        var before = DraftRenameTests.Draft(); var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Single(item => item!["entityId"]!.GetValue<string>() == Id(10))!["payload"]!["duration"]!["value"] = "200";
        var relations = JsonNode.Parse(before.Source.Relations.CanonicalText)!.AsArray();
        relations[0]!["target"]!["reference"]!["entityId"] = Id(10);
        var diff = Compare(before, With(before, declarations, relations));
        Assert.Contains(diff.Changes, item => item.Identity == Id(10) && item.Kind == SemanticChangeKind.Property && item.Path == "/payload/duration/value");
        Assert.Contains(diff.Changes, item => item.Identity == Id(20) && item.Kind == SemanticChangeKind.Reference && item.Path == "/target/reference/entityId");
    }

    [Fact]
    public void RelationIdentityReplacementIsNotAnEndpointEdit()
    {
        var before = DraftRenameTests.Draft(); var relations = JsonNode.Parse(before.Source.Relations.CanonicalText)!.AsArray();
        relations[0]!["relationId"] = Id(21);
        var diff = Compare(before, With(before, relations: relations));
        Assert.Contains(diff.Changes, item => item.RecordType == "relation" && item.Identity == Id(20) && item.Kind == SemanticChangeKind.Removed);
        Assert.Contains(diff.Changes, item => item.RecordType == "relation" && item.Identity == Id(21) && item.Kind == SemanticChangeKind.Added);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("unsupported")]
    [InlineData("missing-reference")]
    public void IncompleteOrUnsupportedInputsNeverBecomeNoChanges(string mode)
    {
        var before = DraftRenameTests.Draft(); CompilerInputManifest input;
        if (mode == "partial") { var settings = Settings(); settings["sourceCoverage"] = "coglatas.partial"; input = Input(settings, before.Source); }
        else
        {
            var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
            var requirement = declarations.Single(item => item!["entityId"]!.GetValue<string>() == Id(30))!;
            if (mode == "unsupported") requirement["payload"]!["future"] = true;
            else requirement["payload"]!["target"]!["entityId"] = Id(99);
            input = With(before, declarations);
        }
        var diff = Compare(input, input); Assert.Equal(SemanticDiffState.Incomplete, diff.State); Assert.False(diff.NoChanges);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void MissingStaleAndForeignPairsExposeNoRowsOrContext()
    {
        var input = DraftRenameTests.Draft(); var model = ProjectSemanticBinder.Bind(input);
        var missing = ProjectSemanticDiff.Compare(null, model, input.InputDigest, input.InputDigest);
        Assert.Equal(SemanticDiffState.Unavailable, missing.State); Assert.Null(missing.AfterContext); Assert.False(missing.NoChanges);
        var stale = ProjectSemanticDiff.Compare(model, model, DraftRenameTests.Committed().InputDigest, input.InputDigest);
        Assert.Equal(SemanticDiffState.StaleInput, stale.State); Assert.Empty(stale.Changes); Assert.Null(stale.BeforeInputDigest);
        var otherContext = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(99)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(4))));
        var other = Input(source: ProjectSource.Create(otherContext, Source().Documents));
        var foreign = Compare(input, other); Assert.Equal(SemanticDiffState.OutOfScope, foreign.State); Assert.Null(foreign.AfterContext);
    }

    [Fact]
    public void OutputBoundsAreAtomicAndCancellationNeverReturnsAnEmptySuccess()
    {
        var before = DraftRenameTests.Draft(); var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
        foreach (var declaration in declarations) declaration!["displayName"] = "Changed";
        var limited = Compare(before, With(before, declarations), 1);
        Assert.Equal(SemanticDiffState.ResourceLimit, limited.State); Assert.Empty(limited.Changes); Assert.False(limited.NoChanges);
        var model = ProjectSemanticBinder.Bind(before); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticDiff.Compare(model, model, before.InputDigest, before.InputDigest, cancellationToken: cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectSemanticDiff.Compare(model, model, before.InputDigest, before.InputDigest, 0));
    }

    [Fact]
    public void AbsentAndExplicitNullRemainDifferentAndReturnedRowsCannotBeMutated()
    {
        var before = DraftRenameTests.Draft(); var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
        var requirement = declarations.Single(item => item!["entityId"]!.GetValue<string>() == Id(30))!;
        requirement["displayName"] = "";
        var diff = Compare(before, With(before, declarations));
        var change = Assert.Single(diff.Changes); Assert.Null(change.Before); Assert.Equal("", change.After!.Value.GetString());
        Assert.Throws<NotSupportedException>(() => ((IList<SemanticChange>)diff.Changes).Clear());
        Assert.False(diff.Changes is System.Collections.ICollection);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("list")]
    [InlineData("sequence")]
    [InlineData("set")]
    [InlineData("map")]
    [InlineData("optional")]
    [InlineData("nullable")]
    [InlineData("structure")]
    public void PinnedValueTargetsStayExactWhileDeclaringOriginsAdvance(string shape)
    {
        var before = DraftRenameTests.Committed(); var external = Source(9);
        var declarations = JsonNode.Parse(before.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        port["type"] = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = "coglatas.project" });
        port["defaultValue"] = Obj(new { type = port["type"]!.DeepClone(), state = "coglatas.known", value = new
        {
            tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = external.Context.Data.Value,
            origin = DraftRenameTests.Target(before).Data.Value, manifest = new { version = "1", digest = external.Digest.Data.Value }
        } });
        if (shape != "reference")
        {
            var childType = port["type"]!.DeepClone(); var childValue = port["defaultValue"]!.DeepClone();
            var type = Obj(new { kind = "coglatas." + shape, schemaVersion = 1, nullable = shape == "nullable", acceptsUnknown = false });
            JsonNode value;
            if (shape == "structure")
            {
                type["fields"] = new JsonArray(Obj(new { name = "a/b~", type = childType, required = true }));
                value = new JsonObject { ["a/b~"] = childValue };
            }
            else if (shape == "map")
            {
                type["keyType"] = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = false });
                type["valueType"] = childType; type["identity"] = "coglatas.canonical-value";
                value = new JsonArray(Obj(new { key = new { type = type["keyType"]!.DeepClone(), state = "coglatas.known", value = "entry" }, value = childValue }));
            }
            else
            {
                type["elementType"] = childType;
                if (shape == "set") type["identity"] = "coglatas.canonical-value";
                value = shape == "optional" ? Obj(new { present = true, value = childValue }) :
                    shape == "nullable" ? childValue : new JsonArray(childValue);
            }
            port["type"] = type.DeepClone(); port["defaultValue"] = Obj(new { type, state = "coglatas.known", value });
        }
        var settings = Settings(); settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(60), version = "1",
            context = external.Context.Data.Value, digest = external.Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        before = Input(settings, ProjectSource.Create(before.Source.Context, before.Source.Documents, Json(declarations), before.Source.Relations));
        var supplied = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(60)), "1", new(external, true, true, true));
        var model = ProjectSemanticBinder.Bind(before, [supplied]); Assert.True(model.BindingComplete);
        var after = ProjectDraftRename.Fork(model, DraftRenameTests.Proposal(before));
        var diff = ProjectSemanticDiff.Compare(model, ProjectSemanticBinder.Bind(after, [supplied]), before.InputDigest, after.InputDigest);
        Assert.True(diff.NoChanges);
        var changed = JsonNode.Parse(after.Source.Declarations.CanonicalText)!.AsArray();
        var leaf = changed[0]!["payload"]!["ports"]![0]!["defaultValue"]!;
        leaf = shape switch
        {
            "structure" => leaf["value"]!["a/b~"]!, "map" => leaf["value"]![0]!["value"]!,
            "optional" => leaf["value"]!["value"]!, "nullable" => leaf["value"]!,
            "list" or "sequence" or "set" => leaf["value"]![0]!, _ => leaf
        };
        leaf["value"]!["context"] = JsonNode.Parse(after.Source.Context.Data.CanonicalText);
        var retargeted = With(after, changed); var targetDiff = Compare(after, retargeted);
        Assert.Contains(targetDiff.Changes, item => item.Identity == Id(10) && item.Kind == SemanticChangeKind.Reference);
    }

    [Fact]
    public void DocumentRenameIsMetadataAndDoesNotRenameAnEntity()
    {
        var before = DraftRenameTests.Draft(); var document = before.Source.Documents[0];
        var after = Input(source: ProjectSource.Create(before.Source.Context,
            [SourceDocument.Create(document.DocumentId, "Changed document", document.Root.Data)], before.Source.Declarations, before.Source.Relations,
            sourceReferences: before.Source.SourceReferences));
        var diff = Compare(before, after); var change = Assert.Single(diff.Changes);
        Assert.Equal("document", change.RecordType); Assert.Equal(SemanticChangeKind.Property, change.Kind);
        Assert.Equal("/logicalName", change.Path); Assert.Equal(document.DocumentId.ToString(), change.Identity);
    }

    [Fact]
    public void OpaqueChangesArePreservedWithoutInterpretingThemAsLayout()
    {
        var before = DraftRenameTests.Draft(); var after = Input(source: ProjectSource.Create(before.Source.Context, before.Source.Documents,
            before.Source.Declarations, before.Source.Relations, Json(System.Text.Json.JsonSerializer.SerializeToNode(new[] { new { @namespace = "example.layout", x = 20 } })!),
            before.Source.PackageLock, before.Source.SourceReferences));
        var diff = Compare(before, after); Assert.Equal(SemanticDiffState.Incomplete, diff.State); Assert.False(diff.NoChanges);
        var change = Assert.Single(diff.Changes); Assert.Equal(SemanticChangeKind.Uninterpreted, change.Kind);
        Assert.Equal(after.Source.ExtensionDeclarations, change.After);
    }

    [Fact]
    public async Task WindowsPairPreparationClearsOnLifecycleChangeAndRefusesReentrantPublication()
    {
        var input = DraftRenameTests.Draft();
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var host = new LocalNetCompilerHost(profile); var request = new CompilerHostRequest(new(input), 1);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(await host.CompileAsync(request)));
        var model = ProjectSemanticBinder.Bind(input); Assert.True(view.CompareSynthetic(model, input.InputDigest)!.NoChanges);
        view.BeginSynthetic(new(new(input), 2)); Assert.Null(view.SemanticDiff);
        Assert.Null(view.CompareSynthetic(model, input.InputDigest));
        Assert.True(view.TryAccept(await host.CompileAsync(new(new(input), 2))));
        view.PropertyChanged += (_, _) => { if (view.SemanticDiff is not null) view.Reset(); };
        Assert.Null(view.CompareSynthetic(model, input.InputDigest)); Assert.Null(view.SemanticDiff); Assert.Equal("Empty", view.State);
    }

    [Fact]
    public void PreparedWebPairsReproduceCoreComparisonExactly()
    {
        var cases = WebPreviewContractTests.Cases().Select(item => SourceJson.Parse((string)item[0]).Value).ToArray();
        var count = 0;
        foreach (var before in cases)
        {
            if (!before.TryGetProperty("preparedDiff", out var prepared) || prepared.ValueKind == System.Text.Json.JsonValueKind.Null) continue;
            var after = cases.Single(item => item.GetProperty("key").GetString() == prepared.GetProperty("afterCaseKey").GetString());
            CompilerInputManifest Materialize(System.Text.Json.JsonElement item)
            {
                var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
                return CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
            }
            var diff = Compare(Materialize(before), Materialize(after));
            Assert.Equal(diff.Data, SourceJson.Parse(prepared.GetProperty("comparison").GetRawText())); count++;
        }
        Assert.Equal(3, count);
    }

    internal static CompilerInputManifest With(CompilerInputManifest input, JsonArray? declarations = null, JsonArray? relations = null) =>
        Input(JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(), ProjectSource.Create(input.Source.Context, input.Source.Documents,
            declarations is null ? input.Source.Declarations : Json(declarations), relations is null ? input.Source.Relations : Json(relations),
            input.Source.ExtensionDeclarations, input.Source.PackageLock, input.Source.SourceReferences));
}
