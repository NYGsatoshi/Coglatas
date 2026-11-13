namespace Coglatas.Domain.ProjectIde;

/// <summary>Provisional local processing limits, not a product capacity or required-suite policy.</summary>
public sealed record ProjectTestLimits(int MaximumTests = 256, int MaximumAssertions = 1024)
{
    internal void Validate()
    {
        if (MaximumTests is < 1 or > 256 || MaximumAssertions is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(ProjectTestLimits));
    }
}

public sealed class ProjectTestAssertionResult
{
    public ProjectLocation Location { get; }
    public CompilerVerdict Verdict { get; }
    public string Reason { get; }
    public SourceJson Data { get; }
    internal ProjectTestAssertionResult(ProjectLocation location, CompilerVerdict verdict, string reason, SourceJson assertion, SourceJson? actual = null)
    {
        Location = location; Verdict = verdict; Reason = reason;
        Data = SourceJson.FromObject(new { location = location.Data.Value, verdict = verdict.ToString(), reason,
            assertionDigest = ContentDigest.Compute("coglatas.project-test-assertion/1", assertion.ToCanonicalBytes()).Data.Value,
            actualDigest = actual is null ? (System.Text.Json.JsonElement?)null : ContentDigest.Compute("coglatas.project-test-actual/1", actual.ToCanonicalBytes()).Data.Value });
    }
}

public sealed class ProjectTestCaseResult
{
    public EntityId TestId { get; }
    public CompilerVerdict Verdict { get; }
    public string Reason { get; }
    public IReadOnlyList<ProjectTestAssertionResult> Assertions { get; }
    public SourceJson Data { get; }
    internal ProjectTestCaseResult(EntityId testId, IEnumerable<ProjectTestAssertionResult> assertions, string reason, bool complete, CompilerVerdict? refusedVerdict = null)
    {
        TestId = testId; Assertions = new ImmutableListView<ProjectTestAssertionResult>(assertions); Reason = reason;
        Verdict = refusedVerdict ?? ProjectTestRun.Aggregate(Assertions.Select(item => item.Verdict), complete);
        Data = SourceJson.FromObject(new { testId = testId.ToString(), verdict = Verdict.ToString(), reason,
            assertions = Assertions.Select(item => item.Data.Value).ToArray() });
    }
}

