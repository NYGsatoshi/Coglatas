namespace Coglatas.Application.Common.Interfaces;

public enum UploadCommitDisposition
{
    NotCommitted,
    Committed,
    Unknown
}

/// <summary>
/// Evidence for one upload save attempt. NotCommitted means no save was attempted,
/// or an owned precommit transaction was rolled back. Destructive compensation
/// additionally requires discarding the failed scope's pending writes.
/// </summary>
public sealed class UploadSaveOutcome
{
    public UploadCommitDisposition Disposition { get; set; } = UploadCommitDisposition.NotCommitted;
}
