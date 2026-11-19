using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent local recording comparisons; no current authority, prediction or Project/Test outcome.</summary>
public sealed class SecurityRunComparisonCandidateTests
{
    private static readonly TenantId Tenant = new(Id(1));
    private static readonly ProjectId Project = new(Id(2));
    private static readonly BranchRef Branch = new(Tenant, Project, new(Id(3)));
    private static readonly DocumentId Document = new(Id(20));
    private static readonly EntityId Root = new(Id(21));
    private static readonly EntityId TaskEntity = new(Id(22));

    [Fact]
    public void CompleteComparisonBindsBothOriginalRunsAndEverySummaryField()
    {
        var before = Record(Prepare(Source(Context(10), 13)), 1001, start: 41);
        var after = Record(Prepare(Source(Context(11), 19)), 1002, start: 83);
        var comparison = Compare(before, after);

        AssertComplete(comparison, before, after, 6);
        Assert.False(comparison.SameInput);
        Assert.True(comparison.SameLimits);
        foreach (var run in new[] { before, after })
        {
            Assert.Equal(ProjectSimulation.Engine.Data, run.Manifest.Engine.Data);
            Assert.Equal(CompilerExecutionPlan.Engine.Data, Assert.IsType<CompilerArtifactIdentity>(run.Manifest.Plan.Input.PlanLowerer).Data);
            Assert.Equal(run.Manifest.Plan.Digest.Data, Child(run.Manifest.Data, "planDigest"));
            Assert.Equal(run.Manifest.Digest.Data, Child(run.Data, "manifestDigest"));
            Assert.Equal(run.Manifest.Plan.Input.InputDigest.Data, Child(run.Manifest.Data, "inputDigest"));
            Assert.Equal(run.Manifest.Plan.Input.Source.Digest.Data, Child(run.Manifest.Data, "sourceDigest"));
        }
    }

    [Fact]
    public void ComparisonBytesUseTheirOwnDomainAndDeclareNoOutcomeAuthority()
    {
        var plan = Prepare(Source(Context(12), 17));
        var before = Record(plan, 1003);
        var after = Record(plan, 1004);
        var comparison = Compare(before, after);
        AssertComplete(comparison, before, after, 0);

        var bytes = comparison.Data.ToCanonicalBytes();
        Assert.Equal(ContentDigest.Compute(ProjectSimulationRunComparison.Format, bytes), comparison.Digest);
        Assert.NotEqual(ContentDigest.Compute(ProjectSimulationRun.Format, bytes), comparison.Digest);
        Assert.NotEqual(ContentDigest.Compute(SimulationRunManifest.Format, bytes), comparison.Digest);
        Assert.Equal(ProjectSimulationRunComparison.Format, comparison.Data.Value.GetProperty("format").GetString());
        Assert.Equal("NotRun", comparison.Data.Value.GetProperty("verdict").GetString());
        Assert.Equal("coglatas.simulation", comparison.Data.Value.GetProperty("evidenceKind").GetString());
        Assert.Equal("NotEvaluated", comparison.Data.Value.GetProperty("authority").GetString());
        foreach (var run in new[] { before, after })
        {
            Assert.Equal(CompilerVerdict.NotRun, run.Verdict);
            Assert.Equal("NotRun", run.Data.Value.GetProperty("verdict").GetString());
            Assert.Equal("coglatas.simulation", run.Data.Value.GetProperty("evidenceKind").GetString());
            Assert.Equal("coglatas.no-outcome-assertions", run.Data.Value.GetProperty("verdictReason").GetString());
        }
    }

    [Fact]
    public void DistinctRunsOfTheSamePlanRemainSeparateButCompareEqualElapsed()
    {
        var plan = Prepare(Source(Context(13), 23));
        var before = Record(plan, 1005);
        var after = Record(plan, 1006);
        Assert.NotSame(before, after);
        Assert.NotEqual(before.Digest, after.Digest);
        Assert.NotEqual(before.Manifest.Digest, after.Manifest.Digest);
        Assert.Same(plan, before.Manifest.Plan);
        Assert.Same(plan, after.Manifest.Plan);

        var comparison = Compare(before, after);
        AssertComplete(comparison, before, after, 0);
        Assert.True(comparison.SameInput);
        Assert.True(comparison.SameLimits);
        Assert.Equal(2, before.Events.Count);
        Assert.Equal(2, after.Events.Count);
    }

