namespace Coglatas.Application.Projects;

/// <summary>PostgreSQL constraint identities owned by Task V1 command contracts.</summary>
public static class TaskCommandConstraintNames
{
    public const string ProjectTaskLabelNormalizedName = "IX_project_task_labels_TenantId_ProjectId_NormalizedName";
    public const string WorkItemLabelIdentity = "IX_work_item_labels_TenantId_TaskItemId_LabelId";
    public const string WorkItemWatchStateIdentity = "IX_work_item_watch_states_TenantId_TaskItemId_UserId";
    public const string ActiveTaskAttachmentIdentity = "IX_attachments_OwnerType_OwnerId_FileObjectId_active_task";
    public const string TaskAssignmentIdentity = "IX_task_assignments_TenantId_TaskItemId_UserId_Role";
}
