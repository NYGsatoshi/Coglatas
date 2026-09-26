using Coglatas.Domain.Enums;

namespace Coglatas.Application.Forms;

public sealed record FormListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null,
    FormStatus? Status = null,
    FormType? FormType = null);

public sealed record FormScopeSummaryResponse(
    string ScopeType,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    string Label);

public sealed record FormListItemResponse(
    Guid Id,
    string Title,
    string? Description,
    FormType FormType,
    FormStatus Status,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    bool IsAnonymous,
    FormScopeSummaryResponse RelatedScope,
    DateTimeOffset CreatedAt);

public sealed record FormDetailResponse(
    Guid Id,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    FormType FormType,
    FormStatus Status,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    bool IsAnonymous,
    IReadOnlyList<FormQuestionResponse> Questions,
    FormScopeSummaryResponse RelatedScope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateFormRequest(
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    string Title,
    string? Description,
    FormType FormType = FormType.Other,
    FormStatus Status = FormStatus.Draft,
    DateTimeOffset? OpensAt = null,
    DateTimeOffset? ClosesAt = null,
    bool IsAnonymous = false);

public sealed record UpdateFormRequest(
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null,
    string? Title = null,
    string? Description = null,
    FormType? FormType = null,
    FormStatus? Status = null,
    DateTimeOffset? OpensAt = null,
    DateTimeOffset? ClosesAt = null,
    bool? IsAnonymous = null);

public sealed record FormQuestionResponse(
    Guid Id,
    string QuestionText,
    FormQuestionType QuestionType,
    bool IsRequired,
    int SortOrder,
    IReadOnlyList<string>? Options);

public sealed record CreateFormQuestionRequest(
    string QuestionText,
    FormQuestionType QuestionType,
    bool IsRequired = false,
    int SortOrder = 0,
    IReadOnlyList<string>? Options = null);

public sealed record UpdateFormQuestionRequest(
    string? QuestionText = null,
    FormQuestionType? QuestionType = null,
    bool? IsRequired = null,
    int? SortOrder = null,
    IReadOnlyList<string>? Options = null);

public sealed record SubmitFormResponseRequest(IReadOnlyList<SubmitFormAnswerRequest> Answers);

public sealed record SubmitFormAnswerRequest(
    Guid FormQuestionId,
    string? AnswerText = null,
    string? AnswerJson = null);

public sealed record FormResponseDetailResponse(
    Guid Id,
    Guid FormId,
    Guid? RespondentUserId,
    string? RespondentDisplayName,
    string? RespondentEmail,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<FormAnswerResponse> Answers);

public sealed record FormAnswerResponse(
    Guid FormQuestionId,
    string QuestionText,
    FormQuestionType QuestionType,
    string? AnswerText,
    string? AnswerJson);

public sealed record FormSummaryResponse(
    Guid FormId,
    string Title,
    int TotalResponses,
    IReadOnlyList<FormQuestionSummaryResponse> QuestionSummaries);

public sealed record FormQuestionSummaryResponse(
    Guid FormQuestionId,
    string QuestionText,
    FormQuestionType QuestionType,
    int AnsweredCount,
    IReadOnlyDictionary<string, int>? Counts,
    IReadOnlyList<string>? SampleTextAnswers);

public sealed record UnansweredUserResponse(
    Guid UserId,
    string? DisplayName,
    string? ScopeRole);
