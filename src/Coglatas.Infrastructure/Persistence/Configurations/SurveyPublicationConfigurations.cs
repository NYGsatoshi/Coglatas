using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class SurveyCampaignConfiguration : IEntityTypeConfiguration<SurveyCampaign>
{
    public void Configure(EntityTypeBuilder<SurveyCampaign> builder)
    {
        builder.ToTable("survey_campaigns");
        builder.ConfigureEntity();
        builder.Property(campaign => campaign.VersionNo).IsConcurrencyToken();
        builder.HasAlternateKey(campaign => new { campaign.Id, campaign.TenantId, campaign.EventId, campaign.FormDefinitionId });
        builder.HasOne<ActivityEvent>().WithMany().HasForeignKey(campaign => new { campaign.EventId, campaign.TenantId })
            .HasPrincipalKey(activityEvent => new { activityEvent.Id, activityEvent.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FormDefinition>().WithMany().HasForeignKey(campaign => new { campaign.FormDefinitionId, campaign.TenantId })
            .HasPrincipalKey(form => new { form.Id, form.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyTargetConfiguration : IEntityTypeConfiguration<SurveyTarget>
{
    public void Configure(EntityTypeBuilder<SurveyTarget> builder)
    {
        builder.ToTable("survey_targets", table =>
            table.HasCheckConstraint("CK_survey_targets_window", "\"StartsAtUtc\" IS NULL OR \"EndsAtUtc\" IS NULL OR \"StartsAtUtc\" < \"EndsAtUtc\""));
        builder.ConfigureEntity();
        builder.Property(target => target.VersionNo).IsConcurrencyToken();
        builder.Property(target => target.LocalMarkerKey).HasMaxLength(128).IsRequired();
        builder.HasAlternateKey(target => new { target.Id, target.TenantId });
        builder.HasAlternateKey(target => new { target.Id, target.TenantId, target.FormDefinitionId });
        builder.HasAlternateKey(target => new { target.Id, target.TenantId, target.EventId, target.PresentationId });
        builder.HasIndex(target => new { target.TenantId, target.CampaignId, target.PresentationId }).IsUnique();
        builder.HasIndex(target => target.LocalMarkerKey).IsUnique();

        builder.HasOne<SurveyCampaign>().WithMany()
            .HasForeignKey(target => new { target.CampaignId, target.TenantId, target.EventId, target.FormDefinitionId })
            .HasPrincipalKey(campaign => new { campaign.Id, campaign.TenantId, campaign.EventId, campaign.FormDefinitionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Presentation>().WithMany()
            .HasForeignKey(target => new { target.PresentationId, target.TenantId, target.EventId })
            .HasPrincipalKey(presentation => new { presentation.Id, presentation.TenantId, presentation.EventId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedSurveySchema>().WithMany()
            .HasForeignKey(target => new { target.PublishedSchemaId, target.TenantId, target.Id })
            .HasPrincipalKey(schema => new { schema.Id, schema.TenantId, schema.SurveyTargetId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyProgramEntryConfiguration : IEntityTypeConfiguration<SurveyProgramEntry>
{
    public void Configure(EntityTypeBuilder<SurveyProgramEntry> builder)
    {
        builder.ToTable("survey_program_entries");
        builder.ConfigureEntity();
        builder.HasIndex(entry => new { entry.TenantId, entry.EventId, entry.PresentationId }).IsUnique();
        builder.HasOne<SurveyTarget>().WithMany()
            .HasForeignKey(entry => new { entry.SurveyTargetId, entry.TenantId, entry.EventId, entry.PresentationId })
            .HasPrincipalKey(target => new { target.Id, target.TenantId, target.EventId, target.PresentationId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PublishedSurveySchemaConfiguration : IEntityTypeConfiguration<PublishedSurveySchema>
{
    public void Configure(EntityTypeBuilder<PublishedSurveySchema> builder)
    {
        builder.ToTable("published_survey_schemas", table =>
            table.HasCheckConstraint("CK_published_survey_schemas_digest", "\"SchemaDigest\" ~ '^[0-9a-f]{64}$'"));
        builder.ConfigureEntity();
        builder.Property(schema => schema.VersionRef).HasMaxLength(128).IsRequired();
        builder.Property(schema => schema.SchemaDigest).HasMaxLength(64).IsRequired();
        builder.HasAlternateKey(schema => new { schema.Id, schema.TenantId });
        builder.HasAlternateKey(schema => new { schema.Id, schema.TenantId, schema.SurveyTargetId });
        builder.HasAlternateKey(schema => new { schema.Id, schema.TenantId, schema.SurveyTargetId, schema.FormDefinitionId, schema.FormVersionId });
        builder.HasIndex(schema => new { schema.SurveyTargetId, schema.TenantId, schema.FormVersionId, schema.TargetQuestionSetVersionId }).IsUnique()
            .HasDatabaseName("IX_published_survey_schema_version_pair");
        builder.HasIndex(schema => new { schema.SurveyTargetId, schema.TenantId, schema.VersionRef }).IsUnique();

        builder.HasOne<SurveyTarget>().WithMany()
            .HasForeignKey(schema => new { schema.SurveyTargetId, schema.TenantId, schema.FormDefinitionId })
            .HasPrincipalKey(target => new { target.Id, target.TenantId, target.FormDefinitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FormVersion>().WithMany()
            .HasForeignKey(schema => new { schema.FormVersionId, schema.TenantId, schema.FormDefinitionId })
            .HasPrincipalKey(version => new { version.Id, version.TenantId, version.FormDefinitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TargetQuestionSetVersion>().WithMany()
            .HasForeignKey(schema => new { schema.TargetQuestionSetVersionId, schema.TenantId, schema.SurveyTargetId })
            .HasPrincipalKey(version => new { version.Id, version.TenantId, version.SurveyTargetId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PublishedSurveySchemaFieldConfiguration : IEntityTypeConfiguration<PublishedSurveySchemaField>
{
    public void Configure(EntityTypeBuilder<PublishedSurveySchemaField> builder)
    {
        builder.ToTable("published_survey_schema_fields", table =>
            table.HasCheckConstraint("CK_published_survey_schema_fields_order", "\"SortOrder\" >= 0"));
        builder.ConfigureEntity();
        builder.HasAlternateKey(field => new { field.PublishedSurveySchemaId, field.FormFieldDefinitionId, field.TenantId })
            .HasName("AK_published_survey_schema_field_identity");
        builder.HasIndex(field => new { field.PublishedSurveySchemaId, field.TenantId, field.SortOrder }).IsUnique();
        builder.HasOne<PublishedSurveySchema>().WithMany(schema => schema.Fields)
            .HasForeignKey(field => new { field.PublishedSurveySchemaId, field.TenantId })
            .HasPrincipalKey(schema => new { schema.Id, schema.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(field => field.FormFieldDefinition).WithMany()
            .HasForeignKey(field => new { field.FormFieldDefinitionId, field.TenantId })
            .HasPrincipalKey(definition => new { definition.Id, definition.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
