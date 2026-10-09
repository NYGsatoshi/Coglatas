using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class SecurityEvaluationRunConfiguration : IEntityTypeConfiguration<SecurityEvaluationRun>
{
    public void Configure(EntityTypeBuilder<SecurityEvaluationRun> builder)
    {
        builder.ToTable("security_evaluation_runs", table =>
        {
            table.HasCheckConstraint("CK_security_evaluation_runs_schema", "\"SchemaVersion\" = 1");
            table.HasCheckConstraint("CK_security_evaluation_runs_digest", "\"InputDigest\" ~ '^[0-9a-f]{64}$' AND \"BindingDigest\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_security_evaluation_runs_mode", "\"EnforcementMode\" IN ('Disabled', 'Shadow')");
            table.HasCheckConstraint("CK_security_evaluation_runs_context", "\"ContextKind\" IN ('coglatas.committed', 'coglatas.candidate', 'coglatas.scenario')");
            table.HasCheckConstraint("CK_security_evaluation_runs_identity", "jsonb_typeof(\"IdentityJson\") = 'object'");
            table.HasCheckConstraint("CK_security_evaluation_runs_lifecycle", """
                ("Status" = 'Pending' AND "Outcome" IS NULL AND "TerminalAtUtc" IS NULL AND "ReasonCode" = 'NotExecuted') OR
                ("Status" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND "TerminalAtUtc" IS NOT NULL AND "TerminalAtUtc" >= "CreatedAtUtc"
                 AND (("Status" = 'Completed' AND "Outcome" IS NOT NULL AND "Outcome" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR
                      ("Status" <> 'Completed' AND "Outcome" IS NULL)))
                """);
            table.HasCheckConstraint("CK_security_evaluation_runs_reason", SecurityEvaluationConstraintSql.Reason);
        });
        builder.HasKey(run => run.Id);
        builder.HasAlternateKey(run => new { run.Id, run.TenantId, run.ProjectId });
        builder.Property(run => run.ContextKind).HasMaxLength(32).IsRequired();
        builder.Property(run => run.InputDigest).HasMaxLength(64).IsRequired();
        builder.Property(run => run.BindingDigest).HasMaxLength(64).IsRequired();
        builder.Property(run => run.IdentityJson).HasColumnType("jsonb").IsRequired();
        builder.Property(run => run.EnforcementMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
        builder.Property(run => run.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(run => run.ReasonCode).HasConversion<string>().HasMaxLength(48);
        builder.HasIndex(run => new { run.TenantId, run.ProjectId, run.CreatedAtUtc });
        builder.HasIndex(run => new { run.TenantId, run.ProjectId, run.BranchId, run.RevisionId, run.CandidateRevisionId });
        builder.HasOne<Project>().WithMany().HasForeignKey(run => new { run.ProjectId, run.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SecurityEvaluationRuleRecordConfiguration : IEntityTypeConfiguration<SecurityEvaluationRuleRecord>
{
    public void Configure(EntityTypeBuilder<SecurityEvaluationRuleRecord> builder)
    {
        builder.ToTable("security_evaluation_rule_results", table =>
        {
            table.HasCheckConstraint("CK_security_evaluation_rule_results_schema", "\"SchemaVersion\" = 1");
            table.HasCheckConstraint("CK_security_evaluation_rule_results_sequence", "\"Sequence\" >= 0");
            table.HasCheckConstraint("CK_security_evaluation_rule_results_id", "\"RuleId\" ~ '^[A-Za-z0-9_./:+-]{1,128}$'");
            table.HasCheckConstraint("CK_security_evaluation_rule_results_execution", """
                ("Status" = 'Completed' AND "Outcome" IS NOT NULL AND "Outcome" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR
                ("Status" IN ('Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND "Outcome" IS NULL)
                """);
            table.HasCheckConstraint("CK_security_evaluation_rule_results_reason", SecurityEvaluationConstraintSql.Reason);
        });
        builder.HasKey(rule => new { rule.EvaluationId, rule.Sequence });
        builder.Property(rule => rule.RuleId).HasMaxLength(128).IsRequired();
        builder.Property(rule => rule.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(rule => rule.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(rule => rule.ReasonCode).HasConversion<string>().HasMaxLength(48);
        builder.HasIndex(rule => new { rule.EvaluationId, rule.RuleId }).IsUnique();
        builder.HasIndex(rule => new { rule.EvaluationId, rule.TenantId, rule.ProjectId });
        builder.HasOne<SecurityEvaluationRun>().WithMany()
            .HasForeignKey(rule => new { rule.EvaluationId, rule.TenantId, rule.ProjectId })
            .HasPrincipalKey(run => new { run.Id, run.TenantId, run.ProjectId }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class SecurityEvaluationConstraintSql
{
    internal const string Reason = """
        ("Status" IN ('Pending', 'NotExecuted') AND "ReasonCode" IN ('NotExecuted', 'RuleNotExecuted')) OR
        ("Status" = 'Failed' AND "ReasonCode" IN ('EvaluationFailed', 'RuleExecutionFailed')) OR
        ("Status" = 'Cancelled' AND "ReasonCode" = 'EvaluationCancelled') OR
        ("Status" = 'TimedOut' AND "ReasonCode" = 'EvaluationTimedOut') OR
        ("Status" = 'Completed' AND (
            ("Outcome" = 'Allow' AND "ReasonCode" IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR
            ("Outcome" = 'Deny' AND "ReasonCode" = 'PolicyViolation') OR
            ("Outcome" = 'Unknown' AND "ReasonCode" IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR
            ("Outcome" = 'Quarantine' AND "ReasonCode" IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))
        """;
}
