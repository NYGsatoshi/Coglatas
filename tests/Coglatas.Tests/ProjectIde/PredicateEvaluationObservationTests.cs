using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class PredicateEvaluationObservationTests
{
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static PurePredicate Binary(string kind, bool? left, bool? right) =>
        PurePredicate.Parse(Json(new { kind, operands = new[] { PlanDeclarationTests.Literal(left), PlanDeclarationTests.Literal(right) } }));
    private static RecordedPredicateEvaluation Capture(PurePredicate predicate) => predicate.EvaluateWithObservation(new(PredicateEvaluationCaptureMode.ObservedOperators));
    private static PredicateObservationSnapshot Available(PurePredicate predicate, RecordedPredicateEvaluation evaluation) =>
        PredicateObservationSnapshot.Read(evaluation, predicate, evaluation.Observation, PredicateObservationSnapshotMode.AvailableOperators);
    private static JsonElement Node(PredicateObservationSnapshot snapshot, int ordinal) =>
        snapshot.Bodies.Values.Single(body => body.Value.GetProperty("ordinal").GetInt32() == ordinal).Value;

    public static IEnumerable<object[]> BinaryCases()
    {
        foreach (var kind in new[] { "coglatas.and", "coglatas.or" })
        foreach (var left in new bool?[] { true, false, null })
        foreach (var right in new bool?[] { true, false, null })
            yield return [kind, left!, right!];
    }

    [Theory]
    [MemberData(nameof(BinaryCases))]
    public void ActualOperatorObservationPreservesTruthOriginalPremiseReferencesAndShortCircuit(string kind, bool? left, bool? right)
    {
        var predicate = Binary(kind, left, right); var legacy = predicate.Evaluate(); var recorded = Capture(predicate);
        var expected = kind == "coglatas.and"
            ? left == false || right == false ? PredicateTruth.False : left == true && right == true ? PredicateTruth.True : PredicateTruth.Unknown
            : left == true || right == true ? PredicateTruth.True : left == false && right == false ? PredicateTruth.False : PredicateTruth.Unknown;
        Assert.NotNull(recorded.Evaluation); Assert.Equal(expected, recorded.Evaluation.Truth);
        Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(recorded.Evaluation));
        Assert.Equal(legacy.EvaluatedPremises.Count, recorded.Evaluation.EvaluatedPremises.Count);
        for (var index = 0; index < legacy.EvaluatedPremises.Count; index++) Assert.Same(legacy.EvaluatedPremises[index], recorded.Evaluation.EvaluatedPremises[index]);
        var snapshot = Available(predicate, recorded); var root = Node(snapshot, 0);
        var shortCircuit = kind == "coglatas.and" && left == false || kind == "coglatas.or" && left == true;
        Assert.Equal(shortCircuit ? 2 : 3, recorded.Observation.ObservedNodes);
        Assert.Equal(shortCircuit ? 1 : 2, root.GetProperty("evaluatedChildren").GetArrayLength());
        Assert.Equal(shortCircuit ? 1 : 0, root.GetProperty("skippedOperands").GetInt32());
        Assert.Equal(kind, root.GetProperty("kind").GetString()); Assert.Equal(expected.ToString(), root.GetProperty("truth").GetString());
        Assert.Equal(0, root.GetProperty("path").GetArrayLength()); Assert.Equal(recorded.Observation.ObservedNodes, snapshot.Bodies.Count);
        foreach (var descriptor in recorded.Observation.Nodes)
        {
            var frame = snapshot.Bodies[descriptor.Digest.Value];
            Assert.Equal(descriptor.Bytes, frame.ToCanonicalBytes().Length);
            Assert.Equal(descriptor.Digest, ContentDigest.Compute(PredicateEvaluationObservation.NodeDomain, frame.ToCanonicalBytes()));
            var literal = frame.Value.GetProperty("literal");
            if (literal.ValueKind == JsonValueKind.Null) continue;
            var ordinal = literal.GetProperty("premiseOrdinal").GetInt32();
            Assert.Equal(ContentDigest.Compute(PredicateEvaluationObservation.LiteralDomain, legacy.EvaluatedPremises[ordinal].ToCanonicalBytes()).Data,
                SourceJson.Parse(literal.GetProperty("digest").GetRawText()));
        }
        Assert.Equal(ContentDigest.Compute("coglatas.predicate/1", predicate.Data.ToCanonicalBytes()), recorded.Observation.PredicateDigest);
        Assert.NotNull(recorded.Observation.EvaluationFrame);
        Assert.Equal(recorded.Observation.EvaluationDigest, ContentDigest.Compute(PredicateEvaluationObservation.EvaluationDomain, recorded.Observation.EvaluationFrame.ToCanonicalBytes()));
        Assert.Equal(legacy.EvaluatedPremises.Count, recorded.Observation.EvaluationFrame.Value.GetProperty("premises").GetArrayLength());
        Assert.Equal("ObservedBooleanEvaluationTree", recorded.Observation.Data.Value.GetProperty("relationshipStructure").GetString());
        Assert.Equal("NotRecorded", recorded.Observation.Data.Value.GetProperty("causalMeaning").GetString());
        Assert.Equal("NotAllocated", recorded.Observation.Data.Value.GetProperty("compilerRunIdentity").GetString());
    }

    [Theory]
    [InlineData(true, PredicateTruth.False)]
    [InlineData(false, PredicateTruth.True)]
    [InlineData(null, PredicateTruth.Unknown)]
    public void NegationRecordsTheActualChildAndRetainsUnknown(bool? value, PredicateTruth expected)
    {
        var predicate = PurePredicate.Parse(Json(new { kind = "coglatas.not", operands = new[] { PlanDeclarationTests.Literal(value) } }));
        var recorded = Capture(predicate); var snapshot = Available(predicate, recorded);
        Assert.Equal(expected, recorded.Evaluation!.Truth); Assert.Equal(2, snapshot.Bodies.Count);
        var root = Node(snapshot, 0); var child = Node(snapshot, 1);
        Assert.Equal(new[] { 1 }, root.GetProperty("evaluatedChildren").EnumerateArray().Select(element => element.GetInt32()));
        Assert.Equal(0, root.GetProperty("skippedOperands").GetInt32());
        Assert.Equal(new[] { 0 }, child.GetProperty("path").EnumerateArray().Select(element => element.GetInt32()));
        Assert.Equal(0, child.GetProperty("evaluatedChildren").GetArrayLength());
        Assert.Equal(0, child.GetProperty("skippedOperands").GetInt32());
    }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (var a in new bool?[] { true, false, null })
        foreach (var b in new bool?[] { true, false, null })
        foreach (var c in new bool?[] { true, false, null }) yield return [a!, b!, c!];
    }

    [Theory]
    [MemberData(nameof(NestedCases))]
    public void NestedObservedBranchesRetainEvaluationOrderAndOriginalLiteralSequenceAcrossCaptureChoices(bool? a, bool? b, bool? c)
    {
        object Or() => new { kind = "coglatas.or", operands = new[] { PlanDeclarationTests.Literal(a), PlanDeclarationTests.Literal(b) } };
        object Not() => new { kind = "coglatas.not", operands = new[] { PlanDeclarationTests.Literal(c) } };
        var predicate = PurePredicate.Parse(Json(new { kind = "coglatas.and", operands = new[] { Or(), Not() } }));
        var original = predicate.Evaluate(); ContentDigest? resultDigest = null;
        foreach (var policy in new[] { new PredicateEvaluationCapturePolicy(), new(PredicateEvaluationCaptureMode.ObservedOperators),
                     new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1), new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1) })
        {
            var run = predicate.EvaluateWithObservation(policy); Assert.NotNull(run.Evaluation);
            Assert.Equal(original.Truth, run.Evaluation.Truth);
            Assert.Equal(original.EvaluatedPremises.Count, run.Evaluation.EvaluatedPremises.Count);
            for (var i = 0; i < original.EvaluatedPremises.Count; i++) Assert.Same(original.EvaluatedPremises[i], run.Evaluation.EvaluatedPremises[i]);
            resultDigest ??= run.Observation.EvaluationDigest; Assert.Equal(resultDigest, run.Observation.EvaluationDigest);
            var metadata = PredicateObservationSnapshot.Read(run, predicate, run.Observation); Assert.Empty(metadata.Bodies);
            var available = Available(predicate, run);
            if (run.Observation.State != PredicateEvaluationCaptureState.RecordedOperators) { Assert.Empty(available.Bodies); continue; }
            Assert.Equal(Enumerable.Range(0, run.Observation.ObservedNodes), run.Observation.Nodes.Select(node => node.Ordinal));
            foreach (var body in available.Bodies.Values)
            {
                var data = body.Value; var ordinal = data.GetProperty("ordinal").GetInt32();
                foreach (var child in data.GetProperty("evaluatedChildren").EnumerateArray())
                    Assert.InRange(child.GetInt32(), ordinal + 1, run.Observation.ObservedNodes - 1);
            }
        }
    }

    [Fact]
    public void DefaultManifestAndPublishedMetadataContainNoWholeInputOrOperatorBodyMap()
    {
        var predicate = Binary("coglatas.and", true, false); var legacy = predicate.Evaluate();
        var manifest = predicate.EvaluateWithObservation(new()); var full = Capture(predicate);
        Assert.Equal(PredicateEvaluationCaptureState.ManifestOnly, manifest.Observation.State);
        Assert.Empty(manifest.Observation.Nodes); Assert.Empty(Available(predicate, manifest).Bodies);
        Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(manifest.Evaluation));
        foreach (var run in new[] { manifest, full })
        {
            var metadata = PredicateObservationSnapshot.Read(run, predicate, run.Observation);
            Assert.Empty(metadata.Bodies); Assert.Same(run.Observation.Data, metadata.OriginalRecordMetadata);
            var serialized = JsonSerializer.Serialize(run);
            Assert.False(JsonSerializer.SerializeToElement(run).TryGetProperty("Evaluation", out _));
            Assert.False(JsonSerializer.SerializeToElement(run.Observation).TryGetProperty("EvaluationFrame", out _));
            Assert.DoesNotContain("evaluatedChildren", serialized);
            Assert.DoesNotContain("originalSource", serialized);
            Assert.DoesNotContain(predicate.Data.CanonicalText, serialized);
        }
        Assert.Equal(3, Available(predicate, full).Bodies.Count);
    }

    [Fact]
    public void AWholeGenuineEqualDigestOlderRunAndAnEqualPredicateObjectDoNotOwnTheCurrentRead()
    {
        var predicate = Binary("coglatas.or", null, false); var first = Capture(predicate); var current = Capture(predicate);
        var equalPredicate = PurePredicate.Parse(predicate.Data); var foreign = Capture(equalPredicate);
        Assert.Equal(first.Observation.Digest, current.Observation.Digest); Assert.Equal(first.Observation.Digest, foreign.Observation.Digest);
        Assert.NotSame(first.Evaluation, current.Evaluation); Assert.False(first.Observation.IsOwnedBy(current)); Assert.True(first.Observation.IsOwnedBy(first));
        foreach (var refused in new[] {
            PredicateObservationSnapshot.Read(current, predicate, first.Observation, PredicateObservationSnapshotMode.AvailableOperators),
            PredicateObservationSnapshot.Read(first, equalPredicate, first.Observation, PredicateObservationSnapshotMode.AvailableOperators),
            PredicateObservationSnapshot.Read(first, predicate, foreign.Observation, PredicateObservationSnapshotMode.AvailableOperators) })
        {
            Assert.Equal(PredicateObservationSnapshotState.RefusedInvocation, refused.State); Assert.Empty(refused.Bodies);
            Assert.Null(refused.OriginalRecordMetadata); Assert.Null(refused.OriginalRecordDigest); Assert.Null(refused.OriginalCaptureState);
        }
        Assert.Equal(3, Available(predicate, first).Bodies.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicCaptureGapCannotBeRepairedByIncreasingTheLaterViewerBudget(bool countGap)
    {
        var predicate = Binary("coglatas.and", true, false); var legacy = predicate.Evaluate();
        var run = predicate.EvaluateWithObservation(new(PredicateEvaluationCaptureMode.ObservedOperators,
            maximumNodes: countGap ? 1 : PredicateEvaluationCapturePolicy.HardNodeLimit,
            maximumBodyBytes: countGap ? ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes : 1));
        var state = countGap ? PredicateEvaluationCaptureState.NodeLimitReached : PredicateEvaluationCaptureState.BodyBudgetExceeded;
        Assert.Equal(state, run.Observation.State); Assert.True(run.Observation.Completed); Assert.Empty(run.Observation.Nodes);
        Assert.Equal(0, run.Observation.RetainedBodyBytes); Assert.Equal(legacy.Truth, run.Evaluation!.Truth);
        var snapshot = PredicateObservationSnapshot.Read(run, predicate, run.Observation, PredicateObservationSnapshotMode.AvailableOperators,
            ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
        Assert.Equal(PredicateObservationSnapshotState.NoRecordedOperators, snapshot.State);
        Assert.Equal(state, snapshot.OriginalCaptureState); Assert.Empty(snapshot.Bodies);
    }

    [Fact]
    public void ViewerBudgetWithdrawsAllFramesWithoutChangingOriginalCapturedStateOrInstances()
    {
        var predicate = Binary("coglatas.or", false, true); var run = Capture(predicate); var original = run.Observation.Data;
        var before = Available(predicate, run);
        var gap = PredicateObservationSnapshot.Read(run, predicate, run.Observation, PredicateObservationSnapshotMode.AvailableOperators, 1);
        Assert.Equal(PredicateObservationSnapshotState.BodyBudgetExceeded, gap.State);
        Assert.Equal(PredicateEvaluationCaptureState.RecordedOperators, gap.OriginalCaptureState); Assert.Empty(gap.Bodies);
        Assert.Same(original, run.Observation.Data); Assert.Same(original, gap.OriginalRecordMetadata);
        var after = Available(predicate, run);
        foreach (var pair in before.Bodies) Assert.Same(pair.Value, after.Bodies[pair.Key]);
        Assert.Equal(3, before.Bodies.Count);
    }

    [Fact]
    public void AParsedTreeLargerThanCaptureCapacityStillCompletesWithoutPartialGraphOrChangedTruth()
    {
        object Group() => new { kind = "coglatas.and", operands = Enumerable.Range(0, 32).Select(_ => PlanDeclarationTests.Literal(true)).ToArray() };
        var predicate = PurePredicate.Parse(Json(new { kind = "coglatas.and", operands = Enumerable.Range(0, 32).Select(_ => Group()).ToArray() }), maximumNodes: 2000);
        var legacy = predicate.Evaluate(); var observed = Capture(predicate);
        Assert.Equal(1057, observed.Observation.ObservedNodes); Assert.Equal(PredicateTruth.True, observed.Evaluation!.Truth);
        Assert.Equal(PredicateEvaluationCaptureState.NodeLimitReached, observed.Observation.State);
        Assert.Empty(observed.Observation.Nodes); Assert.Empty(Available(predicate, observed).Bodies);
        Assert.Equal(1024, observed.Evaluation.EvaluatedPremises.Count);
        for (var i = 0; i < legacy.EvaluatedPremises.Count; i++) Assert.Same(legacy.EvaluatedPremises[i], observed.Evaluation.EvaluatedPremises[i]);
    }

    [Fact]
    public void CancellationHasNoInventedEvaluationResultOrRecoverableOperatorGraph()
    {
        var predicate = Binary("coglatas.and", true, false);
        using var source = new CancellationTokenSource(); source.Cancel(); var token = source.Token;
        foreach (var policy in new[] { new PredicateEvaluationCapturePolicy(), new(PredicateEvaluationCaptureMode.ObservedOperators) })
        {
            var run = predicate.EvaluateWithObservation(policy, token); var other = predicate.EvaluateWithObservation(policy, token);
            Assert.Null(run.Evaluation); Assert.False(run.Observation.Completed); Assert.Null(run.Observation.EvaluationDigest); Assert.Null(run.Observation.EvaluationFrame);
            Assert.Equal(PredicateEvaluationCaptureState.CaptureInterrupted, run.Observation.State); Assert.Equal(0, run.Observation.ObservedNodes);
            Assert.Empty(run.Observation.Nodes); Assert.Empty(Available(predicate, run).Bodies);
            Assert.Equal(JsonValueKind.Null, run.Observation.Data.Value.GetProperty("truth").ValueKind);
            Assert.Equal("Cancelled", run.Observation.Data.Value.GetProperty("execution").GetString());
            Assert.Equal(PredicateObservationSnapshotState.RefusedInvocation,
                PredicateObservationSnapshot.Read(other, predicate, run.Observation).State);
        }
        var completed = Capture(predicate);
        Assert.Throws<OperationCanceledException>(() => PredicateObservationSnapshot.Read(completed, predicate, completed.Observation,
            PredicateObservationSnapshotMode.AvailableOperators, cancellationToken: token));
        Assert.Equal(3, Available(predicate, completed).Bodies.Count);
    }

    [Fact]
    public void PublishedDescriptorsAndOriginalFrameMapsRemainImmutable()
    {
        var predicate = Binary("coglatas.and", true, true); var run = Capture(predicate); var snapshot = Available(predicate, run);
        var descriptors = Assert.IsAssignableFrom<IList<PredicateObservationNode>>(run.Observation.Nodes);
        Assert.Throws<NotSupportedException>(() => descriptors[0] = descriptors[0]);
        var bodies = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(snapshot.Bodies); var first = snapshot.Bodies.First();
        Assert.Throws<NotSupportedException>(() => bodies.Remove(first.Key));
        var bytes = first.Value.ToCanonicalBytes(); bytes[0] = 0;
        Assert.Same(first.Value, Available(predicate, run).Bodies[first.Key]);
        Assert.Equal(run.Observation.Nodes.Single(node => node.Digest.Value == first.Key).Digest,
            ContentDigest.Compute(PredicateEvaluationObservation.NodeDomain, first.Value.ToCanonicalBytes()));
    }

    [Fact]
    public void InvalidDeclarationAndReadBoundsFailBeforePublishingAnyBody()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateEvaluationCapturePolicy((PredicateEvaluationCaptureMode)100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateEvaluationCapturePolicy(maximumNodes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateEvaluationCapturePolicy(maximumNodes: 1025));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateEvaluationCapturePolicy(maximumBodyBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateEvaluationCapturePolicy(maximumBodyBytes: ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1));
        var predicate = Binary("coglatas.or", false, false); var run = Capture(predicate);
        Assert.Throws<ArgumentOutOfRangeException>(() => PredicateObservationSnapshot.Read(run, predicate, run.Observation, (PredicateObservationSnapshotMode)100));
        Assert.Throws<ArgumentOutOfRangeException>(() => PredicateObservationSnapshot.Read(run, predicate, run.Observation, maximumBodyBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PredicateObservationSnapshot.Read(run, predicate, run.Observation,
            maximumBodyBytes: ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1));
        Assert.Equal(PredicateTruth.False, run.Evaluation!.Truth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingAViewDoesNotEraseAnEarlierOriginalCaptureGap(bool countGap)
    {
        var predicate = Binary("coglatas.and", true, true);
        var run = predicate.EvaluateWithObservation(new(PredicateEvaluationCaptureMode.ObservedOperators,
            maximumNodes: countGap ? 1 : PredicateEvaluationCapturePolicy.HardNodeLimit,
            maximumBodyBytes: countGap ? ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes : 1));
        var state = run.Observation.State; var metadata = run.Observation.Data;
        using var source = new CancellationTokenSource(); source.Cancel(); var token = source.Token;
        Assert.Throws<OperationCanceledException>(() => PredicateObservationSnapshot.Read(run, predicate, run.Observation,
            PredicateObservationSnapshotMode.AvailableOperators, cancellationToken: token));
        Assert.Equal(state, run.Observation.State); Assert.Same(metadata, run.Observation.Data); Assert.True(run.Observation.Completed);
        var later = Available(predicate, run); Assert.Equal(state, later.OriginalCaptureState); Assert.Empty(later.Bodies);
    }

    [Fact]
    public void OptInObservationRejectsDeeperOpaqueJsonWithoutChangingTheValidLegacyEvaluationOrInput()
    {
        object basis = new { coverage = "coglatas.partial" };
        for (var depth = 0; depth < 70; depth++) basis = new { nested = basis };
        var raw = JsonSerializer.Serialize(new { kind = "coglatas.literal", value = new {
            type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true },
            state = "coglatas.unknown", reason = "coglatas.missing-input", basis } }, new JsonSerializerOptions { MaxDepth = 128 });
        var predicate = PurePredicate.Parse(SourceJson.Parse(raw, new(MaximumDepth: 128))); var input = predicate.Data; var legacy = predicate.Evaluate();
        Assert.Equal(PredicateTruth.Unknown, legacy.Truth);
        Assert.Throws<UnsupportedSourceException>(() => predicate.EvaluateWithObservation(new(PredicateEvaluationCaptureMode.ObservedOperators)));
        Assert.Same(input, predicate.Data); Assert.Same(legacy.EvaluatedPremises[0], predicate.Evaluate().EvaluatedPremises[0]);
    }
}
