using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Xunit;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent synthetic Scenario input transitions; no runtime authority or execution qualification.</summary>
public sealed class SecurityScenarioCoreCandidateTests
{
    private static readonly TenantId Tenant = new(Id(1));
    private static readonly ProjectId Project = new(Id(2));
    private static readonly BranchRef Branch = new(Tenant, Project, new(Id(3)));
    private static readonly DocumentId FirstDocument = new(Id(10));
    private static readonly DocumentId SecondDocument = new(Id(11));
    private static readonly EntityId FirstTask = new(Id(20));
    private static readonly EntityId SecondTask = new(Id(21));
    private static readonly EntityId Milestone = new(Id(22));
    private static readonly PortId BooleanInput = new(Id(30));
    private static readonly PortId TextInput = new(Id(31));
    private static readonly PortId ReferenceInput = new(Id(32));
    private static readonly PortId BooleanOutput = new(Id(33));
    private static readonly ScenarioId Scenario = new(Id(100));
    private static readonly ScenarioRevisionId InitialRevision = new(Id(101));
    private static readonly ScenarioRevisionId NextRevision = new(Id(102));
    private static readonly string[] PinFields = ["authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence"];

    [Fact]
    public void PreparedScenarioBindsExactBasisOverlayAndAllSettings()
    {
        var settings = Settings();
        var node = Node(settings);
        for (var index = 0; index < PinFields.Length; index++)
            node[PinFields[index]] = new JsonArray(JsonSerializer.SerializeToNode(Pin(Committed(700 + index), 800 + index)));
        var basis = Make(settings: Json(node));
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        Assert.Same(basis.Input, plan.Basis);
        Assert.Same(plan.Input, plan.Materialize(basis.Input));
        var context = Assert.IsType<ScenarioRef>(plan.Input.Source.Context.Scenario);
        Assert.Equal(Scenario, context.ScenarioId);
        Assert.Equal(InitialRevision, context.ScenarioRevisionId);
        Assert.Equal(basis.Input.Source.Context, context.BaseContext);
        Assert.Equal(plan.OverlayDigest, context.OverlayDigest);
        Assert.Equal(ContentDigest.Compute("coglatas.local-scenario-overlay/1", plan.Overlay.ToCanonicalBytes()), plan.OverlayDigest);
        Assert.Equal(basis.Input.InputDigest.Data.CanonicalText, Property(plan.Overlay, "baseInputDigest"));
        Assert.Equal(basis.Input.Source.Digest.Data.CanonicalText, Property(plan.Overlay, "baseSourceDigest"));
        Assert.Equal(basis.Input.Source.Context.Data.CanonicalText, Property(plan.Overlay, "baseContext"));
        Assert.Equal(Property(plan.Overlay, "overrides"), Json(plan.Input.Settings.Value.GetProperty("scenario").GetProperty("assumptions")).CanonicalText);
        foreach (var member in basis.Input.Settings.Value.EnumerateObject().Where(member => member.Name != "scenario"))
            Assert.Equal(Json(member.Value).CanonicalText, Property(plan.Input.Settings, member.Name));
        Assert.NotEqual(basis.Input.InputDigest, plan.Input.InputDigest);
        Assert.NotEqual(basis.Input.Source.Digest, plan.Input.Source.Digest);
        var dependency = Assert.Single(TypedRelationReader.Read(plan.Input.Source)).Relation;
        Assert.NotNull(dependency);
        Assert.Equal(RelationKinds.DependsOn, dependency!.Kind);
        Assert.Equal(plan.Input.Source.Context, dependency.Source.Reference.Context);
        Assert.Equal(plan.Input.Source.Context, dependency.Target.Reference.Context);
        Assert.Equal(FirstTask, dependency.Source.Reference.EntityId);
        Assert.Equal(SecondTask, dependency.Target.Reference.EntityId);
        Assert.Equal("0", Assert.IsType<DependencyRelationPayload>(dependency.Payload).Lag.Data.Value.GetProperty("value").GetString());
        var expectedDependency = Node(Json(basis.Input.Source.Relations.Value[0]));
        expectedDependency["source"]!["reference"]!["context"] = JsonNode.Parse(plan.Input.Source.Context.Data.CanonicalText);
        expectedDependency["target"]!["reference"]!["context"] = JsonNode.Parse(plan.Input.Source.Context.Data.CanonicalText);
        Assert.Equal(Json(expectedDependency), dependency.Data);
        Assert.Equal(basis.Input.Source.Manifest.Data, plan.Input.Source.Manifest.Data);
        Assert.Equal(basis.Input.Source.Documents.Select(document => document.Data.CanonicalText), plan.Input.Source.Documents.Select(document => document.Data.CanonicalText));
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void TypedOverridesApplyAtomicallyWithoutChangingCapturedInputs()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var submitted = new ProjectScenarioOverride[]
        {
            DurationOverride(basis, 75), InputOverride(basis, BooleanInput, Known("coglatas.boolean", true)),
            new(Target(basis, Milestone), ProjectScenarioOverrideKind.MilestoneTarget, Known("coglatas.instant", "2032-05-06T07:08:09.0000000Z"))
        };
        var plan = Ready(basis, submitted);
        var capturedChanges = plan.Changes.ToArray();
        var capturedInput = Frozen(plan.Input);
        Array.Fill(submitted, DurationOverride(basis, 999));
        Assert.Equal(3, plan.Changes.Count);
        Assert.Equal(capturedChanges, plan.Changes.ToArray());
        Assert.Equal(capturedInput, Frozen(plan.Input));
        Assert.Equal("75", Task(plan.Input, FirstTask).Duration.Data.Value.GetProperty("value").GetString());
        Assert.True(Task(plan.Input, FirstTask).Ports.Single(port => port.PortId == BooleanInput).DefaultValue!.Data.Value.GetProperty("value").GetBoolean());
        Assert.Equal("2032-05-06T07:08:09.0000000Z", Declaration<MilestonePlanDeclaration>(plan.Input, Milestone).Target.Data.Value.GetProperty("value").GetString());
        Assert.All(plan.Changes, change => Assert.NotNull(change.Before));
        Assert.Equal(original, Frozen(basis.Input));
        var copied = plan.Input.Source.Data.ToCanonicalBytes();
        copied[0] = 0;
        Assert.Equal((byte)'{', plan.Input.Source.Data.ToCanonicalBytes()[0]);
    }

