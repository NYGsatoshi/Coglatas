using System.Text.Json;
using Coglatas.Application;
using Coglatas.Application.ProjectIde.Evaluations;
using Coglatas.Application.ProjectIde.Security;
using Coglatas.Domain.ProjectIde;
using Coglatas.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.ProjectIde;

public sealed class RevisionSecurityGateTests
{
    public static TheoryData<bool, string> LegacyMatrix => new()
    {
        { false, "disabled" }, { true, "disabled" },
        { false, "allow" }, { true, "allow" },
        { false, "deny" }, { true, "deny" },
        { false, "unknown" }, { true, "unknown" },
        { false, "quarantine" }, { true, "quarantine" },
        { false, "exception" }, { true, "exception" },
        { false, "cancelled" }, { true, "cancelled" },
        { false, "timeout" }, { true, "timeout" },
        { false, "create-failed" }, { true, "create-failed" },
        { false, "terminal-failed" }, { true, "terminal-failed" },
        { false, "unavailable" }, { true, "unavailable" },
        { false, "already-terminal" }, { true, "already-terminal" }
    };

    [Theory]
    [MemberData(nameof(LegacyMatrix))]
    public async Task HeadlessAnalysisPreservesLegacyDecisionAndReportsTruthfulShadowMetadata(bool permitted, string scenario)
    {
        var baseline = JsonSerializer.SerializeToUtf8Bytes(new { permitted, requiredChecks = false, conflict = true, expectedHead = "original" });
        var store = new TestStore(scenario);
        var coordinator = new TestCoordinator(scenario);
        var mode = scenario == "disabled" ? SecurityEnforcementMode.Disabled : SecurityEnforcementMode.Shadow;
        var gate = new RevisionSecurityGate(coordinator, store, mode);
        var request = SecurityEvaluationTestData.Request();
        // Deterministic future #905 call site: await analysis before returning the existing decision.
        var (after, analysis) = await AnalyzeCandidateAsync(gate, request, baseline);
        Assert.Same(baseline, after);
        Assert.Equal(baseline, after);
        Assert.Equal(permitted, JsonDocument.Parse(after).RootElement.GetProperty("permitted").GetBoolean());
        Assert.False(analysis.Summary.IsAuthoritative);
        Assert.Equal(mode, analysis.Summary.EnforcementMode);
        Assert.True(analysis.Matches(SecurityEvaluationTestData.Binding(request)));
        Assert.Equal(SecurityEvaluationTestData.Binding(request).Digest, analysis.BindingDigest);
        if (mode == SecurityEnforcementMode.Disabled)
        {
            Assert.Null(analysis.Summary.EvaluationId);
            Assert.Equal(SecurityEvaluationStatus.NotExecuted, analysis.Summary.Status);
            Assert.Null(analysis.Summary.Outcome);
            Assert.Equal(SecurityReasonCode.NotExecuted, analysis.Summary.ReasonCode);
            Assert.Equal(SecurityRecordingStatus.NotRequested, analysis.RecordingStatus);
            Assert.Equal(0, coordinator.Calls);
            Assert.Equal(0, store.Creates);
            Assert.Equal(0, store.Terminals);
            return;
        }
        Assert.Equal(request.EvaluationId, analysis.Summary.EvaluationId);
        Assert.Equal(1, coordinator.Calls);
        Assert.Equal(1, store.Creates);
        var expectedStatus = scenario switch
        {
            "exception" => SecurityEvaluationStatus.Failed,
            "cancelled" => SecurityEvaluationStatus.Cancelled,
            "timeout" => SecurityEvaluationStatus.TimedOut,
            _ => SecurityEvaluationStatus.Completed
        };
        Assert.Equal(expectedStatus, analysis.Summary.Status);
        Assert.Equal(scenario switch
        {
            "deny" => SecurityDecisionOutcome.Deny,
            "unknown" => SecurityDecisionOutcome.Unknown,
            "quarantine" => SecurityDecisionOutcome.Quarantine,
            "exception" or "cancelled" or "timeout" => null,
            _ => SecurityDecisionOutcome.Allow
        }, analysis.Summary.Outcome);
        Assert.Equal(scenario switch
        {
            "create-failed" or "terminal-failed" => SecurityRecordingStatus.Failed,
            "unavailable" => SecurityRecordingStatus.Unavailable,
            "already-terminal" => SecurityRecordingStatus.Pending,
            _ => SecurityRecordingStatus.Recorded
        }, analysis.RecordingStatus);
        Assert.Equal(scenario is "unavailable" or "create-failed" ? 0 : 1, store.Terminals);
        Assert.DoesNotContain("private-evaluator-canary", JsonSerializer.Serialize(analysis));
    }

