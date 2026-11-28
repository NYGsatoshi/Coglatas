using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.SurveyFeedback;

public sealed record FeedbackAnswerResource(Guid PresentationId, Guid SubmissionId, Guid AnswerId,
    string FieldKey, string FieldType, bool IsHidden, long VersionNo);

public sealed record FeedbackSubmissionResource(Guid PresentationId, FormSubmission Submission);

public sealed record EligibleFeedbackRecipient(Guid RecipientRef, string Label);

public sealed record FeedbackApprovalCandidate(FeedbackViewerGrant Invitation, string? InviterLabel, string? RecipientLabel);

public sealed record FeedbackInvitationApprovalRow(FeedbackInvitationDto Invitation, string PresentationLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InviterLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RecipientLabel);

public sealed record FeedbackInvitationSummary(Guid Id, string PresentationLabel, string Direction, string Status,
    long VersionNo, DateTimeOffset DecisionDeadline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ExpiresAt);

public sealed record FeedbackInvitationDto(Guid Id, Guid PresentationId, string Status, long VersionNo,
    string ApprovalState, bool RecipientAccepted, DateTimeOffset DecisionDeadline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ExpiresAt);

public sealed record HiddenAnswerModerationDetail(Guid AnswerId, string Visibility, long VersionNo, string Body);

public sealed record FeedbackVisibilityDto(Guid AnswerId, string Visibility, long VersionNo);

public sealed record FeedbackReportDto(Guid ReportId, string State, long VersionNo);

public sealed record FeedbackReportQueueRow(FeedbackReportDto Report, DateTimeOffset OpenedAtUtc);

public sealed record FeedbackReportQueueProjection(Guid ReportId, FeedbackReportStatus Status, long VersionNo,
    DateTimeOffset OpenedAtUtc);

public sealed record FeedbackReportDetail(FeedbackReportDto Report, Guid PresentationId, string Reason,
    DateTimeOffset OpenedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Visibility,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DispositionReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DisposedAtUtc);

public sealed record FeedbackSubmissionDto(Guid SubmissionId, string Status, long VersionNo);

public sealed record FeedbackReaderState(bool IsRead, long ObservedRevision);

public interface IFeedbackLifecycleRepository
{
    Task<Result<T>> InWriteTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken, string concurrentErrorCode = "ConcurrentModification");
    Task FlushAsync(CancellationToken cancellationToken);
    Task<FeedbackInvitationPolicy?> ReadInvitationPolicyAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken);
    Task<FeedbackInvitationPolicy?> FindInvitationPolicyForUpdateAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken);
    void AddInvitationPolicy(FeedbackInvitationPolicy policy);
    Task<IReadOnlyList<FeedbackViewerGrant>> ReadInvitationsAsync(Guid tenantId, Guid userId, string? direction,
        FeedbackPagePosition? after, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeedbackApprovalCandidate>> ReadApprovalCandidatesAsync(Guid tenantId, Guid eventId,
        DateTimeOffset now, FeedbackPagePosition? after, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<EligibleFeedbackRecipient>> ReadRecipientCandidatesAsync(Guid tenantId, Guid workspaceId,
        Guid inviterId, Guid? after, int take, CancellationToken cancellationToken);
    Task<FeedbackViewerGrant?> GetInvitationAsync(Guid tenantId, Guid invitationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<FeedbackViewerGrant>> GetLiveInvitationsAsync(Guid tenantId, Guid presentationId,
        Guid recipientId, CancellationToken cancellationToken);
    Task<FeedbackAnswerResource?> FindAnswerAsync(Guid tenantId, Guid answerId, CancellationToken cancellationToken);
    Task<AnswerVisibilityDecision?> GetVisibilityAsync(Guid tenantId, Guid answerId, CancellationToken cancellationToken);
    Task<string?> ReadAnswerValueJsonAsync(Guid tenantId, Guid answerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeedbackReportQueueProjection>> ReadReportQueueAsync(Guid tenantId, Guid presentationId,
        FeedbackReportStatus? status, FeedbackPagePosition? after, int take, CancellationToken cancellationToken);
    Task<FeedbackReportCase?> GetReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken);
    Task<FeedbackSubmissionResource?> GetSubmissionAsync(Guid tenantId, Guid submissionId,
        CancellationToken cancellationToken);
    Task<FeedbackReadState?> GetReadStateAsync(Guid tenantId, Guid submissionId, Guid userId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ReadInvalidationCandidatesAsync(Guid tenantId, Guid presentationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TeacherPrivateNote>> ReadPrivateNotesAsync(Guid tenantId, Guid presentationId, Guid authorId,
        FeedbackPagePosition? after, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<SharedTeacherFeedback>> ReadSharedFeedbackAsync(Guid tenantId, Guid presentationId, Guid? authorId,
        FeedbackPagePosition? after, int take, CancellationToken cancellationToken);
    void AddPrivateNote(TeacherPrivateNote note);
    void AddSharedFeedback(SharedTeacherFeedback note);
    void AddInvitation(FeedbackViewerGrant invitation);
    void AddVisibility(AnswerVisibilityDecision decision);
    void AddReport(FeedbackReportCase report);
    void AddReadState(FeedbackReadState state);
}

public interface ISurveyFeedbackInvalidation
{
    Task<IReadOnlyList<Guid>> ReadCurrentRecipientsAsync(Guid tenantId, Guid presentationId,
        CancellationToken cancellationToken);
    Task InvalidateAsync(Guid tenantId, Guid presentationId, CancellationToken cancellationToken);
    Task InvalidateAccessAsync(Guid tenantId, Guid recipientId,
        CancellationToken cancellationToken);
}
