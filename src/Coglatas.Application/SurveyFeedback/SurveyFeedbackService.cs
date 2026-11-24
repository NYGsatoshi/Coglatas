using System.Text.Json;
using Coglatas.Application.Common;

namespace Coglatas.Application.SurveyFeedback;

public sealed class SurveyFeedbackService(
    SurveyFeedbackAuthorization authorization,
    ISurveyFeedbackRepository repository,
    ISurveyFeedbackCursor cursors)
{
    public async Task<Result<FeedbackPage<PresentationWorkspace>>> WorkspacesAsync(
        string? cursor, int pageSize, string? relation, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return AuthenticationRequired<FeedbackPage<PresentationWorkspace>>();
        }

        if (pageSize is < 1 or > 100 || relation is not (null or "own" or "assigned" or "granted" or "managed"))
        {
            return Invalid<FeedbackPage<PresentationWorkspace>>();
        }

        var cursorScope = new FeedbackCursorScope(actor.TenantId, actor.UserId, null, $"workspace/{relation}");
        if (!TryReadPosition(cursor, cursorScope, out var after))
        {
            return Invalid<FeedbackPage<PresentationWorkspace>>();
        }

        var candidates = await repository.GetCandidateScopesAsync(actor.TenantId, actor.UserId, cancellationToken);
        var result = new List<PresentationWorkspace>();
        var accepted = candidates.OrderBy(scope => scope.Id)
            .Where(scope => after is null || scope.Id.CompareTo(after.Id) > 0);
        foreach (var candidate in accepted)
        {
            var access = await authorization.ReadAccessAsync(actor, candidate.Id, cancellationToken);
            if (access is not { CanRead: true } || relation is not null && !access.Relations.Contains(relation))
            {
                continue;
            }

            var capabilities = await authorization.ReadActionCapabilitiesAsync(actor, access, cancellationToken);
            result.Add(new PresentationWorkspace(ToSummary(access.Scope), access.Relations, capabilities));
            if (result.Count > pageSize)
            {
                break;
            }
        }

        return Result<FeedbackPage<PresentationWorkspace>>.Success(Page(result, pageSize, cursorScope,
            row => new FeedbackPagePosition(DateTimeOffset.UnixEpoch, row.Presentation.Id)));
    }

    public async Task<Result<FeedbackSummary>> SummaryAsync(
        Guid presentationId, string? fieldKey, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return AuthenticationRequired<FeedbackSummary>();
        }

        var access = await authorization.ReadAccessAsync(actor, presentationId, cancellationToken);
        if (access is not { CanRead: true })
        {
            return NotFound<FeedbackSummary>();
        }

        var input = await repository.ReadSummaryAsync(actor.TenantId, presentationId, fieldKey, cancellationToken);
        return Result<FeedbackSummary>.Success(SurveyFeedbackAggregation.Summarize(input));
    }

    public async Task<Result<FeedbackPage<FeedbackRow>>> FeedbackAsync(Guid presentationId, string? cursor,
        int pageSize, string? fieldKey, string? visibility, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return AuthenticationRequired<FeedbackPage<FeedbackRow>>();
        }

        var access = await authorization.ReadAccessAsync(actor, presentationId, cancellationToken);
        if (access is not { CanRead: true })
        {
            return NotFound<FeedbackPage<FeedbackRow>>();
        }

        var canModerate = (access.Reviewer || access.Operator) &&
            await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.moderate", cancellationToken);
        if (visibility is not (null or "visible" or "hidden"))
        {
            return Invalid<FeedbackPage<FeedbackRow>>();
        }
        if (visibility == "hidden" && !canModerate)
        {
            return NotFound<FeedbackPage<FeedbackRow>>();
        }

        var projection = visibility ?? (canModerate ? "all" : "visible");
        var scope = new FeedbackCursorScope(actor.TenantId, actor.UserId, presentationId,
            $"feedback/{fieldKey}/{projection}/{canModerate}/{string.Join(',', access.Relations)}");
        if (pageSize is < 1 or > 100 || !TryReadPosition(cursor, scope, out var after))
        {
            return Invalid<FeedbackPage<FeedbackRow>>();
        }

        var rows = await repository.ReadAsync(actor.TenantId, presentationId, fieldKey, projection, after,
            pageSize + 1, cancellationToken);
        return Result<FeedbackPage<FeedbackRow>>.Success(Page(rows.Select(row => ToRow(row, canModerate)).ToList(), pageSize, scope,
            row => new FeedbackPagePosition(row.SubmittedAt, row.Id)));
    }

    private bool TryReadPosition(string? cursor, FeedbackCursorScope scope, out FeedbackPagePosition? position)
    {
        position = null;
        return cursor is null || cursors.TryRead(cursor, scope, out position);
    }

    private FeedbackPage<T> Page<T>(List<T> rows, int size, FeedbackCursorScope scope,
        Func<T, FeedbackPagePosition> position)
    {
        if (rows.Count <= size)
        {
            return new FeedbackPage<T>(rows);
        }

        rows.RemoveAt(rows.Count - 1);
        return new FeedbackPage<T>(rows, cursors.Write(scope, position(rows[^1])));
    }

    private static PresentationFeedbackSummary ToSummary(PresentationFeedbackScope scope) =>
        new(scope.Id, scope.EventId, scope.Title, scope.StartsAtUtc, scope.EndsAtUtc, scope.Venue);

    internal static FeedbackRow ToRow(SurveyFeedbackSnapshot source, bool canModerate) =>
        new(source.Id, source.PresentationId, source.SubmittedAt, source.Answers.Select(answer =>
            new FeedbackAnswer(answer.Id, answer.FieldKey, answer.IsHidden ? "hidden" : "visible",
                answer.IsHidden ? null : JsonSerializer.Deserialize<JsonElement>(answer.ValueJson),
                canModerate ? answer.VisibilityVersionNo : null, answer.Label)).ToArray());

    internal static Result<T> AuthenticationRequired<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail("AuthenticationRequired", "Authentication is required."));

    internal static Result<T> NotFound<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail("NotFound", "The requested resource was not found."));

    internal static Result<T> Invalid<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail("ValidationFailed", "The request parameters are invalid."));
}
