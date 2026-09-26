using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coglatas.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(project => project.Name).HasMaxLength(200).IsRequired();
        builder.Property(project => project.Slug).HasMaxLength(140).IsRequired();
        builder.Property(project => project.Description).HasMaxLength(4000);
        builder.Property(project => project.Status).HasEnumStringConversion().IsRequired();
        builder.Property(project => project.VersionNo).HasDefaultValue(1L).IsConcurrencyToken();

        builder.HasIndex(project => project.WorkspaceId);
        builder.HasIndex(project => project.GroupId);
        builder.HasIndex(project => project.OwnerUserId);
        builder.HasIndex(project => new { project.TenantId, project.WorkspaceId, project.Slug }).IsUnique();
        builder.HasIndex(project => project.Status);
        builder.HasIndex(project => project.DueDate);
        builder.HasIndex(project => new { project.TenantId, project.GroupId, project.Status });
        builder.HasIndex(project => new { project.TenantId, project.Status });
        builder.HasIndex(project => new { project.TenantId, project.CreatedAt });

        builder
            .HasOne(project => project.Workspace)
            .WithMany()
            .HasForeignKey(project => project.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(project => project.Group)
            .WithMany()
            .HasForeignKey(project => project.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(project => project.CreatedByUser)
            .WithMany()
            .HasForeignKey(project => project.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(project => project.OwnerUser)
            .WithMany()
            .HasForeignKey(project => project.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.ConfigureAuditableEntity();

        builder.Property(member => member.Role).HasEnumStringConversion().IsRequired();
        builder.Property(member => member.JoinedAt).IsRequired();

        builder.HasIndex(member => new { member.TenantId, member.ProjectId, member.UserId }).IsUnique();
        builder.HasIndex(member => member.UserId);

        builder
            .HasOne(member => member.Project)
            .WithMany(project => project.Members)
            .HasForeignKey(member => member.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> builder)
    {
        builder.ToTable("milestones");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(milestone => milestone.Name).HasMaxLength(200).IsRequired();
        builder.Property(milestone => milestone.Description).HasMaxLength(4000);
        builder.Property(milestone => milestone.Status).HasEnumStringConversion().IsRequired();
        builder.Property(milestone => milestone.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);

        builder.HasIndex(milestone => milestone.ProjectId);
        builder.HasIndex(milestone => milestone.DueDate);
        builder.HasIndex(milestone => new { milestone.ProjectId, milestone.SortOrder });

        builder
            .HasOne(milestone => milestone.Project)
            .WithMany(project => project.Milestones)
            .HasForeignKey(milestone => milestone.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("task_items");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(task => task.Title).HasMaxLength(240).IsRequired();
        builder.Property(task => task.Description).HasMaxLength(8000);
        builder.Property(task => task.BriefGoal).HasMaxLength(4_000);
        builder.Property(task => task.BriefDeliverable).HasMaxLength(4_000);
        builder.Property(task => task.BriefConstraints).HasMaxLength(4_000);
        builder.Property(task => task.Status).HasEnumStringConversion().IsRequired();
        builder.Property(task => task.Priority).HasEnumStringConversion().IsRequired();
        builder.Property(task => task.Kind).HasEnumStringConversion().IsRequired();
        builder.Property(task => task.BlockedReason).HasMaxLength(500);
        builder.Property(task => task.CancellationReason).HasMaxLength(1000);
        builder.Property(task => task.ReviewStatus).HasEnumStringConversion().HasDefaultValue(TaskReviewStatus.None).IsRequired();
        builder.Property(task => task.ReviewReturnReason).HasMaxLength(1000);
        builder.Property(task => task.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);

        builder.HasIndex(task => task.ProjectId);
        builder.HasIndex(task => task.MilestoneId);
        builder.HasIndex(task => task.CreatedByUserId);
        builder.HasIndex(task => task.Status);
        builder.HasIndex(task => task.Priority);
        builder.HasIndex(task => task.DueDate);
        builder.HasIndex(task => new { task.ProjectId, task.Status });
        builder.HasIndex(task => new { task.ProjectId, task.SortOrder });
        builder.HasIndex(task => new { task.TenantId, task.ProjectId, task.Status });
        builder.HasIndex(task => new { task.TenantId, task.DueDate });
        builder.HasIndex(task => new { task.ProjectId, task.WorkflowStageId, task.SortKey });
        builder.HasIndex(task => new { task.TenantId, task.WorkspaceId, task.ProjectId });
        builder.HasIndex(task => task.PrimaryAssigneeUserId);
        builder.HasIndex(task => task.ReviewerUserId);
        builder.HasIndex(task => task.TargetGroupId);
        builder.HasIndex(task => task.ParentTaskItemId);
        builder.HasIndex(task => new { task.ProjectId, task.TargetGroupId, task.PrimaryAssigneeUserId, task.WorkflowStageId });
        builder.HasIndex(task => new { task.ProjectId, task.PlannedEndDate });
        builder.HasIndex(task => new { task.ProjectId, task.DeadlineAt });
        builder.HasIndex(task => new { task.ProjectId, task.IsBlocked });
        // My Tasks starts from a tenant/workspace-scoped Task set and then applies
        // relationship predicates.  These indexes keep the paged projection from
        // falling back to a tenant-wide Task scan for the common active views.
        builder.HasIndex(task => new { task.TenantId, task.WorkspaceId, task.IsBlocked, task.Priority, task.DeadlineAt });
        builder.HasIndex(task => new { task.TenantId, task.WorkspaceId, task.PrimaryAssigneeUserId });
        builder.HasIndex(task => new { task.TenantId, task.WorkspaceId, task.ReviewerUserId });
        builder.HasIndex(task => new { task.TenantId, task.WorkspaceId, task.CreatedByUserId });
        builder.HasAlternateKey(task => new { task.Id, task.ProjectId });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_task_items_reviewer_not_primary", "\"ReviewerUserId\" IS NULL OR \"PrimaryAssigneeUserId\" IS NULL OR \"ReviewerUserId\" <> \"PrimaryAssigneeUserId\"");
            table.HasCheckConstraint("CK_task_items_planned_dates", "\"PlannedEndDate\" IS NULL OR \"PlannedStartDate\" IS NULL OR \"PlannedEndDate\" >= \"PlannedStartDate\"");
            table.HasCheckConstraint("CK_task_items_effort", "\"EstimatedEffortMinutes\" IS NULL OR \"EstimatedEffortMinutes\" >= 0");
        });

        builder
            .HasOne(task => task.Project)
            .WithMany(project => project.Tasks)
            .HasForeignKey(task => task.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(task => task.ParentTaskItem)
            .WithMany(task => task.ChildTaskItems)
            .HasForeignKey(task => new { task.ParentTaskItemId, task.ProjectId })
            .HasPrincipalKey(task => new { task.Id, task.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(task => task.WorkflowStage)
            .WithMany()
            .HasForeignKey(task => new { task.WorkflowStageId, task.ProjectId })
            .HasPrincipalKey(stage => new { stage.Id, stage.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(task => task.TargetGroup)
            .WithMany()
            .HasForeignKey(task => task.TargetGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(task => task.PrimaryAssigneeUser)
            .WithMany()
            .HasForeignKey(task => task.PrimaryAssigneeUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(task => task.ReviewerUser)
            .WithMany()
            .HasForeignKey(task => task.ReviewerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(task => task.Milestone)
            .WithMany(milestone => milestone.Tasks)
            .HasForeignKey(task => task.MilestoneId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(task => task.CreatedByUser)
            .WithMany()
            .HasForeignKey(task => task.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("task_assignments");
        builder.ConfigureEntity();

        builder.Property(assignment => assignment.Role).HasEnumStringConversion().IsRequired();
        builder.Property(assignment => assignment.EstimatedHours).HasPrecision(8, 2);
        builder.Property(assignment => assignment.ActualHours).HasPrecision(8, 2);
        builder.Property(assignment => assignment.AssignedAt).IsRequired();

        builder.HasIndex(assignment => new { assignment.TenantId, assignment.TaskItemId, assignment.UserId, assignment.Role }).IsUnique();
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.UserId });
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.TaskItemId });
        builder.HasIndex(assignment => assignment.UserId);
        builder.HasIndex(assignment => assignment.AssignedByUserId);

        builder
            .HasOne(assignment => assignment.TaskItem)
            .WithMany(task => task.Assignments)
            .HasForeignKey(assignment => assignment.TaskItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(assignment => assignment.User)
            .WithMany()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(assignment => assignment.AssignedByUser)
            .WithMany()
            .HasForeignKey(assignment => assignment.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("task_dependencies");
        builder.ConfigureAuditableEntity();

        builder.Property(dependency => dependency.DependencyType).HasEnumStringConversion().IsRequired();

        builder.HasIndex(dependency => dependency.ProjectId);
        builder.HasIndex(dependency => new { dependency.TenantId, dependency.PredecessorTaskItemId, dependency.SuccessorTaskItemId }).IsUnique();
        builder.HasIndex(dependency => new { dependency.ProjectId, dependency.PredecessorTaskItemId });
        builder.HasIndex(dependency => new { dependency.ProjectId, dependency.SuccessorTaskItemId });

        builder
            .HasOne(dependency => dependency.Project)
            .WithMany()
            .HasForeignKey(dependency => dependency.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(dependency => dependency.PredecessorTaskItem)
            .WithMany(task => task.SuccessorDependencies)
            .HasForeignKey(dependency => dependency.PredecessorTaskItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(dependency => dependency.SuccessorTaskItem)
            .WithMany(task => task.PredecessorDependencies)
            .HasForeignKey(dependency => dependency.SuccessorTaskItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskWorkflowDefinitionConfiguration : IEntityTypeConfiguration<TaskWorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<TaskWorkflowDefinition> builder)
    {
        builder.ToTable("task_workflow_definitions");
        builder.ConfigureEntity();
        builder.Property(definition => definition.Name).HasMaxLength(120).IsRequired();
        builder.Property(definition => definition.KanbanDefaultSwimlane).HasEnumStringConversion().HasDefaultValue(ProjectKanbanSwimlane.None).IsRequired();
        builder.Property(definition => definition.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);
        builder.HasIndex(definition => definition.ProjectId).IsUnique();
        builder.HasIndex(definition => new { definition.TenantId, definition.WorkspaceId, definition.ProjectId });
        builder.HasAlternateKey(definition => new { definition.Id, definition.ProjectId });
        builder.HasOne(definition => definition.Project)
            .WithMany(project => project.TaskWorkflowDefinitions)
            .HasForeignKey(definition => definition.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskWorkflowStageConfiguration : IEntityTypeConfiguration<TaskWorkflowStage>
{
    public void Configure(EntityTypeBuilder<TaskWorkflowStage> builder)
    {
        builder.ToTable("task_workflow_stages");
        builder.ConfigureEntity();
        builder.Property(stage => stage.Name).HasMaxLength(120).IsRequired();
        builder.Property(stage => stage.InternalCategory).HasEnumStringConversion().IsRequired();
        builder.Property(stage => stage.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);
        builder.HasIndex(stage => new { stage.DefinitionId, stage.SortKey }).IsUnique();
        builder.HasIndex(stage => new { stage.ProjectId, stage.InternalCategory });
        builder.HasAlternateKey(stage => new { stage.Id, stage.ProjectId });
        builder.ToTable(table => table.HasCheckConstraint("CK_task_workflow_stages_wip", "\"WipWarningLimit\" IS NULL OR \"WipWarningLimit\" > 0"));
        builder.HasOne(stage => stage.Definition)
            .WithMany(definition => definition.Stages)
            .HasForeignKey(stage => stage.DefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkItemCollaboratorConfiguration : IEntityTypeConfiguration<WorkItemCollaborator>
{
    public void Configure(EntityTypeBuilder<WorkItemCollaborator> builder)
    {
        builder.ToTable("task_item_collaborators");
        builder.ConfigureEntity();
        builder.Property(collaborator => collaborator.AddedAt).IsRequired();
        builder.HasIndex(collaborator => new { collaborator.TenantId, collaborator.TaskItemId, collaborator.UserId }).IsUnique();
        builder.HasIndex(collaborator => collaborator.UserId);
        builder.HasIndex(collaborator => new { collaborator.TenantId, collaborator.UserId, collaborator.TaskItemId });
        builder.HasOne(collaborator => collaborator.TaskItem)
            .WithMany(task => task.Collaborators)
            .HasForeignKey(collaborator => collaborator.TaskItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(collaborator => collaborator.User)
            .WithMany()
            .HasForeignKey(collaborator => collaborator.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(collaborator => collaborator.AddedByUser)
            .WithMany()
            .HasForeignKey(collaborator => collaborator.AddedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskMigrationInventoryConfiguration : IEntityTypeConfiguration<TaskMigrationInventory>
{
    public void Configure(EntityTypeBuilder<TaskMigrationInventory> builder)
    {
        builder.ToTable("task_migration_inventory");
        builder.ConfigureEntity();
        builder.Property(item => item.FindingCode).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Details).HasMaxLength(1000).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.HasIndex(item => new { item.TenantId, item.FindingCode });
        builder.HasIndex(item => item.TaskItemId);
        builder.HasIndex(item => item.ProjectId);
    }
}

public sealed class TaskChecklistItemConfiguration : IEntityTypeConfiguration<TaskChecklistItem>
{
    public void Configure(EntityTypeBuilder<TaskChecklistItem> builder)
    {
        builder.ToTable("task_checklist_items"); builder.ConfigureEntity();
        builder.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TaskItemId, x.SortKey });
        builder.HasOne(x => x.TaskItem).WithMany(x => x.ChecklistItems).HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CompletedByUser).WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments"); builder.ConfigureSoftDeletableEntity();
        builder.Property(x => x.BodyPlainText).HasMaxLength(12000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TaskItemId, x.CreatedAt });
        builder.HasOne(x => x.TaskItem).WithMany(x => x.TaskComments).HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AuthorUser).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkItemWatchStateConfiguration : IEntityTypeConfiguration<WorkItemWatchState>
{
    public void Configure(EntityTypeBuilder<WorkItemWatchState> builder)
    {
        builder.ToTable("work_item_watch_states", table =>
            table.HasCheckConstraint(
                "CK_work_item_watch_states_manual_opt_out_exclusive",
                "NOT (\"IsManualWatch\" AND \"IsExplicitOptOut\")"));
        builder.ConfigureEntity();
        builder.Property(x => x.AutomaticSources).HasConversion<int>();
        builder.Property(x => x.IsManualWatch).HasDefaultValue(false);
        builder.Property(x => x.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);
        builder.HasIndex(x => new { x.TenantId, x.TaskItemId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.IsWatching, x.TaskItemId });
        builder.HasOne(x => x.TaskItem).WithMany(x => x.WatchStates).HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProjectTaskLabelConfiguration : IEntityTypeConfiguration<ProjectTaskLabel>
{
    public void Configure(EntityTypeBuilder<ProjectTaskLabel> builder)
    {
        builder.ToTable("project_task_labels"); builder.ConfigureEntity();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired(); builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.NormalizedName)
            .HasMaxLength(120)
            .HasComputedColumnSql("lower(btrim(\"Name\"))", stored: true);
        builder.Property(x => x.VersionNo).IsConcurrencyToken().HasDefaultValue(1L);
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.NormalizedName }).IsUnique();
        builder.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkItemLabelConfiguration : IEntityTypeConfiguration<WorkItemLabel>
{
    public void Configure(EntityTypeBuilder<WorkItemLabel> builder)
    {
        builder.ToTable("work_item_labels"); builder.ConfigureEntity();
        builder.HasIndex(x => new { x.TenantId, x.TaskItemId, x.LabelId }).IsUnique();
        builder.HasOne(x => x.TaskItem).WithMany(x => x.Labels).HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Label).WithMany().HasForeignKey(x => x.LabelId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs");
        builder.ConfigureAuditableEntity();

        builder.Property(log => log.ActivityType).HasEnumStringConversion().IsRequired();
        builder.Property(log => log.Body).HasMaxLength(12000).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();

        builder.HasIndex(log => log.ProjectId);
        builder.HasIndex(log => log.TaskItemId);
        builder.HasIndex(log => log.AuthorUserId);
        builder.HasIndex(log => log.OccurredAt);

        builder
            .HasOne(log => log.Project)
            .WithMany()
            .HasForeignKey(log => log.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(log => log.TaskItem)
            .WithMany()
            .HasForeignKey(log => log.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(log => log.AuthorUser)
            .WithMany()
            .HasForeignKey(log => log.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("artifacts");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(artifact => artifact.Name).HasMaxLength(240).IsRequired();
        builder.Property(artifact => artifact.Description).HasMaxLength(4000);
        builder.Property(artifact => artifact.ArtifactType).HasEnumStringConversion().IsRequired();
        builder.Property(artifact => artifact.Status).HasEnumStringConversion().IsRequired();

        builder.HasIndex(artifact => artifact.ProjectId);
        builder.HasIndex(artifact => artifact.TaskItemId);
        builder.HasIndex(artifact => artifact.CurrentVersionId);
        builder.HasIndex(artifact => artifact.CreatedByUserId);
        builder.HasIndex(artifact => artifact.Status);
        builder.HasIndex(artifact => new { artifact.TenantId, artifact.ProjectId });
        builder.HasIndex(artifact => new { artifact.TenantId, artifact.Status });

        builder
            .HasOne(artifact => artifact.Project)
            .WithMany()
            .HasForeignKey(artifact => artifact.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(artifact => artifact.TaskItem)
            .WithMany()
            .HasForeignKey(artifact => artifact.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(artifact => artifact.CurrentVersion)
            .WithMany()
            .HasForeignKey(artifact => artifact.CurrentVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(artifact => artifact.CreatedByUser)
            .WithMany()
            .HasForeignKey(artifact => artifact.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ArtifactVersionConfiguration : IEntityTypeConfiguration<ArtifactVersion>
{
    public void Configure(EntityTypeBuilder<ArtifactVersion> builder)
    {
        builder.ToTable("artifact_versions");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(version => version.Notes).HasMaxLength(4000);

        builder.HasIndex(version => new { version.TenantId, version.ArtifactId, version.VersionNumber }).IsUnique();
        builder.HasIndex(version => version.AttachmentId);
        builder.HasIndex(version => version.FileObjectId);
        builder.HasIndex(version => version.CreatedByUserId);

        builder
            .HasOne(version => version.Artifact)
            .WithMany(artifact => artifact.Versions)
            .HasForeignKey(version => version.ArtifactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(version => version.Attachment)
            .WithMany()
            .HasForeignKey(version => version.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(version => version.FileObject)
            .WithMany()
            .HasForeignKey(version => version.FileObjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(version => version.CreatedByUser)
            .WithMany()
            .HasForeignKey(version => version.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(comment => comment.TargetType).HasEnumStringConversion().IsRequired();
        builder.Property(comment => comment.Body).HasMaxLength(12000).IsRequired();

        builder.HasIndex(comment => comment.WorkspaceId);
        builder.HasIndex(comment => comment.AuthorUserId);
        builder.HasIndex(comment => new { comment.TargetType, comment.TargetId });
        builder.HasIndex(comment => new { comment.TenantId, comment.TargetType, comment.TargetId, comment.CreatedAt });

        builder
            .HasOne(comment => comment.Workspace)
            .WithMany()
            .HasForeignKey(comment => comment.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(comment => comment.AuthorUser)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("feedback");
        builder.ConfigureSoftDeletableEntity();

        builder.Property(feedback => feedback.TargetType).HasEnumStringConversion().IsRequired();
        builder.Property(feedback => feedback.Body).HasMaxLength(12000).IsRequired();

        builder.HasIndex(feedback => feedback.WorkspaceId);
        builder.HasIndex(feedback => feedback.AuthorUserId);
        builder.HasIndex(feedback => feedback.TargetUserId);
        builder.HasIndex(feedback => feedback.ProjectId);
        builder.HasIndex(feedback => feedback.TaskItemId);
        builder.HasIndex(feedback => feedback.ArtifactId);
        builder.HasIndex(feedback => feedback.ActivityLogId);

        builder
            .HasOne(feedback => feedback.Workspace)
            .WithMany()
            .HasForeignKey(feedback => feedback.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(feedback => feedback.AuthorUser)
            .WithMany()
            .HasForeignKey(feedback => feedback.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(feedback => feedback.TargetUser)
            .WithMany()
            .HasForeignKey(feedback => feedback.TargetUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(feedback => feedback.Project)
            .WithMany()
            .HasForeignKey(feedback => feedback.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(feedback => feedback.TaskItem)
            .WithMany()
            .HasForeignKey(feedback => feedback.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(feedback => feedback.Artifact)
            .WithMany()
            .HasForeignKey(feedback => feedback.ArtifactId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(feedback => feedback.ActivityLog)
            .WithMany()
            .HasForeignKey(feedback => feedback.ActivityLogId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
