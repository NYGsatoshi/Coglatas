using Coglatas.Application.ProjectIde.Evaluations;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class SecurityEvaluationDiagnosticsTests
{
    [Fact]
    public async Task RejectedEnforceRequestIsVisibleWithoutExecutingOrRecording()
    {
        var diagnostics = new SecurityEvaluationDiagnostics();
        var gate = new RevisionSecurityGate(new UnusedCoordinator(), new UnusedStore(), diagnostics: diagnostics);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await gate.AnalyzeAsync(SecurityEvaluationTestData.Request(SecurityEnforcementMode.Enforce), new()));
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(1, snapshot.Total);
        Assert.Equal(1, snapshot.RequestedEnforce);
        Assert.Equal(0, snapshot.Allow);
        Assert.Equal(0, snapshot.Deny);
        Assert.Equal(0, snapshot.Failed);
        Assert.Equal(0, snapshot.PersistenceFailed);
    }

    [Fact]
    public void ConcurrentProcessCountersAreBoundedAndDoNotLoseRequests()
    {
        var diagnostics = new SecurityEvaluationDiagnostics();
        Parallel.For(0, 1000, _ => diagnostics.RecordRequested(SecurityEnforcementMode.Disabled));
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(1000, snapshot.Total);
        Assert.Equal(1000, snapshot.RequestedDisabled);
        Assert.Equal(0, snapshot.RequestedShadow);
        Assert.Equal(0, snapshot.RequestedEnforce);
        Assert.All(typeof(SecurityEvaluationCounterSnapshot).GetProperties(), property => Assert.Equal(typeof(long), property.PropertyType));
        Assert.Equal(13, typeof(SecurityEvaluationCounterSnapshot).GetProperties().Length);
    }

    private sealed class UnusedCoordinator : Coglatas.Application.ProjectIde.Security.ISecurityEvaluationCoordinator
    {
        public ValueTask<SecurityDecision> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rejected configuration must not evaluate.");
    }

    private sealed class UnusedStore : ISecurityEvaluationStore
    {
        public Task<bool> CreatePendingAsync(SecurityBinding binding, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rejected configuration must not write.");
        public Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Rejected configuration must not write.");
        public Task<SecurityEvaluationRecord?> FindAsync(Guid projectId, Guid evaluationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rejected configuration must not read.");
    }
}
