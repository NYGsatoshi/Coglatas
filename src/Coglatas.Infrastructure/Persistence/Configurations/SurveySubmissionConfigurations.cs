using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable("form_submissions", table =>
        {
            table.HasCheckConstraint("CK_form_submissions_shape", """
                ("SurveyTargetId" IS NULL AND "PublishedSurveySchemaId" IS NULL) OR
                ("SurveyTargetId" IS NOT NULL AND "PublishedSurveySchemaId" IS NOT NULL AND "RespondentUserId" IS NULL)
                """);
            table.HasCheckConstraint("CK_form_submissions_lock", """jsonb_typeof("FieldLockSnapshotJson") = 'object'""");
        });
        builder.ConfigureEntity();
        builder.Property(submission => submission.Status).HasEnumStringConversion().IsRequired();
        builder.Property(submission => submission.VersionNo).IsConcurrencyToken();
        builder.Property(submission => submission.FieldLockSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property<bool>("LegacyMaskRespondent").HasDefaultValue(false);
        builder.HasAlternateKey(submission => new { submission.Id, submission.TenantId });
        builder.HasIndex(submission => new { submission.Id, submission.TenantId, submission.PublishedSurveySchemaId }).IsUnique()
            .HasDatabaseName("IX_form_submissions_schema_identity");
        builder.HasIndex(submission => new { submission.Id, submission.TenantId, submission.SurveyTargetId }).IsUnique()
            .HasDatabaseName("IX_form_submissions_target_identity");
        builder.HasIndex(submission => new { submission.TenantId, submission.FormDefinitionId, submission.RespondentUserId }).IsUnique()
            .HasFilter(""" "SurveyTargetId" IS NULL AND "RespondentUserId" IS NOT NULL """);
        builder.HasIndex(submission => new { submission.TenantId, submission.SurveyTargetId, submission.SubmittedAtUtc, submission.Id });
        builder.HasOne<FormVersion>().WithMany()
            .HasForeignKey(submission => new { submission.FormVersionId, submission.TenantId, submission.FormDefinitionId })
            .HasPrincipalKey(version => new { version.Id, version.TenantId, version.FormDefinitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedSurveySchema>().WithMany()
            .HasForeignKey(submission => new { submission.PublishedSurveySchemaId, submission.TenantId, submission.SurveyTargetId,
                submission.FormDefinitionId, submission.FormVersionId })
            .HasPrincipalKey(schema => new { schema.Id, schema.TenantId, schema.SurveyTargetId, schema.FormDefinitionId, schema.FormVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(submission => submission.RespondentUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class FormSubmissionValueConfiguration : IEntityTypeConfiguration<FormSubmissionValue>
{
    public void Configure(EntityTypeBuilder<FormSubmissionValue> builder)
    {
        builder.ToTable("form_submission_values");
        builder.ConfigureEntity();
        builder.Property(value => value.ValueJson).HasColumnType("jsonb").IsRequired();
        builder.Property<Guid?>("LegacyQuestionId");
        builder.Property<string>("LegacyAnswerText").HasMaxLength(4000);
        builder.Property<string>("LegacyAnswerJson").HasColumnType("jsonb");
        builder.Property<bool>("HasUninterpretedLegacyValue").HasDefaultValue(false);
        builder.HasAlternateKey(value => new { value.Id, value.TenantId });
        builder.HasIndex(value => new { value.FormSubmissionId, value.TenantId, value.FormFieldDefinitionId }).IsUnique();
        builder.HasOne<FormSubmission>().WithMany(submission => submission.Values)
            .HasForeignKey(value => new { value.FormSubmissionId, value.TenantId })
            .HasPrincipalKey(submission => new { submission.Id, submission.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FormFieldDefinition>().WithMany()
            .HasForeignKey(value => new { value.FormFieldDefinitionId, value.TenantId })
            .HasPrincipalKey(field => new { field.Id, field.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedSurveySchemaField>().WithMany()
            .HasForeignKey(value => new { value.PublishedSurveySchemaId, value.FormFieldDefinitionId, value.TenantId })
            .HasPrincipalKey(field => new { field.PublishedSurveySchemaId, field.FormFieldDefinitionId, field.TenantId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyRespondentIdentityConfiguration : IEntityTypeConfiguration<SurveyRespondentIdentity>
{
    public void Configure(EntityTypeBuilder<SurveyRespondentIdentity> builder)
    {
        builder.ToTable("survey_respondent_identities", table =>
        {
            table.HasCheckConstraint("CK_survey_respondent_identities_present", """num_nonnulls("EnteredName", "AccountUserId") > 0""");
            table.HasCheckConstraint("CK_survey_respondent_identities_consent", """num_nonnulls("AccountUserId", "AccountConsentAtUtc") <> 1""");
        });
        builder.ConfigureEntity();
        builder.Property(identity => identity.EnteredName).HasMaxLength(120);
        builder.HasIndex(identity => new { identity.FormSubmissionId, identity.TenantId }).IsUnique();
        builder.HasOne<FormSubmission>().WithMany()
            .HasForeignKey(identity => new { identity.FormSubmissionId, identity.TenantId })
            .HasPrincipalKey(submission => new { submission.Id, submission.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(identity => identity.AccountUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubmissionIdempotencyConfiguration : IEntityTypeConfiguration<SubmissionIdempotency>
{
    public void Configure(EntityTypeBuilder<SubmissionIdempotency> builder)
    {
        builder.ToTable("submission_idempotency", table =>
        {
            table.HasCheckConstraint("CK_submission_idempotency_digests", """ "KeyDigest" ~ '^[0-9a-f]{64}$' AND "IntentDigest" ~ '^[0-9a-f]{64}$' """);
            table.HasCheckConstraint("CK_submission_idempotency_status", """ "StatusCode" = 201 """);
        });
        builder.ConfigureEntity();
        builder.Property(receipt => receipt.KeyDigest).HasMaxLength(64).IsRequired();
        builder.Property(receipt => receipt.IntentDigest).HasMaxLength(64).IsRequired();
        // Keep the exact recorded byte sequence for a successful replay.
        builder.Property(receipt => receipt.ReceiptJson).IsRequired();
        builder.HasIndex(receipt => new { receipt.TenantId, receipt.SurveyTargetId, receipt.KeyDigest }).IsUnique();
        builder.HasIndex(receipt => receipt.ExpiresAtUtc);
        builder.HasOne<FormSubmission>().WithMany()
            .HasForeignKey(receipt => new { receipt.FormSubmissionId, receipt.TenantId })
            .HasPrincipalKey(submission => new { submission.Id, submission.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SurveyTarget>().WithMany()
            .HasForeignKey(receipt => new { receipt.SurveyTargetId, receipt.TenantId })
            .HasPrincipalKey(target => new { target.Id, target.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyPublicCodeConfiguration : IEntityTypeConfiguration<SurveyPublicCode>
{
    public void Configure(EntityTypeBuilder<SurveyPublicCode> builder)
    {
        builder.ToTable("survey_public_codes", table =>
        {
            table.HasCheckConstraint("CK_survey_public_codes_subject", """num_nonnulls("EventId", "SurveyTargetId") = 1""");
            table.HasCheckConstraint("CK_survey_public_codes_digest", """ "CodeDigest" ~ '^[0-9a-f]{64}$' """);
        });
        builder.ConfigureEntity();
        builder.Property(code => code.CodeDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(code => code.CodeDigest).IsUnique();
        builder.HasIndex(code => new { code.TenantId, code.EventId }).IsUnique()
            .HasFilter(""" "EventId" IS NOT NULL AND "RevokedAtUtc" IS NULL """);
        builder.HasIndex(code => new { code.TenantId, code.SurveyTargetId }).IsUnique()
            .HasFilter(""" "SurveyTargetId" IS NOT NULL AND "RevokedAtUtc" IS NULL """);
        builder.HasOne<ActivityEvent>().WithMany()
            .HasForeignKey(code => new { code.EventId, code.TenantId })
            .HasPrincipalKey(activityEvent => new { activityEvent.Id, activityEvent.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SurveyTarget>().WithMany()
            .HasForeignKey(code => new { code.SurveyTargetId, code.TenantId })
            .HasPrincipalKey(target => new { target.Id, target.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
