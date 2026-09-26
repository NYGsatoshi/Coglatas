using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ResearchPlanConfiguration : IEntityTypeConfiguration<ResearchPlan>
{
    public void Configure(EntityTypeBuilder<ResearchPlan> builder)
    {
        builder.ToTable("research_plans", table =>
        {
            table.HasCheckConstraint("CK_research_plans_version_positive", "\"VersionNo\" > 0");
        });
        builder.ConfigureEntity();

        builder.Property(plan => plan.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);

        builder.HasIndex(plan => plan.TaskItemId).IsUnique();
        builder.HasIndex(plan => new { plan.TenantId, plan.WorkspaceId, plan.ProjectId, plan.TaskItemId });
        builder.HasIndex(plan => plan.CurrentRevisionId);

        builder
            .HasOne(plan => plan.Project)
            .WithMany()
            .HasForeignKey(plan => plan.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(plan => plan.TaskItem)
            .WithOne(task => task.ResearchPlan)
            .HasForeignKey<ResearchPlan>(plan => new { plan.TaskItemId, plan.ProjectId })
            .HasPrincipalKey<TaskItem>(task => new { task.Id, task.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);

        // The composite key prevents a raw persistence caller from pointing a
        // plan at a revision belonging to a different Task or plan.
        builder
            .HasOne(plan => plan.CurrentRevision)
            .WithMany()
            .HasForeignKey(plan => new { plan.CurrentRevisionId, plan.Id })
            .HasPrincipalKey(revision => new { revision.Id, revision.ResearchPlanId })
            // PostgreSQL defers this optional pointer constraint so a newly
            // created plan can atomically append its first revision and set
            // the pointer in the same SaveChanges transaction. The migration
            // marks the resulting NO ACTION foreign key DEFERRABLE.
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ResearchPlanRevisionConfiguration : IEntityTypeConfiguration<ResearchPlanRevision>
{
    public void Configure(EntityTypeBuilder<ResearchPlanRevision> builder)
    {
        builder.ToTable("research_plan_revisions", table =>
        {
            table.HasCheckConstraint("CK_research_plan_revisions_revision_positive", "\"RevisionNo\" > 0");
        });
        builder.ConfigureEntity();

        builder.Property(revision => revision.RevisionNo).HasDefaultValue(1L).IsRequired();
        builder.Property(revision => revision.CreatedAtUtc).IsRequired();

        builder.HasAlternateKey(revision => new { revision.Id, revision.ResearchPlanId });
        // Execution provenance carries both the opaque revision identity and
        // its human-readable revision number. Keep them bound to one scoped
        // revision at the database boundary rather than trusting a duplicate
        // positive-number claim on the run.
        builder.HasAlternateKey(revision => new
        {
            revision.Id,
            revision.TenantId,
            revision.WorkspaceId,
            revision.ProjectId,
            revision.TaskItemId,
            revision.RevisionNo
        }).HasName("AK_research_plan_revisions_execution_snapshot_identity");
        builder.HasIndex(revision => new { revision.ResearchPlanId, revision.RevisionNo }).IsUnique();
        builder.HasIndex(revision => new
        {
            revision.TenantId,
            revision.WorkspaceId,
            revision.ProjectId,
            revision.TaskItemId,
            revision.RevisionNo
        });
        builder.HasIndex(revision => revision.CreatedByUserId);

        builder
            .HasOne(revision => revision.ResearchPlan)
            .WithMany(plan => plan.Revisions)
            .HasForeignKey(revision => revision.ResearchPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(revision => revision.Project)
            .WithMany()
            .HasForeignKey(revision => revision.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(revision => revision.TaskItem)
            .WithMany()
            .HasForeignKey(revision => new { revision.TaskItemId, revision.ProjectId })
            .HasPrincipalKey(task => new { task.Id, task.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(revision => revision.CreatedByUser)
            .WithMany()
            .HasForeignKey(revision => revision.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ResearchPlanStepConfiguration : IEntityTypeConfiguration<ResearchPlanStep>
{
    public void Configure(EntityTypeBuilder<ResearchPlanStep> builder)
    {
        builder.ToTable("research_plan_steps", table =>
        {
            table.HasCheckConstraint("CK_research_plan_steps_sort_order_positive", "\"SortOrder\" > 0");
        });
        builder.ConfigureEntity();

        builder.Property(step => step.SortOrder).IsRequired();
        builder.Property(step => step.Title).HasMaxLength(240).IsRequired();
        builder.Property(step => step.Objective).HasMaxLength(4_000).IsRequired();
        builder.Property(step => step.ScopeSummary).HasMaxLength(4_000).IsRequired();
        builder.Property(step => step.Status)
            .HasEnumStringConversion()
            .HasDefaultValue(ResearchPlanStepStatus.Planned)
            .IsRequired();

        builder.HasIndex(step => new { step.ResearchPlanRevisionId, step.SortOrder }).IsUnique();
        builder.HasIndex(step => new
        {
            step.TenantId,
            step.WorkspaceId,
            step.ProjectId,
            step.TaskItemId,
            step.ResearchPlanRevisionId
        });

        builder
            .HasOne(step => step.ResearchPlan)
            .WithMany()
            .HasForeignKey(step => step.ResearchPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(step => step.ResearchPlanRevision)
            .WithMany(revision => revision.Steps)
            .HasForeignKey(step => new { step.ResearchPlanRevisionId, step.ResearchPlanId })
            .HasPrincipalKey(revision => new { revision.Id, revision.ResearchPlanId })
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(step => step.Project)
            .WithMany()
            .HasForeignKey(step => step.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(step => step.TaskItem)
            .WithMany()
            .HasForeignKey(step => new { step.TaskItemId, step.ProjectId })
            .HasPrincipalKey(task => new { task.Id, task.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
