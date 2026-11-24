using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.SurveyFeedback;

public sealed class FeedbackModerationService(
    SurveyFeedbackAuthorization authorization,
    IFeedbackLifecycleRepository repository,
    IAuditLogger audit,
    ISurveyFeedbackInvalidation invalidation,
    IClock clock)
{
    public Task<Result<FeedbackReportDto>> ReportAsync(Guid answerId, string? reason, CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackReportDto>();
            }

            var answer = await repository.FindAnswerAsync(actor.TenantId, answerId, token);
            var access = answer is null ? null : await authorization.ReadAccessAsync(actor, answer.PresentationId, token);
            if (answer is not { IsHidden: false } || access is not { CanRead: true } ||
                !await authorization.CanReportAsync(actor, access, token))
            {
                return SurveyFeedbackService.NotFound<FeedbackReportDto>();
            }

            if (!IsValidReason(reason))
            {
                return SurveyFeedbackService.Invalid<FeedbackReportDto>();
            }

            var report = new FeedbackReportCase
            {
                TenantId = actor.TenantId,
                PresentationId = answer.PresentationId,
                FormSubmissionValueId = answerId,
                ReporterUserId = actor.UserId,
                Reason = reason!
            };
            repository.AddReport(report);
            await audit.LogUserActionAsync(actor.UserId, "Feedback.ReportOpened", nameof(FeedbackReportCase), report.Id,
                cancellationToken: token);
            await invalidation.InvalidateAsync(actor.TenantId, report.PresentationId, token);
            return Result<FeedbackReportDto>.Success(ToDto(report));
        }, cancellationToken);

    public Task<Result<FeedbackVisibilityDto>> ChangeVisibilityAsync(Guid answerId, string change, string? reason,
        long expectedVersionNo, CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackVisibilityDto>();
            }

            var answer = await repository.FindAnswerAsync(actor.TenantId, answerId, token);
            var access = answer is null ? null : await authorization.ReadAccessAsync(actor, answer.PresentationId, token);
            if (answer is null || !await CanModerateAsync(actor, access, token) || answer.FieldType != "Text")
            {
                return SurveyFeedbackService.NotFound<FeedbackVisibilityDto>();
            }

            if (!IsValidReason(reason) || expectedVersionNo <= 0 || change is not ("hide" or "urgentHide" or "restore"))
            {
                return SurveyFeedbackService.Invalid<FeedbackVisibilityDto>();
            }

            if (answer.VersionNo != expectedVersionNo)
            {
                return Conflict<FeedbackVisibilityDto>("RowVersionConflict");
            }

            var hide = change != "restore";
            if (answer.IsHidden == hide)
            {
                return Conflict<FeedbackVisibilityDto>("InvalidStateTransition");
            }

            var visibility = await repository.GetVisibilityAsync(actor.TenantId, answerId, token);
            if (visibility is null)
            {
                visibility = new AnswerVisibilityDecision
                {
                    TenantId = actor.TenantId,
                    FormSubmissionValueId = answerId
                };
                repository.AddVisibility(visibility);
            }

            visibility.Decide(hide, actor.UserId, reason!, expectedVersionNo);
            await audit.LogUserActionAsync(actor.UserId, "Feedback.VisibilityChanged", nameof(AnswerVisibilityDecision),
                visibility.Id, metadata: new Dictionary<string, object?> { ["decision"] = change },
                cancellationToken: token);
            await invalidation.InvalidateAsync(actor.TenantId, answer.PresentationId, token);
            return Result<FeedbackVisibilityDto>.Success(new FeedbackVisibilityDto(answerId, hide ? "hidden" : "visible",
                visibility.VersionNo));
        }, cancellationToken);

    public async Task<Result<FeedbackReportDetail>> ReportDetailAsync(Guid reportId,
        CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return SurveyFeedbackService.AuthenticationRequired<FeedbackReportDetail>();
        }

        var report = await repository.GetReportAsync(actor.TenantId, reportId, cancellationToken);
        var access = report is null ? null : await authorization.ReadAccessAsync(actor, report.PresentationId,
            cancellationToken);
        if (report is null || !await CanModerateAsync(actor, access, cancellationToken))
        {
            return SurveyFeedbackService.NotFound<FeedbackReportDetail>();
        }

        var answer = await repository.FindAnswerAsync(actor.TenantId, report.FormSubmissionValueId, cancellationToken);
        return Result<FeedbackReportDetail>.Success(new FeedbackReportDetail(ToDto(report), report.PresentationId,
            report.Reason, report.CreatedAt, answer is null ? null : answer.IsHidden ? "hidden" : "visible",
            report.DispositionReason, report.DisposedAtUtc));
    }

    public Task<Result<FeedbackReportDto>> ResolveAsync(Guid reportId, string resolution, string? reason,
        long expectedVersionNo, CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackReportDto>();
            }

            var report = await repository.GetReportAsync(actor.TenantId, reportId, token);
            var access = report is null ? null : await authorization.ReadAccessAsync(actor, report.PresentationId, token);
            if (report is null || !await CanModerateAsync(actor, access, token))
            {
                return SurveyFeedbackService.NotFound<FeedbackReportDto>();
            }

            if (expectedVersionNo <= 0 || !IsValidReason(reason) || resolution is not ("resolve" or "dismiss"))
            {
                return SurveyFeedbackService.Invalid<FeedbackReportDto>();
            }
            if (report.VersionNo != expectedVersionNo)
            {
                return Conflict<FeedbackReportDto>("RowVersionConflict");
            }
            if (report.Status != FeedbackReportStatus.Open)
            {
                return Conflict<FeedbackReportDto>("InvalidStateTransition");
            }

            report.Dispose(resolution == "dismiss", actor.UserId, reason!, expectedVersionNo, clock.UtcNow);
            await audit.LogUserActionAsync(actor.UserId, "Feedback.ReportDisposed", nameof(FeedbackReportCase), report.Id,
                metadata: new Dictionary<string, object?> { ["resolution"] = resolution }, cancellationToken: token);
            await invalidation.InvalidateAsync(actor.TenantId, report.PresentationId, token);
            return Result<FeedbackReportDto>.Success(ToDto(report));
        }, cancellationToken);

    public Task<Result<HiddenAnswerModerationDetail>> HiddenDetailAsync(Guid answerId,
        CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<HiddenAnswerModerationDetail>();
            }

            var answer = await repository.FindAnswerAsync(actor.TenantId, answerId, token);
            var access = answer is null ? null : await authorization.ReadAccessAsync(actor, answer.PresentationId, token);
            if (answer is not { IsHidden: true, FieldType: "Text" } ||
                !await CanModerateAsync(actor, access, token))
            {
                return SurveyFeedbackService.NotFound<HiddenAnswerModerationDetail>();
            }

            var json = await repository.ReadAnswerValueJsonAsync(actor.TenantId, answerId, token);
            var body = json is null ? null : JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrWhiteSpace(body))
            {
                return SurveyFeedbackService.NotFound<HiddenAnswerModerationDetail>();
            }

            await audit.LogUserActionAsync(actor.UserId, "FeedbackSecurity.HiddenAnswerRead",
                nameof(FormSubmissionValue), answerId, cancellationToken: token);
            return Result<HiddenAnswerModerationDetail>.Success(
                new HiddenAnswerModerationDetail(answerId, "hidden", answer.VersionNo, body));
        }, cancellationToken);

    public Task<Result<FeedbackSubmissionDto>> VoidAsync(Guid submissionId, string? reason, long expectedVersionNo,
        CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackSubmissionDto>();
            }

            var resource = await repository.GetSubmissionAsync(actor.TenantId, submissionId, token);
            var access = resource is null ? null : await authorization.ReadAccessAsync(actor, resource.PresentationId, token);
            if (resource is null || access is null ||
                !await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.submission.void", token))
            {
                return SurveyFeedbackService.NotFound<FeedbackSubmissionDto>();
            }

            if (expectedVersionNo <= 0 || !IsValidReason(reason))
            {
                return SurveyFeedbackService.Invalid<FeedbackSubmissionDto>();
            }
            if (resource.Submission.VersionNo != expectedVersionNo)
            {
                return Conflict<FeedbackSubmissionDto>("RowVersionConflict");
            }
            if (resource.Submission.Status != FormSubmissionStatus.Submitted)
            {
                return Conflict<FeedbackSubmissionDto>("InvalidStateTransition");
            }

            resource.Submission.Void(expectedVersionNo);
            await audit.LogUserActionAsync(actor.UserId, "Feedback.SubmissionVoided", nameof(FormSubmission),
                resource.Submission.Id, cancellationToken: token);
            await invalidation.InvalidateAsync(actor.TenantId, resource.PresentationId, token);
            return Result<FeedbackSubmissionDto>.Success(new FeedbackSubmissionDto(submissionId, "voided",
                resource.Submission.VersionNo));
        }, cancellationToken);

    public Task<Result<FeedbackReaderState>> SetReadStateAsync(Guid submissionId, long observedRevision, bool read,
        CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackReaderState>();
            }

            var resource = await repository.GetSubmissionAsync(actor.TenantId, submissionId, token);
            var access = resource is null ? null : await authorization.ReadAccessAsync(actor, resource.PresentationId, token);
            if (resource is null || access is not { CanRead: true } ||
                resource.Submission.Status != FormSubmissionStatus.Submitted)
            {
                return SurveyFeedbackService.NotFound<FeedbackReaderState>();
            }

            if (observedRevision < 0 || observedRevision > resource.Submission.VersionNo)
            {
                return SurveyFeedbackService.Invalid<FeedbackReaderState>();
            }

            var readState = await repository.GetReadStateAsync(actor.TenantId, submissionId, actor.UserId, token);
            if (readState is null)
            {
                readState = new FeedbackReadState
                {
                    TenantId = actor.TenantId,
                    FormSubmissionId = submissionId,
                    UserId = actor.UserId
                };
                repository.AddReadState(readState);
            }
            else if (observedRevision < readState.ObservedRevision)
            {
                return Conflict<FeedbackReaderState>("RowVersionConflict");
            }
            readState.ObservedRevision = observedRevision;
            readState.IsRead = read;
            return Result<FeedbackReaderState>.Success(new FeedbackReaderState(read, observedRevision));
        }, cancellationToken);

    private async Task<bool> CanModerateAsync(FeedbackActor actor, PresentationFeedbackAccess? access,
        CancellationToken cancellationToken) =>
        access is { CanRead: true } && (access.Reviewer || access.Operator) &&
        await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.moderate", cancellationToken);

    private static bool IsValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Length <= 1000;

    private static FeedbackReportDto ToDto(FeedbackReportCase report) =>
        new(report.Id, report.Status.ToString().ToLowerInvariant(), report.VersionNo);

    private static Result<T> Conflict<T>(string code) => Result<T>.Failure(
        new ApplicationErrorDetail(code, "The feedback resource cannot make the requested transition."));
}
