using Coglatas.Domain.Common;

namespace Coglatas.Domain.Entities;

public sealed class SurveyFeedbackExportRequest : AuditableEntity, ITenantEntity
{
    private SurveyFeedbackExportRequest()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }

    public Guid PresentationId { get; private init; }
    public Guid RequesterId { get; private init; }
    public string Mode { get; private init; } = "";
    public string ColumnsJson { get; private init; } = "";
    public string SourceDigest { get; private init; } = "";
    public string ReceiptRequestId { get; private init; } = "";
    public DateTimeOffset ExpiresAtUtc { get; private init; }

    public static SurveyFeedbackExportRequest Prepare(
        Guid id, Guid tenantId, Guid presentationId, Guid requesterId, string mode, string columnsJson,
        string sourceDigest, string receiptRequestId, DateTimeOffset expiresAtUtc)
    {
        return new SurveyFeedbackExportRequest
        {
            Id = id,
            TenantId = tenantId,
            PresentationId = presentationId,
            RequesterId = requesterId,
            Mode = mode,
            ColumnsJson = columnsJson,
            SourceDigest = sourceDigest,
            ReceiptRequestId = receiptRequestId,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}

public sealed class SurveyFeedbackExportGrant : AuditableEntity, ITenantEntity
{
    private SurveyFeedbackExportGrant()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }

    public Guid ExportRequestId { get; private init; }
    public Guid RecipientUserId { get; private init; }
    public string ReceiptRequestId { get; private init; } = "";
    public DateTimeOffset ExpiresAtUtc { get; private init; }

    public static SurveyFeedbackExportGrant Issue(
        Guid id, SurveyFeedbackExportRequest request, string receiptRequestId, DateTimeOffset expiresAtUtc)
    {
        if (expiresAtUtc > request.ExpiresAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));

        return new SurveyFeedbackExportGrant
        {
            Id = id,
            TenantId = request.TenantId,
            ExportRequestId = request.Id,
            RecipientUserId = request.RequesterId,
            ReceiptRequestId = receiptRequestId,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
