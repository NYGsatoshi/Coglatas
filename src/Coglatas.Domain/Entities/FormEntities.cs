using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class InternalForm : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FormType FormType { get; set; } = FormType.Other;
    public FormStatus Status { get; set; } = FormStatus.Draft;
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
    public bool IsAnonymous { get; set; }

    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public Project? Project { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<FormQuestion> Questions { get; } = new List<FormQuestion>();
    public ICollection<FormResponse> Responses { get; } = new List<FormResponse>();
}

public sealed class FormQuestion : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FormId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public FormQuestionType QuestionType { get; set; } = FormQuestionType.ShortText;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? OptionsJson { get; set; }

    public InternalForm? Form { get; set; }
    public ICollection<FormAnswer> Answers { get; } = new List<FormAnswer>();
}

public sealed class FormResponse : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FormId { get; set; }
    public Guid? RespondentUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }

    public InternalForm? Form { get; set; }
    public User? RespondentUser { get; set; }
    public ICollection<FormAnswer> Answers { get; } = new List<FormAnswer>();
}

public sealed class FormAnswer : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FormResponseId { get; set; }
    public Guid FormQuestionId { get; set; }
    public string? AnswerText { get; set; }
    public string? AnswerJson { get; set; }

    public FormResponse? FormResponse { get; set; }
    public FormQuestion? FormQuestion { get; set; }
}
