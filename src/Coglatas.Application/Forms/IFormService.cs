using Coglatas.Application.Common;

namespace Coglatas.Application.Forms;

public interface IFormService
{
    Task<Result<PagedResponse<FormListItemResponse>>> ListAsync(FormListQuery query, CancellationToken cancellationToken = default);
    Task<Result<FormDetailResponse>> CreateAsync(CreateFormRequest request, CancellationToken cancellationToken = default);
    Task<Result<FormDetailResponse>> GetAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<FormDetailResponse>> UpdateAsync(Guid formId, UpdateFormRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<FormDetailResponse>> OpenAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<FormDetailResponse>> CloseAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<FormQuestionResponse>>> ListQuestionsAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<FormQuestionResponse>> AddQuestionAsync(Guid formId, CreateFormQuestionRequest request, CancellationToken cancellationToken = default);
    Task<Result<FormQuestionResponse>> UpdateQuestionAsync(Guid formId, Guid questionId, UpdateFormQuestionRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteQuestionAsync(Guid formId, Guid questionId, CancellationToken cancellationToken = default);
    Task<Result<FormResponseDetailResponse>> SubmitResponseAsync(Guid formId, SubmitFormResponseRequest request, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<FormResponseDetailResponse>>> ListResponsesAsync(Guid formId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<Result<FormResponseDetailResponse?>> GetMyResponseAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<FormSummaryResponse>> GetSummaryAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<UnansweredUserResponse>>> ListUnansweredUsersAsync(Guid formId, CancellationToken cancellationToken = default);
}
