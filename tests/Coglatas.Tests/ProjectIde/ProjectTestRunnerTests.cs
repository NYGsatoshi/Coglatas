using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestRunnerTests
{
    private static CompilerArtifactIdentity Evaluator => new(Json(Artifact("example.fixed-test-evaluator")));
    private static JsonObject Value(string kind, object value) => Obj(new { type = new { kind, schemaVersion = 1, nullable = true, acceptsUnknown = true }, state = "coglatas.known", value });
    private static JsonObject Assertion(SourceRevisionContext context, string kind = "coglatas.assert-boolean")
    {
        var assertion = Obj(new { key = "example.check", kind, target = CompilerAnalysisFixtures.Reference(7, context), portId = Id(8) });
        if (kind == "coglatas.assert-boolean") assertion["expected"] = Value("coglatas.boolean", true);
        if (kind == "coglatas.assert-numeric-comparison") { assertion["comparison"] = "coglatas.equal"; assertion["expected"] = Value("coglatas.decimal", "2"); }
        if (kind == "coglatas.assert-collection-count") assertion["expectedCount"] = 2;
        return assertion;
    }
    private static CompilerInputManifest Fixture(JsonObject? value = null, Action<JsonObject, JsonArray, JsonObject>? mutate = null, JsonObject? settings = null)
    {
        var original = Source(); value ??= Value("coglatas.boolean", true);
        var port = Obj(new { portId = Id(8), direction = "coglatas.input", type = value["type"]!.DeepClone(), minimumConnections = 0, maximumConnections = 1, defaultValue = value });
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" }, ports = new JsonArray(port)
        } });
        var assertions = new JsonArray(Assertion(original.Context));
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1, displayName = "Input contract", payload = new { assertions } });
        mutate?.Invoke(task["payload"]!["ports"]![0]!.AsObject(), test["payload"]!["assertions"]!.AsArray(), test);
        settings ??= Settings(); settings["evaluator"] = JsonNode.Parse(Evaluator.Data.CanonicalText);
        return Input(settings, ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task, test))));
    }
    private static ProjectTestRun Run(CompilerInputManifest input) => ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator).Execute(input.InputDigest);

    [Fact]
    public void Typed_source_roundtrips_and_compiles_without_executing_tests()
    {
        var input = Fixture();
        var decoded = ProjectSourceCodec.Decode(ProjectSourceCodec.Encode(input.Source)).RequireSource();
        Assert.Equal(input.Source.Digest, decoded.Digest);
        var declaration = Assert.IsType<ProjectTestPlanDeclaration>(PlanDeclarationReader.Read(input.Source).Single(item => item.Data.Value.GetProperty("kind").GetString() == "coglatas.project-test").Declaration);
        Assert.Single(declaration.Assertions);
        var compilation = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(CompilerVerdict.Pass, compilation.Result.Verdict);
        Assert.Contains(compilation.Validation!.Ir!.Nodes, node => node.Kind == "coglatas.project-test");
        Assert.Equal(CompilerVerdict.Pass, Run(input).Verdict);
    }

    [Theory]
    [InlineData(true, CompilerVerdict.Pass)]
    [InlineData(false, CompilerVerdict.Fail)]
    public void Boolean_assertions_retain_exact_input_locations_and_counterexample_basis(bool actual, CompilerVerdict expected)
    {
        var input = Fixture(Value("coglatas.boolean", actual)); var result = Run(input);
        Assert.Equal(expected, result.Verdict); Assert.Equal(CompilerCoverage.Complete, result.Coverage);
        Assert.Equal(1, result.DiscoveredTests); Assert.Equal(1, result.ExecutedAssertions);
        Assert.Equal(CompilerEvidenceKind.ModelProof, result.EvidenceKind);
        var assertion = Assert.Single(Assert.Single(result.Tests).Assertions);
        Assert.Equal(input.Source.Context, assertion.Location.Context);
        Assert.Equal("/payload/assertions/example.check", assertion.Location.PropertyPath);
        Assert.True(assertion.Data.Value.TryGetProperty("actualDigest", out _));
        Assert.Equal(result.Digest, Run(input).Digest);
    }

    [Theory]
    [InlineData("coglatas.unknown", "coglatas.unknown-actual")]
    [InlineData("coglatas.null", "coglatas.null-actual")]
    public void Unknown_and_null_never_satisfy_a_known_expectation(string state, string reason)
    {
        var value = Value("coglatas.boolean", true); value["state"] = state; value.Remove("value");
        if (state == "coglatas.unknown") { value["reason"] = "example.missing"; value["basis"] = new JsonObject(); }
        var result = Run(Fixture(value));
        Assert.Equal(CompilerVerdict.Inconclusive, result.Verdict);
        Assert.Equal(reason, result.Tests[0].Assertions[0].Reason);
    }

    [Theory]
    [InlineData("coglatas.equal", "2", CompilerVerdict.Pass)]
    [InlineData("coglatas.less", "3", CompilerVerdict.Pass)]
    [InlineData("coglatas.greater", "3", CompilerVerdict.Fail)]
    public void Numeric_assertions_use_shared_exact_rational_comparison(string comparison, string expected, CompilerVerdict verdict)
    {
        var input = Fixture(Value("coglatas.rational", new { numerator = "2", denominator = "1" }), (_, assertions, _) =>
        {
            var assertion = Assertion(Source().Context, "coglatas.assert-numeric-comparison");
            assertion["comparison"] = comparison; assertion["expected"] = Value("coglatas.decimal", expected);
            assertions[0] = assertion;
        });
        Assert.Equal(verdict, Run(input).Verdict);
    }

    [Theory]
    [InlineData(2, CompilerVerdict.Pass)]
    [InlineData(0, CompilerVerdict.Fail)]
    public void Collection_count_uses_the_decoded_collection_without_zero_filling(int expected, CompilerVerdict verdict)
    {
        var element = Value("coglatas.boolean", true);
        var value = Obj(new { type = new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = true, elementType = element["type"]!.DeepClone() },
            state = "coglatas.known", value = new JsonArray(element, Value("coglatas.boolean", false)) });
        var input = Fixture(value, (_, assertions, _) => { var assertion = Assertion(Source().Context, "coglatas.assert-collection-count"); assertion["expectedCount"] = expected; assertions[0] = assertion; });
        Assert.Equal(verdict, Run(input).Verdict);
    }

    [Fact]
    public void Missing_default_and_foreign_fixture_target_are_not_executed()
    {
        foreach (var foreign in new[] { false, true })
        {
            var input = Fixture(mutate: (port, assertions, _) => { if (foreign) assertions[0]!["target"] = CompilerAnalysisFixtures.Reference(7, Source(99).Context); else port.Remove("defaultValue"); });
            var result = Run(input);
            Assert.Equal(CompilerVerdict.NotRun, result.Verdict); Assert.Equal(0, result.ExecutedAssertions);
            Assert.Equal(foreign ? "coglatas.fixture-target-unavailable" : "coglatas.fixture-default-missing", result.Tests[0].Assertions[0].Reason);
        }
    }

    [Fact]
    public void Unknown_ast_and_invalid_expectation_are_preserved_and_distinguished()
    {
        foreach (var unsupported in new[] { true, false })
        {
            var input = Fixture(mutate: (_, assertions, _) => { if (unsupported) assertions[0]!["kind"] = "example.future"; else assertions[0]!["expected"] = Value("coglatas.decimal", "1"); });
            var result = Run(input);
            Assert.Equal(unsupported ? CompilerVerdict.NotRun : CompilerVerdict.Error, result.Verdict);
            Assert.Equal(unsupported ? "coglatas.unsupported-test" : "coglatas.invalid-test", result.Tests[0].Reason);
            Assert.Equal(input.Source.Data, ProjectSourceCodec.Decode(ProjectSourceCodec.Encode(input.Source)).RequireSource().Data);
        }
    }

    [Fact]
    public void Empty_discovery_and_empty_test_do_not_pass()
    {
        var noTests = Fixture(mutate: (_, _, test) => test["kind"] = "example.not-a-test"); var result = Run(noTests);
        Assert.Equal(0, result.DiscoveredTests); Assert.Equal("coglatas.zero-tests", result.Reason); Assert.Equal(CompilerVerdict.NotRun, result.Verdict);
        var empty = Run(Fixture(mutate: (_, assertions, _) => assertions.Clear()));
        Assert.Equal(1, empty.DiscoveredTests); Assert.Equal(CompilerVerdict.NotRun, empty.Verdict);
        Assert.Equal("coglatas.zero-assertions", empty.Tests[0].Reason);
    }

    [Fact]
    public void Stale_cancellation_and_logical_budget_do_not_execute_or_pass()
    {
        var input = Fixture(); var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator);
        var stale = plan.Execute(Input(source: Source(99)).InputDigest);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness); Assert.Equal(0, stale.ExecutedAssertions);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Equal(CompilerExecutionState.Cancelled, plan.Execute(input.InputDigest, cancel.Token).Execution);
        var bounded = Fixture(mutate: (_, assertions, _) => { var second = Assertion(Source().Context); second["key"] = "example.second"; assertions.Add(second); });
        var exhausted = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(bounded), Evaluator, new(MaximumAssertions: 1)).Execute(bounded.InputDigest);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, exhausted.Execution); Assert.Equal(0, exhausted.ExecutedAssertions);
    }

    [Fact]
    public void Partial_source_does_not_become_a_complete_suite_pass()
    {
        var settings = Settings(); settings["sourceCoverage"] = "coglatas.partial";
        var result = Run(Fixture(settings: settings));
        Assert.Equal(CompilerVerdict.Pass, result.Tests[0].Assertions[0].Verdict);
        Assert.Equal(CompilerVerdict.Inconclusive, result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Coverage);
    }

    [Fact]
    public void Shared_binding_exhaustion_is_not_misreported_as_zero_discovery()
    {
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 1;
        var result = Run(Fixture(settings: settings));
        Assert.Equal(1, result.DiscoveredTests); Assert.Equal(0, result.ExecutedAssertions);
        Assert.Equal("coglatas.test-discovery-unavailable", result.Reason);
    }


    [Theory]
    [InlineData("resolved", CompilerVerdict.Pass)]
    [InlineData("missing", CompilerVerdict.Fail)]
    [InlineData("denied", CompilerVerdict.Inconclusive)]
    [InlineData("unavailable", CompilerVerdict.Inconclusive)]
    public void Reference_assertion_consumes_shared_pinned_binding_without_inventing_target_authority(string mode, CompilerVerdict verdict)
    {
        var original = Source();
        var targetContext = SourceRevisionContext.Committed(new(new(original.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var target = ProjectSource.Create(targetContext, original.Documents);
        var referenceType = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.project" });
        var value = Obj(new { type = referenceType, state = "coglatas.known", value = new
        {
            tenantId = target.TenantId.ToString(), projectId = target.ProjectId.ToString(), context = target.Context.Data.Value,
            entityId = Id(mode == "missing" ? 99 : 6),
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), original.Context, entityId: EntityId.Parse(Id(7))).Data.Value,
            manifest = new { version = "1", digest = target.Digest.Data.Value }
        } });
        var settings = Settings(); settings["authorizedExternalSnapshots"]!.AsArray().Add(Obj(new
        {
            snapshotId = Id(30), version = "1", digest = target.Digest.Data.Value, context = target.Context.Data.Value,
            coverage = "coglatas.complete", asOf = (string?)null
        }));
        var input = Fixture(value, (_, assertions, _) => assertions[0] = Assertion(original.Context, "coglatas.assert-reference-resolved"), settings);
        var snapshot = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(30)), "1", new(target, mode != "denied", true, true));
        var model = ProjectSemanticBinder.Bind(input, mode == "unavailable" ? [] : [snapshot]);
        var result = ProjectTestPlan.Compile(model, Evaluator).Execute(input.InputDigest);
        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(verdict, result.Tests[0].Assertions[0].Verdict);
        var unavailable = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator);
        if (mode == "resolved") Assert.NotEqual(unavailable.Digest, ProjectTestPlan.Compile(model, Evaluator).Digest);
    }


    [Fact]
    public void Draft_fork_and_rename_preserve_test_identity_and_rebase_only_local_fixture_targets()
    {
        var input = Fixture(); var basis = input.Source.Context.CommittedRevision!;
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(input), new(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(51)), basis, basis));
        Assert.Equal(CompilerVerdict.Pass, Run(draft).Verdict);
        var model = ProjectSemanticBinder.Bind(draft);
        var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), draft.Source.Context, entityId: EntityId.Parse(Id(7)));
        var next = SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(52)), basis, basis));
        var preview = ProjectDraftRename.Preview(model, target, draft.InputDigest, "Renamed fixture", next);
        Assert.Equal(DraftRenameState.Ready, preview.State);
        var after = preview.Plan!.After;
        Assert.Equal(CompilerVerdict.Pass, Run(after).Verdict);
        var test = Assert.IsType<ProjectTestPlanDeclaration>(ProjectSemanticBinder.Bind(after).Declarations.Single(item => item.Declaration is ProjectTestPlanDeclaration).Declaration);
        Assert.Equal(EntityId.Parse(Id(9)), test.EntityId);
        Assert.Equal(after.Source.Context, test.Assertions[0].Target.Context);
        var diff = ProjectSemanticDiff.Compare(model, ProjectSemanticBinder.Bind(after), draft.InputDigest, after.InputDigest);
        Assert.DoesNotContain(diff.Changes, change => change.Identity == Id(9));
    }


    [Fact]
    public void Changed_evaluator_binding_is_not_run_even_when_the_source_input_matches()
    {
        var input = Fixture(); var other = new CompilerArtifactIdentity(Json(Artifact("example.other-evaluator")));
        var result = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), other).Execute(input.InputDigest);
        Assert.Equal(CompilerVerdict.NotRun, result.Verdict); Assert.Equal(0, result.ExecutedAssertions);
        Assert.Equal("coglatas.evaluator-identity-mismatch", result.Reason);
    }


    [Fact]
    public void Known_failure_is_retained_when_another_test_is_unsupported()
    {
        var input = Fixture(Value("coglatas.boolean", false));
        var declarations = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        var unsupported = declarations.Single(item => item!["kind"]!.GetValue<string>() == "coglatas.project-test")!.DeepClone();
        unsupported["entityId"] = Id(10); unsupported["payload"]!["assertions"]![0]!["kind"] = "example.future";
        declarations.Add(unsupported);
        var mixed = new CompilerInputManifest(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations)), input.Settings);
        var result = Run(mixed);
        Assert.Equal(CompilerVerdict.Fail, result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Coverage);
        Assert.Equal(2, result.DiscoveredTests); Assert.Equal(1, result.ExecutedAssertions);
        Assert.Contains(result.Tests, test => test.Reason == "coglatas.unsupported-test");
    }


    [Fact]
    public void Unresolved_test_targets_reserve_the_next_draft_context()
    {
        var basis = Source().Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(51)), basis, basis);
        var next = SourceRevisionContext.Candidate(proposal);
        var input = Fixture(mutate: (_, assertions, _) => assertions[0]!["target"] = CompilerAnalysisFixtures.Reference(7, next));
        Assert.Throws<ArgumentException>(() => ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(input), proposal));
    }

    [Theory]
    [InlineData("unknown-member", CompilerVerdict.NotRun)]
    [InlineData("duplicate-key", CompilerVerdict.Error)]
    [InlineData("assertion-bound", CompilerVerdict.Error)]
    public void Unsupported_members_and_invalid_bounded_ast_never_execute(string mode, CompilerVerdict verdict)
    {
        var input = Fixture(mutate: (_, assertions, _) =>
        {
            if (mode == "unknown-member") assertions[0]!["example.future"] = true;
            else if (mode == "duplicate-key") assertions.Add(assertions[0]!.DeepClone());
            else for (var index = 1; index < 65; index++) { var item = assertions[0]!.DeepClone(); item["key"] = "example.check" + index; assertions.Add(item); }
        });
        var result = Run(input);
        Assert.Equal(verdict, result.Verdict); Assert.Equal(0, result.ExecutedAssertions);
    }


    [Fact]
    public void Assertion_project_identity_must_agree_with_its_exact_context()
    {
        var input = Fixture(mutate: (_, assertions, _) => assertions[0]!["target"]!["projectId"] = Id(99));
        var model = ProjectSemanticBinder.Bind(input);
        Assert.Equal(ValueDecodeStatus.Invalid, model.Declarations.Single(item => item.Data.Value.GetProperty("kind").GetString() == "coglatas.project-test").Status);
        Assert.DoesNotContain(model.References, reference => reference.Origin.PropertyPath.StartsWith("/payload/assertions/", StringComparison.Ordinal));
        var result = ProjectTestPlan.Compile(model, Evaluator).Execute(input.InputDigest);
        Assert.Equal(CompilerVerdict.Error, result.Verdict); Assert.Equal(0, result.ExecutedAssertions);
    }

    [Theory]
    [InlineData("2", CompilerVerdict.Inconclusive)]
    [InlineData("3", CompilerVerdict.Fail)]
    public void Multiple_assertions_share_the_remaining_run_budget_and_retain_known_failures(string actual, CompilerVerdict verdict)
    {
        var original = Fixture(Value("coglatas.decimal", actual), (_, assertions, _) =>
        {
            assertions[0] = Assertion(Source().Context, "coglatas.assert-numeric-comparison");
            var second = assertions[0]!.DeepClone(); second["key"] = "example.second"; assertions.Add(second);
        });
        var initial = ProjectSemanticBinder.Bind(original);
        Assert.True(initial.BindingComplete);
        var settings = JsonNode.Parse(original.Settings.CanonicalText)!;
        settings["executionProfile"]!["maximumOperations"] = initial.LogicalOperationsConsumed + 1;
        var input = new CompilerInputManifest(original.Source, Json(settings));
        var model = ProjectSemanticBinder.Bind(input);
        Assert.True(model.BindingComplete); Assert.Equal(1, input.ExecutionProfile.MaximumOperations - model.LogicalOperationsConsumed);
        var result = ProjectTestPlan.Compile(model, Evaluator).Execute(input.InputDigest);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Execution);
        Assert.Equal(CompilerCoverage.Partial, result.Coverage); Assert.Equal(verdict, result.Verdict);
        Assert.Equal(1, result.ExecutedAssertions); Assert.Equal("coglatas.test-operation-budget-exhausted", result.Reason);
    }

    [Fact]
    public void Wrong_actual_type_is_an_error_and_concurrent_runs_are_deterministic()
    {
        var input = Fixture(Value("coglatas.text", "not Boolean")); var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator);
        var results = Enumerable.Range(0, 8).AsParallel().Select(_ => plan.Execute(input.InputDigest)).ToArray();
        Assert.All(results, result => Assert.Equal(CompilerVerdict.Error, result.Verdict));
        Assert.Single(results.Select(item => item.Digest).Distinct());
    }
}