    [Fact]
    public void DifferentStartsAreRemovedBeforeSignedElapsedDifference()
    {
        var before = Record(Prepare(Source(Context(14), 13)), 1007, start: 7);
        var after = Record(Prepare(Source(Context(15), 20)), 1008, start: 90_000);
        Assert.Equal(20, before.VirtualTicks);
        Assert.Equal(90_020, after.VirtualTicks);
        var comparison = Compare(before, after);
        AssertComplete(comparison, before, after, 7);
        Assert.NotEqual(after.VirtualTicks - before.VirtualTicks, comparison.ObservedElapsedDelta);
        AssertComplete(Compare(after, before), after, before, -7);
    }

    [Fact]
    public void LargeElapsedDifferencesRemainExactInBothDirections()
    {
        const long large = 9_007_199_254_741_119;
        var before = Record(Prepare(Source(Context(16), 2)), 1009, start: 17);
        var after = Record(Prepare(Source(Context(17), large)), 1010, start: 101);
        var forward = Compare(before, after);
        var reverse = Compare(after, before);

        AssertComplete(forward, before, after, large - 2);
        AssertComplete(reverse, after, before, -(large - 2));
        Assert.Equal("9007199254741117", forward.Data.Value.GetProperty("observedElapsedDelta").GetString());
        Assert.Equal("-9007199254741117", reverse.Data.Value.GetProperty("observedElapsedDelta").GetString());
        Assert.Equal(large.ToString(CultureInfo.InvariantCulture), forward.Data.Value.GetProperty("after").GetProperty("observedElapsedTicks").GetString());
    }

    [Fact]
    public void WrongAndSwappedRecordingDigestsRefuseWithoutSummaries()
    {
        var plan = Prepare(Source(Context(18), 29));
        var before = Record(plan, 1011);
        var after = Record(plan, 1012);
        Assert.NotEqual(before.Digest, after.Digest);
        AssertRefused(before, after, "coglatas.comparison.wrong-run", Digest("wrong-before"), after.Digest);
        AssertRefused(before, after, "coglatas.comparison.wrong-run", before.Digest, Digest("wrong-after"));
        AssertRefused(before, after, "coglatas.comparison.wrong-run", after.Digest, before.Digest);
        AssertComplete(Compare(before, after), before, after, 0);
    }

    [Fact]
    public void ForeignTenantProjectAndBranchPairsRefuseWithoutSummaries()
    {
        var before = Record(Prepare(Source(Context(19), 31)), 1013);
        var foreignBranches = new[]
        {
            new BranchRef(new(Id(501)), Project, new(Id(3))),
            new BranchRef(Tenant, new(Id(502)), new(Id(3))),
            new BranchRef(Tenant, Project, new(Id(503)))
        };
        for (var index = 0; index < foreignBranches.Length; index++)
        {
            var after = Record(Prepare(Source(Context(19, foreignBranches[index]), 31)), 1014 + index);
            AssertRefused(before, after, "coglatas.comparison.different-root-branch");
            AssertRefused(after, before, "coglatas.comparison.different-root-branch");
        }
    }

    [Fact]
    public void ReusedRunIdentityCannotNameDifferentRecordings()
    {
        var plan = Prepare(Source(Context(23), 37));
        var before = Record(plan, 1017, start: 10);
        var conflicting = Record(plan, 1017, start: 20);
        Assert.Equal(before.Manifest.RunId, conflicting.Manifest.RunId);
        Assert.NotEqual(before.Digest, conflicting.Digest);
        AssertRefused(before, conflicting, "coglatas.comparison.run-identity-conflict");
        AssertComplete(Compare(before, before), before, before, 0);
    }

