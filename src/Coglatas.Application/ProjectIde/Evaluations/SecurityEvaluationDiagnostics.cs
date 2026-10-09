using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Evaluations;

public sealed record SecurityEvaluationCounterSnapshot(
    long Total, long Failed, long Cancelled, long TimedOut,
    long Allow, long Deny, long Unknown, long Quarantine,
    long PersistenceFailed, long BindingMismatch,
    long RequestedDisabled, long RequestedShadow, long RequestedEnforce);

/// <summary>Process-local attempt counters using the existing diagnostics pattern; no ID labels or payloads.</summary>
public sealed class SecurityEvaluationDiagnostics
{
    private long _total, _failed, _cancelled, _timedOut;
    private long _allow, _deny, _unknown, _quarantine;
    private long _persistenceFailed, _bindingMismatch;
    private long _requestedDisabled, _requestedShadow, _requestedEnforce;

    public void RecordRequested(SecurityEnforcementMode mode)
    {
        Interlocked.Increment(ref _total);
        switch (mode)
        {
            case SecurityEnforcementMode.Disabled: Interlocked.Increment(ref _requestedDisabled); break;
            case SecurityEnforcementMode.Shadow: Interlocked.Increment(ref _requestedShadow); break;
            case SecurityEnforcementMode.Enforce: Interlocked.Increment(ref _requestedEnforce); break;
        }
    }

    public void RecordResult(SecurityAnalysisSummary summary, SecurityRecordingStatus recording)
    {
        switch (summary.Status)
        {
            case SecurityEvaluationStatus.Failed: Interlocked.Increment(ref _failed); break;
            case SecurityEvaluationStatus.Cancelled: Interlocked.Increment(ref _cancelled); break;
            case SecurityEvaluationStatus.TimedOut: Interlocked.Increment(ref _timedOut); break;
        }
        if (summary.Status == SecurityEvaluationStatus.Completed)
        {
            switch (summary.Outcome)
            {
                case SecurityDecisionOutcome.Allow: Interlocked.Increment(ref _allow); break;
                case SecurityDecisionOutcome.Deny: Interlocked.Increment(ref _deny); break;
                case SecurityDecisionOutcome.Unknown: Interlocked.Increment(ref _unknown); break;
                case SecurityDecisionOutcome.Quarantine: Interlocked.Increment(ref _quarantine); break;
            }
        }
        if (recording == SecurityRecordingStatus.Failed)
            Interlocked.Increment(ref _persistenceFailed);
        if (summary.ReasonCode == SecurityReasonCode.BindingMismatch)
            RecordBindingMismatch();
    }

    public void RecordBindingMismatch() => Interlocked.Increment(ref _bindingMismatch);

    public SecurityEvaluationCounterSnapshot Snapshot() => new(
        Interlocked.Read(ref _total), Interlocked.Read(ref _failed), Interlocked.Read(ref _cancelled), Interlocked.Read(ref _timedOut),
        Interlocked.Read(ref _allow), Interlocked.Read(ref _deny), Interlocked.Read(ref _unknown), Interlocked.Read(ref _quarantine),
        Interlocked.Read(ref _persistenceFailed), Interlocked.Read(ref _bindingMismatch),
        Interlocked.Read(ref _requestedDisabled), Interlocked.Read(ref _requestedShadow), Interlocked.Read(ref _requestedEnforce));
}
