using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.SurveyFeedback.Csv;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeedbackCsvRequest(string? Mode, IReadOnlyList<string>? Columns);

public sealed record FeedbackCsvQueued(Guid ExportRequestId, string State);

public sealed record FeedbackCsvRequestReceipt(FeedbackCsvQueued Data, string RequestId);

public sealed record FeedbackCsvStatus(
    Guid ExportRequestId,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ExpiresAt);

public sealed record FeedbackCsvGrantReceipt(Guid GrantId, DateTimeOffset ExpiresAt);

public sealed record FeedbackCsvGrantResponse(FeedbackCsvGrantReceipt Data, string RequestId);

public sealed record FeedbackCsvFile(byte[] Content, string SourceDigest);

public sealed record FeedbackCsvTarget(Guid Id, long VersionNo, bool ResultsStopped);

public sealed record FeedbackCsvSubmission(Guid Id, DateTimeOffset SubmittedAtUtc, long VersionNo);

public sealed record FeedbackCsvAnswer(
    Guid Id,
    Guid SubmissionId,
    Guid FieldId,
    string FieldKey,
    string Label,
    SurveyFieldType FieldType,
    string OptionsJson,
    string ValueJson,
    long VisibilityVersionNo,
    bool IsHidden);

public sealed record FeedbackCsvSnapshot(
    long PresentationVersion,
    long EventSettingsVersion,
    long PresentationSettingsVersion,
    IReadOnlyList<FeedbackCsvTarget> Targets,
    IReadOnlyList<FeedbackCsvSubmission> Submissions,
    IReadOnlyList<FeedbackCsvAnswer> Answers);

public interface IFeedbackCsvRepository
{
    Task<Result<FeedbackCsvSnapshot>> ReadVisibleAsync(
        PresentationFeedbackScope scope, int maximum, CancellationToken cancellationToken);

    Task<SurveyFeedbackExportRequest?> FindRequestAsync(
        Guid tenantId, Guid requesterId, Guid requestId, CancellationToken cancellationToken);

    Task<SurveyFeedbackExportGrant?> FindGrantAsync(
        Guid tenantId, Guid requesterId, Guid exportRequestId, Guid grantId, CancellationToken cancellationToken);

    void AddRequest(SurveyFeedbackExportRequest request);

    void AddGrant(SurveyFeedbackExportGrant grant);
}
