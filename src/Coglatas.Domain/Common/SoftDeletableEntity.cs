namespace Coglatas.Domain.Common;

public abstract class SoftDeletableEntity : AuditableEntity
{
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; private set; }
    public string? DeleteReason { get; private set; }

    public bool IsDeleted => DeletedAt.HasValue;

    public void MarkDeleted(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
    }

    public void MarkDeleted(DateTimeOffset deletedAt, Guid? deletedByUserId, string? reason = null)
    {
        DeletedAt = deletedAt;
        DeletedByUserId = deletedByUserId;
        DeleteReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedByUserId = null;
        DeleteReason = null;
    }
}