    [Fact]
    public void SameContextWithDifferentSourceBodiesIsRejected()
    {
        var context = Context(24);
        var before = Record(Prepare(Source(context, 41)), 1018);
        var after = Record(Prepare(Source(context, 43)), 1019);
        Assert.Equal(before.Manifest.Plan.Input.Source.Context, after.Manifest.Plan.Input.Source.Context);
        Assert.NotEqual(before.Manifest.Plan.Input.Source.Digest, after.Manifest.Plan.Input.Source.Digest);
        AssertRefused(before, after, "coglatas.comparison.capture-identity-conflict");
    }

    [Fact]
    public void SameSourceUnderDifferentSettingsKeepsSourceAndInputAxesSeparate()
    {
        var source = Source(Context(25), 47);
        var before = Record(Prepare(source, purpose: "coglatas.normal"), 1020);
        var after = Record(Prepare(source, purpose: "coglatas.interactive"), 1021);
        Assert.Same(source, before.Manifest.Plan.Input.Source);
        Assert.Same(source, after.Manifest.Plan.Input.Source);
        Assert.NotEqual(before.Manifest.Plan.Input.InputDigest, after.Manifest.Plan.Input.InputDigest);
        Assert.NotEqual(before.Manifest.Plan.Digest, after.Manifest.Plan.Digest);

        var comparison = Compare(before, after);
        AssertComplete(comparison, before, after, 0);
        Assert.False(comparison.SameInput);
        Assert.True(comparison.SameLimits);
        Assert.Equal(Child(comparison.Data, "before").Value.GetProperty("sourceDigest").GetRawText(),
            Child(comparison.Data, "after").Value.GetProperty("sourceDigest").GetRawText());
    }

    [Fact]
    public void ExactSharedCapturedSourcesAreRetainedAcrossBothPlans()
    {
        var source = Source(Context(26), 53);
        var captured = Source(ExternalContext(200), 59);
        var supplied = Materialized(captured, 3000);
        var pins = new[] { Pin(captured, 3000) };
        var before = Record(Prepare(source, pins, [supplied]), 1022);
        var after = Record(Prepare(source, pins, [supplied]), 1023, start: 200);
        AssertComplete(Compare(before, after), before, after, 0);

        foreach (var run in new[] { before, after })
        {
            Assert.Equal(2, run.Manifest.Plan.Scope.Regions.Count);
            Assert.Contains(run.Manifest.Plan.Scope.Regions, region => region.Binding.Source.Digest.Equals(captured.Digest));
            var retainedPin = run.Manifest.Plan.Input.Settings.Value.GetProperty("authorizedExternalSnapshots")[0];
            Assert.Equal(captured.Context.Data, Json(retainedPin.GetProperty("context")));
            Assert.Equal(captured.Digest.Data, Json(retainedPin.GetProperty("digest")));
            Assert.Equal(supplied.SnapshotId.ToString(), retainedPin.GetProperty("snapshotId").GetString());
            Assert.Equal(supplied.Version, retainedPin.GetProperty("version").GetString());
            Assert.Equal(2, run.StartedTasks);
            Assert.Equal(2, run.CompletedTasks);
            Assert.Equal(4, run.Events.Count);
        }
    }

    [Fact]
    public void SameSnapshotVersionCannotRebindAnotherContext()
    {
        var source = Source(Context(27), 61);
        var firstCapture = Source(ExternalContext(201), 5);
        var secondCapture = Source(ExternalContext(202), 5);
        var before = Record(Prepare(source, [Pin(firstCapture, 3001)], [Materialized(firstCapture, 3001)]), 1024);
        var after = Record(Prepare(source, [Pin(secondCapture, 3001)], [Materialized(secondCapture, 3001)]), 1025);
        Assert.NotEqual(firstCapture.Context, secondCapture.Context);
        Assert.Equal(2, before.Manifest.Plan.Scope.Regions.Count);
        Assert.Equal(2, after.Manifest.Plan.Scope.Regions.Count);
        AssertRefused(before, after, "coglatas.comparison.capture-identity-conflict");
    }

