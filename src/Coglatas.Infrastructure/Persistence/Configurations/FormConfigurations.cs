using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class InternalFormConfiguration : IEntityTypeConfiguration<InternalForm>
{
    public void Configure(EntityTypeBuilder<InternalForm> builder)
    {
        builder.ToTable("internal_forms");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(form => form.Title).HasMaxLength(240).IsRequired();
        builder.Property(form => form.Description).HasMaxLength(4000);
        builder.Property(form => form.FormType).HasEnumStringConversion().IsRequired();
        builder.Property(form => form.Status).HasEnumStringConversion().IsRequired();
        builder.Property(form => form.IsAnonymous).IsRequired();

        builder.HasIndex(form => form.WorkspaceId);
        builder.HasIndex(form => form.GroupId);
        builder.HasIndex(form => form.ProjectId);
        builder.HasIndex(form => form.CreatedByUserId);
        builder.HasIndex(form => form.FormType);
        builder.HasIndex(form => form.Status);
        builder.HasIndex(form => form.OpensAt);
        builder.HasIndex(form => form.ClosesAt);

        builder
            .HasOne(form => form.Workspace)
            .WithMany()
            .HasForeignKey(form => form.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(form => form.Group)
            .WithMany()
            .HasForeignKey(form => form.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(form => form.Project)
            .WithMany()
            .HasForeignKey(form => form.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(form => form.CreatedByUser)
            .WithMany()
            .HasForeignKey(form => form.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormQuestionConfiguration : IEntityTypeConfiguration<FormQuestion>
{
    public void Configure(EntityTypeBuilder<FormQuestion> builder)
    {
        builder.ToTable("form_questions");
        builder.ConfigureAuditableEntity();

        builder.Property(question => question.QuestionText).HasMaxLength(1000).IsRequired();
        builder.Property(question => question.QuestionType).HasEnumStringConversion().IsRequired();
        builder.Property(question => question.OptionsJson).HasColumnType("jsonb");

        builder.HasIndex(question => question.FormId);
        builder.HasIndex(question => new { question.FormId, question.SortOrder });

        builder
            .HasOne(question => question.Form)
            .WithMany(form => form.Questions)
            .HasForeignKey(question => question.FormId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormResponseConfiguration : IEntityTypeConfiguration<FormResponse>
{
    public void Configure(EntityTypeBuilder<FormResponse> builder)
    {
        builder.ToTable("form_responses");
        builder.ConfigureAuditableEntity();

        builder.Property(response => response.SubmittedAt).IsRequired();

        builder.HasIndex(response => response.FormId);
        builder.HasIndex(response => response.RespondentUserId);
        builder.HasIndex(response => new { response.TenantId, response.FormId, response.RespondentUserId }).IsUnique();
        builder.HasIndex(response => response.SubmittedAt);

        builder
            .HasOne(response => response.Form)
            .WithMany(form => form.Responses)
            .HasForeignKey(response => response.FormId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(response => response.RespondentUser)
            .WithMany()
            .HasForeignKey(response => response.RespondentUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class FormAnswerConfiguration : IEntityTypeConfiguration<FormAnswer>
{
    public void Configure(EntityTypeBuilder<FormAnswer> builder)
    {
        builder.ToTable("form_answers");
        builder.ConfigureAuditableEntity();

        builder.Property(answer => answer.AnswerText).HasMaxLength(4000);
        builder.Property(answer => answer.AnswerJson).HasColumnType("jsonb");

        builder.HasIndex(answer => answer.FormResponseId);
        builder.HasIndex(answer => answer.FormQuestionId);
        builder.HasIndex(answer => new { answer.TenantId, answer.FormResponseId, answer.FormQuestionId }).IsUnique();

        builder
            .HasOne(answer => answer.FormResponse)
            .WithMany(response => response.Answers)
            .HasForeignKey(answer => answer.FormResponseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(answer => answer.FormQuestion)
            .WithMany(question => question.Answers)
            .HasForeignKey(answer => answer.FormQuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
