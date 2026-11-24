using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coglatas.Application.SurveyFeedback;

public sealed record PresentationFeedbackScope(
    Guid TenantId,
    Guid Id,
    Guid EventId,
    Guid ProjectId,
    Guid WorkspaceId,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string? Venue,
    bool ResultsStopped);

public sealed record FeedbackEventScope(Guid Id, Guid ProjectId, Guid WorkspaceId);

public sealed record PresentationFeedbackSummary(Guid Id, Guid EventId, string Title,
    DateTimeOffset StartAt, DateTimeOffset EndAt, string? Venue);

public sealed record FeedbackActionCapabilities(bool Report, bool Invite, bool PrivateNote, bool Share,
    bool ExportAggregate, bool ExportDetail);

public sealed record PresentationWorkspace(PresentationFeedbackSummary Presentation, IReadOnlyList<string> Relations,
    FeedbackActionCapabilities Capabilities);

public sealed record FeedbackPage<T>(IReadOnlyList<T> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null);

public sealed record FeedbackPagePosition(DateTimeOffset SubmittedAt, Guid Id);

public sealed record FeedbackCursorScope(Guid TenantId, Guid UserId, Guid? PresentationId, string Query);

public interface ISurveyFeedbackCursor
{
    string Write(FeedbackCursorScope scope, FeedbackPagePosition position);
    bool TryRead(string cursor, FeedbackCursorScope scope, out FeedbackPagePosition? position);
}

public sealed record FeedbackValueSnapshot(
    Guid Id,
    string FieldKey,
    string FieldType,
    string CompatibilityStamp,
    string ValueJson,
    bool IsHidden,
    long VisibilityVersionNo = 1);

public sealed record SurveyFeedbackSnapshot(
    Guid Id,
    Guid PresentationId,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<FeedbackValueSnapshot> Answers);

public sealed record FeedbackAnswer(
    Guid Id,
    string FieldKey,
    string Visibility,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? VisibilityVersionNo);

public sealed record FeedbackRow(Guid Id, Guid PresentationId, DateTimeOffset SubmittedAt,
    IReadOnlyList<FeedbackAnswer> Answers);

public sealed record FeedbackOptionCount(string OptionKey, long Responses);

public sealed record FeedbackQuestionSummary(string FieldKey, long Denominator,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FeedbackOptionCount>? Distribution,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Mean);

public sealed record FeedbackSummary(long Responses, IReadOnlyList<FeedbackQuestionSummary> Questions);

public sealed record FeedbackSummaryInput(long Count, IReadOnlyList<FeedbackValueSnapshot> Values);

public sealed record FeedbackComparison(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Comparison = null);

public interface ISurveyFeedbackRepository
{
    Task<PresentationFeedbackScope?> GetScopeAsync(Guid tenantId, Guid presentationId, CancellationToken cancellationToken);
    Task<bool> IsInvitationEnabledAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
    Task<FeedbackEventScope?> GetEventScopeAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PresentationFeedbackScope>> GetCandidateScopesAsync(Guid tenantId, Guid userId,
        CancellationToken cancellationToken);
    Task<(bool Presenter, bool Reviewer)> GetAssignmentsAsync(Guid tenantId, Guid presentationId, Guid userId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetActiveGrantIssuersAsync(Guid tenantId, Guid presentationId, Guid userId,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<SurveyFeedbackSnapshot>> ReadAsync(Guid tenantId, Guid presentationId, string? fieldKey,
        string visibility, FeedbackPagePosition? after, int take, CancellationToken cancellationToken);
    Task<FeedbackSummaryInput> ReadSummaryAsync(Guid tenantId, Guid presentationId, string? fieldKey,
        CancellationToken cancellationToken);
}
