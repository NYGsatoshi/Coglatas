using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Evaluations;

/// <summary>Application-only hosts have no persistence authority. Infrastructure replaces this registration.</summary>
internal sealed class UnavailableSecurityEvaluationStore : ISecurityEvaluationStore
{
    public Task<bool> CreatePendingAsync(SecurityBinding binding, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
        CancellationToken cancellationToken = default) => Task.FromResult(SecurityTerminalizationResult.Unavailable);

    public Task<SecurityEvaluationRecord?> FindAsync(Guid projectId, Guid evaluationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SecurityEvaluationRecord?>(null);
}

