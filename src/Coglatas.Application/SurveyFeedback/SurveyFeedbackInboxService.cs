using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Coglatas.Application.Common;

namespace Coglatas.Application.SurveyFeedback;

public sealed record FeedbackInboxFilters(Guid? PresentationId, Guid? PersonRef, string? FieldKey,
    bool? IsRead, bool? HasReport, string? Visibility, string? ReportStatus);

public sealed record FeedbackInboxRow(FeedbackRow Submission, FeedbackReaderState ReaderState,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReportStatus);

public sealed record FeedbackInboxPage(IReadOnlyList<FeedbackInboxRow> Items, long UnreadCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? UnresolvedReportCount);

public sealed record FeedbackInboxSnapshot(SurveyFeedbackSnapshot Submission, FeedbackReaderState ReaderState,
    string? ReportStatus, bool CanModerate);

public sealed record FeedbackInboxSnapshotPage(IReadOnlyList<FeedbackInboxSnapshot> Items,
    long UnreadCount, long? UnresolvedReportCount);

public interface ISurveyFeedbackInboxRepository
{
    Task<FeedbackInboxSnapshotPage> ReadAsync(Guid tenantId, Guid userId, Guid[] readable,
        Guid[] moderatorScopes, FeedbackInboxFilters filters, FeedbackPagePosition? after,
        int take, CancellationToken cancellationToken);
}

public sealed class SurveyFeedbackInboxService(
    SurveyFeedbackAuthorization authorization,
    ISurveyFeedbackRepository repository,
    ISurveyFeedbackInboxRepository inbox,
    ISurveyFeedbackCursor cursors)
{
    public async Task<Result<FeedbackInboxPage>> ReadAsync(FeedbackInboxFilters filters, string? cursor,
        int pageSize, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return SurveyFeedbackService.AuthenticationRequired<FeedbackInboxPage>();
        }

        if (pageSize is < 1 or > 100 || filters.Visibility is not (null or "visible" or "hidden") ||
            filters.ReportStatus is not (null or "none" or "open" or "resolved"))
        {
            return SurveyFeedbackService.Invalid<FeedbackInboxPage>();
        }

        Guid[] candidates = filters.PresentationId is { } id
            ? [id]
            : (await repository.GetCandidateScopesAsync(actor.TenantId, cancellationToken))
                .Select(scope => scope.Id).Distinct().ToArray();
        var visible = new List<Guid>();
        var moderation = new List<Guid>();
        foreach (var candidate in candidates)
        {
            var access = await authorization.ReadAccessAsync(actor, candidate, cancellationToken);
            if (access is not { CanRead: true } || !(access.Reviewer || access.Operator))
            {
                continue;
            }

            visible.Add(candidate);
            if (await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.moderate", cancellationToken))
            {
                moderation.Add(candidate);
            }
        }

        if (filters.PresentationId.HasValue && visible.Count == 0)
        {
            return SurveyFeedbackService.NotFound<FeedbackInboxPage>();
        }
        if (filters.PersonRef.HasValue ||
            (filters.HasReport.HasValue || filters.ReportStatus is not null || filters.Visibility == "hidden") &&
            moderation.Count == 0)
        {
            return Result<FeedbackInboxPage>.Failure(new ApplicationErrorDetail(
                "RestrictedFieldDenied", "The requested filter is outside the permitted feedback projection."));
        }

        // Bind a page to its exact query and current scoped authority to stop older pages after a scope change.
        var authority = string.Join(',', visible.Order()) + "/" + string.Join(',', moderation.Order());
        var query = System.Text.Json.JsonSerializer.Serialize(filters) + "/" + authority;
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
        var scope = new FeedbackCursorScope(actor.TenantId, actor.UserId, filters.PresentationId, stamp);
        FeedbackPagePosition? after = null;
        if (cursor is not null && !cursors.TryRead(cursor, scope, out after))
        {
            return SurveyFeedbackService.Invalid<FeedbackInboxPage>();
        }

        var result = await inbox.ReadAsync(actor.TenantId, actor.UserId, visible.ToArray(), moderation.ToArray(),
            filters, after, pageSize + 1, cancellationToken);
        var next = result.Items.Count > pageSize
            ? cursors.Write(scope, new FeedbackPagePosition(result.Items[pageSize - 1].Submission.SubmittedAt,
                result.Items[pageSize - 1].Submission.Id))
            : null;
        return Result<FeedbackInboxPage>.Success(new FeedbackInboxPage(result.Items.Take(pageSize)
            .Select(row => new FeedbackInboxRow(SurveyFeedbackService.ToRow(row.Submission, row.CanModerate),
                row.ReaderState, row.ReportStatus)).ToArray(), result.UnreadCount, next,
            result.UnresolvedReportCount));
    }
}