    [Fact]
    public void SameCapturedContextCannotHideAChangedBody()
    {
        var source = Source(Context(28), 67);
        var capturedContext = ExternalContext(203);
        var firstCapture = Source(capturedContext, 7);
        var secondCapture = Source(capturedContext, 11);
        var before = Record(Prepare(source, [Pin(firstCapture, 3002)], [Materialized(firstCapture, 3002)]), 1026);
        var after = Record(Prepare(source, [Pin(secondCapture, 3003)], [Materialized(secondCapture, 3003)]), 1027);
        Assert.Equal(firstCapture.Context, secondCapture.Context);
        Assert.NotEqual(firstCapture.Digest, secondCapture.Digest);
        AssertRefused(before, after, "coglatas.comparison.capture-identity-conflict");
    }

    [Fact]
    public void CapturedContextCannotReplaceTheOtherRootBody()
    {
        var originalRoot = Source(Context(29), 71);
        var before = Record(Prepare(originalRoot), 1028);
        var substitutedCapture = Source(originalRoot.Context, 73);
        var afterRoot = Source(Context(30), 71);
        var after = Record(Prepare(afterRoot, [Pin(substitutedCapture, 3004)], [Materialized(substitutedCapture, 3004)]), 1029);
        Assert.Equal(2, after.Manifest.Plan.Scope.Regions.Count);
        Assert.Contains(after.Manifest.Plan.Scope.Regions, region => region.Binding.Source.Context.Equals(originalRoot.Context));
        AssertRefused(before, after, "coglatas.comparison.capture-identity-conflict");
    }

    [Fact]
    public void DeclaredCaptureBoundaryAdmits256AndRefuses257WithOneActualSupply()
    {
        var source = Source(Context(31), 79);
        var captured = Source(ExternalContext(204), 3);
        var supplied = Materialized(captured, 6000);
        object[] Pins(int count) => Enumerable.Range(0, count).Select(index => Pin(captured, 6000 + index)).ToArray();
        var before = Record(Prepare(source), 1030);
        var exact = Record(Prepare(source, Pins(256), [supplied]), 1031);
        var excess = Record(Prepare(source, Pins(257), [supplied]), 1032);

        Assert.Equal(256, ProjectSimulationRunComparison.MaximumCapturedInputsPerRun);
        Assert.Equal(256, exact.Manifest.Plan.Input.Settings.Value.GetProperty("authorizedExternalSnapshots").GetArrayLength());
        Assert.Equal(257, excess.Manifest.Plan.Input.Settings.Value.GetProperty("authorizedExternalSnapshots").GetArrayLength());
        Assert.Equal(2, exact.Manifest.Plan.Scope.Regions.Count);
        Assert.Equal(2, excess.Manifest.Plan.Scope.Regions.Count);
        Assert.Equal(2, exact.CompletedTasks);
        Assert.Equal(2, excess.CompletedTasks);
        AssertComplete(Compare(before, exact), before, exact, 0);
        AssertRefused(before, excess, "coglatas.comparison.capture-bound");
        AssertRefused(excess, before, "coglatas.comparison.capture-bound");
    }

    [Fact]
    public void FutureEngineRecordingsAreRefusedWithoutSummaries()
    {
        var plan = Prepare(Source(Context(32), 83));
        var before = Record(plan, 1033);
        var futureEngine = new CompilerArtifactIdentity(Json(new
        {
            artifactId = "coglatas.security-run-comparison.future-simulator", version = "99", digest = Digest("future-engine").Data.Value
        }));
        var after = Record(plan, 1034, engine: futureEngine);
        Assert.Equal(CompilerExecutionState.NotRun, after.Execution);
        Assert.Equal("coglatas.simulation.engine-unsupported", after.Reason);
        Assert.Empty(after.Events);
        AssertRefused(before, after, "coglatas.comparison.engine-unsupported");
        AssertRefused(after, before, "coglatas.comparison.engine-unsupported");
    }

