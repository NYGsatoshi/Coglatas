using System.ComponentModel;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.RegionalPredicateObservationCompilerHostTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticRegionalOperatorRecordingTests
{
    private static ProjectSource Constraints(ProjectSource source, bool? truth, int count = 1, string? operatorExample = null)
    {
        var template = CompilerAnalysisFixtures.Constraint(truth).Source;
        var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
        var relations = JsonNode.Parse(source.Relations.CanonicalText)!.AsArray();
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var entity = 85 + ordinal;
            var constraint = JsonNode.Parse(template.Declarations.CanonicalText)!.AsArray()[0]!.DeepClone();
            constraint["entityId"] = Id(entity); declarations.Add(constraint);
            var predicate = constraint["payload"]!["predicate"]!;
            if (operatorExample is not null)
            {
                predicate = JsonNode.Parse(ObservedPredicate(operatorExample).CanonicalText)!;
                constraint["payload"]!["predicate"] = predicate;
                if (operatorExample.StartsWith("requirement-", StringComparison.Ordinal))
                {
                    constraint["kind"] = "coglatas.requirement";
                    constraint["payload"] = Obj(new { target = CompilerAnalysisFixtures.Reference(6, source.Context),
                        condition = predicate.DeepClone(), evidenceKind = "coglatas.model-proof" });
                    continue;
                }
            }
            relations.Add(Obj(new { relationId = Id(200 + ordinal), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = CompilerAnalysisFixtures.Reference(entity, source.Context) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = CompilerAnalysisFixtures.Reference(6, source.Context) },
                payload = new { predicateReference = CompilerAnalysisFixtures.Reference(entity, source.Context),
                    version = source.Context.CommittedRevision!.RevisionId.ToString(),
                    predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(predicate).ToCanonicalBytes()).Data.Value } }));
        }
        return ProjectSource.Create(source.Context, source.Documents, Json(declarations), Json(relations));
    }

    private static CompilerBindingClosureTests.Fixture TwoRegions(bool? truth, int rootCount = 1, int externalCount = 1, string? operatorExample = null)
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: false);
        var external = Assert.Single(fixture.Snapshots);
        var changed = Constraints(external.Snapshot.Source, truth, externalCount, operatorExample);
        var local = JsonNode.Parse(Constraints(fixture.Input.Source, truth, rootCount).Declarations.CanonicalText)!.AsArray();
        local[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(changed.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]![0]!["digest"] = JsonNode.Parse(changed.Digest.Data.CanonicalText);
        settings["rules"] = CompilerAnalysisFixtures.AllRulesSettings()["rules"]!.DeepClone();
        if (rootCount > 1 || externalCount > 1) settings["executionProfile"]!["maximumOperations"] = 10000000;
        var rootWithConstraint = Constraints(fixture.Input.Source, truth, rootCount);
        var input = Upgrade(new(ProjectSource.Create(rootWithConstraint.Context, rootWithConstraint.Documents,
            Json(local), rootWithConstraint.Relations), Json(settings)));
        return fixture with { Input = input, Snapshots = [new(external.SnapshotId, external.Version, new(changed, true, true, true))] };
    }


    private static async Task<RegionalPredicateObservationCompilerRecordedInvocation> Run(SyntheticWorkbenchViewModel vm,
        LocalRegionalPredicateObservationCompilerRecordingSession session, RegionalPredicateObservationCompilerHostRequest request)
    {
        var declaration = session.Prepare(request, Guid.NewGuid()); vm.BeginRegionalOperatorSynthetic(declaration, request);
        var actual = await session.ExecuteAsync(declaration, request); Assert.True(vm.TryAcceptRegionalOperator(actual)); return actual;
    }
    private static CompilerDependencyRegion Region(RegionalPredicateObservationCompilerRecordedInvocation actual, bool root = true) =>
        actual.Response.Execution!.Compilation.Dependencies!.Regions.Single(r => r.Binding.IsRoot == root);
    private static bool Read(SyntheticWorkbenchViewModel vm, RegionalPredicateObservationCompilerRecordedInvocation actual,
        int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default) =>
        vm.ReadRecordedRegionalOperators(actual.Response.Execution!.Compilation.Result.ResultDigest,
            CompilerRegionalPredicateSnapshotMode.AvailableOperators, bytes, token);
    private static void AdmitAndSelect(SyntheticWorkbenchViewModel vm, RegionalPredicateObservationCompilerRecordedInvocation actual, bool root = true)
    {
        Assert.True(vm.LoadSyntheticScopedSources());
        var region = Region(actual, root); Assert.True(vm.SelectRegionalOperatorDiagnostic(region, Assert.Single(region.Diagnostics)));
    }
    private static SourceJson ObservedPredicate(string example)
    {
        object Literal(bool? truth) => JsonNode.Parse(CompilerAnalysisFixtures.Constraint(truth).Source.Declarations.Value
            .EnumerateArray().First().GetProperty("payload").GetProperty("predicate").GetRawText())!;
        object Op(string kind, params object[] operands) => new { kind, operands };
        return Json(Obj(example switch {
            "nested-duplicates" or "requirement-false" => Op("coglatas.and", Op("coglatas.or", Literal(false), Literal(true)), Op("coglatas.not", Literal(true)), Literal(null)),
            "or-unknown" or "requirement-unknown" => Op("coglatas.or", Literal(null), Literal(false)),
            "requirement-true" => Op("coglatas.or", Literal(true), Literal(null)),
            _ => throw new ArgumentException("Unknown actual regional predicate fixture.", nameof(example)) }));
    }
    public static IEnumerable<object[]> OperatorExamples()
    {
        foreach (var (example, nodes) in new[] { ("nested-duplicates", 6), ("or-unknown", 3), ("requirement-false", 6), ("requirement-true", 2), ("requirement-unknown", 3) })
        foreach (var mode in Enum.GetValues<PredicateEvaluationCaptureMode>()) yield return [example, nodes, mode];
    }

    [Theory]
    [MemberData(nameof(OperatorExamples))]
    public async Task ActualCompoundAndRequirementRegionalFramesStayBoundToTheSelectedOriginalDiagnostic(string example, int observedNodes, PredicateEvaluationCaptureMode mode)
    {
        var f = TwoRegions(false, rootCount: 0, operatorExample: example);
        var request = new RegionalPredicateObservationCompilerHostRequest(new(new(f.Input), 7, f.Snapshots), new(mode));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var execution = actual.Response.Execution!;
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.ReadRegionalOperatorRecordingMetadata());
        Assert.Equal(1, execution.Observation.StartedEvaluations); Assert.Equal(1, execution.Observation.CompletedEvaluations);
        Assert.Equal(observedNodes, execution.Observation.ObservedNodes);
        var region = Region(actual, false); Assert.NotEmpty(region.Diagnostics);
        var requirement = example.StartsWith("requirement-", StringComparison.Ordinal);
        if (requirement) Assert.False(execution.Compilation.Result.EvidenceComplete);
        foreach (var diagnostic in region.Diagnostics)
        {
            Assert.True(vm.SelectRegionalOperatorDiagnostic(region, diagnostic));
            Assert.True(vm.ReadRecordedRegionalOperators(execution.Compilation.Result.ResultDigest)); Assert.Empty(vm.RecordedRegionalOperators!.Bodies);
            Assert.True(Read(vm, actual)); var snapshot = vm.RecordedRegionalOperators!;
            var selected = !requirement || diagnostic.Code == "coglatas.analysis.requirement-condition-false";
            var retained = selected && mode == PredicateEvaluationCaptureMode.ObservedOperators;
            Assert.Equal(retained ? observedNodes : 0, snapshot.Bodies.Count); Assert.Equal(retained ? 1 : 0, snapshot.EvaluationFrames.Count);
            var original = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
                region, diagnostic, CompilerRegionalPredicateSnapshotMode.AvailableOperators);
            Assert.Equal(original.Data, snapshot.Data); Assert.Equal(original.Digest, snapshot.Digest);
            if (requirement && !selected && mode == PredicateEvaluationCaptureMode.ObservedOperators)
                Assert.Equal(CompilerRegionalPredicateSnapshotState.NoObservedRegionalDiagnostic, snapshot.State);
            Assert.Same(region, vm.SelectedRegionalOperatorRegion); Assert.Same(diagnostic, vm.SelectedRegionalOperatorDiagnostic);
            Assert.Equal(execution.Compilation.Result.Data, vm.Result!.Data);
        }
    }

    public static IEnumerable<object[]> Modes()
    {
        foreach (var truth in new bool?[] { false, true, null })
        foreach (var mode in Enum.GetValues<PredicateEvaluationCaptureMode>()) yield return [truth!, mode];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task CurrentActualRegionalRunNeedsExplicitSourceAndExactRegionalDiagnostic(bool? truth, PredicateEvaluationCaptureMode mode)
    {
        var f = TwoRegions(truth); var request = new RegionalPredicateObservationCompilerHostRequest(new(new(f.Input), 7, f.Snapshots), new(mode));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); var execution = actual.Response.Execution!;
        Assert.Null(vm.RegionalOperatorReceiptMetadata); Assert.Null(vm.RegionalOperatorRecordMetadata); Assert.Null(vm.RecordedRegionalOperators);
        Assert.False(vm.ReadRegionalOperatorRecordingMetadata()); Assert.False(Read(vm, actual));
        Assert.True(vm.LoadSyntheticRootSource(execution.Compilation.Result.ResultDigest)); Assert.True(vm.ReadRegionalOperatorRecordingMetadata());
        Assert.Same(actual.Receipt.Data, vm.RegionalOperatorReceiptMetadata); Assert.Same(actual.Receipt.ObservationMetadata, vm.RegionalOperatorRecordMetadata);
        Assert.False(Read(vm, actual)); Assert.Null(vm.OperatorReceiptMetadata); Assert.Null(vm.RecordedOperators);
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.ReadRegionalOperatorRecordingMetadata());
        foreach (var region in execution.Compilation.Dependencies!.Regions)
        {
            if (truth == true) { Assert.Empty(region.Diagnostics); continue; }
            var diagnostic = Assert.Single(region.Diagnostics); Assert.True(vm.SelectRegionalOperatorDiagnostic(region, diagnostic));
            Assert.Same(region, vm.SelectedRegionalOperatorRegion); Assert.Same(diagnostic, vm.SelectedRegionalOperatorDiagnostic);
            Assert.True(vm.ReadRecordedRegionalOperators(execution.Compilation.Result.ResultDigest)); Assert.Empty(vm.RecordedRegionalOperators!.Bodies);
            Assert.True(Read(vm, actual));
            var direct = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation, region, diagnostic,
                CompilerRegionalPredicateSnapshotMode.AvailableOperators);
            Assert.Equal(direct.Data, vm.RecordedRegionalOperators!.Data); Assert.Equal(direct.Digest, vm.RecordedRegionalOperators.Digest);
            Assert.Equal(mode == PredicateEvaluationCaptureMode.Manifest ? 0 : 1, vm.RecordedRegionalOperators.Bodies.Count);
            Assert.Equal(execution.Compilation.Result.Data, vm.Result!.Data);
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RootAdmissionCannotExposeExternalRegionalFramesAndUnrelatedProtocolsCannotSelectTheRun(bool root)
    {
        var f = TwoRegions(false); var core = new CompilerHostRequest(new(f.Input), 7, f.Snapshots); var request = Full(core);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); vm.BeginRegionalOperatorSynthetic(manifest, request);
        var ordinary = await new LocalNetCompilerHost(Profile(f.Input)).CompileAsync(core); Assert.False(vm.TryAccept(ordinary));
        var actual = await session.ExecuteAsync(manifest, request); Assert.True(vm.TryAcceptRegionalOperator(actual));
        Assert.True(vm.LoadSyntheticRootSource(actual.Response.Execution!.Compilation.Result.ResultDigest));
        var region = Region(actual, root); var diagnostic = Assert.Single(region.Diagnostics);
        Assert.Equal(root, vm.SelectRegionalOperatorDiagnostic(region, diagnostic)); Assert.Equal(root, Read(vm, actual));
        if (!root) Assert.Null(vm.RecordedRegionalOperators);
        AdmitAndSelect(vm, actual, root); Assert.True(Read(vm, actual));
        var retained = vm.RecordedRegionalOperators!;
        Assert.False(vm.ReadRecordedOperators(actual.Response.Execution.Compilation.Result.ResultDigest)); Assert.Null(vm.RecordedOperators);
        Assert.Same(retained, vm.RecordedRegionalOperators);
        Assert.True(vm.SelectDiagnostic(actual.Response.Execution.Compilation.Result.Diagnostics.First())); Assert.Null(vm.RecordedRegionalOperators);
    }

    [Fact]
    public async Task GenuineOlderPairRegionAndDiagnosticCannotOwnTheCurrentRegionalSelection()
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var old = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var declaration = session.Prepare(request, Guid.NewGuid()); vm.BeginRegionalOperatorSynthetic(declaration, request);
        var actual = await session.ExecuteAsync(declaration, request);
        Assert.Equal(old.Receipt.ObservationRecordDigest, actual.Receipt.ObservationRecordDigest); Assert.False(vm.TryAcceptRegionalOperator(old));
        Assert.Equal("Waiting", vm.State); Assert.True(vm.TryAcceptRegionalOperator(actual)); AdmitAndSelect(vm, actual); Assert.True(Read(vm, actual));
        var current = Region(actual); var earlier = Region(old);
        Assert.Equal(earlier.Digest, current.Digest);
        Assert.False(vm.SelectRegionalOperatorDiagnostic(earlier, Assert.Single(earlier.Diagnostics))); Assert.Null(vm.RecordedRegionalOperators);
        Assert.False(vm.SelectRegionalOperatorDiagnostic(current, Assert.Single(earlier.Diagnostics))); Assert.Null(vm.SelectedRegionalOperatorDiagnostic);
        Assert.True(vm.SelectRegionalOperatorDiagnostic(current, Assert.Single(current.Diagnostics))); Assert.True(Read(vm, actual));
        var summary = actual.Response.Execution!.Compilation.Result.Diagnostics.Single(d => d.Code == "coglatas.analysis.dependency-finding");
        Assert.False(vm.SelectRegionalOperatorDiagnostic(Region(actual, false), summary)); Assert.Null(vm.RecordedRegionalOperators);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task NewerRegionalSelectionAndReadSurviveAnOlderSnapshotNotification(int notification)
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); AdmitAndSelect(vm, actual); var next = Region(actual, false);
        var events = 0; var switched = false; ProjectCompilerRegionalPredicateObservationSnapshot? newer = null;
        vm.PropertyChanged += (sender, _) => {
            if (switched || ++events != notification) return;
            switched = true;
            var notifying = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            Assert.True(notifying.SelectRegionalOperatorDiagnostic(next, Assert.Single(next.Diagnostics)));
            Assert.True(Read(notifying, actual)); newer = notifying.RecordedRegionalOperators;
        };
        Assert.False(Read(vm, actual)); Assert.True(switched); Assert.Same(next, vm.SelectedRegionalOperatorRegion); Assert.Same(newer, vm.RecordedRegionalOperators);
        Assert.Single(vm.RecordedRegionalOperators!.Bodies);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task RegionalDiagnosticNavigationPreservesTheSelectedOriginalFrames(bool root, bool rootOnly)
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request);
        if (rootOnly)
        {
            Assert.True(vm.LoadSyntheticRootSource(actual.Response.Execution!.Compilation.Result.ResultDigest));
            var selected = Region(actual); Assert.True(vm.SelectRegionalOperatorDiagnostic(selected, Assert.Single(selected.Diagnostics)));
        }
        else AdmitAndSelect(vm, actual, root);
        Assert.True(Read(vm, actual));
        var region = vm.SelectedRegionalOperatorRegion; var diagnostic = vm.SelectedRegionalOperatorDiagnostic; var retained = vm.RecordedRegionalOperators;
        Assert.True(vm.NavigateRegionalOperatorDiagnostic()); Assert.Equal("ready", vm.NavigationState);
        Assert.Same(region, vm.SelectedRegionalOperatorRegion); Assert.Same(diagnostic, vm.SelectedRegionalOperatorDiagnostic);
        Assert.Same(retained, vm.RecordedRegionalOperators); Assert.True(Read(vm, actual));
        Assert.Equal(diagnostic!.Location, root ? vm.SourceTarget : vm.ScopedSourceTarget);
    }

    public static IEnumerable<object[]> NavigationBoundaries()
    {
        foreach (var root in new[] { true, false })
        foreach (var boundary in new[] { "reset", "dispose", "selection", "root-source", "scoped-sources" }) yield return [root, boundary];
    }

    [Theory]
    [MemberData(nameof(NavigationBoundaries))]
    public async Task RegionalNavigationCannotReportSuccessAcrossANewerBoundary(bool root, string boundary)
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); AdmitAndSelect(vm, actual, root); Assert.True(Read(vm, actual));
        var retained = vm.RecordedRegionalOperators!; var next = Region(actual, !root); var switched = false;
        ProjectCompilerRegionalPredicateObservationSnapshot? newer = null;
        vm.PropertyChanged += (sender, _) => {
            if (switched) return;
            switched = true;
            var notifying = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            switch (boundary)
            {
                case "reset": notifying.Reset(); break;
                case "dispose": notifying.Dispose(); break;
                case "selection":
                    Assert.True(notifying.SelectRegionalOperatorDiagnostic(next, Assert.Single(next.Diagnostics)));
                    Assert.True(Read(notifying, actual)); newer = notifying.RecordedRegionalOperators; break;
                case "root-source": Assert.True(notifying.LoadSyntheticRootSource(actual.Response.Execution!.Compilation.Result.ResultDigest)); break;
                case "scoped-sources": Assert.True(notifying.LoadSyntheticScopedSources()); break;
                default: throw new ArgumentException("Unknown navigation boundary.", nameof(boundary));
            }
        };
        Assert.False(vm.NavigateRegionalOperatorDiagnostic()); Assert.True(switched); Assert.Single(retained.Bodies);
        if (boundary == "selection")
        {
            Assert.Same(next, vm.SelectedRegionalOperatorRegion); Assert.Same(newer, vm.RecordedRegionalOperators);
            Assert.NotSame(retained, newer); Assert.Single(newer!.Bodies);
        }
        else Assert.Null(vm.RecordedRegionalOperators);
        if (boundary is "reset" or "dispose") Assert.Null(vm.Result);
        else Assert.Same(actual.Response.Execution!.Compilation.Result, vm.Result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewerReadOfTheSameRegionalSelectionSurvivesNavigationNotification(bool root)
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); AdmitAndSelect(vm, actual, root); Assert.True(Read(vm, actual));
        var retained = vm.RecordedRegionalOperators; var switched = false;
        ProjectCompilerRegionalPredicateObservationSnapshot? newer = null;
        vm.PropertyChanged += (sender, _) => {
            if (switched) return;
            switched = true;
            var notifying = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            Assert.True(Read(notifying, actual)); newer = notifying.RecordedRegionalOperators;
        };
        Assert.True(vm.NavigateRegionalOperatorDiagnostic()); Assert.True(switched);
        Assert.NotSame(retained, newer); Assert.Same(newer, vm.RecordedRegionalOperators); Assert.Single(newer!.Bodies);
    }

    private sealed class CancelOnNotification : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly int _notification;
        private int _events;
        internal CancellationToken Token => _source.Token;
        internal CancelOnNotification(SyntheticWorkbenchViewModel vm, int notification)
        { _vm = vm; _notification = notification; vm.PropertyChanged += Changed; }
        private void Changed(object? sender, PropertyChangedEventArgs args) { if (++_events == _notification) _source.Cancel(); }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task ActualSnapshotCancellationWithdrawsOnlyTheCancelledRead(int notification)
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); AdmitAndSelect(vm, actual); Assert.True(Read(vm, actual)); var retained = vm.RecordedRegionalOperators!;
        using var cancellation = new CancelOnNotification(vm, notification);
        Assert.Throws<OperationCanceledException>(() => Read(vm, actual, token: cancellation.Token)); Assert.Null(vm.RecordedRegionalOperators);
        Assert.Single(retained.Bodies); Assert.NotNull(vm.SelectedRegionalOperatorDiagnostic);
    }

    private sealed class ThrowingPublicationObserver : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly Func<bool> _hasPublication;
        private readonly Action? _replace;
        private readonly bool _throwOnPublication;
        private readonly CancellationTokenSource _source = new();
        private bool _fired;

        public CancellationToken Token => _source.Token;
        public InvalidOperationException PublicationError { get; } = new("Regional publication observer failed.");
        public InvalidOperationException WithdrawalError { get; } = new("Regional withdrawal observer failed.");
        public int WithdrawalNotifications { get; private set; }

        public ThrowingPublicationObserver(SyntheticWorkbenchViewModel view, Func<bool> hasPublication, Action? replace,
            bool throwOnPublication)
        {
            _view = view;
            _hasPublication = hasPublication;
            _replace = replace;
            _throwOnPublication = throwOnPublication;
            _view.PropertyChanged += Changed;
        }

        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (_fired)
            {
                if (Token.IsCancellationRequested)
                {
                    WithdrawalNotifications++;
                    if (!_hasPublication()) throw WithdrawalError;
                }
                return;
            }
            if (!_hasPublication()) return;
            _fired = true;
            _replace?.Invoke();
            _source.Cancel();
            if (_throwOnPublication) throw PublicationError;
        }

        public void Dispose()
        {
            _view.PropertyChanged -= Changed;
            _source.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThrowingMetadataObserverWithdrawsOnlyItsCancelledRegionalRead(bool replace) =>
        VerifyMetadataWithdrawal(replace, throwOnPublication: true);

    [Fact]
    public Task ThrowingRegionalMetadataWithdrawalObserverPreservesCancellation() =>
        VerifyMetadataWithdrawal(replace: false, throwOnPublication: false);

    private static async Task VerifyMetadataWithdrawal(bool replace, bool throwOnPublication)
    {
        var fixture = TwoRegions(false);
        var request = Full(new(new(fixture.Input), 7, fixture.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request);
        AdmitAndSelect(vm, actual);
        Assert.True(vm.ReadRegionalOperatorRecordingMetadata());
        var result = vm.Result;
        var receipt = Assert.IsType<SourceJson>(vm.RegionalOperatorReceiptMetadata);
        var recording = Assert.IsType<SourceJson>(vm.RegionalOperatorRecordMetadata);

        void PublishReplacement() => Assert.True(vm.ReadRegionalOperatorRecordingMetadata());
        using var observer = new ThrowingPublicationObserver(vm, () => vm.RegionalOperatorReceiptMetadata is not null,
            replace ? PublishReplacement : null, throwOnPublication);
        var error = Record.Exception(() => { vm.ReadRegionalOperatorRecordingMetadata(observer.Token); });

        Assert.True(observer.Token.IsCancellationRequested);
        Assert.Same(result, vm.Result);
        Assert.Same(receipt, actual.Receipt.Data);
        Assert.Same(recording, actual.Receipt.ObservationMetadata);
        if (replace)
        {
            Assert.Same(receipt, vm.RegionalOperatorReceiptMetadata);
            Assert.Same(recording, vm.RegionalOperatorRecordMetadata);
        }
        else
        {
            Assert.Null(vm.RegionalOperatorReceiptMetadata);
            Assert.Null(vm.RegionalOperatorRecordMetadata);
        }
        AssertObserverError(observer, error, throwOnPublication);
        Assert.Equal(replace ? 0 : 1, observer.WithdrawalNotifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThrowingSnapshotObserverWithdrawsOnlyItsCancelledRegionalRead(bool replace) =>
        VerifySnapshotWithdrawal(replace, throwOnPublication: true);

    [Fact]
    public Task ThrowingRegionalSnapshotWithdrawalObserverPreservesCancellation() =>
        VerifySnapshotWithdrawal(replace: false, throwOnPublication: false);

    private static async Task VerifySnapshotWithdrawal(bool replace, bool throwOnPublication)
    {
        var fixture = TwoRegions(false);
        var request = Full(new(new(fixture.Input), 7, fixture.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request);
        AdmitAndSelect(vm, actual);
        Assert.True(Read(vm, actual));
        var result = vm.Result;
        var selection = vm.SelectedRegionalOperatorDiagnostic;
        var retained = Assert.IsType<ProjectCompilerRegionalPredicateObservationSnapshot>(vm.RecordedRegionalOperators);
        ProjectCompilerRegionalPredicateObservationSnapshot? replacement = null;

        void PublishReplacement()
        {
            Assert.True(Read(vm, actual));
            replacement = vm.RecordedRegionalOperators;
        }
        using var observer = new ThrowingPublicationObserver(vm, () => vm.RecordedRegionalOperators is not null,
            replace ? PublishReplacement : null, throwOnPublication);
        var error = Record.Exception(() => { Read(vm, actual, token: observer.Token); });

        Assert.True(observer.Token.IsCancellationRequested);
        Assert.Same(result, vm.Result);
        Assert.Same(selection, vm.SelectedRegionalOperatorDiagnostic);
        Assert.Single(retained.Bodies);
        if (replace)
        {
            Assert.NotNull(replacement);
            Assert.NotSame(retained, replacement);
            Assert.Same(replacement, vm.RecordedRegionalOperators);
            Assert.Single(replacement.Bodies);
        }
        else Assert.Null(vm.RecordedRegionalOperators);
        AssertObserverError(observer, error, throwOnPublication);
        Assert.Equal(replace ? 0 : 1, observer.WithdrawalNotifications);
    }

    private static void AssertObserverError(ThrowingPublicationObserver observer, Exception? error, bool throwOnPublication)
    {
        if (throwOnPublication)
        {
            Assert.Same(observer.PublicationError, error);
            return;
        }
        var cancelled = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(observer.Token, cancelled.CancellationToken);
        Assert.Same(observer.WithdrawalError, cancelled.InnerException);
    }

    [Fact]
    public async Task ViewerGapAndDeniedSourceReloadPreserveOriginalCompilerFactsAndCallerOwnedFrames()
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var actual = await Run(vm, session, request); AdmitAndSelect(vm, actual); Assert.True(vm.ReadRegionalOperatorRecordingMetadata()); Assert.True(Read(vm, actual));
        var retained = vm.RecordedRegionalOperators!; var execution = actual.Response.Execution!;
        Assert.True(Read(vm, actual, 1)); Assert.Equal(CompilerRegionalPredicateSnapshotState.BodyBudgetExceeded, vm.RecordedRegionalOperators!.State);
        Assert.Empty(vm.RecordedRegionalOperators.Bodies); Assert.Equal(retained.OriginalCaptureState, vm.RecordedRegionalOperators.OriginalCaptureState);
        Assert.Equal(execution.Compilation.Result.Data, vm.Result!.Data);
        Assert.False(vm.LoadSyntheticRootSource(execution.Compilation.Input.InputDigest)); Assert.Null(vm.RegionalOperatorReceiptMetadata);
        Assert.Null(vm.RegionalOperatorRecordMetadata); Assert.Null(vm.RecordedRegionalOperators); Assert.Single(retained.Bodies);
        AdmitAndSelect(vm, actual); Assert.True(Read(vm, actual));
    }

    [Theory]
    [InlineData("Empty")] [InlineData("Waiting")] [InlineData("Ready")]
    public async Task ResetDuringBeginOrAcceptanceCannotReviveRegionalIntent(string trigger)
    {
        var f = TwoRegions(false); var request = Full(new(new(f.Input), 7, f.Snapshots));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        using var vm = new SyntheticWorkbenchViewModel();
        var manifest = session.Prepare(request, Guid.NewGuid()); var reset = false;
        vm.PropertyChanged += (sender, _) => {
            var notifying = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (!reset && notifying.State == trigger) { reset = true; notifying.Reset(); }
        };
        vm.BeginRegionalOperatorSynthetic(manifest, request); var actual = await session.ExecuteAsync(manifest, request);
        Assert.False(vm.TryAcceptRegionalOperator(actual)); Assert.True(reset); Assert.Equal("Empty", vm.State);
        Assert.Null(vm.Result); Assert.Null(vm.RegionalOperatorReceiptMetadata); Assert.Null(vm.RecordedRegionalOperators);
    }
}