/// <summary>Local reproducible test evidence. Neither artifact identity nor PASS grants viewing, Merge or execution authority.</summary>
public sealed class ProjectTestRun
{
    public CompilerInputManifest Input { get; }
    public CompilerExecutionState Execution { get; }
    public CompilerCoverage Coverage { get; }
    public CompilerVerdict Verdict { get; }
    public CompilerFreshness Freshness { get; }
    public CompilerEvidenceKind EvidenceKind { get; }
    public int DiscoveredTests { get; }
    public int ExecutedAssertions { get; }
    public string Reason { get; }
    public IReadOnlyList<ProjectTestCaseResult> Tests { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal ProjectTestRun(ProjectTestPlan plan, CompilerExecutionState execution, CompilerCoverage coverage,
        CompilerFreshness freshness, IEnumerable<ProjectTestCaseResult> tests, string reason)
    {
        Input = plan.Input; Execution = execution; Coverage = coverage; Freshness = freshness; Reason = reason;
        DiscoveredTests = plan.DiscoveredTests; Tests = new ImmutableListView<ProjectTestCaseResult>(tests);
        ExecutedAssertions = Tests.Sum(test => test.Assertions.Count(item => item.Verdict is not CompilerVerdict.NotRun));
        Verdict = Aggregate(Tests.Select(item => item.Verdict), execution == CompilerExecutionState.Completed &&
            coverage == CompilerCoverage.Complete && freshness == CompilerFreshness.Current && ExecutedAssertions > 0);
        EvidenceKind = Verdict is CompilerVerdict.Pass or CompilerVerdict.Fail ? CompilerEvidenceKind.ModelProof : CompilerEvidenceKind.Missing;
        Data = SourceJson.FromObject(new { format = "coglatas.project-test-run/1", compilerInputId = Input.InputId.ToString(),
            inputDigest = Input.InputDigest.Data.Value, sourceDigest = Input.Source.Digest.Data.Value,
            planDigest = plan.Digest.Data.Value, execution = execution.ToString(), coverage = coverage.ToString(), verdict = Verdict.ToString(),
            freshness = freshness.ToString(), evidenceKind = CompilerContractTokens.Evidence(EvidenceKind), discoveredTests = DiscoveredTests, executedAssertions = ExecutedAssertions, reason,
            tests = Tests.Select(item => item.Data.Value).ToArray() });
        Digest = ContentDigest.Compute("coglatas.project-test-run/1", Data.ToCanonicalBytes());
    }
    internal static CompilerVerdict Aggregate(IEnumerable<CompilerVerdict> verdicts, bool complete)
    {
        var items = verdicts.ToArray();
        if (items.Contains(CompilerVerdict.Error)) return CompilerVerdict.Error;
        if (items.Contains(CompilerVerdict.Fail)) return CompilerVerdict.Fail;
        if (items.Length == 0 || items.All(item => item == CompilerVerdict.NotRun)) return CompilerVerdict.NotRun;
        return complete && items.All(item => item == CompilerVerdict.Pass) ? CompilerVerdict.Pass : CompilerVerdict.Inconclusive;
    }
}

/// <summary>Typed test frontend for an admitted immutable local Source. No scripts, ambient inputs, jobs or live mutations.</summary>
public sealed class ProjectTestPlan
{
    private readonly PartialSemanticModel _model;
    private readonly bool _evaluatorMatches;
    private readonly DeclarationDecodeResult[] _tests;
    private readonly Dictionary<EntityId, TaskPlanDeclaration> _tasks;
    private readonly Dictionary<string, CompilerBoundReference[]> _references;
    public CompilerInputManifest Input => _model.Input;
    public int DiscoveredTests { get; }
    public bool DiscoveryComplete { get; }
    public bool WithinLimits { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    private ProjectTestPlan(PartialSemanticModel model, CompilerArtifactIdentity expectedEvaluator, ProjectTestLimits limits, CancellationToken cancellationToken)
    {
        _model = model; limits.Validate(); cancellationToken.ThrowIfCancellationRequested();
        _evaluatorMatches = expectedEvaluator.Data.Equals(SourceJson.FromElement(SourceFields.Object(model.Input.Settings.Value, "evaluator")));
        _tests = model.Declarations.Where(item => SourceFields.Token(item.Data.Value, "kind") == "coglatas.project-test")
            .OrderBy(item => SourceFields.Identity<EntityId>(item.Data.Value, "entityId").ToString(), StringComparer.Ordinal).ToArray();
        DiscoveredTests = model.Input.Source.Declarations.Value.EnumerateArray().Count(item => SourceFields.Token(item, "kind") == "coglatas.project-test") +
            model.Input.Source.Documents.Count(item => item.Root.Kind == "coglatas.project-test");
        DiscoveryComplete = _tests.Length == DiscoveredTests;
        WithinLimits = DiscoveredTests <= limits.MaximumTests &&
            _tests.Sum(item => (item.Declaration as ProjectTestPlanDeclaration)?.Assertions.Count ?? 0) <= limits.MaximumAssertions;
        _tasks = new();
        foreach (var item in model.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Declaration is TaskPlanDeclaration task) _tasks.Add(task.EntityId, task);
        }
        _references = model.References.GroupBy(item => item.Origin.Data.CanonicalText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        try
        {
            Data = SourceJson.FromObject(new { format = "coglatas.project-test-plan/1", inputDigest = Input.InputDigest.Data.Value,
            compilerInputId = Input.InputId.ToString(), engine = expectedEvaluator.Data.Value,
            limits = new { limits.MaximumTests, limits.MaximumAssertions }, withinLimits = WithinLimits, discoveryComplete = DiscoveryComplete,
            bindingComplete = model.BindingComplete, bindingLogicalOperationsConsumed = model.LogicalOperationsConsumed, references = model.References.Select(item => new { origin = item.Origin.Data.Value, state = item.State.ToString(), symbolKey = item.SymbolKey }).ToArray(),
            tests = _tests.Select(item => new { declarationDigest = ContentDigest.Compute("coglatas.project-test-declaration/1", item.Data.ToCanonicalBytes()).Data.Value,
                status = item.Status.ToString() }).ToArray() });
        }
        catch (FormatException)
        {
            WithinLimits = false;
            Data = SourceJson.FromObject(new { format = "coglatas.project-test-plan/1", inputDigest = Input.InputDigest.Data.Value,
                state = "coglatas.artifact-bound-exhausted", discoveredTests = DiscoveredTests });
        }
        Digest = ContentDigest.Compute("coglatas.project-test-plan/1", Data.ToCanonicalBytes());
        cancellationToken.ThrowIfCancellationRequested();
    }
    public static ProjectTestPlan Compile(PartialSemanticModel model, CompilerArtifactIdentity expectedEvaluator, ProjectTestLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(expectedEvaluator);
        return new(model, expectedEvaluator, limits ?? new(), cancellationToken);
    }
    public ProjectTestRun Execute(ContentDigest expectedInputDigest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedInputDigest);
        if (!Input.InputDigest.Equals(expectedInputDigest))
            return Result(CompilerExecutionState.NotRun, CompilerCoverage.None, [], "coglatas.stale-input", CompilerFreshness.Stale);
        if (cancellationToken.IsCancellationRequested) return Result(CompilerExecutionState.Cancelled, CompilerCoverage.None, [], "coglatas.cancelled");
        if (!_evaluatorMatches) return Result(CompilerExecutionState.NotRun, CompilerCoverage.None, [], "coglatas.evaluator-identity-mismatch");
        if (!DiscoveryComplete) return Result(CompilerExecutionState.NotRun, CompilerCoverage.None, [], "coglatas.test-discovery-unavailable");
        if (!WithinLimits) return Result(CompilerExecutionState.ResourceExhausted, CompilerCoverage.None, [], "coglatas.test-bound-exhausted");
        if (DiscoveredTests == 0) return Result(CompilerExecutionState.NotRun, CompilerCoverage.None, [], "coglatas.zero-tests");
        var results = new List<ProjectTestCaseResult>();
        var complete = _model.BindingComplete;
        var remainingOperations = Input.ExecutionProfile.MaximumOperations - _model.LogicalOperationsConsumed;
        foreach (var decoded in _tests)
        {
            var id = SourceFields.Identity<EntityId>(decoded.Data.Value, "entityId");
            if (cancellationToken.IsCancellationRequested) return Cancelled();
            if (decoded.Declaration is not ProjectTestPlanDeclaration test)
            {
                complete = false;
                results.Add(new(id, [], decoded.Status == ValueDecodeStatus.Invalid ? "coglatas.invalid-test" : "coglatas.unsupported-test", false,
                    decoded.Status == ValueDecodeStatus.Invalid ? CompilerVerdict.Error : CompilerVerdict.NotRun));
                continue;
            }
            var assertions = new List<ProjectTestAssertionResult>();
            foreach (var assertion in test.Assertions)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    results.Add(new(id, assertions, "coglatas.cancelled", false)); return Cancelled();
                }
                if (remainingOperations <= 0)
                {
                    results.Add(new(id, assertions, "coglatas.test-operation-budget-exhausted", false));
                    return Result(CompilerExecutionState.ResourceExhausted, results.Any(item => item.Assertions.Count != 0) ? CompilerCoverage.Partial : CompilerCoverage.None,
                        results, "coglatas.test-operation-budget-exhausted");
                }
                try { assertions.Add(Evaluate(test, assertion, remainingOperations--)); }
                catch (FormatException) { return Result(CompilerExecutionState.ResourceExhausted, CompilerCoverage.None, [], "coglatas.test-artifact-bound-exhausted"); }
            }
            var done = assertions.Count > 0 && assertions.All(item => item.Verdict is CompilerVerdict.Pass or CompilerVerdict.Fail);
            complete &= done;
            results.Add(new(id, assertions, assertions.Count == 0 ? "coglatas.zero-assertions" : "coglatas.local-input-defaults", done));
        }
        return Result(CompilerExecutionState.Completed, complete ? CompilerCoverage.Complete : CompilerCoverage.Partial, results,
            complete ? "coglatas.local-input-defaults" : "coglatas.incomplete-test-basis");
        ProjectTestRun Cancelled() => Result(CompilerExecutionState.Cancelled, results.Any(item => item.Assertions.Count != 0) ? CompilerCoverage.Partial : CompilerCoverage.None,
            results, "coglatas.cancelled");
    }
    private ProjectTestRun Result(CompilerExecutionState execution, CompilerCoverage coverage, IEnumerable<ProjectTestCaseResult> tests,
        string reason, CompilerFreshness freshness = CompilerFreshness.Current)
    {
        try { return new(this, execution, coverage, freshness, tests, reason); }
        catch (FormatException) { return new(this, CompilerExecutionState.ResourceExhausted, CompilerCoverage.None, freshness, [], "coglatas.test-artifact-bound-exhausted"); }
    }
    private ProjectTestAssertionResult Evaluate(ProjectTestPlanDeclaration test, ProjectTestAssertion assertion, int remainingOperations)
    {
        var origin = ProjectLocation.Create(test.DocumentId, Input.Source.Context, "/payload/assertions/" + assertion.Key, entityId: test.EntityId);
        ProjectTestAssertionResult Outcome(CompilerVerdict verdict, string reason, SourceJson? actual = null) => new(origin, verdict, reason, assertion.Data, actual);
        var targetOrigin = ProjectLocation.Create(test.DocumentId, Input.Source.Context, origin.PropertyPath + "/target", entityId: test.EntityId);
        var targetBindings = _references.GetValueOrDefault(targetOrigin.Data.CanonicalText) ?? [];
        if (targetBindings.Length != 1 || targetBindings[0].State != ReferenceResolutionState.Resolved)
            return Outcome(CompilerVerdict.NotRun, "coglatas.fixture-target-unavailable");
        if (!assertion.Target.Context.Equals(Input.Source.Context)) return Outcome(CompilerVerdict.NotRun, "coglatas.external-fixture-unsupported");
        if (!_tasks.TryGetValue(assertion.Target.EntityId, out var task)) return Outcome(CompilerVerdict.Error, "coglatas.fixture-requires-task");
        var port = task.Ports.SingleOrDefault(item => item.PortId == assertion.PortId);
        if (port?.DefaultValue is not { } value) return Outcome(CompilerVerdict.NotRun, "coglatas.fixture-default-missing");
        if (value.State != ValueState.Known) return Outcome(CompilerVerdict.Inconclusive, value.State == ValueState.Unknown ? "coglatas.unknown-actual" : "coglatas.null-actual", value.Data);
        bool passed;
        switch (assertion.Kind)
        {
            case "coglatas.assert-boolean":
                if (value.KnownValue is not bool actual) return Outcome(CompilerVerdict.Error, "coglatas.assertion-type-mismatch", value.Data);
                passed = actual == (bool)assertion.Expected!.KnownValue!; break;
            case "coglatas.assert-numeric-comparison":
                var numbers = Input.ExecutionProfile.Numbers;
                var comparison = ProjectNumericOperations.Evaluate(NumericOperand.From(value, numbers), NumericOperand.From(assertion.Expected!, numbers),
                    NumericOperator.Compare, new(numbers, Math.Min(100_000, remainingOperations)));
                if (comparison.Status != NumericEvaluationStatus.Known)
                    return Outcome(comparison.Status == NumericEvaluationStatus.Invalid ? CompilerVerdict.Error : CompilerVerdict.Inconclusive, comparison.Reason, value.Data);
                passed = assertion.Comparison switch { "coglatas.equal" => comparison.Comparison == 0,
                    "coglatas.less" => comparison.Comparison < 0, _ => comparison.Comparison > 0 }; break;
            case "coglatas.assert-collection-count":
                if (value.KnownValue is not ProjectCollectionValue collection) return Outcome(CompilerVerdict.Error, "coglatas.assertion-type-mismatch", value.Data);
                passed = (value.Type.Kind == "coglatas.map" ? collection.Entries.Count : collection.Items.Count) == assertion.ExpectedCount; break;
            default:
                if (value.KnownValue is not TypedProjectReference) return Outcome(CompilerVerdict.Error, "coglatas.assertion-type-mismatch", value.Data);
                var referenceOrigin = ProjectLocation.Create(task.DocumentId, Input.Source.Context, "/payload/ports/" + port.PortId + "/defaultValue", entityId: task.EntityId);
                var bindings = _references.GetValueOrDefault(referenceOrigin.Data.CanonicalText) ?? [];
                if (bindings.Length != 1) return Outcome(CompilerVerdict.Inconclusive, "coglatas.reference-basis-unavailable", value.Data);
                if (bindings[0].State is not (ReferenceResolutionState.Resolved or ReferenceResolutionState.MissingWithinCompleteScope))
                    return Outcome(CompilerVerdict.Inconclusive, "coglatas.reference-unresolved", value.Data);
                passed = bindings[0].State == ReferenceResolutionState.Resolved; break;
        }
        return Outcome(passed ? CompilerVerdict.Pass : CompilerVerdict.Fail, passed ? "coglatas.assertion-satisfied" : "coglatas.assertion-counterexample", value.Data);
    }
}