    [Fact]
    public void InvalidLaterOverrideCannotPublishAPartialScenario()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var valid = DurationOverride(basis, 75);
        Assert.Equal(ProjectScenarioState.Ready, Preview(basis, valid).State);
        var invalid = new ProjectScenarioOverride(Target(basis, SecondTask), ProjectScenarioOverrideKind.TaskDuration, Known("coglatas.elapsed-duration", "-1"));
        Refused(Preview(basis, valid, invalid), ProjectScenarioState.InvalidOverride);
        Refused(Preview(basis, valid, invalid with { Value = Known("coglatas.boolean", true) }), ProjectScenarioState.InvalidOverride);
        Assert.Equal(original, Frozen(basis.Input));
        Assert.Equal("40", Task(basis.Input, FirstTask).Duration.Data.Value.GetProperty("value").GetString());
    }

    [Fact]
    public void PortDirectionTypeAndTargetKindsAreEnforced()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        Assert.Equal(ProjectScenarioState.Ready, Preview(basis, InputOverride(basis, BooleanInput, Known("coglatas.boolean", true))).State);
        Refused(Preview(basis, InputOverride(basis, BooleanOutput, Known("coglatas.boolean", true))), ProjectScenarioState.Unavailable);
        Refused(Preview(basis, InputOverride(basis, new(Id(999)), Known("coglatas.boolean", true))), ProjectScenarioState.Unavailable);
        Refused(Preview(basis, InputOverride(basis, BooleanInput, Known("coglatas.text", "true"))), ProjectScenarioState.InvalidOverride);
        Refused(Preview(basis, DurationOverride(basis, 75) with { Target = Target(basis, Milestone) }), ProjectScenarioState.Unsupported);
        Refused(Preview(basis, DurationOverride(basis, 75) with { Target = ProjectLocation.Create(FirstDocument, basis.Input.Source.Context) }), ProjectScenarioState.InvalidOverride);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void StaleExpectedInputsAndForeignTargetContextsAreRefused()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var request = DurationOverride(basis, 75);
        Assert.Equal(ProjectScenarioState.Ready, Preview(basis, request).State);
        Refused(ProjectScenarios.Prepare(basis.Model, Digest("unrelated-input"), Scenario, InitialRevision, [request]), ProjectScenarioState.StaleInput);
        foreach (var context in new[]
        {
            Committed(5),
            SourceRevisionContext.Committed(new(new BranchRef(Tenant, Project, new(Id(6))), new(Id(4)))),
            SourceRevisionContext.Committed(new(new BranchRef(Tenant, new(Id(7)), new(Id(3))), new(Id(4)))),
            SourceRevisionContext.Committed(new(new BranchRef(new(Id(8)), Project, new(Id(3))), new(Id(4))))
        })
            Refused(Preview(basis, request with { Target = ProjectLocation.Create(FirstDocument, context, entityId: FirstTask) }), ProjectScenarioState.OutOfScope);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void SameVersionSourceSubstitutionCannotMaterializeReviseRebaseOrConvert()
    {
        var basis = Make();
        var changed = Make(duration: 41);
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        Assert.Equal(basis.Input.Source.Context, changed.Input.Source.Context);
        Assert.NotEqual(basis.Input.Source.Data, changed.Input.Source.Data);
        Assert.Same(plan.Input, plan.Materialize(basis.Input));
        Assert.Null(plan.Materialize(changed.Input));
        Refused(ProjectScenarios.Revise(plan, changed.Model, changed.Input.InputDigest, NextRevision, [DurationOverride(changed, 80)]), ProjectScenarioState.StaleInput);
        Refused(ProjectScenarios.Rebase(plan, changed.Model, changed.Input.InputDigest, NextRevision), ProjectScenarioState.Conflict);
        Refused(ProjectScenarios.ToDraft(plan, changed.Model, changed.Input.InputDigest, Proposal(changed)), ProjectScenarioState.StaleInput);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void ScenarioBasesAndMixedContextIdentitiesAreRefused()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        var hypothesis = new Fixture(plan.Input, ProjectSemanticBinder.Bind(plan.Input));
        Refused(Preview(hypothesis, DurationOverride(hypothesis, 80)), ProjectScenarioState.InvalidContext);
        Assert.Throws<ArgumentException>(() => new ScenarioRef(new(Id(200)), new(Id(201)), plan.Input.Source.Context, Digest("nested")));
        var mixed = Node(basis.Input.Source.Context.Data);
        mixed["scenarioId"] = Scenario.ToString();
        Assert.Throws<FormatException>(() => SourceRevisionContext.Parse(Json(mixed)));
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void PinnedScenarioVersionsCannotBeReallocated()
    {
        var plain = Make();
        Assert.Equal(ProjectScenarioState.Ready, Preview(plain, DurationOverride(plain, 75)).State);
        var reserved = SourceRevisionContext.Hypothetical(new(Scenario, InitialRevision, plain.Input.Source.Context, Digest("different-overlay")));
        foreach (var field in PinFields)
        {
            var settings = Node(Settings());
            settings[field] = new JsonArray(JsonSerializer.SerializeToNode(Pin(reserved, 800)));
            var basis = Make(settings: Json(settings));
            var original = Frozen(basis.Input);
            Refused(Preview(basis, DurationOverride(basis, 75)), ProjectScenarioState.ContextConflict);
            Assert.Equal(original, Frozen(basis.Input));
        }
    }

    [Fact]
    public void CanonicalOrderingDuplicatesAndNoChangeRemainDistinct()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var duration = DurationOverride(basis, 75);
        var boolean = InputOverride(basis, BooleanInput, Known("coglatas.boolean", true));
        var forward = Ready(basis, duration, boolean);
        var reversed = Ready(basis, boolean, duration);
        Assert.Equal(forward.Overlay.CanonicalText, reversed.Overlay.CanonicalText);
        Assert.Equal(forward.OverlayDigest, reversed.OverlayDigest);
        Assert.Equal(forward.Input.InputDigest, reversed.Input.InputDigest);
        Refused(Preview(basis, duration, duration with { Value = Known("coglatas.elapsed-duration", "80") }), ProjectScenarioState.Conflict);
        Refused(Preview(basis), ProjectScenarioState.NoChange);
        Refused(Preview(basis, DurationOverride(basis, 40)), ProjectScenarioState.NoChange);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void OpaqueSourceIsPreservedAndFutureOverrideTypesAreRefused()
    {
        var basis = Make(opaque: true);
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        Assert.Equal(Property(basis.Input.Source.Data, "extensions"), Property(plan.Input.Source.Data, "extensions"));
        Assert.Equal(Property(basis.Input.Source.Manifest.Data, "opaqueMarker"), Property(plan.Input.Source.Manifest.Data, "opaqueMarker"));
        Assert.Equal(Property(basis.Input.Source.Documents[1].Data, "extensions"), Property(plan.Input.Source.Documents[1].Data, "extensions"));
        Assert.Equal(basis.Input.Source.ExtensionDeclarations, plan.Input.Source.ExtensionDeclarations);
        Assert.Equal(basis.Input.Source.PackageLock, plan.Input.Source.PackageLock);
        Assert.Equal(basis.Input.Source.SourceReferences, plan.Input.Source.SourceReferences);
        var future = Node(Known("coglatas.elapsed-duration", "80"));
        future["type"]!["schemaVersion"] = 2;
        Refused(Preview(basis, DurationOverride(basis, 80) with { Value = Json(future) }), ProjectScenarioState.Unsupported);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void OverrideCountBoundaryIsExact()
    {
        var basis = Make(extraPorts: 129);
        var original = Frozen(basis.Input);
        var requests = Enumerable.Range(0, 129).Select(index => InputOverride(basis, new(Id(1000 + index)), Known("coglatas.boolean", true))).ToArray();
        var exact = Ready(basis, requests.Take(128).ToArray());
        Assert.Equal(128, exact.Changes.Count);
        Assert.Equal(128, Task(exact.Input, FirstTask).Ports.Count(port => port.DefaultValue?.KnownValue is true));
        Refused(Preview(basis, requests), ProjectScenarioState.ResourceLimit);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void CombinedUtf8OverrideByteBoundaryIsExact()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var empty = InputOverride(basis, TextInput, Known("coglatas.text", ""));
        var overhead = Bytes(empty.Target!.Data) + Bytes(empty.Value!);
        var payload = new string('x', ProjectScenarios.MaximumOverrideBytes - overhead);
        var exact = empty with { Value = Known("coglatas.text", payload) };
        Assert.Equal(262_144, Bytes(exact.Target!.Data) + Bytes(exact.Value!));
        var accepted = Ready(basis, exact);
        Assert.Equal(payload, Task(accepted.Input, FirstTask).Ports.Single(port => port.PortId == TextInput).DefaultValue!.KnownValue);
        var excessive = exact with { Value = Known("coglatas.text", payload + "x") };
        Assert.Equal(262_145, Bytes(excessive.Target!.Data) + Bytes(excessive.Value!));
        Refused(Preview(basis, excessive), ProjectScenarioState.ResourceLimit);
        var unicode = exact with { Value = Known("coglatas.text", payload[..^1] + "\u00e9") };
        Assert.True(Bytes(unicode.Target!.Data) + Bytes(unicode.Value!) > ProjectScenarios.MaximumOverrideBytes);
        Refused(Preview(basis, unicode), ProjectScenarioState.ResourceLimit);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void CancellationBeforeAndDuringEnumerationPreservesOriginals()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        var draft = ReadyDraft(ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, Proposal(basis)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectScenarios.Prepare(basis.Model, basis.Input.InputDigest, Scenario, InitialRevision, [DurationOverride(basis, 75)], cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectScenarios.Revise(plan, basis.Model, basis.Input.InputDigest, NextRevision, [DurationOverride(basis, 80)], cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectScenarios.Rebase(plan, basis.Model, basis.Input.InputDigest, NextRevision, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, Proposal(basis), cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => plan.Materialize(basis.Input, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => draft.ApplyToDraft(basis.Input, cancelled.Token));
        using var during = new CancellationTokenSource();
        var disposed = false;
        IEnumerable<ProjectScenarioOverride> Requests()
        {
            try
            {
                yield return DurationOverride(basis, 75);
                during.Cancel();
                yield return InputOverride(basis, BooleanInput, Known("coglatas.boolean", true));
            }
            finally { disposed = true; }
        }
        Assert.Throws<OperationCanceledException>(() => ProjectScenarios.Prepare(basis.Model, basis.Input.InputDigest, Scenario, InitialRevision, Requests(), during.Token));
        Assert.True(disposed);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void RevisionRequiresANewVersionAndTheExactOriginalBasis()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var old = Ready(basis, DurationOverride(basis, 75));
        var oldBytes = Frozen(old.Input);
        Refused(ProjectScenarios.Revise(old, basis.Model, basis.Input.InputDigest, InitialRevision, [DurationOverride(basis, 80)]), ProjectScenarioState.ContextConflict);
        var revised = RequireReady(ProjectScenarios.Revise(old, basis.Model, basis.Input.InputDigest, NextRevision, [DurationOverride(basis, 80)]));
        Assert.Equal(Scenario, revised.Input.Source.Context.Scenario!.ScenarioId);
        Assert.Equal(NextRevision, revised.Input.Source.Context.Scenario!.ScenarioRevisionId);
        Assert.Equal("80", Task(revised.Input, FirstTask).Duration.Data.Value.GetProperty("value").GetString());
        var later = Make(context: Committed(5));
        Refused(ProjectScenarios.Revise(old, later.Model, later.Input.InputDigest, NextRevision, [DurationOverride(later, 80)]), ProjectScenarioState.StaleInput);
        Assert.Equal(oldBytes, Frozen(old.Input));
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void ExplicitRebasePreservesUnrelatedNewBaseChanges()
    {
        var basis = Make();
        var plan = Ready(basis, DurationOverride(basis, 75));
        var oldBytes = Frozen(plan.Input);
        var settings = Node(Settings());
        settings["clock"] = JsonSerializer.SerializeToNode(new { kind = "coglatas.known", instant = "2032-05-06T07:08:09.0000000Z" });
        var current = Make(context: Committed(5), otherDuration: 125, settings: Json(settings));
        var currentBytes = Frozen(current.Input);
        var rebased = RequireReady(ProjectScenarios.Rebase(plan, current.Model, current.Input.InputDigest, NextRevision));
        Assert.Same(current.Input, rebased.Basis);
        Assert.Equal(current.Input.Source.Context, rebased.Input.Source.Context.Scenario!.BaseContext);
        Assert.Equal("75", Task(rebased.Input, FirstTask).Duration.Data.Value.GetProperty("value").GetString());
        Assert.Equal("125", Task(rebased.Input, SecondTask).Duration.Data.Value.GetProperty("value").GetString());
        Assert.Equal(Property(current.Input.Settings, "clock"), Property(rebased.Input.Settings, "clock"));
        Assert.Null(plan.Materialize(current.Input));
        Assert.Same(rebased.Input, rebased.Materialize(current.Input));
        Assert.Equal(oldBytes, Frozen(plan.Input));
        Assert.Equal(currentBytes, Frozen(current.Input));
    }

    [Fact]
    public void RebaseRefusesChangedMissingMovedAndForeignTargets()
    {
        var basis = Make();
        var plan = Ready(basis, DurationOverride(basis, 75));
        var oldBytes = Frozen(plan.Input);
        foreach (var current in new[]
        {
            Make(context: Committed(5), duration: 41), Make(context: Committed(5), includeFirstTask: false),
            Make(context: Committed(5), moveFirstTask: true)
        })
        {
            var currentBytes = Frozen(current.Input);
            Refused(ProjectScenarios.Rebase(plan, current.Model, current.Input.InputDigest, NextRevision), ProjectScenarioState.Conflict);
            Assert.Equal(currentBytes, Frozen(current.Input));
        }
        var foreign = Make(context: SourceRevisionContext.Committed(new(new BranchRef(Tenant, Project, new(Id(6))), new(Id(5)))));
        Refused(ProjectScenarios.Rebase(plan, foreign.Model, foreign.Input.InputDigest, NextRevision), ProjectScenarioState.OutOfScope);
        Refused(ProjectScenarios.Rebase(plan, basis.Model, Digest("wrong-rebase-input"), NextRevision), ProjectScenarioState.StaleInput);
        Assert.Equal(oldBytes, Frozen(plan.Input));
    }

    [Fact]
    public void CommittedBaseConversionBindsTheDraftAndRestoresOriginalSettings()
    {
        var basis = Make();
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        var hypothesisBytes = Frozen(plan.Input);
        var proposal = Proposal(basis);
        var draft = ReadyDraft(ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, proposal));
        Assert.Same(plan, draft.Scenario);
        Assert.Equal(SourceRevisionContext.Candidate(proposal), draft.After.Source.Context);
        Assert.Null(draft.After.Source.Context.Scenario);
        Assert.Equal(basis.Input.Settings, draft.After.Settings);
        Assert.Equal("75", Task(draft.After, FirstTask).Duration.Data.Value.GetProperty("value").GetString());
        Assert.Equal(basis.Input.InputDigest.Data.CanonicalText, Property(draft.Data, "baseInputDigest"));
        Assert.Equal(plan.Input.InputDigest.Data.CanonicalText, Property(draft.Data, "scenarioInputDigest"));
        Assert.Equal(draft.After.InputDigest.Data.CanonicalText, Property(draft.Data, "afterInputDigest"));
        Assert.NotEqual(plan.Input.InputDigest, draft.After.InputDigest);
        Assert.Same(draft.After, draft.ApplyToDraft(basis.Input));
        var wrongBase = new ProposalContext(proposal.ProposalId, proposal.CandidateRevisionId, new(Branch, new(Id(5))), proposal.CapturedTargetHead);
        Refused(ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, wrongBase), ProjectScenarioState.InvalidContext);
        Assert.Equal(original, Frozen(basis.Input));
        Assert.Equal(hypothesisBytes, Frozen(plan.Input));
    }

    [Fact]
    public void CandidateConversionRequiresTheSameProposalAndANewCandidate()
    {
        var committed = new RevisionRef(Branch, new(Id(4)));
        var currentProposal = new ProposalContext(new(Id(110)), new(Id(111)), committed, committed);
        var basis = Make(context: SourceRevisionContext.Candidate(currentProposal));
        var original = Frozen(basis.Input);
        var plan = Ready(basis, DurationOverride(basis, 75));
        var next = new ProposalContext(currentProposal.ProposalId, new(Id(112)), committed, committed);
        var draft = ReadyDraft(ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, next));
        Assert.Equal(next, draft.After.Source.Context.Proposal);
        Assert.Equal(basis.Input.Settings, draft.After.Settings);
        foreach (var invalid in new[]
        {
            currentProposal,
            new ProposalContext(new(Id(113)), next.CandidateRevisionId, committed, committed),
            new ProposalContext(next.ProposalId, next.CandidateRevisionId, new(Branch, new(Id(5))), committed),
            new ProposalContext(next.ProposalId, next.CandidateRevisionId, committed, new(Branch, new(Id(5))))
        })
            Refused(ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, invalid), ProjectScenarioState.InvalidContext);
        Assert.Equal(original, Frozen(basis.Input));
    }

    [Fact]
    public void DraftContextsReservedByPinsOrHypotheticalReferencesAreRefused()
    {
        var plain = Make();
        var proposal = Proposal(plain);
        var nextContext = SourceRevisionContext.Candidate(proposal);
        var settings = Node(Settings());
        settings["evidence"] = new JsonArray(JsonSerializer.SerializeToNode(Pin(nextContext, 800)));
        var pinned = Make(settings: Json(settings));
        var pinnedOriginal = Frozen(pinned.Input);
        var pinnedPlan = Ready(pinned, DurationOverride(pinned, 75));
        Refused(ProjectScenarios.ToDraft(pinnedPlan, pinned.Model, pinned.Input.InputDigest, proposal), ProjectScenarioState.ContextConflict);
        var referenceValue = ReferenceValue(plain, nextContext);
        var referencePlan = Ready(plain, InputOverride(plain, ReferenceInput, referenceValue));
        var retained = Frozen(referencePlan.Input);
        var actual = Assert.IsType<TypedProjectReference>(Task(referencePlan.Input, FirstTask).Ports.Single(port => port.PortId == ReferenceInput).DefaultValue!.KnownValue);
        Assert.Equal(nextContext, actual.Context);
        Assert.Equal(referencePlan.Input.Source.Context, actual.Origin.Context);
        Refused(ProjectScenarios.ToDraft(referencePlan, plain.Model, plain.Input.InputDigest, proposal), ProjectScenarioState.ContextConflict);
        Assert.Equal(pinnedOriginal, Frozen(pinned.Input));
        Assert.Equal(retained, Frozen(referencePlan.Input));
    }

    [Fact]
    public void DraftApplicationRequiresCurrentExactSettingsContextAndSource()
    {
        var basis = Make();
        var plan = Ready(basis, DurationOverride(basis, 75));
        var draft = ReadyDraft(ProjectScenarios.ToDraft(plan, basis.Model, basis.Input.InputDigest, Proposal(basis)));
        var original = Frozen(basis.Input);
        var after = Frozen(draft.After);
        Assert.Same(draft.After, draft.ApplyToDraft(basis.Input));
        var settings = Node(Settings());
        settings["executionProfile"]!["maximumOperations"] = 1_999_999;
        foreach (var current in new[] { Make(settings: Json(settings)), Make(context: Committed(5)), Make(duration: 41) })
        {
            Assert.Null(draft.ApplyToDraft(current.Input));
            Refused(ProjectScenarios.ToDraft(plan, current.Model, current.Input.InputDigest, Proposal(current)), ProjectScenarioState.StaleInput);
        }
        Assert.Equal(original, Frozen(basis.Input));
        Assert.Equal(after, Frozen(draft.After));
    }

    private sealed record Fixture(CompilerInputManifest Input, PartialSemanticModel Model);
    private static Guid Id(int number) => Guid.Parse("f5195100-0000-4000-8000-" + number.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static JsonObject Node(SourceJson value) => JsonNode.Parse(value.CanonicalText)!.AsObject();
    private static string Property(SourceJson value, string property) => Json(value.Value.GetProperty(property)).CanonicalText;
    private static int Bytes(SourceJson value) => Encoding.UTF8.GetByteCount(value.CanonicalText);
    private static string Frozen(CompilerInputManifest input) => input.Data.CanonicalText + "\n" + input.Source.Data.CanonicalText + "\n" + input.Settings.CanonicalText;
    private static ContentDigest Digest(string text) => ContentDigest.Compute("coglatas.security-scenario-fixture/1", Encoding.UTF8.GetBytes(text));
    private static SourceRevisionContext Committed(int revision = 4) => SourceRevisionContext.Committed(new(Branch, new(Id(revision))));
    private static SourceJson Type(string kind) => Json(new { kind, schemaVersion = 1, nullable = false, acceptsUnknown = true });
    private static SourceJson ReferenceType() => Json(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.task" });
    private static SourceJson Known(string kind, object value) => Json(new { type = Type(kind).Value, state = "coglatas.known", value });
    private static ProjectLocation Target(Fixture basis, EntityId entity) => ProjectLocation.Create(entity == SecondTask ? SecondDocument : FirstDocument, basis.Input.Source.Context, entityId: entity);
    private static ProjectScenarioOverride DurationOverride(Fixture basis, long ticks) => new(Target(basis, FirstTask), ProjectScenarioOverrideKind.TaskDuration, Known("coglatas.elapsed-duration", ticks.ToString(CultureInfo.InvariantCulture)));
    private static ProjectScenarioOverride InputOverride(Fixture basis, PortId port, SourceJson value) => new(Target(basis, FirstTask), ProjectScenarioOverrideKind.TaskInputDefault, value, port);
    private static ProjectScenarioPreview Preview(Fixture basis, params ProjectScenarioOverride[] requests) => ProjectScenarios.Prepare(basis.Model, basis.Input.InputDigest, Scenario, InitialRevision, requests);
    private static ProjectScenarioPlan Ready(Fixture basis, params ProjectScenarioOverride[] requests) => RequireReady(Preview(basis, requests));
    private static ProjectScenarioPlan RequireReady(ProjectScenarioPreview preview)
    { Assert.Equal(ProjectScenarioState.Ready, preview.State); return Assert.IsType<ProjectScenarioPlan>(preview.Plan); }
    private static ProjectScenarioDraftPlan ReadyDraft(ProjectScenarioDraftPreview preview)
    { Assert.Equal(ProjectScenarioState.Ready, preview.State); return Assert.IsType<ProjectScenarioDraftPlan>(preview.Plan); }
    private static void Refused(ProjectScenarioPreview preview, ProjectScenarioState state)
    { Assert.Equal(state, preview.State); Assert.Null(preview.Plan); }
    private static void Refused(ProjectScenarioDraftPreview preview, ProjectScenarioState state)
    { Assert.Equal(state, preview.State); Assert.Null(preview.Plan); }
    private static T Declaration<T>(CompilerInputManifest input, EntityId entity) where T : PlanDeclaration => Assert.IsType<T>(ProjectSemanticBinder.Bind(input).Declarations.Single(item => item.Declaration?.EntityId == entity).Declaration);
    private static TaskPlanDeclaration Task(CompilerInputManifest input, EntityId entity) => Declaration<TaskPlanDeclaration>(input, entity);
    private static ProposalContext Proposal(Fixture basis) => new(new(Id(110)), new(Id(111)), basis.Input.Source.Context.CommittedRevision!, basis.Input.Source.Context.CommittedRevision!);

    private static SourceJson Settings()
    {
        object Artifact(string id) => new { artifactId = id, version = "1", digest = Digest(id).Data.Value };
        object None() => new { kind = "coglatas.none" };
        return Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-scenario.compiler"),
            ruleset = Artifact("coglatas.security-scenario.ruleset"), semantics = Artifact("coglatas.security-scenario.semantics"),
            ir = Artifact("coglatas.security-scenario.ir"), evaluator = Artifact("coglatas.security-scenario.evaluator"),
            authorizedExternalSnapshots = Array.Empty<object>(), resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(),
            observations = Array.Empty<object>(), evidence = Array.Empty<object>(),
            descriptors = new[] { Artifact("coglatas.security-scenario.descriptor") }, units = new[] { Artifact("coglatas.security-scenario.unit") },
            calendars = new[] { Artifact("coglatas.security-scenario.calendar") }, timeZones = new[] { Artifact("coglatas.security-scenario.time-zone") },
            solver = None(), stochastic = None(), importer = None(), scheduler = None(), scenario = None(),
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }.Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = new[] { "coglatas.security-scenario.local" }, targets = Array.Empty<string>(), analysisScope = new[] { "coglatas.security-scenario.structural" },
            executionProfile = new { purpose = "coglatas.interactive", maximumOperations = 2_000_000, maximumDeclarations = 2048, maximumRelations = 1024, maximumDigits = 32, maximumScale = 8, rounding = "coglatas.ties-to-even" },
            clock = new { kind = "coglatas.not-applicable" }, sourceCoverage = "coglatas.complete"
        });
    }

    private static object Pin(SourceRevisionContext context, int id) => new
    {
        snapshotId = new ExternalSnapshotId(Id(id)).ToString(), version = "fixed-1", digest = Digest("synthetic-pin-" + id).Data.Value,
        context = context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null
    };

    private static object Port(PortId id, string direction, SourceJson type, SourceJson? value = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["portId"] = id.ToString(), ["direction"] = direction, ["type"] = type.Value,
            ["minimumConnections"] = 0, ["maximumConnections"] = 1
        };
        if (value is not null) fields["defaultValue"] = value.Value;
        return fields;
    }

    private static Fixture Make(SourceRevisionContext? context = null, long duration = 40, long otherDuration = 90,
        bool includeFirstTask = true, bool moveFirstTask = false, int extraPorts = 0, SourceJson? settings = null, bool opaque = false)
    {
        context ??= Committed();
        object Root(int id) => new { entityId = new EntityId(Id(id)).ToString(), kind = "coglatas.project", schemaVersion = 1, payload = new { } };
        var documents = new[]
        {
            SourceDocument.Create(FirstDocument, "independent-scenario-one", Json(Root(12))),
            SourceDocument.Create(SecondDocument, "independent-scenario-two", Json(Root(13)), opaque ? Json(new { privateShape = new { context = "opaque-original" } }) : null)
        };
        var ports = new List<object>
        {
            Port(BooleanInput, "coglatas.input", Type("coglatas.boolean"), Known("coglatas.boolean", false)),
            Port(TextInput, "coglatas.input", Type("coglatas.text"), Known("coglatas.text", "")),
            Port(ReferenceInput, "coglatas.input", ReferenceType()),
            Port(BooleanOutput, "coglatas.output", Type("coglatas.boolean"))
        };
        ports.AddRange(Enumerable.Range(0, extraPorts).Select(index => Port(new(Id(1000 + index)), "coglatas.input", Type("coglatas.boolean"), Known("coglatas.boolean", false))));
        var declarations = new List<object>();
        if (includeFirstTask) declarations.Add(new { entityId = FirstTask.ToString(), documentId = (moveFirstTask ? SecondDocument : FirstDocument).ToString(), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = Known("coglatas.elapsed-duration", duration.ToString(CultureInfo.InvariantCulture)).Value, ports = ports.ToArray() } });
        declarations.Add(new { entityId = SecondTask.ToString(), documentId = SecondDocument.ToString(), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = Known("coglatas.elapsed-duration", otherDuration.ToString(CultureInfo.InvariantCulture)).Value, ports = Array.Empty<object>() } });
        declarations.Add(new { entityId = Milestone.ToString(), documentId = FirstDocument.ToString(), kind = "coglatas.milestone", schemaVersion = 1,
            payload = new { target = Known("coglatas.instant", "2031-04-01T12:00:00.0000000Z").Value } });
        object Endpoint(EntityId id) => new { kind = "coglatas.entity", expectedKind = "coglatas.task",
            reference = new { tenantId = Tenant.ToString(), projectId = Project.ToString(), entityId = id.ToString(), context = context.Data.Value } };
        var relations = Json(new[] { new { relationId = new RelationId(Id(50)).ToString(), documentId = FirstDocument.ToString(), kind = RelationKinds.DependsOn, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 }, source = Endpoint(FirstTask), target = Endpoint(SecondTask),
            payload = new { mode = "coglatas.finish-to-start", lag = Known("coglatas.elapsed-duration", "0").Value } } });
        var source = ProjectSource.Create(context, documents, Json(declarations), relations,
            opaque ? Json(new[] { new { future = "kept-exactly" } }) : null,
            opaque ? Json(new[] { new { package = "fixed-opaque-package" } }) : null,
            opaque ? Json(new[] { new { reference = "opaque-reference" } }) : null,
            opaque ? Json(new { context = "opaque-root-context", nested = new[] { "original" } }) : null);
        if (opaque)
        {
            var raw = Node(source.Data);
            raw["manifest"]!["opaqueMarker"] = JsonSerializer.SerializeToNode(new { preserved = "exact-value" });
            source = ProjectSourceCodec.Decode(Json(raw).ToCanonicalBytes()).RequireSource();
        }
        var input = new CompilerInputManifest(source, settings ?? Settings());
        var model = ProjectSemanticBinder.Bind(input);
        Assert.Equal(source.Declarations.Value.GetArrayLength() + source.Documents.Count, model.Declarations.Count);
        Assert.Equal(source.Relations.Value.GetArrayLength(), model.Relations.Count);
        return new(input, model);
    }

    private static SourceJson ReferenceValue(Fixture basis, SourceRevisionContext target) => Json(new
    {
        type = ReferenceType().Value, state = "coglatas.known",
        value = new
        {
            tenantId = Tenant.ToString(), projectId = Project.ToString(), entityId = SecondTask.ToString(), context = target.Data.Value,
            origin = ProjectLocation.Create(FirstDocument, basis.Input.Source.Context, "/payload/ports/2/defaultValue", entityId: FirstTask).Data.Value,
            manifest = new { version = "fixed-target-1", digest = Digest("fixed-target-manifest").Data.Value }
        }
    });
}
