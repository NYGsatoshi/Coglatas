using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IFormRepository
{
    Task<IReadOnlyList<InternalForm>> ListAsync(FormListQuery query, CancellationToken cancellationToken = default);
    Task<InternalForm?> GetAsync(Guid formId, CancellationToken cancellationToken = default);
    Task AddAsync(InternalForm form, CancellationToken cancellationToken = default);
    Task AddQuestionAsync(FormQuestion question, CancellationToken cancellationToken = default);
    Task<FormQuestion?> GetQuestionAsync(Guid formId, Guid questionId, CancellationToken cancellationToken = default);
    void RemoveQuestion(FormQuestion question);
    Task<bool> HasResponsesAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<FormResponse?> GetResponseForUserAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FormResponse>> ListResponsesAsync(Guid formId, CancellationToken cancellationToken = default);
    Task AddResponseAsync(FormResponse response, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListScopeRecipientUserIdsAsync(InternalForm form, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FormScopeMember>> ListScopeMembersAsync(InternalForm form, CancellationToken cancellationToken = default);
}

public sealed record FormScopeMember(Guid UserId, string? DisplayName, string? ScopeRole);
