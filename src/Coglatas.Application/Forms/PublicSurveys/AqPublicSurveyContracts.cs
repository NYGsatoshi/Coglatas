using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Forms.PublicSurveys;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AqPublicAnswerInput(
    [property: JsonRequired] string FieldKey,
    [property: JsonRequired] JsonElement Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AqPublicSubmissionInput(
    [property: JsonRequired] string VersionRef,
    [property: JsonRequired] string SchemaDigest,
    [property: JsonRequired] IReadOnlyList<AqPublicAnswerInput> Answers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    bool AccountOptIn);

public sealed record AqPublicProgram(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimeZone,
    IReadOnlyList<AqPublicProgramEntry> Presentations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

public sealed record AqPublicProgramEntry(
    string SurveyEntryRef,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? StartAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? EndAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Venue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? PresenterDisplayNames = null);

public sealed record AqPublicOption(string OptionKey, string Label);

public sealed record AqPublicField(
    string FieldKey,
    string Label,
    bool Required,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AqPublicOption>? Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Constraints);

public sealed record AqPublicForm(
    string Title,
    string VersionRef,
    string SchemaDigest,
    IReadOnlyList<AqPublicField> Fields,
    string NameMode,
    bool AccountOptInAvailable,
    string ReceptionState,
    string LocalMarkerKey,
    string Notice);

public sealed record AqPublicReceipt(string RequestId);

public sealed record AqPublicCommandResult(string? ErrorCode, AqPublicReceipt? Receipt = null)
{
    public static AqPublicCommandResult Reject(string code) => new(code);
    public static AqPublicCommandResult Accept(AqPublicReceipt receipt) => new(null, receipt);
}

public sealed record AqPublicSession(Guid UserId, Guid SessionId);

public sealed record AqPublicSurveyTarget(
    SurveyTarget Target,
    PublishedSurveySchema Schema,
    EventFeedbackSettings Settings,
    SurveyCampaign Campaign,
    FormDefinition Definition,
    IReadOnlyList<FormFieldDefinition> Fields,
    bool AccountEligible,
    bool ParentCanReceive = true);

public sealed record AqPublicWritePlan(
    string IntentDigest,
    string RequestId,
    string? RespondentName,
    Guid? AccountUserId,
    IReadOnlyDictionary<string, JsonElement> Answers,
    DateTimeOffset SubmittedAt);

public sealed record AqPublicWriteDecision(AqPublicCommandResult Result, AqPublicWritePlan? Plan = null);

public interface IAqPublicSurveyStore
{
    Task<AqPublicProgram?> ReadProgramAsync(string codeDigest, string? cursor, int pageSize, CancellationToken cancellationToken);

    Task<AqPublicSurveyTarget?> ReadTargetAsync(string codeDigest, AqPublicSession? session, CancellationToken cancellationToken);

    /// <summary>
    /// Resolve and revalidate the current code and parent before loading a receipt. Serialize competing key writers.
    /// Run the supplied decision against the revalidated snapshot and commit any accepted plan and receipt together.
    /// </summary>
    Task<AqPublicCommandResult> SubmitAsync(
        string codeDigest,
        string keyDigest,
        AqPublicSession? session,
        Func<AqPublicSurveyTarget, SubmissionIdempotency?, AqPublicWriteDecision> decide,
        CancellationToken cancellationToken);
}

public sealed class AqInvalidProgramCursorException() : Exception("The public program cursor is invalid.");

public sealed record AqProgramCursor(Guid EventId, Guid CodeId, DateTimeOffset StartsAtUtc, Guid PresentationId);

public interface IAqPublicProjectionCodec
{
    string ProtectEntry(string code);
    string? UnprotectEntry(string protectedCode);
    string ProtectCursor(AqProgramCursor cursor);
    AqProgramCursor? UnprotectCursor(string protectedCursor);
}
