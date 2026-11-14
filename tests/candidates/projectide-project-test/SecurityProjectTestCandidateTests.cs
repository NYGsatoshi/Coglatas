using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>
/// Security-owned, synthetic typed Project Test candidate for P0 #1945.
/// These local facts never authorize runtime operations or server-side Merge.
/// </summary>
public sealed class SecurityProjectTestCandidateTests
{
    private static CompilerArtifactIdentity Evaluator =>
        new(Json(Artifact("example.security-owned-fixed-project-test-evaluator")));

    private static JsonObject Known(string kind, object value) => Obj(new
    {
        type = new { kind, schemaVersion = 1, nullable = true, acceptsUnknown = true },
        state = "coglatas.known", value
    });

    private static JsonObject Target(SourceRevisionContext context) => Obj(new
    {
        tenantId = context.Branch.TenantId.ToString(),
        projectId = context.Branch.ProjectId.ToString(),
        entityId = Id(7),
        context = context.Data.Value
    });

    private static JsonObject Check(SourceRevisionContext context, string kind = "coglatas.assert-boolean")
    {
        var item = Obj(new { key = "example.independent-check", kind, target = Target(context), portId = Id(8) });
        if (kind == "coglatas.assert-boolean") item["expected"] = Known("coglatas.boolean", true);
        if (kind == "coglatas.assert-numeric-comparison")
        {
            item["comparison"] = "coglatas.equal";
            item["expected"] = Known("coglatas.decimal", "2");
        }
        if (kind == "coglatas.assert-collection-count") item["expectedCount"] = 2;
        return item;
    }

    private static CompilerInputManifest Sample(
        JsonObject? actual = null, Action<JsonObject, JsonArray, JsonObject>? change = null,
        JsonObject? settings = null)
    {
        var source = Source();
        actual ??= Known("coglatas.boolean", true);
        var port = Obj(new
        {
            portId = Id(8), direction = "coglatas.input",
            type = actual["type"]!.DeepClone(),
            minimumConnections = 0, maximumConnections = 1,
            defaultValue = actual
        });
        var task = Obj(new
        {
            entityId = Id(7), documentId = Id(4),
            kind = "coglatas.task", schemaVersion = 1,
            payload = new
            {
                duration = new
                {
                    type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                    state = "coglatas.known", value = "1"
                },
                ports = new JsonArray(port)
            }
        });
        var assertions = new JsonArray(Check(source.Context));
        var test = Obj(new
        {
            entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test",
            schemaVersion = 1, displayName = "Security test fixture",
            payload = new { assertions }
        });
        change?.Invoke(task["payload"]!["ports"]![0]!.AsObject(),
            test["payload"]!["assertions"]!.AsArray(), test);
        settings ??= Settings();
        settings["evaluator"] = JsonNode.Parse(Evaluator.Data.CanonicalText);
        return Input(settings, ProjectSource.Create(source.Context, source.Documents,
            Json(new JsonArray(task, test))));
    }

