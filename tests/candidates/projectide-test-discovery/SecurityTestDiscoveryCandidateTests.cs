using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent local discovery and real portable CLI boundaries; no persisted authority grant.</summary>
public sealed class SecurityTestDiscoveryCandidateTests
{
    private static CompilerArtifactIdentity Evaluator => new(Json(Artifact("example.security-discovery-evaluator")));
    private static readonly object ProcessEvidenceLock = new();

    // Security-owned canonical fixture construction; no Product test helper is invoked.
    private static string Id(int value) => new Guid(value, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1).ToString();
    private static JsonObject Obj(object value) => JsonSerializer.SerializeToNode(value)!.AsObject();
    private static SourceJson Json(JsonNode value) => SourceJson.Parse(value.ToJsonString());
    private static JsonObject Artifact(string identity) => Obj(new
    {
        artifactId = identity, version = "1", digest = new { algorithm = "sha-256", value = new string('d', 64) }
    });
    private static ProjectSource Source(int revision = 5)
    {
        var branch = new BranchRef(TenantId.Parse(Id(1)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(3)));
        var context = SourceRevisionContext.Committed(new(branch, RevisionId.Parse(Id(revision))));
        var document = SourceDocument.Create(DocumentId.Parse(Id(4)), "independent-discovery",
            Json(Obj(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, payload = new { } })));
        return ProjectSource.Create(context, [document]);
    }
    private static JsonObject Settings()
    {
        var settings = new JsonObject();
        foreach (var name in new[] { "schema", "compiler", "ruleset", "semantics", "ir", "evaluator" })
            settings[name] = Artifact("example.security-discovery-" + name);
        settings["schema"] = Artifact(ProjectSource.SchemaId);
        settings["semantics"] = Artifact("coglatas.semantic-model");
        settings["ir"] = Artifact("coglatas.project-ir");
        foreach (var name in new[] { "units", "authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence", "calendars", "timeZones" })
            settings[name] = new JsonArray();
        foreach (var name in new[] { "scheduler", "solver", "stochastic", "importer", "scenario" })
            settings[name] = Obj(new { kind = "coglatas.none" });
        settings["descriptors"] = JsonSerializer.SerializeToNode(RelationKindDescriptor.BuiltIns.Select(descriptor =>
            new { artifactId = descriptor.DescriptorId, version = "1", digest = descriptor.Digest.Data.Value }));
        settings["rules"] = JsonSerializer.SerializeToNode(new[] { "coglatas.core.schema", "coglatas.core.declarations", "coglatas.core.relations" }
            .Select(rule => new { ruleId = rule, version = 1, required = true, configuration = new { } }));
        settings["featureSet"] = JsonSerializer.SerializeToNode(RelationKindDescriptor.BuiltIns.SelectMany(descriptor => descriptor.RequiredCapabilities)
            .Prepend("coglatas.core.structural").ToArray());
        settings["targets"] = new JsonArray("coglatas.semantic-model");
        settings["analysisScope"] = new JsonArray("coglatas.source-structure");
        settings["sourceCoverage"] = "coglatas.complete";
        settings["clock"] = Obj(new { kind = "coglatas.not-applicable" });
        settings["executionProfile"] = Obj(new { purpose = "coglatas.normal", maximumOperations = 200000, maximumDeclarations = 10000,
            maximumRelations = 20000, maximumDigits = 256, maximumScale = 256, rounding = "coglatas.ties-to-even" });
        return settings;
    }

    private static CompilerInputManifest Fixture(bool actual = true, int count = 2,
        Action<JsonArray, JsonObject>? change = null, SourceRevisionContext? context = null,
        CompilerArtifactIdentity? evaluator = null)
    {
        var basis = Source();
        context ??= basis.Context;
        var value = Obj(new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false },
            state = "coglatas.known", value = actual });
        var task = Obj(new { entityId = Id(231), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                state = "coglatas.known", value = "7" }, ports = new[] { new { portId = Id(232), direction = "coglatas.input",
                    type = value["type"]!.DeepClone(), minimumConnections = 0, maximumConnections = 1, defaultValue = value } } } });
        var declarations = new JsonArray(task);
        // Deliberately reverse storage order: the public query must own stable TestId order.
        for (var ordinal = count; ordinal >= 1; ordinal--)
        {
            var assertion = Obj(new { key = "example.discovery." + ordinal, kind = "coglatas.assert-boolean",
                target = new { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(),
                    entityId = Id(231), context = context.Data.Value }, portId = Id(232),
                expected = new { type = value["type"]!.DeepClone(), state = "coglatas.known", value = true } });
            declarations.Add(Obj(new { entityId = Id(1200 + ordinal), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
                displayName = "Independent inventory <" + ordinal + ">", payload = new { assertions = new JsonArray(assertion) } }));
        }
        var settings = Settings();
        settings["evaluator"] = JsonNode.Parse((evaluator ?? Evaluator).Data.CanonicalText);
        change?.Invoke(declarations, settings);
        return new(ProjectSource.Create(context, basis.Documents, Json(declarations)), Json(settings));
    }

    private static ProjectTestPlan Plan(CompilerInputManifest input, ProjectTestLimits? limits = null,
        CompilerArtifactIdentity? expected = null) => ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), expected ?? Evaluator, limits);

    private static void Empty(ProjectTestDiscovery value, ProjectTestDiscoveryState state)
    {
        Assert.Equal(state, value.State);
        Assert.Empty(value.Tests);
        Assert.Equal(0, value.DiscoveredTests);
        Assert.Null(value.InputDigest);
        Assert.Null(value.PlanDigest);
        Assert.False(value.BindingComplete);
        Assert.False(value.EvaluatorMatches);
    }

    private static void Owned<T>(IReadOnlyList<T> values)
    {
        var before = values.ToArray();
        Assert.False(values is T[]);
        if (values is ICollection legacy) Assert.False(legacy.SyncRoot is T[]);
        if (values is ICollection<T> collection)
        {
            Assert.True(collection.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => collection.Clear());
            Assert.Throws<NotSupportedException>(() => collection.Add(default!));
        }
        if (values is IList<T> list && list.Count != 0)
            Assert.Throws<NotSupportedException>(() => list[0] = default!);
        Assert.Equal(before, values.ToArray());
    }

    [Fact]
    public void ExactInventoryBindsInputAndPlanWhileKnownCounterexamplesStillFail()
    {
        var input = Fixture(actual: false);
        var plan = Plan(input);
        var before = plan.Data.ToCanonicalBytes();
        var failedBefore = plan.Execute(input.InputDigest);
        var inventory = plan.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State);
        Assert.Equal(input.InputDigest, inventory.InputDigest);
        Assert.Equal(plan.Digest, inventory.PlanDigest);
        Assert.True(inventory.BindingComplete);
        Assert.True(inventory.EvaluatorMatches);
        Assert.Equal(new[] { Id(1201), Id(1202) }, inventory.Tests.Select(item => item.TestId.ToString()));
        Assert.All(inventory.Tests, item => Assert.Equal(1, item.AssertionCount));
        Assert.Equal(CompilerVerdict.Fail, failedBefore.Verdict);
        Assert.Equal(2, failedBefore.ExecutedAssertions);
        Assert.Equal(before, plan.Data.ToCanonicalBytes());
        Assert.Equal(failedBefore.Data, plan.Execute(input.InputDigest).Data);
    }

    [Fact]
    public void SourceContextAndSettingsDriftCannotDiscloseAnEarlierRosterOrStamps()
    {
        var input = Fixture(); var plan = Plan(input);
        var contexts = new[]
        {
            Source(80).Context,
            SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(81)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(5)))),
            SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(82)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(5)))),
            SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(83))), RevisionId.Parse(Id(5))))
        };
        foreach (var context in contexts)
            Empty(plan.Discover(Fixture(context: context).InputDigest), ProjectTestDiscoveryState.StaleInput);
        var settingsChanged = Fixture(change: (_, settings) => settings["executionProfile"]!["maximumOperations"] = 199999);
        Empty(plan.Discover(settingsChanged.InputDigest), ProjectTestDiscoveryState.StaleInput);
        Empty(plan.Discover(Fixture(actual: false).InputDigest), ProjectTestDiscoveryState.StaleInput);
        Assert.Equal(2, plan.Discover(input.InputDigest).DiscoveredTests);
    }

    [Fact]
    public void EvaluatorMismatchRemainsAnIndependentAxisAndNeverExecutesAssertions()
    {
        var input = Fixture(); var expected = new CompilerArtifactIdentity(Json(Artifact("example.foreign-evaluator")));
        var good = Plan(input); var wrong = Plan(input, expected: expected);
        var inventory = wrong.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State);
        Assert.True(inventory.BindingComplete); Assert.False(inventory.EvaluatorMatches);
        Assert.Equal(2, inventory.DiscoveredTests); Assert.NotEqual(good.Digest, inventory.PlanDigest);
        var run = wrong.Execute(input.InputDigest);
        Assert.Equal(CompilerVerdict.NotRun, run.Verdict); Assert.Equal(0, run.ExecutedAssertions);
        Assert.Equal("coglatas.evaluator-identity-mismatch", run.Reason);
    }

    [Fact]
    public void EmptySuiteAndKnownEmptyAssertionListStayDistinctWithoutTestPass()
    {
        var noTests = Fixture(count: 0); var noPlan = Plan(noTests);
        var empty = noPlan.Discover(noTests.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, empty.State); Assert.Empty(empty.Tests);
        Assert.Equal(noTests.InputDigest, empty.InputDigest); Assert.Equal(noPlan.Digest, empty.PlanDigest);
        Assert.Equal("coglatas.zero-tests", noPlan.Execute(noTests.InputDigest).Reason);
        var noAssertions = Fixture(count: 1, change: (declarations, _) => declarations[1]!["payload"]!["assertions"]!.AsArray().Clear());
        var plan = Plan(noAssertions); var definition = Assert.Single(plan.Discover(noAssertions.InputDigest).Tests);
        Assert.Equal(ValueDecodeStatus.Decoded, definition.DecodeStatus); Assert.Equal(0, definition.AssertionCount);
        var run = plan.Execute(noAssertions.InputDigest);
        Assert.Equal(CompilerVerdict.NotRun, run.Verdict); Assert.Equal(0, run.ExecutedAssertions);
        Assert.Equal("coglatas.zero-assertions", Assert.Single(run.Tests).Reason);
    }

    [Fact]
    public void InvalidAndUnsupportedDefinitionsRetainUnknownCountsInsteadOfZero()
    {
        var input = Fixture(change: (declarations, _) =>
        {
            declarations[1]!["payload"]!["assertions"]![0]!["kind"] = "example.future-assertion";
            declarations[2]!["payload"]!.AsObject().Remove("assertions");
        });
        var inventory = Plan(input).Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.False(inventory.BindingComplete);
        Assert.Equal(new[] { ValueDecodeStatus.Invalid, ValueDecodeStatus.Unsupported }, inventory.Tests.Select(test => test.DecodeStatus));
        Assert.All(inventory.Tests, test => Assert.Null(test.AssertionCount));
        Assert.Equal(CompilerVerdict.Error, Plan(input).Execute(input.InputDigest).Verdict);
    }

    [Fact]
    public void TestCountLimitRejectsTheWholeInventoryAndAnUnboundedPositiveRestoresIt()
    {
        var input = Fixture(count: 3);
        Empty(Plan(input, new(MaximumTests: 2)).Discover(input.InputDigest), ProjectTestDiscoveryState.ResourceLimit);
        var complete = Plan(input).Discover(input.InputDigest);
        Assert.Equal(new[] { Id(1201), Id(1202), Id(1203) }, complete.Tests.Select(item => item.TestId.ToString()));
    }

    [Fact]
    public void AggregateAssertionLimitRejectsAllDefinitionsWithoutAPrefix()
    {
        var input = Fixture();
        var plan = Plan(input, new(MaximumAssertions: 1));
        Assert.Equal(2, plan.DiscoveredTests); Assert.False(plan.WithinLimits);
        Empty(plan.Discover(input.InputDigest), ProjectTestDiscoveryState.ResourceLimit);
        Assert.Equal(2, Plan(input, new(MaximumAssertions: 2)).Discover(input.InputDigest).DiscoveredTests);
    }

    [Fact]
    public void BindingBudgetAndDeclarationBoundsPublishUnavailableWithNoPriorIdentities()
    {
        foreach (var key in new[] { "maximumOperations", "maximumDeclarations" })
        {
            var input = Fixture(change: (_, settings) => settings["executionProfile"]![key] = 1);
            var plan = Plan(input);
            Assert.Equal(2, plan.DiscoveredTests); Assert.False(plan.DiscoveryComplete);
            Empty(plan.Discover(input.InputDigest), ProjectTestDiscoveryState.Unavailable);
            Assert.NotEqual(CompilerVerdict.Pass, plan.Execute(input.InputDigest).Verdict);
        }
    }

    [Fact]
    public void PartialSourceInventoryRetainsItsIncompleteBindingAndMissingExecutionEvidence()
    {
        var input = Fixture(change: (_, settings) => settings["sourceCoverage"] = "coglatas.partial"); var plan = Plan(input);
        var inventory = plan.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.False(inventory.BindingComplete);
        Assert.True(inventory.EvaluatorMatches); Assert.Equal(2, inventory.DiscoveredTests);
        var run = plan.Execute(input.InputDigest);
        Assert.Equal(CompilerVerdict.Inconclusive, run.Verdict); Assert.Equal(CompilerEvidenceKind.Missing, run.EvidenceKind);
        Assert.Equal(CompilerCoverage.Partial, run.Coverage);
    }

    [Fact]
    public void CandidateLocationsPreserveProposalBaseAndCapturedTargetWithoutPromotingAuthority()
    {
        var basis = Source().Context.CommittedRevision!;
        var captured = new RevisionRef(basis.Branch, RevisionId.Parse(Id(98)));
        var context = SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(96)), CandidateRevisionId.Parse(Id(97)), basis, captured));
        var input = Fixture(context: context); var inventory = Plan(input).Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State);
        foreach (var test in inventory.Tests)
        {
            Assert.Equal(context, test.Location.Context); Assert.Equal("coglatas.candidate", test.Location.Context.Kind);
            Assert.Equal(basis, test.Location.Context.Proposal!.BaseRevision);
            Assert.Equal(captured, test.Location.Context.Proposal.CapturedTargetHead);
            Assert.Equal(ProjectLocation.Create(DocumentId.Parse(Id(4)), context, entityId: test.TestId).Data, test.Location.Data);
        }
        Empty(Plan(input).Discover(Fixture().InputDigest), ProjectTestDiscoveryState.StaleInput);
    }

    [Fact]
    public void CancelledDiscoveryPublishesNothingAndLeavesSubsequentPositiveBytesUnchanged()
    {
        var input = Fixture(); var plan = Plan(input); var before = plan.Data.ToCanonicalBytes();
        var runBefore = plan.Execute(input.InputDigest).Data;
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => plan.Discover(input.InputDigest, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => plan.Discover(Fixture(actual: false).InputDigest, cancel.Token));
        Assert.Equal(before, plan.Data.ToCanonicalBytes()); Assert.Equal(runBefore, plan.Execute(input.InputDigest).Data);
        Assert.Equal(2, plan.Discover(input.InputDigest).DiscoveredTests);
    }

    [Fact]
    public void CallerNodesReturnedCollectionsAndDetachedBytesCannotRewriteRetainedDiscovery()
    {
        JsonArray? caller = null;
        var input = Fixture(change: (declarations, _) => caller = declarations);
        var plan = Plan(input); var inventory = plan.Discover(input.InputDigest);
        var source = input.Source.Data.ToCanonicalBytes(); var envelope = input.ToEnvelope().ToCanonicalBytes();
        var planBytes = plan.Data.ToCanonicalBytes(); var digest = plan.Digest;
        Owned(inventory.Tests);
        caller![1]!["displayName"] = "Caller mutation after admission";
        caller[1]!["payload"]!["assertions"]!.AsArray().Clear();
        var locationBytes = inventory.Tests[0].Location.Data.ToCanonicalBytes(); locationBytes[0] = 0;
        var detachedPlan = plan.Data.ToCanonicalBytes(); detachedPlan[0] = 0;
        Assert.Equal(source, input.Source.Data.ToCanonicalBytes()); Assert.Equal(envelope, input.ToEnvelope().ToCanonicalBytes());
        Assert.Equal(planBytes, plan.Data.ToCanonicalBytes()); Assert.Equal(digest, plan.Digest);
        Assert.Equal("Independent inventory <1>", inventory.Tests[0].DisplayName); Assert.Equal(1, inventory.Tests[0].AssertionCount);
        Assert.Equal(inventory.Tests.ToArray(), plan.Discover(input.InputDigest).Tests.ToArray());
    }

    [Fact]
    public async Task ConcurrentQueriesOwnStableSortedResultsWithoutChangingLaterExecution()
    {
        var input = Fixture(count: 3); var plan = Plan(input); var before = plan.Execute(input.InputDigest).Data;
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => plan.Discover(input.InputDigest))));
        Assert.All(replies, reply =>
        {
            Assert.Equal(ProjectTestDiscoveryState.Ready, reply.State);
            Assert.Equal(plan.Digest, reply.PlanDigest); Assert.Equal(input.InputDigest, reply.InputDigest);
            Assert.Equal(new[] { Id(1201), Id(1202), Id(1203) }, reply.Tests.Select(item => item.TestId.ToString()));
        });
        Assert.Equal(before, plan.Execute(input.InputDigest).Data);
    }

    [Fact]
    public void UnavailableForeignTargetsCannotConvertInventoryIntoAuthorizedExecution()
    {
        var input = Fixture(change: (declarations, _) =>
            declarations[1]!["payload"]!["assertions"]![0]!["target"]!["context"] = JsonNode.Parse(Source(66).Context.Data.CanonicalText));
        var plan = Plan(input); var inventory = plan.Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.False(inventory.BindingComplete);
        Assert.Equal(2, inventory.DiscoveredTests);
        var run = plan.Execute(input.InputDigest);
        Assert.NotEqual(CompilerVerdict.Pass, run.Verdict); Assert.Equal(CompilerEvidenceKind.Missing, run.EvidenceKind);
        Assert.Contains(run.Tests.SelectMany(test => test.Assertions), result => result.Reason == "coglatas.fixture-target-unavailable");
    }

    [Fact]
    public async Task RealCliReadyInventoryBindsActualEvaluatorBinaryAndExactPlanWithoutTestPass()
    {
        using var files = new CliFiles(); var evaluator = await files.Evaluator();
        var input = Fixture(evaluator: evaluator); files.Write(input);
        Assert.Equal(0, (await files.InvokeDiscovery("ready")).ExitCode);
        var artifact = files.Artifact(); NonExecuting(artifact);
        Assert.Equal("Ready", artifact.GetProperty("state").GetString());
        Assert.Equal("coglatas.test-discovery-ready", artifact.GetProperty("reason").GetString());
        Assert.Equal(input.InputDigest.Data.CanonicalText, SourceJson.Parse(artifact.GetProperty("inputDigest").GetRawText()).CanonicalText);
        Assert.Equal(Plan(input, expected: evaluator).Digest.Data.CanonicalText, SourceJson.Parse(artifact.GetProperty("planDigest").GetRawText()).CanonicalText);
        Assert.True(artifact.GetProperty("bindingComplete").GetBoolean()); Assert.True(artifact.GetProperty("evaluatorMatches").GetBoolean());
        Assert.Equal(new[] { Id(1201), Id(1202) }, artifact.GetProperty("tests").EnumerateArray().Select(test => test.GetProperty("testId").GetString()));
        Assert.All(artifact.GetProperty("tests").EnumerateArray(), test =>
            Assert.Equal(input.Source.Context.Data, ProjectLocation.Parse(SourceJson.Parse(test.GetProperty("location").GetRawText())).Context.Data));
    }

    [Fact]
    public async Task RealCliCounterexampleDiscoveryExitsZeroWhileItsSeparateActualRunExitsOne()
    {
        using var files = new CliFiles(); var evaluator = await files.Evaluator();
        var input = Fixture(actual: false, evaluator: evaluator); files.Write(input);
        Assert.Equal(0, (await files.InvokeDiscovery("counterexample-discovery")).ExitCode); NonExecuting(files.Artifact());
        var discoveryBytes = File.ReadAllBytes(files.Output);
        var runPath = Path.Combine(files.DirectoryPath, "run.json");
        Assert.Equal(1, (await files.Invoke("counterexample-execution", [files.Source, files.Envelope, runPath], runPath)).ExitCode);
        var result = SourceJson.Parse(File.ReadAllBytes(runPath)).Value;
        Assert.Equal("coglatas.project-test-result-envelope/1", result.GetProperty("format").GetString());
        Assert.Equal("Fail", result.GetProperty("result").GetProperty("verdict").GetString());
        Assert.Equal(2, result.GetProperty("result").GetProperty("executedAssertions").GetInt32());
        Assert.Equal(discoveryBytes, File.ReadAllBytes(files.Output));
    }

    [Fact]
    public async Task RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults()
    {
        foreach (var mode in new[] { "zero", "unsupported", "empty-assertions" })
        {
            using var files = new CliFiles(mode); var evaluator = await files.Evaluator();
            var input = Fixture(count: mode == "zero" ? 0 : 1, evaluator: evaluator, change: (declarations, _) =>
            {
                if (mode == "unsupported") declarations[1]!["payload"]!["assertions"]![0]!["kind"] = "example.future-kind";
                if (mode == "empty-assertions") declarations[1]!["payload"]!["assertions"]!.AsArray().Clear();
            });
            files.Write(input);
            Assert.Equal(mode == "empty-assertions" ? 0 : 2, (await files.InvokeDiscovery(mode)).ExitCode);
            var artifact = files.Artifact(); NonExecuting(artifact);
            Assert.Equal(mode == "zero" ? "coglatas.zero-tests" : mode == "unsupported" ? "coglatas.unsupported-test-discovery" : "coglatas.test-discovery-ready",
                artifact.GetProperty("reason").GetString());
            var roster = artifact.GetProperty("tests");
            if (mode == "zero") Assert.Equal(0, roster.GetArrayLength());
            else if (mode == "unsupported") Assert.Equal(JsonValueKind.Null, roster[0].GetProperty("assertionCount").ValueKind);
            else Assert.Equal(0, roster[0].GetProperty("assertionCount").GetInt32());
        }
    }

    [Fact]
    public async Task RealCliMixedInvalidDefinitionAndEvaluatorMismatchKeepsInvalidExitThree()
    {
        using var files = new CliFiles(); _ = await files.Evaluator();
        var input = Fixture(change: (declarations, _) => declarations[1]!["payload"]!.AsObject().Remove("assertions")); files.Write(input);
        Assert.Equal(3, (await files.InvokeDiscovery("invalid-and-mismatched-evaluator")).ExitCode);
        var artifact = files.Artifact(); NonExecuting(artifact);
        Assert.False(artifact.GetProperty("evaluatorMatches").GetBoolean());
        Assert.Equal("coglatas.invalid-test-discovery", artifact.GetProperty("reason").GetString());
        var invalid = Assert.Single(artifact.GetProperty("tests").EnumerateArray(), test => test.GetProperty("decodeStatus").GetString() == "Invalid");
        Assert.Equal(JsonValueKind.Null, invalid.GetProperty("assertionCount").ValueKind);
        Assert.Contains(artifact.GetProperty("tests").EnumerateArray(), test => test.GetProperty("decodeStatus").GetString() == "Decoded");
    }

    [Fact]
    public async Task RealCliValidEvaluatorMismatchKeepsExitTwoAndNeverClaimsMissingAssertionsExecuted()
    {
        using var files = new CliFiles(); _ = await files.Evaluator(); files.Write(Fixture());
        Assert.Equal(2, (await files.InvokeDiscovery("mismatched-evaluator")).ExitCode);
        var artifact = files.Artifact(); NonExecuting(artifact);
        Assert.Equal("coglatas.evaluator-identity-mismatch", artifact.GetProperty("reason").GetString());
        Assert.False(artifact.GetProperty("evaluatorMatches").GetBoolean());
        Assert.All(artifact.GetProperty("tests").EnumerateArray(), test => Assert.Equal(1, test.GetProperty("assertionCount").GetInt32()));
    }

    [Fact]
    public async Task RealCliSourceAndEnvelopeDriftFailBeforeDisclosingRosterOrBoundDigests()
    {
        foreach (var mode in new[] { "source", "envelope" })
        {
            using var files = new CliFiles(mode); var evaluator = await files.Evaluator(); var input = Fixture(evaluator: evaluator); files.Write(input);
            if (mode == "source") File.WriteAllBytes(files.Source, ProjectSourceCodec.Encode(Fixture(actual: false, evaluator: evaluator).Source));
            else File.WriteAllBytes(files.Envelope, Fixture(actual: false, evaluator: evaluator).ToEnvelope().ToCanonicalBytes());
            Assert.Equal(3, (await files.InvokeDiscovery(mode + "-drift")).ExitCode);
            Failure(files.Artifact(), "Error", "coglatas.invalid-or-mismatched-input");
        }
    }

    [Fact]
    public async Task RealCliCreateNewProtectsExistingOutputAndBothInputPathsByteForByte()
    {
        using var files = new CliFiles(); files.Write(Fixture(evaluator: await files.Evaluator()));
        File.WriteAllBytes(files.Output, Encoding.UTF8.GetBytes("Existing immutable output\n"));
        foreach (var target in new[] { files.Output, files.Source, files.Envelope })
        {
            var before = File.ReadAllBytes(target);
            var result = await files.Invoke("existing-" + Path.GetFileName(target), ["--discover", files.Source, files.Envelope, target], target);
            Assert.Equal(3, result.ExitCode); Assert.Contains("input/output unavailable", result.Stderr);
            Assert.Equal(before, File.ReadAllBytes(target));
        }
    }

    [Fact]
    public async Task RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory()
    {
        foreach (var mode in new[] { "malformed", "oversized", "future" })
        {
            using var files = new CliFiles(mode); var input = Fixture(evaluator: await files.Evaluator()); files.Write(input);
            if (mode == "malformed") File.WriteAllBytes(files.Source, [0xff, 0xfe, 0x7b]);
            else if (mode == "oversized") File.WriteAllBytes(files.Source, new byte[new SourceCodecLimits().MaximumBytes + 1]);
            else
            {
                var changed = JsonNode.Parse(input.Source.Data.CanonicalText)!.AsObject(); changed["format"] = "example.future-source/8";
                File.WriteAllBytes(files.Source, Json(changed).ToCanonicalBytes());
            }
            Assert.Equal(mode == "future" ? 2 : 3, (await files.InvokeDiscovery(mode)).ExitCode);
            Failure(files.Artifact(), mode == "future" ? "NotRun" : "Error", mode == "future" ? "coglatas.unsupported-input" : "coglatas.invalid-or-mismatched-input");
        }
    }

    [Fact]
    public async Task RealCliPartialBindingAndBudgetExhaustionCannotInventCompleteInventory()
    {
        foreach (var mode in new[] { "partial", "budget" })
        {
            using var files = new CliFiles(mode); var evaluator = await files.Evaluator();
            files.Write(Fixture(evaluator: evaluator, change: (_, settings) =>
            {
                if (mode == "partial") settings["sourceCoverage"] = "coglatas.partial";
                else settings["executionProfile"]!["maximumOperations"] = 1;
            }));
            Assert.Equal(2, (await files.InvokeDiscovery(mode)).ExitCode);
            var artifact = files.Artifact(); NonExecuting(artifact); Assert.False(artifact.GetProperty("bindingComplete").GetBoolean());
            Assert.Equal(mode == "partial" ? "coglatas.incomplete-test-binding" : "coglatas.test-discovery-unavailable", artifact.GetProperty("reason").GetString());
            if (mode == "budget")
            {
                Assert.Equal("Unavailable", artifact.GetProperty("state").GetString()); Assert.Equal(0, artifact.GetProperty("tests").GetArrayLength());
                Assert.Equal(JsonValueKind.Null, artifact.GetProperty("inputDigest").ValueKind); Assert.Equal(JsonValueKind.Null, artifact.GetProperty("planDigest").ValueKind);
            }
            else { Assert.Equal("Ready", artifact.GetProperty("state").GetString()); Assert.Equal(2, artifact.GetProperty("tests").GetArrayLength()); }
        }
    }

    [Fact]
    public async Task RealCliArtifactByteBoundRefusesTheWholeProjectionWithoutTruncatingNames()
    {
        using var files = new CliFiles(); var evaluator = await files.Evaluator();
        var input = Fixture(count: 128, evaluator: evaluator, change: (declarations, settings) =>
        {
            settings["executionProfile"]!["maximumOperations"] = 10000000;
            foreach (var definition in declarations.Skip(1))
            {
                definition!["payload"]!["assertions"]!.AsArray().Clear();
                definition["displayName"] = new string('q', 32500);
            }
        });
        Assert.True(ProjectSourceCodec.Encode(input.Source).Length < new SourceCodecLimits().MaximumBytes);
        var inventory = Plan(input, expected: evaluator).Discover(input.InputDigest);
        Assert.Equal(ProjectTestDiscoveryState.Ready, inventory.State); Assert.Equal(128, inventory.DiscoveredTests);
        Assert.All(inventory.Tests, item => Assert.Equal(32500, item.DisplayName!.Length));
        files.Write(input);
        Assert.Equal(2, (await files.InvokeDiscovery("artifact-byte-bound")).ExitCode);
        var artifact = files.Artifact();
        Failure(artifact, "NotRun", "coglatas.test-discovery-artifact-bound-exhausted", "ResourceExhausted");
        Assert.True(new FileInfo(files.Output).Length < 1024);
    }

    private static void NonExecuting(JsonElement artifact)
    {
        Assert.Equal("coglatas.project-test-discovery-cli/1", artifact.GetProperty("format").GetString());
        Assert.Equal("NotRun", artifact.GetProperty("execution").GetString()); Assert.Equal("NotRun", artifact.GetProperty("verdict").GetString());
        Assert.Equal("coglatas.missing", artifact.GetProperty("evidenceKind").GetString()); Assert.Equal(0, artifact.GetProperty("executedAssertions").GetInt32());
        Assert.False(artifact.TryGetProperty("result", out _)); Assert.False(artifact.TryGetProperty("resultDigest", out _));
    }

    private static void Failure(JsonElement artifact, string verdict, string reason, string execution = "NotRun")
    {
        Assert.Equal("coglatas.project-test-cli-failure/1", artifact.GetProperty("format").GetString());
        Assert.Equal(execution, artifact.GetProperty("execution").GetString()); Assert.Equal(verdict, artifact.GetProperty("verdict").GetString());
        Assert.Equal(reason, artifact.GetProperty("reason").GetString());
        foreach (var forbidden in new[] { "tests", "inputDigest", "planDigest", "result", "resultDigest" }) Assert.False(artifact.TryGetProperty(forbidden, out _));
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
    private sealed class CliFiles : IDisposable
    {
        private readonly string _case;
        private readonly string _binary;
        private readonly string _domain;
        public string DirectoryPath { get; }
        public string Source => Path.Combine(DirectoryPath, "source.json");
        public string Envelope => Path.Combine(DirectoryPath, "input.json");
        public string Output => Path.Combine(DirectoryPath, "inventory.json");
        public CliFiles(string variant = "default", [CallerMemberName] string method = "")
        {
            _case = method + "/" + variant;
            DirectoryPath = Path.Combine(Path.GetTempPath(), "security-discovery-process-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "tools", "ProjectIde.Tests", "ProjectIde.Tests.csproj"))) root = root.Parent;
            Assert.NotNull(root);
            _binary = Path.Combine(root.FullName, "tools", "ProjectIde.Tests", "bin", "Release", "net10.0", "ProjectIde.Tests.dll");
            _domain = Path.Combine(Path.GetDirectoryName(_binary)!, "Coglatas.Domain.dll");
            Assert.True(File.Exists(_binary)); Assert.True(File.Exists(_domain));
            Assert.True(File.Exists(Path.ChangeExtension(_binary, "runtimeconfig.json")));
            Assert.Equal(Hash(File.ReadAllBytes(typeof(ProjectTestPlan).Assembly.Location)), Hash(File.ReadAllBytes(_domain)));
        }
        public async Task<CompilerArtifactIdentity> Evaluator()
        {
            var result = await Invoke("evaluator", ["--evaluator"], null);
            Assert.Equal(0, result.ExitCode); Assert.Equal("", result.Stderr);
            var evaluator = new CompilerArtifactIdentity(SourceJson.Parse(result.Stdout.Trim()));
            Assert.Equal("coglatas.local-project-test-evaluator", evaluator.ArtifactId);
            Assert.Equal(typeof(ProjectTestPlan).Assembly.GetName().Version!.ToString(), evaluator.Version);
            Assert.Equal(ContentDigest.Compute("coglatas.project-test-evaluator-binary/1", File.ReadAllBytes(_domain)), evaluator.Digest);
            return evaluator;
        }
        public void Write(CompilerInputManifest input)
        {
            File.WriteAllBytes(Source, ProjectSourceCodec.Encode(input.Source));
            File.WriteAllBytes(Envelope, input.ToEnvelope().ToCanonicalBytes());
        }
        public JsonElement Artifact() => SourceJson.Parse(File.ReadAllBytes(Output)).Value;
        public Task<ProcessResult> InvokeDiscovery(string label) => Invoke(label, ["--discover", Source, Envelope, Output], Output);
        public async Task<ProcessResult> Invoke(string label, string[] args, string? output)
        {
            var cliBefore = Hash(File.ReadAllBytes(_binary)); var domainBefore = Hash(File.ReadAllBytes(_domain));
            var inputs = new[] { Source, Envelope }.Where(File.Exists).ToDictionary(path => path, File.ReadAllBytes);
            var outputBefore = output is not null && File.Exists(output) ? File.ReadAllBytes(output) : null;
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = DirectoryPath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(_binary); foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = start };
            var startedUtc = DateTimeOffset.UtcNow;
            Assert.True(process.Start()); var pid = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
            var cliAfter = Hash(File.ReadAllBytes(_binary)); var domainAfter = Hash(File.ReadAllBytes(_domain));
            var outputAfter = output is not null && File.Exists(output) ? File.ReadAllBytes(output) : null;
            var evidence = Environment.GetEnvironmentVariable("COGLATAS_DISCOVERY_PROCESS_EVIDENCE");
            Assert.False(string.IsNullOrWhiteSpace(evidence), "The exact-source runner must retain actual process evidence.");
            var record = JsonSerializer.Serialize(new
            {
                caseId = _case, invocation = label, processId = pid, startedUtc, completedUtc = DateTimeOffset.UtcNow,
                executable = "dotnet", cliPath = "tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.dll",
                arguments = args.Select(arg => Path.IsPathRooted(arg) ? Path.GetFileName(arg) : arg).ToArray(),
                cliSha256Before = cliBefore, cliSha256After = cliAfter, domainSha256Before = domainBefore, domainSha256After = domainAfter,
                result.ExitCode, result.Stdout, result.Stderr,
                inputs = inputs.Select(pair => new { name = Path.GetFileName(pair.Key), length = pair.Value.Length,
                    sha256Before = Hash(pair.Value), sha256After = Hash(File.ReadAllBytes(pair.Key)) }).ToArray(),
                outputName = output is null ? null : Path.GetFileName(output),
                outputExisted = outputBefore is not null, outputSha256Before = outputBefore is null ? null : Hash(outputBefore),
                outputSha256After = outputAfter is null ? null : Hash(outputAfter), outputBytes = outputAfter
            });
            lock (ProcessEvidenceLock) File.AppendAllText(evidence!, record + "\n", new UTF8Encoding(false));
            Assert.Equal(cliBefore, cliAfter); Assert.Equal(domainBefore, domainAfter);
            foreach (var pair in inputs) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            if (outputBefore is not null) Assert.Equal(outputBefore, outputAfter);
            return result;
        }
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
        private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
