using Coglatas.Application;
using Coglatas.Application.ProjectIde.Security;
using Coglatas.Domain.ProjectIde;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.ProjectIde;

public sealed class SecurityEvaluationCoordinatorTests
{
    [Theory]
    [InlineData(SecurityDecisionOutcome.Allow, SecurityDecisionOutcome.Allow, SecurityDecisionOutcome.Allow)]
    [InlineData(SecurityDecisionOutcome.Allow, SecurityDecisionOutcome.Unknown, SecurityDecisionOutcome.Unknown)]
    [InlineData(SecurityDecisionOutcome.Unknown, SecurityDecisionOutcome.Deny, SecurityDecisionOutcome.Deny)]
    [InlineData(SecurityDecisionOutcome.Deny, SecurityDecisionOutcome.Quarantine, SecurityDecisionOutcome.Quarantine)]
    [InlineData(SecurityDecisionOutcome.Quarantine, SecurityDecisionOutcome.Unknown, SecurityDecisionOutcome.Quarantine)]
    public async Task SuccessfulOutcomesHaveExplicitSeverityPrecedence(SecurityDecisionOutcome first,
        SecurityDecisionOutcome second, SecurityDecisionOutcome expected)
    {
        var coordinator = new SecurityEvaluationCoordinator([Rule("b", second), Rule("a", first)]);
        var decision = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());