    private static async Task<(byte[] Decision, RevisionSecurityAnalysis Analysis)> AnalyzeCandidateAsync(
        IRevisionSecurityGate gate, SecurityEvaluationRequest request, byte[] existingDecision)
    {
        var analysis = await gate.AnalyzeAsync(request, new(SecurityEvaluationTestData.Source(),
            SecurityEvaluationTestData.Policy(), SecurityEvaluationTestData.Compiler()));
        return (existingDecision, analysis);
    }

    [Fact]
    public async Task DefaultHostModeOverridesRequestShadowOptInAndEnforceIsRejectedBeforeEffects()
    {
        var store = new TestStore("allow");
        var coordinator = new TestCoordinator("allow");
        var gate = new RevisionSecurityGate(coordinator, store);
        var disabled = await gate.AnalyzeAsync(SecurityEvaluationTestData.Request(), new());
        Assert.Equal(SecurityEnforcementMode.Disabled, disabled.Summary.EnforcementMode);
        Assert.Equal(SecurityRecordingStatus.NotRequested, disabled.RecordingStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await gate.AnalyzeAsync(SecurityEvaluationTestData.Request(SecurityEnforcementMode.Enforce), new()));
        Assert.Throws<InvalidOperationException>(() => new RevisionSecurityGate(coordinator, store, SecurityEnforcementMode.Enforce));
        Assert.Throws<InvalidOperationException>(() => new RevisionSecurityGate(coordinator, store, SecurityEnforcementMode.Shadow, true));
        Assert.Equal(0, coordinator.Calls);
        Assert.Equal(0, store.Creates);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("project")]
    [InlineData("branch")]
    [InlineData("revision")]
    [InlineData("candidate")]
    [InlineData("input")]
    [InlineData("policy")]
    [InlineData("compiler")]
    [InlineData("host-policy")]
    [InlineData("host-compiler")]
    public async Task ForeignOrStaleBindingCannotBeAttachedAsMatching(string changed)
    {
        var request = SecurityEvaluationTestData.Request();
        var evidence = new SecurityEvaluationEvidence(SecurityEvaluationTestData.Source(), SecurityEvaluationTestData.Policy(), SecurityEvaluationTestData.Compiler());
        var analysis = await new RevisionSecurityGate(new TestCoordinator("allow"), new TestStore("allow"),
            SecurityEnforcementMode.Shadow).AnalyzeAsync(request, evidence);
        var original = request.Resource.Context;
        var branch = new BranchRef(changed == "tenant" ? TenantId.New() : original.Branch.TenantId,
            changed == "project" ? ProjectId.New() : original.Branch.ProjectId,
            changed == "branch" ? BranchId.New() : original.Branch.BranchId);
        var revision = new RevisionRef(branch, changed == "revision" ? RevisionId.New() : original.CommittedRevision!.RevisionId);
        var context = changed == "candidate"
            ? SourceRevisionContext.Candidate(new(ProposalId.New(), CandidateRevisionId.New(), revision, revision))
            : SourceRevisionContext.Committed(revision);
        var current = new SecurityEvaluationRequest(Guid.NewGuid(), request.Subject, request.Operation,
            new(context, changed == "input" ? ContentDigest.Compute("test", [1]) : request.Resource.InputDigest),
            request.EnforcementMode, request.Source,
            changed == "policy" ? SecurityEvaluationTestData.Policy("{\"rules\":{}}").Snapshot : request.Policy,
            changed == "compiler" ? new("test-host/2", "different") : request.Compiler);
        var host = new SecurityEvaluationEvidence(evidence.Source,
            changed == "host-policy" ? SecurityEvaluationTestData.Policy("{\"rules\":{}}") : evidence.Policy,
            changed == "host-compiler" ? new("test-host/2", "different") : evidence.Compiler);
        Assert.False(analysis.Matches(SecurityBinding.Create(current, host)));
    }

