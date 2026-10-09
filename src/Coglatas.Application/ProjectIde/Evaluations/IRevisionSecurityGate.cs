using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Evaluations;

public enum SecurityRecordingStatus { NotRequested, Pending, Recorded, Unavailable, Failed }

/// <summary>
/// SEAM_ONLY: the future #905 analysis host supplies frozen claims and independent evidence.
/// This adapter neither authorizes nor modifies Source, review, promotion or Merge.
/// </summary>
public interface IRevisionSecurityGate
{
    ValueTask<RevisionSecurityAnalysis> AnalyzeAsync(SecurityEvaluationRequest request,
        SecurityEvaluationEvidence evidence, CancellationToken cancellationToken = default);
}

/// <summary>Safe non-authoritative metadata. Pending is not a durably recorded final decision.</summary>
public sealed class RevisionSecurityAnalysis(
    SecurityAnalysisSummary summary, ContentDigest bindingDigest, SecurityRecordingStatus recordingStatus)
{
    public SecurityAnalysisSummary Summary { get; } = summary;
    public ContentDigest BindingDigest { get; } = bindingDigest;
    public SecurityRecordingStatus RecordingStatus { get; } = recordingStatus;

    /// <summary>Integrity comparison against a fresh host binding, never an authorization check.</summary>
    public bool Matches(SecurityBinding current) => BindingDigest == current.Digest;
}