        Assert.Equal(SecurityEvaluationStatus.Completed, decision.Status);
        Assert.Equal(expected, decision.Outcome);
        Assert.Equal(new[] { "a", "b" }, decision.Rules.Select(rule => rule.RuleId));
        Assert.Equal(new[] { first, second }, decision.Rules.Select(rule => rule.Outcome!.Value));
    }

    [Fact]
    public async Task EveryOutcomeIsRetainedAndQuarantineDominates()
    {
        var coordinator = new SecurityEvaluationCoordinator([
            Rule("unknown", SecurityDecisionOutcome.Unknown), Rule("quarantine", SecurityDecisionOutcome.Quarantine),
            Rule("deny", SecurityDecisionOutcome.Deny), Rule("allow", SecurityDecisionOutcome.Allow)]);
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());
        Assert.Equal(SecurityDecisionOutcome.Quarantine, result.Outcome);
        Assert.Equal(4, result.Rules.Count);
        Assert.All(result.Rules, rule => Assert.Equal(SecurityEvaluationStatus.Completed, rule.Status));
    }

    [Fact]
    public async Task OrderingIsOrdinalStableAndRegistrationCollectionIsFrozen()
    {
        var calls = new List<string>();
        var rules = new List<ISecurityRuleEvaluator>
        {
            new TestRule("z", (_, _) => { calls.Add("z"); return ValueTask.FromResult(Completed("z")); }),
            new TestRule("A", (_, _) => { calls.Add("A"); return ValueTask.FromResult(Completed("A")); }),
            new TestRule("a", (_, _) => { calls.Add("a"); return ValueTask.FromResult(Completed("a")); })
        };
        var coordinator = new SecurityEvaluationCoordinator(rules);
        rules.Clear();
        var binding = SecurityEvaluationTestData.Binding();
        var first = await coordinator.EvaluateAsync(binding);
        var second = await coordinator.EvaluateAsync(binding);
        Assert.Equal(new[] { "A", "a", "z", "A", "a", "z" }, calls);
        Assert.Equal(first.Rules, second.Rules);
        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.ReasonCode, second.ReasonCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unsafe rule")]
    [InlineData("line\nbreak")]
    public void InvalidRuleIdentitiesAreRejected(string ruleId) =>
        Assert.Throws<ArgumentException>(() => new SecurityEvaluationCoordinator([Rule(ruleId)]));

    [Fact]
    public void DuplicateAndNullRegistrationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SecurityEvaluationCoordinator([Rule("same"), Rule("same")]));
        Assert.Throws<ArgumentNullException>(() => new SecurityEvaluationCoordinator([null!]));
        Assert.Throws<ArgumentNullException>(() => new SecurityEvaluationCoordinator(null!));
    }

    [Fact]
    public void ExecutionTimeoutMustFitTheCooperativeTimer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityEvaluationCoordinator([], TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityEvaluationCoordinator([], TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityEvaluationCoordinator([], TimeSpan.MaxValue));
    }

    [Theory]
    [InlineData(SecurityEnforcementMode.Disabled)]
    [InlineData(SecurityEnforcementMode.Shadow)]
    public async Task EmptyRuleCoverageCannotProduceAllow(SecurityEnforcementMode mode)
    {
        var result = await new SecurityEvaluationCoordinator([]).EvaluateAsync(SecurityEvaluationTestData.Binding(mode: mode));
        Assert.Equal(SecurityEvaluationStatus.NotExecuted, result.Status);
        Assert.Null(result.Outcome);
        Assert.Empty(result.Rules);
    }

    [Fact]
    public async Task DisabledDoesNotExecuteAndRetainsUnexecutedCoverage()
    {
        var calls = 0;
        var rule = new TestRule("a", (_, _) => { calls++; throw new InvalidOperationException(); });
        var result = await new SecurityEvaluationCoordinator([rule]).EvaluateAsync(
            SecurityEvaluationTestData.Binding(mode: SecurityEnforcementMode.Disabled));
        Assert.Equal(0, calls);
        Assert.Equal(SecurityEvaluationStatus.NotExecuted, result.Status);
        Assert.Null(result.Outcome);
        Assert.Equal(SecurityReasonCode.RuleNotExecuted, Assert.Single(result.Rules).ReasonCode);
    }

    [Fact]
    public async Task EnforceIsRejectedBeforeEvaluatorExecution()
    {
        var calls = 0;
        var coordinator = new SecurityEvaluationCoordinator([new TestRule("a", (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(Completed("a"));
        })]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await coordinator.EvaluateAsync(
            SecurityEvaluationTestData.Binding(mode: SecurityEnforcementMode.Enforce)));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("null")]
    [InlineData("foreign-id")]
    [InlineData("pending")]
    [InlineData("unrelated-cancellation")]
    public async Task InvalidOrFailedEvaluatorPreservesPartialResultsAndSafeFailure(string failure)
    {
        var trailingCalls = 0;
        var coordinator = new SecurityEvaluationCoordinator([
            Rule("a"), new TestRule("b", (_, _) => failure switch
            {
                "exception" => throw new InvalidOperationException("private-source-and-credential-evidence"),
                "null" => ValueTask.FromResult<SecurityRuleResult>(null!),
                "foreign-id" => ValueTask.FromResult(Completed("other")),
                "pending" => ValueTask.FromResult(new SecurityRuleResult("b", SecurityEvaluationStatus.Pending, null, SecurityReasonCode.RuleNotExecuted)),
                _ => throw new OperationCanceledException()
            }), new TestRule("c", (_, _) => { trailingCalls++; return ValueTask.FromResult(Completed("c")); })]);
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());
        Assert.Equal(SecurityEvaluationStatus.Failed, result.Status);
        Assert.Null(result.Outcome);
        Assert.Equal(SecurityReasonCode.EvaluationFailed, result.ReasonCode);
        Assert.Equal(new[] { SecurityEvaluationStatus.Completed, SecurityEvaluationStatus.Failed, SecurityEvaluationStatus.NotExecuted },
            result.Rules.Select(rule => rule.Status));
        Assert.Equal(SecurityReasonCode.RuleExecutionFailed, result.Rules[1].ReasonCode);
        Assert.Equal(0, trailingCalls);
    }

    [Theory]
    [InlineData(SecurityEvaluationStatus.Failed, SecurityReasonCode.RuleExecutionFailed)]
    [InlineData(SecurityEvaluationStatus.Cancelled, SecurityReasonCode.EvaluationCancelled)]
    [InlineData(SecurityEvaluationStatus.TimedOut, SecurityReasonCode.EvaluationTimedOut)]
    [InlineData(SecurityEvaluationStatus.NotExecuted, SecurityReasonCode.RuleNotExecuted)]
    public async Task EvaluatorExecutionStatesAreNeverPolicyDecisions(SecurityEvaluationStatus status, SecurityReasonCode reason)
    {
        var coordinator = new SecurityEvaluationCoordinator([
            Rule("a", SecurityDecisionOutcome.Deny),
            new TestRule("b", (_, _) => ValueTask.FromResult(new SecurityRuleResult("b", status, null, reason))), Rule("c")]);
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());
        Assert.Equal(status, result.Status);
        Assert.Null(result.Outcome);
        Assert.Equal(SecurityDecisionOutcome.Deny, result.Rules[0].Outcome);
        Assert.Equal(status, result.Rules[1].Status);
        Assert.Equal(SecurityEvaluationStatus.NotExecuted, result.Rules[2].Status);
    }

    [Fact]
    public async Task AlreadyCancelledRequestExecutesNoRule()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new SecurityEvaluationCoordinator([new TestRule("a", (_, _) => throw new InvalidOperationException())]);
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding(), cancellation.Token);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, result.Status);
        Assert.Null(result.Outcome);
        Assert.All(result.Rules, rule => Assert.Equal(SecurityEvaluationStatus.NotExecuted, rule.Status));
    }

    [Fact]
    public async Task CancellationAwaitsEvaluatorCleanupAndPreservesEarlierResults()
    {
        using var cancellation = new CancellationTokenSource();
        var cleanedUp = false;
        var coordinator = new SecurityEvaluationCoordinator([Rule("a"), new TestRule("b", async (_, token) =>
        {
            cancellation.Cancel();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cleanedUp = true; }
            return Completed("b");
        }), Rule("c")]);
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding(), cancellation.Token);
        Assert.True(cleanedUp);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, result.Status);
        Assert.Equal(SecurityReasonCode.EvaluationCancelled, result.ReasonCode);
        Assert.Null(result.Outcome);
        Assert.Equal(SecurityEvaluationStatus.Completed, result.Rules[0].Status);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, result.Rules[1].Status);
        Assert.Equal(SecurityEvaluationStatus.NotExecuted, result.Rules[2].Status);
    }

    [Fact]
    public async Task CooperativeTimeoutIsDistinctAndLeavesNoDetachedEvaluator()
    {
        var cleanedUp = false;
        var coordinator = new SecurityEvaluationCoordinator([new TestRule("a", async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cleanedUp = true; }
            return Completed("a");
        }), Rule("b")], TimeSpan.FromSeconds(1));
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());
        Assert.True(cleanedUp);
        Assert.Equal(SecurityEvaluationStatus.TimedOut, result.Status);
        Assert.Equal(SecurityReasonCode.EvaluationTimedOut, result.ReasonCode);
        Assert.Null(result.Outcome);
        Assert.Equal(SecurityEvaluationStatus.TimedOut, result.Rules[0].Status);
        Assert.Equal(SecurityEvaluationStatus.NotExecuted, result.Rules[1].Status);
    }

    [Fact]
    public async Task CallerCancellationWinsOverAnExpiredDeadline()
    {
        using var caller = new CancellationTokenSource();
        var coordinator = new SecurityEvaluationCoordinator([new TestRule("a", async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { caller.Cancel(); }
            return Completed("a");
        })], TimeSpan.FromSeconds(1));
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding(), caller.Token);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task ResultReturnedAfterCallerCancellationIsNotQualifiedAsAllow()
    {
        using var caller = new CancellationTokenSource();
        var coordinator = new SecurityEvaluationCoordinator([new TestRule("a", (_, _) =>
        {
            caller.Cancel();
            return ValueTask.FromResult(Completed("a"));
        })]);
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding(), caller.Token);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, result.Status);
        Assert.Null(result.Outcome);
        Assert.Null(Assert.Single(result.Rules).Outcome);
    }

    [Fact]
    public async Task LateResultAfterCooperativeDeadlineIsNotQualifiedAsAllow()
    {
        var coordinator = new SecurityEvaluationCoordinator([new TestRule("a", async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { }
            return Completed("a");
        })], TimeSpan.FromSeconds(1));
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());
        Assert.Equal(SecurityEvaluationStatus.TimedOut, result.Status);
        Assert.Null(result.Outcome);
        Assert.Null(Assert.Single(result.Rules).Outcome);
    }

    [Fact]
    public async Task MutableRegistrationCannotChangeItsCapturedRuleIdentity()
    {
        var rule = new MutableIdentityRule();
        var coordinator = new SecurityEvaluationCoordinator([rule]);
        rule.Rename("foreign");
        var result = await coordinator.EvaluateAsync(SecurityEvaluationTestData.Binding());
        Assert.Equal(SecurityEvaluationStatus.Failed, result.Status);
        Assert.Null(result.Outcome);
        Assert.Equal("registered", Assert.Single(result.Rules).RuleId);
    }

    [Fact]
    public async Task ApplicationRegistersExactlyTheThreeAnalysisOnlyFoundationRules()
    {
        using var services = new ServiceCollection().AddApplication().BuildServiceProvider();
        var rules = services.GetServices<ISecurityRuleEvaluator>().ToArray();
        Assert.Equal(3, rules.Length);
        var binding = SecurityEvaluationTestData.Binding();
        var before = binding.Data.CanonicalText;
        var sourceBefore = binding.Request.Source!.Data.CanonicalText;
        var result = await services.GetRequiredService<ISecurityEvaluationCoordinator>().EvaluateAsync(binding);
        Assert.Equal(SecurityDecisionOutcome.Allow, result.Outcome);
        Assert.Equal(new[] { "SEC-FND-COMPILER-PROVENANCE", "SEC-FND-POLICY-BINDING", "SEC-FND-REVISION-BINDING" },
            result.Rules.Select(rule => rule.RuleId));
        Assert.Equal(before, binding.Data.CanonicalText);
        Assert.Equal(sourceBefore, binding.Request.Source.Data.CanonicalText);
        Assert.False(new SecurityAnalysisSummary(binding.Request.EvaluationId, binding.Request.EnforcementMode,
            result.Status, result.Outcome, result.ReasonCode).IsAuthoritative);
    }

    private static TestRule Rule(string ruleId, SecurityDecisionOutcome outcome = SecurityDecisionOutcome.Allow) =>
        new(ruleId, (_, _) => ValueTask.FromResult(Completed(ruleId, outcome)));

    private static SecurityRuleResult Completed(string ruleId, SecurityDecisionOutcome outcome = SecurityDecisionOutcome.Allow) =>
        new(ruleId, SecurityEvaluationStatus.Completed, outcome, outcome switch
        {
            SecurityDecisionOutcome.Quarantine => SecurityReasonCode.BindingMismatch,
            SecurityDecisionOutcome.Deny => SecurityReasonCode.PolicyViolation,
            SecurityDecisionOutcome.Unknown => SecurityReasonCode.MissingEvidence,
            _ => SecurityReasonCode.BindingsVerified
        });

    private sealed class TestRule(string ruleId, Func<SecurityBinding, CancellationToken, ValueTask<SecurityRuleResult>> evaluate)
        : ISecurityRuleEvaluator
    {
        public string RuleId { get; } = ruleId;
        public ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken) =>
            evaluate(binding, cancellationToken);
    }

    private sealed class MutableIdentityRule : ISecurityRuleEvaluator
    {
        private string _ruleId = "registered";
        public string RuleId => _ruleId;
        public void Rename(string ruleId) => _ruleId = ruleId;
        public ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Completed(RuleId));
    }
}

internal static class SecurityEvaluationTestData
{
    internal static ProjectSource Source() => ProjectSourceCodec.Decode(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "source-v1.json"))).RequireSource();

    internal static SecurityPolicyEvidence Policy(string content = "{\"rules\":{\"foundation\":true}}", int schema = 1) =>
        new("foundation-test", "1", SourceJson.Parse(content), schema);

    internal static SecurityCompilerProvenance Compiler() => new("test-host/1", "deterministic-fixture");

    internal static SecurityEvaluationRequest Request(SecurityEnforcementMode mode = SecurityEnforcementMode.Shadow) =>
        new(Guid.NewGuid(), new(Source().TenantId, Guid.Parse("00000000-0000-4000-8000-000000000009")),
            new("projectide.analyze"), new(Source().Context, Source().Digest), mode, Source(), Policy().Snapshot, Compiler());

    internal static SecurityBinding Binding(SecurityEvaluationRequest? request = null, SecurityEvaluationEvidence? evidence = null,
        SecurityEnforcementMode mode = SecurityEnforcementMode.Shadow) =>
        SecurityBinding.Create(request ?? Request(mode), evidence ?? new(Source(), Policy(), Compiler()));
}
