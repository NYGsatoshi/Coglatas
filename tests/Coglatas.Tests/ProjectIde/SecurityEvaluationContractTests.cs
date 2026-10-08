using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class SecurityEvaluationContractTests
{
    private const string Id = "00000000-0000-4000-8000-000000000001";

    [Theory]
    [InlineData(SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified)]
    [InlineData(SecurityDecisionOutcome.Deny, SecurityReasonCode.PolicyViolation)]
    [InlineData(SecurityDecisionOutcome.Unknown, SecurityReasonCode.MissingEvidence)]
    [InlineData(SecurityDecisionOutcome.Quarantine, SecurityReasonCode.BindingMismatch)]
    public void DecisionsAreCompletedOutcomesWithDistinctSemantics(SecurityDecisionOutcome outcome, SecurityReasonCode reason)
    {
        var rule = new SecurityRuleResult("SEC-FND-TEST", SecurityEvaluationStatus.Completed, outcome, reason);
        var decision = new SecurityDecision(SecurityEvaluationStatus.Completed, outcome, reason, [rule]);
        Assert.Equal(SecurityEvaluationStatus.Completed, decision.Status);
        Assert.Equal(outcome, decision.Outcome);
        Assert.Equal(reason, decision.ReasonCode);
        Assert.Same(rule, Assert.Single(decision.Rules));
        Assert.Equal(outcome, rule.Outcome);
        Assert.Equal(reason, rule.ReasonCode);
    }

    [Theory]
    [InlineData(SecurityEvaluationStatus.NotExecuted, SecurityReasonCode.NotExecuted)]
    [InlineData(SecurityEvaluationStatus.Pending, SecurityReasonCode.NotExecuted)]
    [InlineData(SecurityEvaluationStatus.Failed, SecurityReasonCode.EvaluationFailed)]
    [InlineData(SecurityEvaluationStatus.Cancelled, SecurityReasonCode.EvaluationCancelled)]
    [InlineData(SecurityEvaluationStatus.TimedOut, SecurityReasonCode.EvaluationTimedOut)]
    public void NoncompletedExecutionCannotMasqueradeAsUnknownOrDeny(SecurityEvaluationStatus status, SecurityReasonCode reason)
    {
        var decision = new SecurityDecision(status, null, reason, []);
        Assert.Null(decision.Outcome);
        foreach (var outcome in Enum.GetValues<SecurityDecisionOutcome>())
            Assert.Throws<ArgumentException>(() => new SecurityRuleResult("SEC-FND-TEST", status, outcome, reason));
    }

    [Fact]
    public void MissingEvidenceCannotBecomeDenyAndIntegrityAnomalyCannotBecomeRiskDeny()
    {
        Assert.Throws<ArgumentException>(() => new SecurityRuleResult("SEC-FND-TEST",
            SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Deny, SecurityReasonCode.MissingEvidence));
        Assert.Throws<ArgumentException>(() => new SecurityRuleResult("SEC-FND-TEST",
            SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Deny, SecurityReasonCode.BindingMismatch));
        Assert.Throws<ArgumentException>(() => new SecurityRuleResult("SEC-FND-TEST",
            SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Unknown, SecurityReasonCode.RuleNotExecuted));
        Assert.Throws<ArgumentException>(() => new SecurityRuleResult("SEC-FND-TEST",
            SecurityEvaluationStatus.Completed, null, SecurityReasonCode.MissingEvidence));
    }

    [Fact]
    public void CompletedDecisionRequiresExecutedUniqueRules()
    {
        var completed = new SecurityRuleResult("SEC-FND-TEST", SecurityEvaluationStatus.Completed,
            SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified);
        var unexecuted = new SecurityRuleResult("SEC-FND-OTHER", SecurityEvaluationStatus.NotExecuted,
            null, SecurityReasonCode.RuleNotExecuted);
        Assert.Throws<ArgumentException>(() => new SecurityDecision(SecurityEvaluationStatus.Completed,
            SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified, []));
        Assert.Throws<ArgumentException>(() => new SecurityDecision(SecurityEvaluationStatus.Completed,
            SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified, [completed, unexecuted]));
        Assert.Throws<ArgumentException>(() => new SecurityDecision(SecurityEvaluationStatus.Completed,
            SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified, [completed, completed]));
    }

    [Fact]
    public void NullRuleInputsAreRejectedAtTheContractBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => new SecurityDecision(SecurityEvaluationStatus.Failed,
            null, SecurityReasonCode.EvaluationFailed, null!));
        Assert.Throws<ArgumentException>(() => new SecurityDecision(SecurityEvaluationStatus.Failed,
            null, SecurityReasonCode.EvaluationFailed, [null]));
    }

    [Fact]
    public void DecisionFreezesCallerCollectionsAndRetainsActualPartialResults()
    {
        var completed = new SecurityRuleResult("SEC-FND-TEST", SecurityEvaluationStatus.Completed,
            SecurityDecisionOutcome.Quarantine, SecurityReasonCode.BindingMismatch);
        var results = new List<SecurityRuleResult> { completed };
        var decision = new SecurityDecision(SecurityEvaluationStatus.Failed, null, SecurityReasonCode.EvaluationFailed, results);
        results.Clear();
        Assert.Same(completed, Assert.Single(decision.Rules));
        Assert.Throws<NotSupportedException>(() => ((IList<SecurityRuleResult>)decision.Rules).Clear());
        Assert.Null(decision.Outcome);
    }

    [Theory]
    [InlineData(SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified)]
    [InlineData(SecurityDecisionOutcome.Deny, SecurityReasonCode.PolicyViolation)]
    [InlineData(SecurityDecisionOutcome.Unknown, SecurityReasonCode.MissingEvidence)]
    [InlineData(SecurityDecisionOutcome.Quarantine, SecurityReasonCode.BindingMismatch)]
    public void ShadowSummaryNeverGrantsAuthority(SecurityDecisionOutcome outcome, SecurityReasonCode reason)
    {
        var evaluationId = Guid.NewGuid();
        var summary = new SecurityAnalysisSummary(evaluationId, SecurityEnforcementMode.Shadow,
            SecurityEvaluationStatus.Completed, outcome, reason);
        Assert.False(summary.IsAuthoritative);
        Assert.Equal(evaluationId, summary.EvaluationId);
        Assert.Equal(SecurityEvaluationStatus.Completed, summary.Status);
        Assert.Equal(outcome, summary.Outcome);
        Assert.Equal(reason, summary.ReasonCode);
        Assert.Equal(SecurityEnforcementMode.Shadow, summary.EnforcementMode);
    }

    [Theory]
    [InlineData(SecurityEnforcementMode.Disabled)]
    [InlineData(SecurityEnforcementMode.Shadow)]
    public void OnlyDisabledAndShadowAreRuntimeAllowed(SecurityEnforcementMode mode)
    {
        Assert.True(SecurityEnforcementBoundary.IsRuntimeAllowed(mode));
        SecurityEnforcementBoundary.ValidateRuntimeMode(mode);
        Assert.False(SecurityEnforcementBoundary.IsRuntimeAllowed(mode, enforcementAllowed: true));
        Assert.Throws<InvalidOperationException>(() => SecurityEnforcementBoundary.ValidateRuntimeMode(mode, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnforceCompatibilityValueCannotBeActivatedByAnOverride(bool enforcementAllowed)
    {
        Assert.Contains(SecurityEnforcementMode.Enforce, Enum.GetValues<SecurityEnforcementMode>());
        Assert.False(SecurityEnforcementBoundary.IsRuntimeAllowed(SecurityEnforcementMode.Enforce, enforcementAllowed));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityEnforcementBoundary.ValidateRuntimeMode(SecurityEnforcementMode.Enforce, enforcementAllowed));
    }

    [Fact]
    public void RequestReusesCanonicalSourceIdentityAndPreservesAbsentEvidence()
    {
        var source = ProjectSourceCodec.Decode(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "ProjectIde", "Fixtures", "source-v1.json"))).RequireSource();
        var subject = new SecuritySubjectRef(source.TenantId, Guid.Parse(Id));
        var operation = new SecurityOperationRef("projectide.analyze");
        var resource = new SecurityResourceRef(source.Context, source.Digest);
        var request = new SecurityEvaluationRequest(Guid.NewGuid(), subject, operation, resource,
            SecurityEnforcementMode.Shadow, source);
        Assert.Same(source, request.Source);
        Assert.Same(source.Context, request.Resource.Context);
        Assert.Same(source.Digest, request.Resource.InputDigest);
        Assert.Same(subject, request.Subject);
        Assert.Same(operation, request.Operation);
        Assert.Equal(source.TenantId, request.Subject.TenantId);
        Assert.Equal(Guid.Parse(Id), request.Subject.UserId);
        Assert.Equal("projectide.analyze", request.Operation.OperationId);
        Assert.Equal(SecurityEnforcementMode.Shadow, request.EnforcementMode);
        Assert.Null(request.Policy);
        Assert.Null(request.Compiler);
        Assert.NotEqual(Guid.Empty, request.EvaluationId);
    }

    [Fact]
    public void PolicyAndCompilerIdentityAreBoundedMetadataWithoutFabricatedDefaults()
    {
        var digest = ContentDigest.Compute("security-contract-test/1", [1, 2, 3]);
        var policy = new SecurityPolicySnapshot("test-policy", "1.0.0", digest);
        var compiler = new SecurityCompilerProvenance("test-host/1", "fixture-build-1");
        Assert.Same(digest, policy.ContentDigest);
        Assert.Equal("test-policy", policy.PolicySetId);
        Assert.Equal("1.0.0", policy.Version);
        Assert.Equal("test-host/1", compiler.Version);
        Assert.Equal("fixture-build-1", compiler.BuildIdentity);
        Assert.Throws<ArgumentException>(() => new SecurityCompilerProvenance("1", ""));
        Assert.Throws<ArgumentException>(() => new SecurityPolicySnapshot("", "1", digest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("private input\nwith stack")]
    [InlineData("credential=value")]
    public void ReferencesCannotCarryUnrestrictedFreeText(string value) =>
        Assert.Throws<ArgumentException>(() => new SecurityOperationRef(value));

    [Fact]
    public void UndefinedStatesAndDefaultIdentitiesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SecuritySubjectRef(default, Guid.Parse(Id)));
        Assert.Throws<ArgumentException>(() => new SecuritySubjectRef(new TenantId(Guid.Parse(Id)), Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityRuleResult("SEC-FND-TEST",
            (SecurityEvaluationStatus)99, null, SecurityReasonCode.NotExecuted));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityRuleResult("SEC-FND-TEST",
            SecurityEvaluationStatus.Completed, (SecurityDecisionOutcome)99, SecurityReasonCode.BindingsVerified));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityRuleResult("SEC-FND-TEST",
            SecurityEvaluationStatus.NotExecuted, null, (SecurityReasonCode)99));
        Assert.False(SecurityEnforcementBoundary.IsRuntimeAllowed((SecurityEnforcementMode)99));
    }
}