    [Fact]
    public async Task CoordinatorCancellationFinishesPendingWithAwaitedIndependentCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new TestStore("allow");
        var coordinator = new CancellingCoordinator(cancellation);
        var result = await new RevisionSecurityGate(coordinator, store, SecurityEnforcementMode.Shadow)
            .AnalyzeAsync(SecurityEvaluationTestData.Request(), new(), cancellation.Token);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, result.Summary.Status);
        Assert.Null(result.Summary.Outcome);
        Assert.Equal(SecurityRecordingStatus.Recorded, result.RecordingStatus);
        Assert.Equal(SecurityEvaluationStatus.Cancelled, store.Decision!.Status);
        Assert.False(store.CleanupWasCancelled);
        Assert.Equal(1, store.Terminals);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("proposal")]
    [InlineData("base")]
    [InlineData("head")]
    public async Task CandidateIdentityAndCapturedRevisionsCannotBeReused(string changed)
    {
        var original = SecurityEvaluationTestData.Request();
        var branch = original.Resource.Context.Branch;
        var baseline = new RevisionRef(branch, RevisionId.New());
        var head = new RevisionRef(branch, RevisionId.New());
        var proposal = new ProposalContext(ProposalId.New(), CandidateRevisionId.New(), baseline, head);
        var source = ProjectSource.Create(SourceRevisionContext.Candidate(proposal), original.Source!.Documents);
        var request = new SecurityEvaluationRequest(original.EvaluationId, original.Subject, original.Operation,
            new(source.Context, source.Digest), original.EnforcementMode, source, original.Policy, original.Compiler);
        var evidence = new SecurityEvaluationEvidence(source, SecurityEvaluationTestData.Policy(), original.Compiler);
        var result = await new RevisionSecurityGate(new TestCoordinator("allow"), new TestStore("allow"),
            SecurityEnforcementMode.Shadow).AnalyzeAsync(request, evidence);
        Assert.True(result.Matches(SecurityBinding.Create(request, evidence)));
        var changedContext = SourceRevisionContext.Candidate(new(
            changed == "proposal" ? ProposalId.New() : proposal.ProposalId,
            changed == "candidate" ? CandidateRevisionId.New() : proposal.CandidateRevisionId,
            changed == "base" ? new(branch, RevisionId.New()) : baseline,
            changed == "head" ? new(branch, RevisionId.New()) : head));
        var current = new SecurityEvaluationRequest(Guid.NewGuid(), request.Subject, request.Operation,
            new(changedContext, source.Digest), request.EnforcementMode, source, request.Policy, request.Compiler);
        Assert.False(result.Matches(SecurityBinding.Create(current, evidence)));
    }

    [Fact]
    public async Task ReturnedAnalysisWaitsForTerminalPersistence()
    {
        var store = new AwaitedStore();
        var gate = new RevisionSecurityGate(new TestCoordinator("deny"), store, SecurityEnforcementMode.Shadow);
        var pending = gate.AnalyzeAsync(SecurityEvaluationTestData.Request(), new()).AsTask();
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        store.Finish.SetResult();
        var result = await pending;
        Assert.Equal(SecurityRecordingStatus.Recorded, result.RecordingStatus);
        Assert.Equal(SecurityDecisionOutcome.Deny, result.Summary.Outcome);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Shadow")]
    public async Task CompositionRootUsesEffectiveHostOptions(string mode)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<ISecurityEvaluationStore>(new TestStore("allow"));
        services.AddWebServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:EvaluationMode"] = mode
        }).Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IRevisionSecurityGate>();
        var result = await gate.AnalyzeAsync(SecurityEvaluationTestData.Request(), new());
        Assert.Equal(Enum.Parse<SecurityEnforcementMode>(mode), result.Summary.EnforcementMode);
    }

    [Fact]
    public async Task ApplicationOnlyHostHasNoPersistenceAuthorityAndDefaultsToDisabled()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IRevisionSecurityGate>();
        var result = await gate.AnalyzeAsync(SecurityEvaluationTestData.Request(), new());
        Assert.Equal(SecurityRecordingStatus.NotRequested, result.RecordingStatus);
        Assert.Equal(SecurityEnforcementMode.Disabled, result.Summary.EnforcementMode);
        var store = scope.ServiceProvider.GetRequiredService<ISecurityEvaluationStore>();
        var binding = SecurityEvaluationTestData.Binding();
        Assert.False(await store.CreatePendingAsync(binding));
        Assert.Equal(SecurityTerminalizationResult.Unavailable, await store.TerminalizeAsync(binding,
            new(SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.NotExecuted, [])));
        Assert.Null(await store.FindAsync(binding.Request.Resource.Context.Branch.ProjectId.Value,
            binding.Request.EvaluationId));
    }

    private sealed class TestCoordinator(string scenario) : ISecurityEvaluationCoordinator
    {
        public int Calls { get; private set; }
        public ValueTask<SecurityDecision> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (scenario == "exception") throw new InvalidOperationException("private-evaluator-canary");
            var status = scenario switch
            {
                "cancelled" => SecurityEvaluationStatus.Cancelled,
                "timeout" => SecurityEvaluationStatus.TimedOut,
                _ => SecurityEvaluationStatus.Completed
            };
            var outcome = scenario switch
            {
                "deny" => SecurityDecisionOutcome.Deny,
                "unknown" => SecurityDecisionOutcome.Unknown,
                "quarantine" => SecurityDecisionOutcome.Quarantine,
                _ => SecurityDecisionOutcome.Allow
            };
            var reason = status switch
            {
                SecurityEvaluationStatus.Cancelled => SecurityReasonCode.EvaluationCancelled,
                SecurityEvaluationStatus.TimedOut => SecurityReasonCode.EvaluationTimedOut,
                _ => outcome switch
                {
                    SecurityDecisionOutcome.Deny => SecurityReasonCode.PolicyViolation,
                    SecurityDecisionOutcome.Unknown => SecurityReasonCode.MissingEvidence,
                    SecurityDecisionOutcome.Quarantine => SecurityReasonCode.BindingMismatch,
                    _ => SecurityReasonCode.BindingsVerified
                }
            };
            return ValueTask.FromResult(new SecurityDecision(status, status == SecurityEvaluationStatus.Completed ? outcome : null,
                reason, [new("fixture", status, status == SecurityEvaluationStatus.Completed ? outcome : null, reason)]));
        }
    }

    private sealed class CancellingCoordinator(CancellationTokenSource cancellation) : ISecurityEvaluationCoordinator
    {
        public ValueTask<SecurityDecision> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private class TestStore(string scenario) : ISecurityEvaluationStore
    {
        public int Creates { get; private set; }
        public int Terminals { get; private set; }
        public SecurityDecision? Decision { get; private set; }
        public bool CleanupWasCancelled { get; private set; }
        public Task<bool> CreatePendingAsync(SecurityBinding binding, CancellationToken cancellationToken = default)
        {
            Creates++;
            if (scenario == "create-failed") throw new InvalidOperationException("private-persistence-canary");
            return Task.FromResult(scenario != "unavailable");
        }
        public virtual Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
            CancellationToken cancellationToken = default)
        {
            Terminals++;
            Decision = decision;
            CleanupWasCancelled = cancellationToken.IsCancellationRequested;
            if (scenario == "terminal-failed") throw new InvalidOperationException("private-persistence-canary");
            return Task.FromResult(scenario == "already-terminal"
                ? SecurityTerminalizationResult.AlreadyTerminal : SecurityTerminalizationResult.Terminalized);
        }
        public Task<SecurityEvaluationRecord?> FindAsync(Guid projectId, Guid evaluationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SecurityEvaluationRecord?>(null);
    }

    private sealed class AwaitedStore() : TestStore("allow")
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Finish.Task.WaitAsync(cancellationToken);
            return await base.TerminalizeAsync(binding, decision, cancellationToken);
        }
    }
}
