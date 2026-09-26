using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class FormRepository(AppDbContext dbContext) : IFormRepository
{
    public async Task<IReadOnlyList<InternalForm>> ListAsync(FormListQuery query, CancellationToken cancellationToken = default)
    {
        return await ApplyFilters(BaseFormQuery(includeArchived: query.Status == FormStatus.Archived), query)
            .OrderByDescending(form => form.CreatedAt)
            .ThenBy(form => form.Title)
            .ToListAsync(cancellationToken);
    }

    public Task<InternalForm?> GetAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        return dbContext.InternalForms
            .Include(form => form.Workspace)
            .Include(form => form.Group)
            .Include(form => form.Project)
            .Include(form => form.CreatedByUser)
            .Include(form => form.Questions)
            .FirstOrDefaultAsync(form => form.Id == formId, cancellationToken);
    }

    public async Task AddAsync(InternalForm form, CancellationToken cancellationToken = default)
    {
        await dbContext.InternalForms.AddAsync(form, cancellationToken);
    }

    public async Task AddQuestionAsync(FormQuestion question, CancellationToken cancellationToken = default)
    {
        await dbContext.FormQuestions.AddAsync(question, cancellationToken);
    }

    public Task<FormQuestion?> GetQuestionAsync(Guid formId, Guid questionId, CancellationToken cancellationToken = default)
    {
        return dbContext.FormQuestions
            .FirstOrDefaultAsync(question => question.FormId == formId && question.Id == questionId, cancellationToken);
    }

    public void RemoveQuestion(FormQuestion question)
    {
        dbContext.FormQuestions.Remove(question);
    }

    public Task<bool> HasResponsesAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        return dbContext.FormResponses.AnyAsync(response => response.FormId == formId, cancellationToken);
    }

    public Task<FormResponse?> GetResponseForUserAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.FormResponses
            .Include(response => response.RespondentUser)
            .Include(response => response.Answers)
                .ThenInclude(answer => answer.FormQuestion)
            .FirstOrDefaultAsync(response => response.FormId == formId && response.RespondentUserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<FormResponse>> ListResponsesAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        return await dbContext.FormResponses
            .Include(response => response.RespondentUser)
            .Include(response => response.Answers)
                .ThenInclude(answer => answer.FormQuestion)
            .Where(response => response.FormId == formId)
            .OrderByDescending(response => response.SubmittedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddResponseAsync(FormResponse response, CancellationToken cancellationToken = default)
    {
        await dbContext.FormResponses.AddAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListScopeRecipientUserIdsAsync(InternalForm form, CancellationToken cancellationToken = default)
    {
        if (form.WorkspaceId.HasValue)
        {
            return await dbContext.WorkspaceMembers
                .Where(member => member.WorkspaceId == form.WorkspaceId.Value && member.Status == MembershipStatus.Active)
                .Select(member => member.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (form.GroupId.HasValue)
        {
            return await dbContext.GroupMembers
                .Where(member => member.GroupId == form.GroupId.Value)
                .Select(member => member.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (form.ProjectId.HasValue)
        {
            return await dbContext.ProjectMembers
                .Where(member => member.ProjectId == form.ProjectId.Value)
                .Select(member => member.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return [];
    }

    public async Task<IReadOnlyList<FormScopeMember>> ListScopeMembersAsync(InternalForm form, CancellationToken cancellationToken = default)
    {
        if (form.WorkspaceId.HasValue)
        {
            return await dbContext.WorkspaceMembers
                .AsNoTracking()
                .Include(member => member.User)
                .Where(member => member.WorkspaceId == form.WorkspaceId.Value && member.Status == MembershipStatus.Active && member.User != null && member.User.Status == UserStatus.Active)
                .Select(member => new FormScopeMember(member.UserId, member.User!.DisplayName, member.Role.ToString()))
                .ToListAsync(cancellationToken);
        }

        if (form.GroupId.HasValue)
        {
            return await dbContext.GroupMembers
                .AsNoTracking()
                .Include(member => member.User)
                .Where(member => member.GroupId == form.GroupId.Value && member.User != null && member.User.Status == UserStatus.Active)
                .Select(member => new FormScopeMember(member.UserId, member.User!.DisplayName, member.Role.ToString()))
                .ToListAsync(cancellationToken);
        }

        if (form.ProjectId.HasValue)
        {
            return await dbContext.ProjectMembers
                .AsNoTracking()
                .Include(member => member.User)
                .Where(member => member.ProjectId == form.ProjectId.Value && member.User != null && member.User.Status == UserStatus.Active)
                .Select(member => new FormScopeMember(member.UserId, member.User!.DisplayName, member.Role.ToString()))
                .ToListAsync(cancellationToken);
        }

        return [];
    }

    private IQueryable<InternalForm> BaseFormQuery(bool includeArchived)
    {
        var source = dbContext.InternalForms
            .AsNoTracking()
            .Include(form => form.Workspace)
            .Include(form => form.Group)
            .Include(form => form.Project)
            .Include(form => form.CreatedByUser)
            .AsQueryable();

        return includeArchived
            ? source
            : source.Where(form => form.DeletedAt == null && form.Status != FormStatus.Archived);
    }

    private static IQueryable<InternalForm> ApplyFilters(IQueryable<InternalForm> source, FormListQuery query)
    {
        if (query.WorkspaceId.HasValue)
        {
            source = source.Where(form =>
                form.WorkspaceId == query.WorkspaceId.Value ||
                (form.Project != null && form.Project.WorkspaceId == query.WorkspaceId.Value));
        }

        if (query.GroupId.HasValue)
        {
            source = source.Where(form =>
                form.GroupId == query.GroupId.Value ||
                (form.Project != null && form.Project.GroupId == query.GroupId.Value));
        }

        if (query.ProjectId.HasValue)
        {
            source = source.Where(form => form.ProjectId == query.ProjectId.Value);
        }

        if (query.Status.HasValue)
        {
            source = source.Where(form => form.Status == query.Status.Value);
        }

        if (query.FormType.HasValue)
        {
            source = source.Where(form => form.FormType == query.FormType.Value);
        }

        return source;
    }
}
