using Coglatas.ProjectIde.TestCli;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestEditingTests
{
    private static CompilerArtifactIdentity Evaluator => new(Json(Artifact("example.fixed-edit-test-evaluator")));
    private static (CompilerInputManifest Input, ProjectLocation Target, SourceRevisionContext Next) Fixture(Action<JsonObject>? mutate = null)
    {
        var basis = Source();
        var type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true };
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = new[] { new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1,
                defaultValue = new { type, state = "coglatas.known", value = false } } } } });
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1, displayName = "Input Test", payload = new
        { assertions = new[] { new { key = "example.ready", kind = "coglatas.assert-boolean", target = CompilerAnalysisFixtures.Reference(7, basis.Context), portId = Id(8),
            expected = new { type, state = "coglatas.known", value = true } } } } });
        var settings = Settings(); settings["evaluator"] = JsonNode.Parse(Evaluator.Data.CanonicalText);
        var committed = Input(settings, ProjectSource.Create(basis.Context, basis.Documents, Json(new JsonArray(task, test))));
        var proposal = new ProposalContext(ProposalId.Parse(Id(400)), CandidateRevisionId.Parse(Id(401)), basis.Context.CommittedRevision!, basis.Context.CommittedRevision!);
        var before = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), proposal);
        if (mutate is not null)
        {
            var root = JsonNode.Parse(before.Source.Data.CanonicalText)!.AsObject(); mutate(root);
            before = new(ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource(), before.Settings);
        }
        var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), before.Source.Context, entityId: EntityId.Parse(Id(9)));
        return (before, target, SourceRevisionContext.Candidate(new(proposal.ProposalId, CandidateRevisionId.Parse(Id(402)), proposal.BaseRevision, proposal.CapturedTargetHead)));
    }
    private static SourceJson Changed(CompilerInputManifest input, Action<JsonObject>? mutate = null)
    {
        var original = PlanDeclarationReader.Read(input.Source).Single(item => item.Data.Value.GetProperty("kind").GetString() == "coglatas.project-test");
        var copy = JsonNode.Parse(original.Data.CanonicalText)!.AsObject(); copy["payload"]!["assertions"]![0]!["expected"]!["value"] = false;
        mutate?.Invoke(copy); return Json(copy);
    }
    [Fact]
    public void ExplicitImportChangesOnlyTheLocalDraftAndInvalidatesOldRunInput()
    {
        var f = Fixture(); var model = ProjectSemanticBinder.Bind(f.Input);
        var export = ProjectTestEditing.Export(model, f.Target, f.Input.InputDigest);
        Assert.Equal(ProjectTestEditState.Ready, export.State); Assert.Equal(ValueDecodeStatus.Decoded, export.DecodeStatus);
        Assert.Equal(CompilerVerdict.Fail, ProjectTestPlan.Compile(model, Evaluator).Execute(f.Input.InputDigest).Verdict);
        var preview = ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, Changed(f.Input), f.Next);
        Assert.Equal(ProjectTestEditState.Ready, preview.State); var plan = Assert.IsType<ProjectTestEditPlan>(preview.Plan);
        Assert.Same(f.Input, plan.Before); Assert.Same(plan.After, plan.ApplyToDraft(f.Input));
        Assert.Equal(f.Next, plan.After.Source.Context); Assert.NotEqual(f.Input.InputDigest, plan.After.InputDigest);
        Assert.Equal(f.Input.Settings, plan.After.Settings); Assert.Equal(f.Input.Source.PackageLock, plan.After.Source.PackageLock);
        Assert.Null(plan.ApplyToDraft(plan.After));
        var after = ProjectSemanticBinder.Bind(plan.After);
        Assert.Equal(CompilerVerdict.Pass, ProjectTestPlan.Compile(after, Evaluator).Execute(plan.After.InputDigest).Verdict);
        Assert.Equal(CompilerVerdict.NotRun, ProjectTestPlan.Compile(after, Evaluator).Execute(f.Input.InputDigest).Verdict);
        Assert.Equal(export.Definition, ProjectTestEditing.Export(model, f.Target, f.Input.InputDigest).Definition);
        Assert.Equal(ValueDecodeStatus.Decoded, PlanDeclarationReader.Read(plan.After.Source).Single(item => item.Data.Value.GetProperty("entityId").GetString() == Id(9)).Status);
    }
    [Theory]
    [InlineData("coglatas.assert-boolean")]
    [InlineData("coglatas.assert-numeric-comparison")]
    [InlineData("coglatas.assert-collection-count")]
    [InlineData("coglatas.assert-reference-resolved")]
    public void ImportsAllInitialAssertionFamiliesThroughTheCanonicalTypedDecoder(string kind)
    {
        var f = Fixture(); var definition = Changed(f.Input, root =>
        {
            var assertion = root["payload"]!["assertions"]![0]!.AsObject(); assertion["kind"] = kind;
            if (kind != "coglatas.assert-boolean") assertion.Remove("expected");
            if (kind == "coglatas.assert-numeric-comparison")
            {
                assertion["comparison"] = "coglatas.less"; assertion["expected"] = Obj(new { type = new { kind = "coglatas.rational", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                    state = "coglatas.known", value = new { numerator = "3", denominator = "2" } });
            }
            if (kind == "coglatas.assert-collection-count") assertion["expectedCount"] = 2;
        });
        var result = ProjectTestEditing.Preview(ProjectSemanticBinder.Bind(f.Input), f.Target, f.Input.InputDigest, definition, f.Next);
        Assert.Equal(ProjectTestEditState.Ready, result.State);
        var edited = Assert.IsType<ProjectTestPlanDeclaration>(PlanDeclarationReader.Read(result.Plan!.After.Source).Single(item => item.Data.Value.GetProperty("entityId").GetString() == Id(9)).Declaration);
        Assert.Equal(kind, Assert.Single(edited.Assertions).Kind); Assert.Equal(f.Next, edited.Assertions[0].Target.Context);
    }
    [Theory]
    [InlineData("identity", ProjectTestEditState.IdentityMismatch)]
    [InlineData("document", ProjectTestEditState.InvalidDefinition)]
    [InlineData("duplicate", ProjectTestEditState.InvalidDefinition)]
    [InlineData("negative-count", ProjectTestEditState.InvalidDefinition)]
    [InlineData("unknown", ProjectTestEditState.Unsupported)]
    [InlineData("version", ProjectTestEditState.Unsupported)]
    [InlineData("future-context", ProjectTestEditState.ContextConflict)]
    public void InvalidOrForeignDefinitionCannotSupplyAnApplyPlan(string mode, ProjectTestEditState expected)
    {
        var f = Fixture(); var definition = Changed(f.Input, root =>
        {
            var assertions = root["payload"]!["assertions"]!.AsArray();
            if (mode == "identity") root["entityId"] = Id(99);
            if (mode == "document") root["documentId"] = Id(99);
            if (mode == "duplicate") assertions.Add(assertions[0]!.DeepClone());
            if (mode == "negative-count") { var assertion = assertions[0]!.AsObject(); assertion["kind"] = "coglatas.assert-collection-count"; assertion.Remove("expected"); assertion["expectedCount"] = -1; }
            if (mode == "unknown") root["future"] = "retained";
            if (mode == "version") root["schemaVersion"] = 2;
            if (mode == "future-context") assertions[0]!["target"]!["context"] = JsonNode.Parse(f.Next.Data.CanonicalText);
        });
        var result = ProjectTestEditing.Preview(ProjectSemanticBinder.Bind(f.Input), f.Target, f.Input.InputDigest, definition, f.Next);
        Assert.Equal(expected, result.State); Assert.Null(result.Plan);
    }
    [Fact]
    public void ExplicitOpaqueExportIsLosslessAndImportDoesNotDiscardFutureMeaning()
    {
        var f = Fixture(root => root["declarations"]!.AsArray().Single(node => node!["entityId"]!.GetValue<string>() == Id(9))!["future"] = "opaque");
        var model = ProjectSemanticBinder.Bind(f.Input); var export = ProjectTestEditing.Export(model, f.Target, f.Input.InputDigest);
        Assert.Equal(ProjectTestEditState.Ready, export.State); Assert.Equal(ValueDecodeStatus.Unsupported, export.DecodeStatus);
        Assert.Equal("opaque", export.Definition!.Value.GetProperty("future").GetString());
        Assert.Equal(ProjectTestEditState.Unsupported, ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, Changed(f.Input, root => root.Remove("future")), f.Next).State);
    }
    [Fact]
    public void NoChangeStaleScopeAndCancelledRequestsRemainDistinct()
    {
        var f = Fixture(); var model = ProjectSemanticBinder.Bind(f.Input); var original = ProjectTestEditing.Export(model, f.Target, f.Input.InputDigest).Definition!;
        Assert.Equal(ProjectTestEditState.NoChange, ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, original, f.Next).State);
        var stale = ContentDigest.Compute("example.stale", []);
        Assert.Equal(ProjectTestEditState.StaleInput, ProjectTestEditing.Export(model, f.Target, stale).State);
        Assert.Equal(ProjectTestEditState.StaleInput, ProjectTestEditing.Preview(model, f.Target, stale, Changed(f.Input), f.Next).State);
        var foreign = ProjectLocation.Create(f.Target.DocumentId, Source(99).Context, entityId: f.Target.EntityId);
        Assert.Equal(ProjectTestEditState.OutOfScope, ProjectTestEditing.Preview(model, foreign, f.Input.InputDigest, Changed(f.Input), f.Next).State);
        Assert.Equal(ProjectTestEditState.InvalidContext, ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, Changed(f.Input), f.Input.Source.Context).State);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectTestEditing.Export(model, f.Target, f.Input.InputDigest, cancellation.Token));
    }

    [Fact]
    public void RepairsInvalidVersionOneTestsButPreservesPinnedReferenceContexts()
    {
        var f = Fixture(root => {
            var test = root["declarations"]!.AsArray().Single(node => node!["entityId"]!.GetValue<string>() == Id(9))!;
            test["payload"]!["assertions"]!.AsArray().Add(test["payload"]!["assertions"]![0]!.DeepClone());
        });
        var definition = Changed(f.Input, root => root["payload"]!["assertions"]!.AsArray().RemoveAt(1));
        var model = ProjectSemanticBinder.Bind(f.Input);
        Assert.Equal(ValueDecodeStatus.Invalid, ProjectTestEditing.Export(model, f.Target, f.Input.InputDigest).DecodeStatus);
        var repaired = ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, definition, f.Next);
        Assert.Equal(ProjectTestEditState.Ready, repaired.State);
        var pinned = Changed(f.Input, root => {
            root["payload"]!["assertions"]!.AsArray().RemoveAt(1);
            root["payload"]!["assertions"]![0]!["target"]!["context"] = JsonNode.Parse(SourceRevisionContext.Committed(f.Input.Source.Context.Proposal!.BaseRevision).Data.CanonicalText);
        });
        var result = ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, pinned, f.Next);
        Assert.Equal(ProjectTestEditState.Ready, result.State);
        var after = Assert.IsType<ProjectTestPlanDeclaration>(PlanDeclarationReader.Read(result.Plan!.After.Source).Single(item => item.Data.Value.GetProperty("entityId").GetString() == Id(9)).Declaration);
        Assert.Equal(SourceRevisionContext.Committed(f.Input.Source.Context.Proposal!.BaseRevision), after.Assertions[0].Target.Context);
    }

    [Fact]
    public async Task DefinitionCommandsPreserveExistingFilesAndProduceAUsableCanonicalDraft()
    {
        var f = Fixture(); var directory = Path.Combine(Path.GetTempPath(), "coglatas-test-definition-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.json"); var input = Path.Combine(directory, "input.json");
        var definition = Path.Combine(directory, "import.json"); var next = Path.Combine(directory, "next.json");
        var exported = Path.Combine(directory, "export.json"); var plan = Path.Combine(directory, "plan.json");
        await File.WriteAllBytesAsync(source, f.Input.Source.Data.ToCanonicalBytes()); await File.WriteAllBytesAsync(input, f.Input.ToEnvelope().ToCanonicalBytes());
        await File.WriteAllBytesAsync(definition, Changed(f.Input).ToCanonicalBytes()); await File.WriteAllBytesAsync(next, f.Next.Data.ToCanonicalBytes());
        Assert.Equal(0, await ProjectTestDefinitionsCommand.Run(["--export-test", source, input, Id(9), exported]));
        var original = PlanDeclarationReader.Read(f.Input.Source).Single(item => item.Data.Value.GetProperty("entityId").GetString() == Id(9)).Data;
        Assert.Equal(original.ToCanonicalBytes(), await File.ReadAllBytesAsync(exported));
        Assert.Equal(0, await ProjectTestDefinitionsCommand.Run(["--preview-test-edit", source, input, Id(9), definition, next, plan]));
        var artifact = SourceJson.Parse(await File.ReadAllBytesAsync(plan)); Assert.Equal("Ready", artifact.Value.GetProperty("state").GetString());
        var afterSource = ProjectSourceCodec.Decode(SourceJson.Parse(artifact.Value.GetProperty("afterSource").GetRawText()).ToCanonicalBytes()).RequireSource();
        var after = CompilerInputManifest.ReadEnvelope(afterSource, SourceJson.Parse(artifact.Value.GetProperty("afterManifest").GetRawText()));
        Assert.Equal(f.Next, after.Source.Context); Assert.Equal(CompilerVerdict.Pass, ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(after), Evaluator).Execute(after.InputDigest).Verdict);
        foreach (var path in new[] { source, input, definition, next, exported, plan })
        {
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(3, await ProjectTestDefinitionsCommand.Run(["--preview-test-edit", source, input, Id(9), definition, next, path]));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        Assert.Equal(f.Input.Source.Data.ToCanonicalBytes(), await File.ReadAllBytesAsync(source));
        Assert.Equal(f.Input.ToEnvelope().ToCanonicalBytes(), await File.ReadAllBytesAsync(input));
    }

    [Fact]
    public async Task DefinitionCommandsDistinguishUnavailableOversizedAndMalformedImports()
    {
        var f = Fixture(); var directory = Path.Combine(Path.GetTempPath(), "coglatas-test-definition-negative-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.json"); var input = Path.Combine(directory, "input.json");
        var definition = Path.Combine(directory, "import.json"); var next = Path.Combine(directory, "next.json");
        await File.WriteAllBytesAsync(source, f.Input.Source.Data.ToCanonicalBytes()); await File.WriteAllBytesAsync(input, f.Input.ToEnvelope().ToCanonicalBytes());
        await File.WriteAllBytesAsync(next, f.Next.Data.ToCanonicalBytes());
        var absent = Path.Combine(directory, "absent.json");
        Assert.Equal(2, await ProjectTestDefinitionsCommand.Run(["--export-test", source, input, Id(99), absent]));
        Assert.Equal("Unavailable", SourceJson.Parse(await File.ReadAllBytesAsync(absent)).Value.GetProperty("state").GetString());
        foreach (var mode in new[] { "oversized", "malformed" })
        {
            await File.WriteAllBytesAsync(definition, mode == "oversized" ? new byte[new SourceCodecLimits().MaximumBytes + 1] : "{"u8.ToArray());
            var output = Path.Combine(directory, mode + ".json");
            Assert.Equal(3, await ProjectTestDefinitionsCommand.Run(["--preview-test-edit", source, input, Id(9), definition, next, output]));
            Assert.Equal("InvalidInput", SourceJson.Parse(await File.ReadAllBytesAsync(output)).Value.GetProperty("state").GetString());
        }
    }

    [Fact]
    public void RootDocumentDefinitionsRetainTheirImplicitDocumentIdentityAndRecomputeManifestDigests()
    {
        var f = Fixture(); var test = PlanDeclarationReader.Read(f.Input.Source).Single(item => item.Data.Value.GetProperty("entityId").GetString() == Id(9));
        var root = JsonNode.Parse(test.Data.CanonicalText)!.AsObject(); root.Remove("documentId");
        var document = SourceDocument.Create(DocumentId.Parse(Id(5)), "tests", Json(root));
        var declarations = JsonNode.Parse(f.Input.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Remove(declarations.Single(node => node!["entityId"]!.GetValue<string>() == Id(9)));
        var source = ProjectSource.Create(f.Input.Source.Context, f.Input.Source.Documents.Append(document), Json(declarations));
        var input = new CompilerInputManifest(source, f.Input.Settings); var target = ProjectLocation.Create(document.DocumentId, source.Context, entityId: f.Target.EntityId);
        var preview = ProjectTestEditing.Preview(ProjectSemanticBinder.Bind(input), target, input.InputDigest, Changed(input), f.Next);
        Assert.Equal(ProjectTestEditState.Ready, preview.State);
        var after = preview.Plan!.After.Source; var edited = after.Documents.Single(item => item.DocumentId == document.DocumentId);
        Assert.NotEqual(document.Digest, edited.Digest); Assert.False(edited.Root.Data.Value.TryGetProperty("documentId", out _));
        Assert.Equal(after.Data, ProjectSourceCodec.Decode(after.Data.ToCanonicalBytes()).RequireSource().Data);
    }
    [Fact]
    public void OpaqueSourceEnvelopeMembersRemainOwnedAndAnOversizedEditHasNoApplyPlan()
    {
        var f = Fixture(root => root["future/field~"] = "retained"); var model = ProjectSemanticBinder.Bind(f.Input);
        var preview = ProjectTestEditing.Preview(model, f.Target, f.Input.InputDigest, Changed(f.Input), f.Next);
        Assert.Equal(ProjectTestEditState.Ready, preview.State); Assert.Equal("retained", preview.Plan!.After.Source.Data.Value.GetProperty("future/field~").GetString());
        var bytes = f.Input.Source.Data.ToCanonicalBytes(); var limited = ProjectSourceCodec.Decode(bytes, new SourceCodecLimits(MaximumBytes: bytes.Length + 1)).RequireSource();
        var input = new CompilerInputManifest(limited, f.Input.Settings);
        var large = Changed(input, root => root["displayName"] = new string('x', 4096));
        var refused = ProjectTestEditing.Preview(ProjectSemanticBinder.Bind(input), f.Target, input.InputDigest, large, f.Next);
        Assert.Equal(ProjectTestEditState.ResourceLimit, refused.State); Assert.Null(refused.Plan);
    }

    [Fact]
    public void ExplicitImportCanForkAnExactCommittedBaseWithoutChangingThatBase()
    {
        using var item = System.Text.Json.JsonDocument.Parse(WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(text).GetProperty("key").GetString() == "tests-fail"));
        var root = item.RootElement; var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(root.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(root.GetProperty("manifest").GetRawText()));
        var target = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context, entityId: EntityId.Parse(Id(10)));
        var before = ProjectTestEditing.Export(ProjectSemanticBinder.Bind(input), target, input.InputDigest).Definition!;
        var definition = JsonNode.Parse(before.CanonicalText)!; definition["payload"]!["assertions"]![0]!["expected"]!["value"] = false;
        var basis = source.Context.CommittedRevision!; var next = SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(450)), CandidateRevisionId.Parse(Id(451)), basis, basis));
        var result = ProjectTestEditing.Preview(ProjectSemanticBinder.Bind(input), target, input.InputDigest, Json(definition), next);
        Assert.Equal(ProjectTestEditState.Ready, result.State); Assert.Same(input, result.Plan!.Before); Assert.Same(result.Plan.After, result.Plan.ApplyToDraft(input));
        Assert.Equal(source.Context, input.Source.Context); Assert.Equal(next, result.Plan.After.Source.Context);
    }

    [Fact]
    public void ConflictingNonTestIdentityIsRejectedByTheSourceCodecBeforeEditing()
    {
        Assert.Throws<FormatException>(() => Fixture(root => root["declarations"]!.AsArray()
            .Single(node => node!["entityId"]!.GetValue<string>() == Id(7))!["entityId"] = Id(9)));
    }
    [Fact]
    public void IncompleteStructuralDiscoveryCannotClaimAUniqueExportOrEditTarget()
    {
        var f = Fixture(); var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!; settings["executionProfile"]!["maximumDeclarations"] = 1;
        var input = new CompilerInputManifest(f.Input.Source, Json(settings)); var model = ProjectSemanticBinder.Bind(input);
        Assert.Equal(ProjectTestEditState.ResourceLimit, ProjectTestEditing.Export(model, f.Target, input.InputDigest).State);
        Assert.Equal(ProjectTestEditState.ResourceLimit, ProjectTestEditing.Preview(model, f.Target, input.InputDigest, Changed(input), f.Next).State);
    }
}
