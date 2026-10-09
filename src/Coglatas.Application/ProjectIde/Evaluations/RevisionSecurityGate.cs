using Coglatas.Application.ProjectIde.Security;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Evaluations;

public sealed class RevisionSecurityGate : IRevisionSecurityGate
{
    private readonly ISecurityEvaluationCoordinator _coordinator;
    private readonly ISecurityEvaluationStore _store;
    private readonly SecurityEnforcementMode _mode;

    public RevisionSecurityGate(ISecurityEvaluationCoordinator coordinator, ISecurityEvaluationStore store,
        SecurityEnforcementMode mode = SecurityEnforcementMode.Disabled, bool enforcementAllowed = false)
    {
        SecurityEnforcementBoundary.ValidateRuntimeMode(mode, enforcementAllowed);
        _coordinator = coordinator;
        _store = store;
        _mode = mode;
    }

    public async ValueTask<RevisionSecurityAnalysis> AnalyzeAsync(SecurityEvaluationRequest request,
        SecurityEvaluationEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidence);
        // A request cannot activate Shadow or Enforce against host configuration.
        SecurityEnforcementBoundary.ValidateRuntimeMode(request.EnforcementMode);
        var configuredRequest = new SecurityEvaluationRequest(request.EvaluationId, request.Subject,
            request.Operation, request.Resource, _mode, request.Source, request.Policy, request.Compiler);
        var binding = SecurityBinding.Create(configuredRequest, evidence);
        if (_mode == SecurityEnforcementMode.Disabled)
            return new(new(null, _mode, SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.NotExecuted),
                binding.Digest, SecurityRecordingStatus.NotRequested);

        var recording = SecurityRecordingStatus.Unavailable;
        try
        {
            if (await _store.CreatePendingAsync(binding, cancellationToken))
                recording = SecurityRecordingStatus.Pending;
        }
        catch (Exception)
        {
            recording = SecurityRecordingStatus.Failed;
        }

        SecurityDecision decision;
        try
        {
            decision = await _coordinator.EvaluateAsync(binding, cancellationToken);
            if (decision is null || decision.Status == SecurityEvaluationStatus.Pending)
                throw new InvalidOperationException("The coordinator must return a terminal evaluation.");
        }
        catch (Exception)
        {
            var cancelled = cancellationToken.IsCancellationRequested;
            decision = new(cancelled ? SecurityEvaluationStatus.Cancelled : SecurityEvaluationStatus.Failed,
                null, cancelled ? SecurityReasonCode.EvaluationCancelled : SecurityReasonCode.EvaluationFailed, []);
        }

        if (recording == SecurityRecordingStatus.Pending)
        {
            try
            {
                // A cancelled analysis can still finish its already committed Pending record.
                // Cleanup is cooperative and awaited; no detached writer can outlive this call.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var terminal = await _store.TerminalizeAsync(binding, decision, cleanup.Token);
                // AlreadyTerminal may contain another result: never claim this result was recorded.
                if (terminal == SecurityTerminalizationResult.Terminalized)
                    recording = SecurityRecordingStatus.Recorded;
            }
            catch (Exception)
            {
                recording = SecurityRecordingStatus.Failed;
            }
        }

        return new(new(request.EvaluationId, _mode, decision.Status, decision.Outcome, decision.ReasonCode),
            binding.Digest, recording);
    }
}