    private static ProjectTestRun Execute(CompilerInputManifest input)
        => ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator)
            .Execute(input.InputDigest);

    private static void RejectMutableArray<T>(IReadOnlyList<T> value)
    {
        var before = value.ToArray();
        Assert.False(value is T[]);
        if (value is ICollection legacy)
            Assert.False(legacy.SyncRoot is T[], "Public SyncRoot must not disclose a writable owned array.");
        if (value is ICollection<T> generic)
        {
            Assert.Throws<NotSupportedException>(() => generic.Clear());
            Assert.Throws<NotSupportedException>(() => generic.Add(default!));
        }
        if (value is IList<T> indexed && indexed.Count > 0)
            Assert.Throws<NotSupportedException>(() => indexed[0] = default!);
        Assert.Equal(before, value.ToArray());
    }

    [Fact]
    public void Known_boolean_counterexample_is_not_hidden_by_compiler_pass()
    {
        var passed = Sample();
        var failed = Sample(Known("coglatas.boolean", false));
        Assert.Equal(CompilerVerdict.Pass, Execute(passed).Verdict);
        var result = Execute(failed);
        Assert.Equal(CompilerVerdict.Fail, result.Verdict);
        Assert.Equal(CompilerCoverage.Complete, result.Coverage);
        Assert.Equal(CompilerEvidenceKind.ModelProof, result.EvidenceKind);
        Assert.Equal(1, result.DiscoveredTests);
        Assert.Equal(1, result.ExecutedAssertions);
        Assert.Equal("coglatas.assertion-counterexample", result.Tests[0].Assertions[0].Reason);
        Assert.Equal(CompilerVerdict.Pass, PureCoreCompiler.Compile(failed, failed.Compiler).Result.Verdict);
        Assert.NotEqual(Execute(passed).Digest, result.Digest);
    }

    [Fact]
    public void Exact_rational_decimal_comparison_uses_a_single_bound_source()
    {
        var input = Sample(Known("coglatas.rational", new { numerator = "2", denominator = "1" }),
            (_, assertions, _) => assertions[0] = Check(Source().Context, "coglatas.assert-numeric-comparison"));
        var first = Execute(input);
        Assert.Equal(CompilerVerdict.Pass, first.Verdict);
        Assert.Equal(input.InputDigest, first.Input.InputDigest);
        Assert.Equal(first.Digest, Execute(input).Digest);
        Assert.Equal("/payload/assertions/example.independent-check", first.Tests[0].Assertions[0].Location.PropertyPath);

        var altered = Sample(Known("coglatas.rational", new { numerator = "2", denominator = "1" }),
            (_, assertions, _) =>
            {
                var assertion = Check(Source().Context, "coglatas.assert-numeric-comparison");
                assertion["expected"] = Known("coglatas.decimal", "3");
                assertions[0] = assertion;
            });
        Assert.Equal(CompilerVerdict.Fail, Execute(altered).Verdict);
    }

    [Fact]
    public void Collection_count_matches_decoded_items_not_an_implied_zero()
    {
        var element = Known("coglatas.boolean", true);
        var list = Obj(new
        {
            type = new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = true,
                elementType = element["type"]!.DeepClone() },
            state = "coglatas.known", value = new JsonArray(element, Known("coglatas.boolean", false))
        });
        var good = Sample(list, (_, assertions, _) =>
            assertions[0] = Check(Source().Context, "coglatas.assert-collection-count"));
        Assert.Equal(CompilerVerdict.Pass, Execute(good).Verdict);

        var bad = Sample(list, (_, assertions, _) =>
        {
            var assertion = Check(Source().Context, "coglatas.assert-collection-count");
            assertion["expectedCount"] = 0; assertions[0] = assertion;
        });
        Assert.Equal(CompilerVerdict.Fail, Execute(bad).Verdict);
    }

    [Fact]
    public void Unknown_and_null_values_never_become_known_passes()
    {
        foreach (var state in new[] { "coglatas.unknown", "coglatas.null" })
        {
            var unknown = Known("coglatas.boolean", true);
            unknown.Remove("value"); unknown["state"] = state;
            if (state == "coglatas.unknown")
            {
                unknown["reason"] = "example.indeterminate";
                unknown["basis"] = new JsonObject();
            }
            var result = Execute(Sample(unknown));
            Assert.Equal(CompilerVerdict.Inconclusive, result.Verdict);
            Assert.NotEqual(CompilerEvidenceKind.ModelProof, result.EvidenceKind);
            Assert.Equal(state == "coglatas.unknown" ? "coglatas.unknown-actual" : "coglatas.null-actual",
                result.Tests[0].Assertions[0].Reason);
        }
    }

    [Fact]
    public void Empty_discovery_and_empty_assertions_fail_closed_without_test_credit()
    {
        var missing = Execute(Sample(change: (_, _, test) => test["kind"] = "example.unrecognized-test"));
        Assert.Equal(0, missing.DiscoveredTests);
        Assert.Equal(CompilerVerdict.NotRun, missing.Verdict);
        Assert.Equal(0, missing.ExecutedAssertions);

        var empty = Execute(Sample(change: (_, assertions, _) => assertions.Clear()));
        Assert.Equal(1, empty.DiscoveredTests);
        Assert.Equal(0, empty.ExecutedAssertions);
        Assert.Equal(CompilerVerdict.NotRun, empty.Verdict);
        Assert.Equal("coglatas.zero-assertions", empty.Tests[0].Reason);
    }

    [Fact]
    public void A_stale_input_or_foreign_evaluator_cannot_use_an_old_pass()
    {
        var input = Sample();
        var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator);
        var other = Input(source: Source(99));
        var stale = plan.Execute(other.InputDigest);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        Assert.Equal(0, stale.ExecutedAssertions);
        Assert.NotEqual(CompilerVerdict.Pass, stale.Verdict);

        var wrong = new CompilerArtifactIdentity(Json(Artifact("example.unrelated-evaluator")));
        var mismatched = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), wrong).Execute(input.InputDigest);
        Assert.Equal("coglatas.evaluator-identity-mismatch", mismatched.Reason);
        Assert.Equal(0, mismatched.ExecutedAssertions);
        Assert.Equal(CompilerVerdict.NotRun, mismatched.Verdict);
        Assert.NotEqual(plan.Digest, ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), wrong).Digest);
    }

    [Fact]
    public void Forged_project_identity_is_rejected_before_any_assertion_result()
    {
        var input = Sample(change: (_, assertions, _) => assertions[0]!["target"]!["projectId"] = Id(97));
        var model = ProjectSemanticBinder.Bind(input);
        var run = ProjectTestPlan.Compile(model, Evaluator).Execute(input.InputDigest);
        Assert.Equal(CompilerVerdict.Error, run.Verdict);
        Assert.Equal(0, run.ExecutedAssertions);
        Assert.DoesNotContain(model.References,
            x => x.Origin.PropertyPath.StartsWith("/payload/assertions/", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_defaults_and_unknown_declaration_kinds_never_run()
    {
        var empty = Execute(Sample(change: (port, _, _) => port.Remove("defaultValue")));
        Assert.Equal(CompilerVerdict.NotRun, empty.Verdict);
        Assert.Equal("coglatas.fixture-default-missing", empty.Tests[0].Assertions[0].Reason);

        var opaque = Execute(Sample(change: (_, assertions, _) => assertions[0]!["kind"] = "example.unsupported"));
        Assert.Equal(CompilerVerdict.NotRun, opaque.Verdict);
        Assert.Equal("coglatas.unsupported-test", opaque.Tests[0].Reason);
        Assert.Equal(0, opaque.ExecutedAssertions);
    }

    [Fact]
    public void Cancelled_and_bounded_execution_do_not_synthesize_success()
    {
        var input = Sample();
        var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = plan.Execute(input.InputDigest, cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Execution);
        Assert.Equal(0, cancelled.ExecutedAssertions);
        Assert.NotEqual(CompilerVerdict.Pass, cancelled.Verdict);

        var expanded = Sample(change: (_, assertions, _) =>
        {
            var second = Check(Source().Context); second["key"] = "example.second-check";
            assertions.Add(second);
        });
        var bounded = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(expanded), Evaluator,
            new ProjectTestLimits(MaximumAssertions: 1)).Execute(expanded.InputDigest);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, bounded.Execution);
        Assert.Equal(0, bounded.ExecutedAssertions);
        Assert.NotEqual(CompilerVerdict.Pass, bounded.Verdict);
    }

    [Fact]
    public void A_known_counterexample_survives_a_second_unsupported_case()
    {
        var input = Sample(Known("coglatas.boolean", false));
        var values = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        var second = values.Single(x => x!["kind"]!.GetValue<string>() == "coglatas.project-test")!.DeepClone();
        second["entityId"] = Id(10);
        second["payload"]!["assertions"]![0]!["kind"] = "example.future";
        values.Add(second);
        var combined = new CompilerInputManifest(
            ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(values)), input.Settings);
        var result = Execute(combined);
        Assert.Equal(2, result.DiscoveredTests);
        Assert.Equal(CompilerVerdict.Fail, result.Verdict);
        Assert.Equal(CompilerCoverage.Partial, result.Coverage);
        Assert.Equal(1, result.ExecutedAssertions);
        Assert.Contains(result.Tests, x => x.Reason == "coglatas.unsupported-test");
    }

    [Fact]
    public void Partial_source_cannot_promote_a_local_assertion_to_suite_pass()
    {
        var settings = Settings();
        settings["sourceCoverage"] = "coglatas.partial";
        var run = Execute(Sample(settings: settings));
        Assert.Equal(CompilerVerdict.Pass, run.Tests[0].Assertions[0].Verdict);
        Assert.Equal(CompilerVerdict.Inconclusive, run.Verdict);
        Assert.Equal(CompilerCoverage.Partial, run.Coverage);
        Assert.Equal(CompilerEvidenceKind.Missing, run.EvidenceKind);
    }

    [Fact]
    public void Public_result_lists_and_original_caller_values_preserve_digest_and_identity()
    {
        var sourceValue = Known("coglatas.boolean", true);
        var input = Sample(sourceValue);
        var result = Execute(input);
        var original = result.Data.ToCanonicalBytes();
        var fingerprint = result.Digest;
        RejectMutableArray(result.Tests);
        RejectMutableArray(result.Tests[0].Assertions);
        sourceValue["value"] = false;
        Assert.Equal(original, result.Data.ToCanonicalBytes());
        Assert.Equal(fingerprint, result.Digest);
        Assert.Equal(result.Data, Execute(input).Data);
        Assert.Equal(fingerprint, ContentDigest.Compute("coglatas.project-test-run/1", original));
    }
}