    [Fact]
    public void PartialEventBoundKeepsActualCountsAndNoCompleteDelta()
    {
        var plan = Prepare(Source(Context(33), 89));
        var before = Record(plan, 1035, start: 100);
        var after = Record(plan, 1036, start: 200, limits: new(MaximumEvents: 1));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, after.Execution);
        Assert.Equal(CompilerCoverage.Partial, after.Coverage);
        Assert.Equal("coglatas.simulation.event-bound", after.Reason);
        Assert.Equal(1, after.StartedTasks);
        Assert.Equal(0, after.CompletedTasks);
        Assert.Single(after.Events);
        Assert.Equal(200, after.VirtualTicks);
        AssertIncomplete(before, after);
        AssertIncomplete(after, before);
    }

    [Fact]
    public void TimeOperationAndTraceBoundsNeverBecomeCompleteDeltas()
    {
        var plan = Prepare(Source(Context(34), 97));
        var before = Record(plan, 1037);
        var controls = new[]
        {
            (Limits: new SimulationRunLimits(MaximumElapsedTicks: 0), Reason: "coglatas.simulation.time-bound", Events: 1),
            (Limits: new SimulationRunLimits(MaximumOperations: 1), Reason: "coglatas.simulation.operation-bound", Events: 0),
            (Limits: new SimulationRunLimits(MaximumTraceBytes: 2), Reason: "coglatas.simulation.trace-bound", Events: 0)
        };
        for (var index = 0; index < controls.Length; index++)
        {
            var after = Record(plan, 1038 + index, limits: controls[index].Limits);
            Assert.Equal(CompilerExecutionState.ResourceExhausted, after.Execution);
            Assert.Equal(controls[index].Reason, after.Reason);
            Assert.Equal(controls[index].Events, after.Events.Count);
            Assert.Equal(0, after.CompletedTasks);
            AssertIncomplete(before, after);
        }
    }

    [Fact]
    public void StaleCancelledAndOverflowRecordingsRemainIncomplete()
    {
        var plan = Prepare(Source(Context(35), 101));
        var before = Record(plan, 1041);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stale = Record(plan, 1042, expectedInput: Digest("other-input"));
        var interrupted = Record(plan, 1043, cancellationToken: cancelled.Token);
        var overflow = Record(plan, 1044, start: long.MaxValue - 2);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        Assert.Equal(CompilerExecutionState.NotRun, stale.Execution);
        Assert.Equal("coglatas.stale-input", stale.Reason);
        Assert.Equal(CompilerExecutionState.Cancelled, interrupted.Execution);
        Assert.Equal("coglatas.cancelled", interrupted.Reason);
        Assert.Equal(CompilerExecutionState.InternalError, overflow.Execution);
        Assert.Equal("coglatas.simulation.tick-overflow", overflow.Reason);
        foreach (var after in new[] { stale, interrupted, overflow })
        {
            Assert.Equal(CompilerCoverage.None, after.Coverage);
            Assert.Empty(after.Events);
            Assert.Equal(0, after.StartedTasks);
            Assert.Equal(0, after.CompletedTasks);
            AssertIncomplete(before, after);
            AssertIncomplete(after, before);
        }
    }

    [Fact]
    public void DifferentValidLimitsRemainInspectableAndIndependentlyCompared()
    {
        var plan = Prepare(Source(Context(36), 103));
        var before = Record(plan, 1045, limits: new(MaximumEvents: 2));
        var after = Record(plan, 1046, limits: new(MaximumEvents: 3));
        var comparison = Compare(before, after);
        AssertComplete(comparison, before, after, 0);
        Assert.True(comparison.SameInput);
        Assert.False(comparison.SameLimits);
        Assert.Equal(2, comparison.Before!.Manifest.Limits.MaximumEvents);
        Assert.Equal(3, comparison.After!.Manifest.Limits.MaximumEvents);
        Assert.Equal(2, before.Events.Count);
        Assert.Equal(2, after.Events.Count);
    }

    [Fact]
    public void CancellationBeforeComparisonPreservesOriginalRecordings()
    {
        var plan = Prepare(Source(Context(37), 107));
        var before = Record(plan, 1047);
        var after = Record(plan, 1048);
        var originals = new[] { before.Data.CanonicalText, after.Data.CanonicalText, plan.Data.CanonicalText };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSimulationRunComparison.Compare(
            before, before.Digest, after, after.Digest, cancelled.Token));
        Assert.Equal(originals, new[] { before.Data.CanonicalText, after.Data.CanonicalText, plan.Data.CanonicalText });
        AssertComplete(Compare(before, after), before, after, 0);
    }

    [Fact]
    public void ReturnedSummaryAndTraceCopiesCannotRewriteRetainedEvidence()
    {
        var plan = Prepare(Source(Context(38), 109));
        var before = Record(plan, 1049);
        var after = Record(plan, 1050);
        var comparison = Compare(before, after);
        AssertComplete(comparison, before, after, 0);
        var retained = new[] { comparison.Data, before.Data, after.Data, before.Manifest.Data, after.Manifest.Data,
            plan.Data, plan.Input.Data, plan.Input.Source.Data, before.Events[0] };
        var originals = retained.Select(item => item.CanonicalText).ToArray();
        var bytes = comparison.Data.ToCanonicalBytes();
        bytes[0] ^= 1;
        var mutable = JsonNode.Parse(comparison.Data.CanonicalText)!.AsObject();
        mutable["before"]!["completedTasks"] = 999;
        mutable["after"]!["observedElapsedTicks"] = "0";
        mutable["observedElapsedDelta"] = "999";
        var rehashed = ContentDigest.Compute(ProjectSimulationRunComparison.Format, Json(mutable).ToCanonicalBytes());
        Assert.NotEqual(comparison.Digest, rehashed);
        var copiedEvents = before.Events.ToArray();
        copiedEvents[0] = Json(new { kind = "coglatas.foreign-event" });
        if (before.Events is IList<SourceJson> writable)
            Assert.Throws<NotSupportedException>(() => writable[0] = copiedEvents[0]);

        Assert.Equal(originals, retained.Select(item => item.CanonicalText).ToArray());
        Assert.Equal(comparison.Digest, Compare(before, after).Digest);
        Assert.Equal(before.Digest, ContentDigest.Compute(ProjectSimulationRun.Format, before.Data.ToCanonicalBytes()));
        Assert.Equal(after.Digest, ContentDigest.Compute(ProjectSimulationRun.Format, after.Data.ToCanonicalBytes()));
        Assert.Equal(1, before.CompletedTasks);
        Assert.Equal(1, after.CompletedTasks);
    }

    private static Guid Id(int number) => Guid.Parse("f51951c0-0000-4000-8000-" + number.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static SourceJson Child(SourceJson value, string property) => Json(value.Value.GetProperty(property));
    private static ContentDigest Digest(string value) => ContentDigest.Compute("coglatas.security-run-comparison-fixture/1", Encoding.UTF8.GetBytes(value));
    private static SourceRevisionContext Context(int revision, BranchRef? branch = null) => SourceRevisionContext.Committed(new(branch ?? Branch, new(Id(revision))));
    private static SourceRevisionContext ExternalContext(int revision) => Context(revision, new(Tenant, new(Id(2002)), new(Id(2003))));

    private static ProjectSource Source(SourceRevisionContext context, long duration)
    {
        var document = SourceDocument.Create(Document, "independent-run-comparison", Json(new
        {
            entityId = Root.ToString(), kind = "coglatas.project", schemaVersion = 1, payload = new { }
        }));
        var declarations = Json(new[] { new
        {
            entityId = TaskEntity.ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1,
            payload = new
            {
                duration = new
                {
                    type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                    state = "coglatas.known", value = duration.ToString(CultureInfo.InvariantCulture)
                },
                ports = Array.Empty<object>()
            }
        } });
        return ProjectSource.Create(context, [document], declarations);
    }

    private static object Pin(ProjectSource source, int identity) => new
    {
        snapshotId = new ExternalSnapshotId(Id(identity)).ToString(), version = "independent-fixed-1", digest = source.Digest.Data.Value,
        context = source.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null
    };

    private static CompilerMaterializedSnapshot Materialized(ProjectSource source, int identity) =>
        new(new(Id(identity)), "independent-fixed-1", new(source, true, true, true));

    private static CompilerExecutionPlan Prepare(ProjectSource source, object[]? pins = null,
        CompilerMaterializedSnapshot[]? supplied = null, string purpose = "coglatas.normal")
    {
        object Artifact(string identity, string version = "1") => new { artifactId = identity, version, digest = Digest(identity + "/" + version).Data.Value };
        object None() => new { kind = "coglatas.none" };
        var settings = Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-run-comparison.compiler"),
            ruleset = Artifact("coglatas.security-run-comparison.ruleset"), semantics = Artifact("coglatas.semantic-model", "2"),
            ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.security-run-comparison.evaluator"),
            scheduler = new { kind = "coglatas.plan-lowering", artifact = CompilerExecutionPlan.Engine.Data.Value },
            solver = None(), stochastic = None(), importer = None(), scenario = None(),
            authorizedExternalSnapshots = pins ?? [], resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(),
            observations = Array.Empty<object>(), evidence = Array.Empty<object>(), units = Array.Empty<object>(),
            calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }).ToArray(),
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities).Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model", CompilerExecutionPlan.Target }, analysisScope = new[] { "coglatas.source-structure" },
            sourceCoverage = "coglatas.complete", clock = new { kind = "coglatas.not-applicable" },
            executionProfile = new { purpose, maximumOperations = 4_000_000, maximumDeclarations = 2048, maximumRelations = 1024,
                maximumDigits = 64, maximumScale = 32, rounding = "coglatas.ties-to-even" }
        });
        var input = new CompilerInputManifest(source, settings);
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, supplied);
        Assert.Equal(CompilerExecutionState.Completed, compilation.Result.Execution);
        var publication = Assert.IsType<CompilerScopedPublication>(compilation.Publication);
        Assert.True(publication.IsDeclaredBy(compilation.Result));
        var plan = Assert.IsType<CompilerExecutionPlan>(publication.ExecutionPlan);
        Assert.Same(input, plan.Input);
        Assert.Equal(CompilerExecutionPlan.Engine.Data, Assert.IsType<CompilerArtifactIdentity>(input.PlanLowerer).Data);
        return plan;
    }

    private static ProjectSimulationRun Record(CompilerExecutionPlan plan, int identity, long start = 0,
        SimulationRunLimits? limits = null, CompilerArtifactIdentity? engine = null, ContentDigest? expectedInput = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = new SimulationRunManifest(plan, Id(identity), start, limits, engine);
        var run = ProjectSimulation.Execute(manifest, expectedInput ?? plan.Input.InputDigest, cancellationToken);
        Assert.Same(manifest, run.Manifest);
        Assert.Equal(ContentDigest.Compute(ProjectSimulationRun.Format, run.Data.ToCanonicalBytes()), run.Digest);
        return run;
    }

    private static ProjectSimulationRunComparison Compare(ProjectSimulationRun before, ProjectSimulationRun after) =>
        ProjectSimulationRunComparison.Compare(before, before.Digest, after, after.Digest);

    private static void AssertComplete(ProjectSimulationRunComparison comparison, ProjectSimulationRun before,
        ProjectSimulationRun after, long delta)
    {
        Assert.Equal(SimulationRunComparisonState.Complete, comparison.State);
        Assert.Equal("coglatas.comparison.complete-recordings", comparison.Reason);
        Assert.Equal(delta, comparison.ObservedElapsedDelta);
        Assert.Equal(delta.ToString(CultureInfo.InvariantCulture), comparison.Data.Value.GetProperty("observedElapsedDelta").GetString());
        AssertPair(comparison, before, after);
        foreach (var run in new[] { before, after })
        {
            Assert.Equal(CompilerExecutionState.Completed, run.Execution);
            Assert.Equal(CompilerCoverage.Complete, run.Coverage);
            Assert.Equal(CompilerFreshness.Current, run.Freshness);
        }
    }

    private static void AssertIncomplete(ProjectSimulationRun before, ProjectSimulationRun after)
    {
        var comparison = Compare(before, after);
        Assert.Equal(SimulationRunComparisonState.Incomplete, comparison.State);
        Assert.Equal("coglatas.comparison.incomplete-or-stale-recording", comparison.Reason);
        Assert.Null(comparison.ObservedElapsedDelta);
        Assert.Equal(JsonValueKind.Null, comparison.Data.Value.GetProperty("observedElapsedDelta").ValueKind);
        AssertPair(comparison, before, after);
    }

    private static void AssertRefused(ProjectSimulationRun before, ProjectSimulationRun after, string reason,
        ContentDigest? expectedBefore = null, ContentDigest? expectedAfter = null)
    {
        var comparison = ProjectSimulationRunComparison.Compare(before, expectedBefore ?? before.Digest, after, expectedAfter ?? after.Digest);
        Assert.Equal(SimulationRunComparisonState.Refused, comparison.State);
        Assert.Equal(reason, comparison.Reason);
        Assert.Null(comparison.Before);
        Assert.Null(comparison.After);
        Assert.Null(comparison.SameInput);
        Assert.Null(comparison.SameLimits);
        Assert.Null(comparison.ObservedElapsedDelta);
        foreach (var field in new[] { "before", "after", "sameInput", "sameLimits", "observedElapsedDelta" })
            Assert.Equal(JsonValueKind.Null, comparison.Data.Value.GetProperty(field).ValueKind);
        foreach (var run in new[] { before, after })
        {
            Assert.DoesNotContain(run.Manifest.RunId.ToString("D"), comparison.Data.CanonicalText);
            Assert.DoesNotContain(run.Digest.Value, comparison.Data.CanonicalText);
            Assert.DoesNotContain(run.Manifest.Plan.Input.Source.Digest.Value, comparison.Data.CanonicalText);
        }
        Assert.Equal("NotRun", comparison.Data.Value.GetProperty("verdict").GetString());
        Assert.Equal("NotEvaluated", comparison.Data.Value.GetProperty("authority").GetString());
    }

    private static void AssertPair(ProjectSimulationRunComparison comparison, ProjectSimulationRun before, ProjectSimulationRun after)
    {
        Assert.Same(before, comparison.Before);
        Assert.Same(after, comparison.After);
        AssertSummary(comparison.Data.Value.GetProperty("before"), before);
        AssertSummary(comparison.Data.Value.GetProperty("after"), after);
        Assert.Equal(ContentDigest.Compute(ProjectSimulationRunComparison.Format, comparison.Data.ToCanonicalBytes()), comparison.Digest);
        Assert.Equal("NotRun", comparison.Data.Value.GetProperty("verdict").GetString());
        Assert.Equal("coglatas.simulation", comparison.Data.Value.GetProperty("evidenceKind").GetString());
        Assert.Equal("NotEvaluated", comparison.Data.Value.GetProperty("authority").GetString());
    }

    private static void AssertSummary(JsonElement summary, ProjectSimulationRun run)
    {
        Assert.Equal(run.Manifest.RunId.ToString("D"), summary.GetProperty("runId").GetString());
        Assert.Equal(run.Digest.Data, Json(summary.GetProperty("runDigest")));
        Assert.Equal(run.Manifest.Digest.Data, Json(summary.GetProperty("manifestDigest")));
        Assert.Equal(run.Manifest.Plan.Input.InputDigest.Data, Json(summary.GetProperty("inputDigest")));
        Assert.Equal(run.Manifest.Plan.Input.Source.Digest.Data, Json(summary.GetProperty("sourceDigest")));
        Assert.Equal(run.Manifest.Plan.Input.Source.Context.Data, Json(summary.GetProperty("context")));
        Assert.Equal(run.Execution.ToString(), summary.GetProperty("execution").GetString());
        Assert.Equal(run.Coverage.ToString(), summary.GetProperty("coverage").GetString());
        Assert.Equal(run.Freshness.ToString(), summary.GetProperty("freshness").GetString());
        Assert.Equal(run.Reason, summary.GetProperty("reason").GetString());
        Assert.Equal(run.Manifest.StartTicks.ToString(CultureInfo.InvariantCulture), summary.GetProperty("startTicks").GetString());
        Assert.Equal(run.VirtualTicks.ToString(CultureInfo.InvariantCulture), summary.GetProperty("virtualTicks").GetString());
        Assert.Equal((run.VirtualTicks - run.Manifest.StartTicks).ToString(CultureInfo.InvariantCulture), summary.GetProperty("observedElapsedTicks").GetString());
        Assert.Equal(run.StartedTasks, summary.GetProperty("startedTasks").GetInt32());
        Assert.Equal(run.CompletedTasks, summary.GetProperty("completedTasks").GetInt32());
        Assert.Equal(run.Events.Count, summary.GetProperty("eventCount").GetInt32());
    }
}
