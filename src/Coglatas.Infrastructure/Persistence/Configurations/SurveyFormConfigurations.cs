using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable("form_definitions", table =>
            table.HasCheckConstraint("CK_form_definitions_scope", "num_nonnulls(\"WorkspaceId\", \"GroupId\", \"ProjectId\") = 1"));
        builder.ConfigureSoftDeletableEntity();
        builder.Property(form => form.Title).HasMaxLength(240).IsRequired();
        builder.Property(form => form.Description).HasMaxLength(4000);
        builder.Property(form => form.Purpose).HasEnumStringConversion().IsRequired();
        builder.Property(form => form.Status).HasEnumStringConversion().IsRequired();
        builder.Property(form => form.VersionNo).IsConcurrencyToken();
        builder.HasAlternateKey(form => new { form.Id, form.TenantId });

        builder.HasOne<Workspace>().WithMany().HasForeignKey(form => new { form.WorkspaceId, form.TenantId })
            .HasPrincipalKey(workspace => new { workspace.Id, workspace.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Group>().WithMany().HasForeignKey(form => new { form.GroupId, form.TenantId })
            .HasPrincipalKey(group => new { group.Id, group.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(form => new { form.ProjectId, form.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(form => form.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormVersionConfiguration : IEntityTypeConfiguration<FormVersion>
{
    public void Configure(EntityTypeBuilder<FormVersion> builder)
    {
        builder.ToTable("form_versions", table =>
            table.HasCheckConstraint("CK_form_versions_number", "\"Number\" > 0"));
        builder.ConfigureEntity();
        builder.Property(version => version.SnapshotJson).IsRequired();
        builder.HasAlternateKey(version => new { version.Id, version.TenantId });
        builder.HasAlternateKey(version => new { version.Id, version.TenantId, version.FormDefinitionId });
        builder.HasIndex(version => new { version.TenantId, version.FormDefinitionId, version.Number }).IsUnique();
        builder.HasOne<FormDefinition>().WithMany().HasForeignKey(version => new { version.FormDefinitionId, version.TenantId })
            .HasPrincipalKey(form => new { form.Id, form.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TargetQuestionSetVersionConfiguration : IEntityTypeConfiguration<TargetQuestionSetVersion>
{
    public void Configure(EntityTypeBuilder<TargetQuestionSetVersion> builder)
    {
        builder.ToTable("target_question_set_versions", table =>
            table.HasCheckConstraint("CK_target_question_set_versions_number", "\"Number\" > 0"));
        builder.ConfigureEntity();
        builder.Property(version => version.SnapshotJson).IsRequired();
        builder.HasAlternateKey(version => new { version.Id, version.TenantId });
        builder.HasAlternateKey(version => new { version.Id, version.TenantId, version.SurveyTargetId });
        builder.HasIndex(version => new { version.TenantId, version.SurveyTargetId, version.Number }).IsUnique();
        builder.HasOne<SurveyTarget>().WithMany().HasForeignKey(version => new { version.SurveyTargetId, version.TenantId })
            .HasPrincipalKey(target => new { target.Id, target.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FormFieldDefinitionConfiguration : IEntityTypeConfiguration<FormFieldDefinition>
{
    public void Configure(EntityTypeBuilder<FormFieldDefinition> builder)
    {
        builder.ToTable("form_field_definitions", table =>
        {
            table.HasCheckConstraint("CK_form_field_definitions_version", "num_nonnulls(\"FormVersionId\", \"TargetQuestionSetVersionId\") = 1");
            table.HasCheckConstraint("CK_form_field_definitions_order", "\"SortOrder\" >= 0");
        });
        builder.ConfigureEntity();
        builder.Property(field => field.FieldKey).HasMaxLength(128).IsRequired();
        builder.Property(field => field.Label).HasMaxLength(1000).IsRequired();
        builder.Property(field => field.FieldType).HasEnumStringConversion().IsRequired();
        builder.Property(field => field.OptionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(field => field.ConstraintsJson).HasColumnType("jsonb");
        builder.HasAlternateKey(field => new { field.Id, field.TenantId });
        builder.HasIndex(field => new { field.TenantId, field.FormVersionId, field.SortOrder }).IsUnique()
            .HasFilter("\"FormVersionId\" IS NOT NULL");
        builder.HasIndex(field => new { field.TenantId, field.TargetQuestionSetVersionId, field.SortOrder }).IsUnique()
            .HasFilter("\"TargetQuestionSetVersionId\" IS NOT NULL");
        builder.HasIndex(field => new { field.TenantId, field.FormVersionId, field.FieldKey }).IsUnique()
            .HasFilter("\"FormVersionId\" IS NOT NULL");
        builder.HasIndex(field => new { field.TenantId, field.TargetQuestionSetVersionId, field.FieldKey }).IsUnique()
            .HasFilter("\"TargetQuestionSetVersionId\" IS NOT NULL");
        builder.HasOne<FormVersion>().WithMany(version => version.Fields).HasForeignKey(field => new { field.FormVersionId, field.TenantId })
            .HasPrincipalKey(version => new { version.Id, version.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TargetQuestionSetVersion>().WithMany(version => version.Fields).HasForeignKey(field => new { field.TargetQuestionSetVersionId, field.TenantId })
            .HasPrincipalKey(version => new { version.Id, version.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
