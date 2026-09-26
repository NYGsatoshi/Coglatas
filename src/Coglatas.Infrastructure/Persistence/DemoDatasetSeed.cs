using System.Security.Cryptography;
using System.Text;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Creates the small, synthetic fixture used by the Issue #483 demo stack.
/// It is deliberately an idempotent provisioner, not a general seed or a
/// production data migration tool. The Compose reset command owns the
/// isolated database volume; this code only ever creates or refreshes records
/// carrying the <see cref="Namespace"/> identity.
/// </summary>
public static class DemoDatasetSeed
{
    public const string Namespace = "issue-483-demo";
    public const string WorkspaceSlug = "issue-483-demo-workspace";
    public const string ProjectSlug = "issue-483-demo-project";
    public const string OwnerEmail = "demo-operator@example.test";
    public const string ObserverEmail = "demo-observer@example.test";
    public const string ExecutionTaskTitle = "Issue 483 Demo: execute synthetic report";

    private const string WorkspaceDescription = "[issue-483-demo] Synthetic, Test-only Workspace."
        + " It contains no production or personal data.";
    private const string ProjectDescription = "[issue-483-demo] Synthetic, Test-only Project for the repeatable demo.";
    private const string SourceFileName = "issue-483-demo-source.txt";
    private const string SourceFileContents = "Issue #483 synthetic demo source.\n"
        + "This text is safe Test data for the durable Task execution walkthrough.\n";
    private const string ConversationTitle = "Issue #483 Demo Conversation";
    private const string ConversationMessage = "[issue-483-demo] Synthetic conversation message for the demo.";
    private const string PublishedAnnouncementTitle = "Issue #483 Demo: published announcement";
    private const string DraftAnnouncementTitle = "Issue #483 Demo: draft announcement";
    private const string ScheduledAnnouncementTitle = "Issue #483 Demo: scheduled announcement";
    private static readonly DateTimeOffset ReferenceUtc = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ScheduledForUtc = new(2099, 12, 31, 9, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IFileStorageService fileStorage,
        Guid tenantId,
        string ownerEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(fileStorage);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var owner = await EnsureUserAsync(
            dbContext,
            passwordHasher,
            ownerEmail,
            "Demo Operator",
            password,
            cancellationToken);
        var observer = await EnsureUserAsync(
            dbContext,
            passwordHasher,
            ObserverEmail,
            "Demo Observer",
            password,
            cancellationToken);

        await EnsureTenantMemberAsync(dbContext, tenantId, owner, TenantUserRole.Member, cancellationToken);
        await EnsureTenantMemberAsync(dbContext, tenantId, observer, TenantUserRole.Member, cancellationToken);

        var workspace = await dbContext.Workspaces.SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenantId && candidate.Slug == WorkspaceSlug,
            cancellationToken);
        if (workspace is null)
        {
            workspace = new Workspace
            {
                TenantId = tenantId,
                Name = "Issue #483 Demo Workspace",
                Slug = WorkspaceSlug,
                Description = WorkspaceDescription,
                Status = WorkspaceStatus.Active,
                CreatedByUserId = owner.Id
            };
            await dbContext.Workspaces.AddAsync(workspace, cancellationToken);
        }
        else
        {
            EnsureOwnedDescription(workspace.Description, WorkspaceDescription, "Workspace");
            workspace.Name = "Issue #483 Demo Workspace";
            workspace.Description = WorkspaceDescription;
            workspace.Status = WorkspaceStatus.Active;
            workspace.CreatedByUserId = owner.Id;
            if (workspace.IsDeleted)
            {
                workspace.Restore();
            }
        }

        await EnsureWorkspaceMemberAsync(dbContext, tenantId, workspace, owner, WorkspaceRole.Owner, cancellationToken);
        await EnsureWorkspaceMemberAsync(dbContext, tenantId, workspace, observer, WorkspaceRole.ReadOnly, cancellationToken);

        var project = await dbContext.Projects.SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenantId && candidate.WorkspaceId == workspace.Id && candidate.Slug == ProjectSlug,
            cancellationToken);
        if (project is null)
        {
            project = new Project
            {
                TenantId = tenantId,
                WorkspaceId = workspace.Id,
                OwnerUserId = owner.Id,
                CreatedByUserId = owner.Id,
                Name = "Issue #483 Synthetic Demo Project",
                Slug = ProjectSlug,
                Description = ProjectDescription,
                Status = ProjectStatus.Active,
                Visibility = ProjectVisibility.MembersOnly,
                ActivationState = ProjectActivationState.Activated,
                ActivatedAtUtc = ReferenceUtc,
                ActivationVersion = 1,
                VersionNo = 1,
                ExecutionScope = new ProjectExecutionScope
                {
                    TenantId = tenantId,
                    WorkspaceId = workspace.Id,
                    WebEnabled = false,
                    ProjectFilesEnabled = true,
                    VersionNo = 1,
                    UpdatedByUserId = owner.Id
                }
            };
            await dbContext.Projects.AddAsync(project, cancellationToken);
        }
        else
        {
            EnsureOwnedDescription(project.Description, ProjectDescription, "Project");
            project.OwnerUserId = owner.Id;
            project.CreatedByUserId = owner.Id;
            project.Name = "Issue #483 Synthetic Demo Project";
            project.Description = ProjectDescription;
            project.Status = ProjectStatus.Active;
            project.Visibility = ProjectVisibility.MembersOnly;
            project.ActivationState = ProjectActivationState.Activated;
            project.ActivatedAtUtc = ReferenceUtc;
            project.ActivationVersion = 1;
            project.SuspendedFromStatus = null;
            project.ArchivedFromStatus = null;
            project.VersionNo = 1;
            if (project.IsDeleted)
            {
                project.Restore();
            }
        }

        var projectScope = project.ExecutionScope ?? await dbContext.ProjectExecutionScopes.SingleOrDefaultAsync(
            candidate => candidate.ProjectId == project.Id,
            cancellationToken);
        if (projectScope is null)
        {
            projectScope = new ProjectExecutionScope
            {
                TenantId = tenantId,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                UpdatedByUserId = owner.Id,
                VersionNo = 1
            };
            project.ExecutionScope = projectScope;
            await dbContext.ProjectExecutionScopes.AddAsync(projectScope, cancellationToken);
        }
        projectScope.TenantId = tenantId;
        projectScope.WorkspaceId = workspace.Id;
        projectScope.ProjectId = project.Id;
        projectScope.WebEnabled = false;
        projectScope.ProjectFilesEnabled = true;
        projectScope.VersionNo = 1;
        projectScope.UpdatedByUserId = owner.Id;

        await EnsureProjectMemberAsync(dbContext, tenantId, project, owner, ProjectRole.Owner, cancellationToken);

        var workflow = await dbContext.TaskWorkflowDefinitions.SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenantId && candidate.ProjectId == project.Id,
            cancellationToken);
        if (workflow is null)
        {
            workflow = new TaskWorkflowDefinition
            {
                TenantId = tenantId,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                Name = "Issue #483 Demo Workflow",
                ReviewEnforcementEnabled = false,
                VersionNo = 1
            };
            await dbContext.TaskWorkflowDefinitions.AddAsync(workflow, cancellationToken);
        }
        else
        {
            workflow.WorkspaceId = workspace.Id;
            workflow.Name = "Issue #483 Demo Workflow";
            workflow.ReviewEnforcementEnabled = false;
            workflow.VersionNo = 1;
        }

        _ = await EnsureStageAsync(dbContext, tenantId, workspace.Id, project.Id, workflow.Id, "Todo", TaskStageCategory.Todo, 1000, true, false, cancellationToken);
        var inProgress = await EnsureStageAsync(dbContext, tenantId, workspace.Id, project.Id, workflow.Id, "In progress", TaskStageCategory.InProgress, 2000, false, false, cancellationToken);
        var review = await EnsureStageAsync(dbContext, tenantId, workspace.Id, project.Id, workflow.Id, "Review", TaskStageCategory.Review, 3000, false, false, cancellationToken);
        var done = await EnsureStageAsync(dbContext, tenantId, workspace.Id, project.Id, workflow.Id, "Done", TaskStageCategory.Done, 4000, false, true, cancellationToken);

        var executionTask = await EnsureTaskAsync(
            dbContext,
            tenantId,
            workspace.Id,
            project.Id,
            owner.Id,
            ExecutionTaskTitle,
            "[issue-483-demo] Build a durable report from the authorized synthetic source file.",
            TaskItemStatus.InProgress,
            inProgress.Id,
            TaskPriority.High,
            0,
            cancellationToken);
        executionTask.BriefGoal = "Demonstrate an authorized, reproducible Task execution path.";
        executionTask.BriefDeliverable = "A durable report derived only from the synthetic project file.";
        executionTask.BriefConstraints = "Synthetic Test data only. Web retrieval is disabled; use the authorized project attachment only.";

        var reviewTask = await EnsureTaskAsync(
            dbContext,
            tenantId,
            workspace.Id,
            project.Id,
            owner.Id,
            "Issue 483 Demo: review synthetic draft",
            "[issue-483-demo] Representative Task in review.",
            TaskItemStatus.WaitingReview,
            review.Id,
            TaskPriority.Medium,
            75,
            cancellationToken);
        reviewTask.ReviewStatus = TaskReviewStatus.Submitted;
        reviewTask.ReviewSubmittedAt = ReferenceUtc;
        reviewTask.ReviewerUserId = owner.Id;

        var completedTask = await EnsureTaskAsync(
            dbContext,
            tenantId,
            workspace.Id,
            project.Id,
            owner.Id,
            "Issue 483 Demo: completed synthetic summary",
            "[issue-483-demo] Representative completed Task.",
            TaskItemStatus.Completed,
            done.Id,
            TaskPriority.Low,
            100,
            cancellationToken);
        completedTask.CompletedAt = ReferenceUtc;

        var assignment = await dbContext.TaskAssignments.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.TaskItemId == executionTask.Id &&
            candidate.UserId == owner.Id &&
            candidate.Role == TaskAssignmentRole.Assignee,
            cancellationToken);
        if (assignment is null)
        {
            await dbContext.TaskAssignments.AddAsync(new TaskAssignment
            {
                TenantId = tenantId,
                TaskItemId = executionTask.Id,
                UserId = owner.Id,
                Role = TaskAssignmentRole.Assignee,
                AssignedByUserId = owner.Id,
                AssignedAt = ReferenceUtc
            }, cancellationToken);
        }

        await EnsureResearchPlanAsync(dbContext, tenantId, workspace.Id, project.Id, executionTask.Id, owner.Id, cancellationToken);

        var fileBytes = Encoding.UTF8.GetBytes(SourceFileContents);
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        var fileObject = await dbContext.FileObjects.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.ProjectId == project.Id &&
            candidate.OriginalFileName == SourceFileName,
            cancellationToken);
        if (fileObject is null)
        {
            fileObject = new FileObject
            {
                TenantId = tenantId,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                UploadedByUserId = owner.Id,
                OriginalFileName = SourceFileName,
                StorageKey = $"{Namespace}/{tenantId:D}/{project.Id:D}/{SourceFileName}",
                ContentType = "text/plain",
                SizeBytes = fileBytes.LongLength,
                HashSha256 = fileHash,
                Classification = DataClassification.Internal,
                Status = FileObjectStatus.Active
            };
            await dbContext.FileObjects.AddAsync(fileObject, cancellationToken);
        }
        else if (fileObject.Status != FileObjectStatus.Active || fileObject.DeletedAt.HasValue)
        {
            throw new InvalidOperationException("The reserved Issue #483 demo source file is no longer active. Run the isolated reset command.");
        }
        else
        {
            fileObject.WorkspaceId = workspace.Id;
            fileObject.ProjectId = project.Id;
            fileObject.UploadedByUserId = owner.Id;
            fileObject.StorageKey = $"{Namespace}/{tenantId:D}/{project.Id:D}/{SourceFileName}";
            fileObject.ContentType = "text/plain";
            fileObject.SizeBytes = fileBytes.LongLength;
            fileObject.HashSha256 = fileHash;
            fileObject.Classification = DataClassification.Internal;
        }

        var attachment = await dbContext.Attachments.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.OwnerType == AttachmentOwnerType.TaskItem &&
            candidate.OwnerId == executionTask.Id &&
            candidate.FileName == SourceFileName,
            cancellationToken);
        if (attachment is null)
        {
            attachment = new Attachment
            {
                TenantId = tenantId,
                FileObjectId = fileObject.Id,
                WorkspaceId = workspace.Id,
                OwnerType = AttachmentOwnerType.TaskItem,
                OwnerId = executionTask.Id,
                OwnerUserId = owner.Id,
                UploadedByUserId = owner.Id,
                FileName = SourceFileName,
                StoredFileName = SourceFileName,
                FilePath = $"{Namespace}/task-source",
                ContentType = "text/plain",
                Extension = ".txt",
                SizeBytes = fileBytes.LongLength,
                StorageProvider = Namespace,
                StorageKey = fileObject.StorageKey,
                ScanStatus = FileScanStatus.Clean
            };
            await dbContext.Attachments.AddAsync(attachment, cancellationToken);
        }
        else
        {
            attachment.FileObjectId = fileObject.Id;
            attachment.WorkspaceId = workspace.Id;
            attachment.OwnerType = AttachmentOwnerType.TaskItem;
            attachment.OwnerId = executionTask.Id;
            attachment.OwnerUserId = owner.Id;
            attachment.UploadedByUserId = owner.Id;
            attachment.StoredFileName = SourceFileName;
            attachment.FilePath = $"{Namespace}/task-source";
            attachment.ContentType = "text/plain";
            attachment.Extension = ".txt";
            attachment.SizeBytes = fileBytes.LongLength;
            attachment.StorageProvider = Namespace;
            attachment.StorageKey = fileObject.StorageKey;
            attachment.ScanStatus = FileScanStatus.Clean;
            if (attachment.IsDeleted)
            {
                attachment.Restore();
            }
        }

        await EnsureConversationAsync(dbContext, tenantId, workspace.Id, owner, observer, cancellationToken);
        await EnsureAnnouncementsAsync(dbContext, tenantId, workspace.Id, owner, cancellationToken);
        await EnsureAuditAsync(dbContext, tenantId, workspace.Id, project.Id, owner.Id, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await using var sourceStream = new MemoryStream(fileBytes, writable: false);
        var storageResult = await fileStorage.SaveAsync(fileObject.StorageKey, sourceStream, fileObject.ContentType, cancellationToken);
        if (!storageResult.IsSuccess)
        {
            throw new InvalidOperationException("The Issue #483 synthetic demo source file could not be stored.");
        }
    }

    private static async Task<User> EnsureUserAsync(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                DisplayName = displayName,
                Email = email.Trim(),
                NormalizedEmail = normalizedEmail,
                PasswordHash = passwordHasher.HashPassword(password),
                SystemRole = SystemRole.User,
                Status = UserStatus.Active
            };
            await dbContext.Users.AddAsync(user, cancellationToken);
            return user;
        }

        user.DisplayName = displayName;
        user.Email = email.Trim();
        user.NormalizedEmail = normalizedEmail;
        user.SystemRole = SystemRole.User;
        user.Status = UserStatus.Active;
        user.FailedLoginAttempts = 0;
        user.LockoutEndAt = null;
        if (user.IsDeleted)
        {
            user.Restore();
        }
        if (!passwordHasher.VerifyPassword(user.PasswordHash, password))
        {
            user.PasswordHash = passwordHasher.HashPassword(password);
        }
        return user;
    }

    private static async Task EnsureTenantMemberAsync(
        AppDbContext dbContext,
        Guid tenantId,
        User user,
        TenantUserRole role,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.TenantUsers.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.UserId == user.Id,
            cancellationToken);
        if (member is null)
        {
            await dbContext.TenantUsers.AddAsync(new TenantUser
            {
                TenantId = tenantId,
                UserId = user.Id,
                Role = role,
                Status = TenantUserStatus.Active,
                JoinedAt = ReferenceUtc
            }, cancellationToken);
            return;
        }

        member.Role = role;
        member.Status = TenantUserStatus.Active;
        member.JoinedAt = member.JoinedAt == default ? ReferenceUtc : member.JoinedAt;
    }

    private static async Task EnsureWorkspaceMemberAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Workspace workspace,
        User user,
        WorkspaceRole role,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.WorkspaceMembers.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.WorkspaceId == workspace.Id && candidate.UserId == user.Id,
            cancellationToken);
        if (member is null)
        {
            await dbContext.WorkspaceMembers.AddAsync(new WorkspaceMember
            {
                TenantId = tenantId,
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = role,
                Status = MembershipStatus.Active,
                JoinedAt = ReferenceUtc
            }, cancellationToken);
            return;
        }

        member.Role = role;
        member.Status = MembershipStatus.Active;
        member.JoinedAt ??= ReferenceUtc;
    }

    private static async Task EnsureProjectMemberAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Project project,
        User user,
        ProjectRole role,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.ProjectMembers.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.ProjectId == project.Id && candidate.UserId == user.Id,
            cancellationToken);
        if (member is null)
        {
            await dbContext.ProjectMembers.AddAsync(new ProjectMember
            {
                TenantId = tenantId,
                ProjectId = project.Id,
                UserId = user.Id,
                Role = role,
                JoinedAt = ReferenceUtc
            }, cancellationToken);
            return;
        }

        member.Role = role;
        member.JoinedAt = member.JoinedAt == default ? ReferenceUtc : member.JoinedAt;
    }

    private static async Task<TaskWorkflowStage> EnsureStageAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid definitionId,
        string name,
        TaskStageCategory category,
        long sortKey,
        bool isInitial,
        bool isTerminal,
        CancellationToken cancellationToken)
    {
        var stage = await dbContext.TaskWorkflowStages.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.ProjectId == projectId && candidate.InternalCategory == category,
            cancellationToken);
        if (stage is null)
        {
            stage = new TaskWorkflowStage
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                DefinitionId = definitionId,
                Name = name,
                InternalCategory = category,
                SortKey = sortKey,
                IsInitialStage = isInitial,
                IsTerminalStage = isTerminal,
                VersionNo = 1
            };
            await dbContext.TaskWorkflowStages.AddAsync(stage, cancellationToken);
            return stage;
        }

        stage.WorkspaceId = workspaceId;
        stage.DefinitionId = definitionId;
        stage.Name = name;
        stage.SortKey = sortKey;
        stage.IsInitialStage = isInitial;
        stage.IsTerminalStage = isTerminal;
        stage.VersionNo = 1;
        return stage;
    }

    private static async Task<TaskItem> EnsureTaskAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid ownerUserId,
        string title,
        string description,
        TaskItemStatus status,
        Guid workflowStageId,
        TaskPriority priority,
        int progressPercent,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.ProjectId == projectId && candidate.Title == title,
            cancellationToken);
        if (task is null)
        {
            task = new TaskItem
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                Title = title,
                CreatedByUserId = ownerUserId
            };
            await dbContext.TaskItems.AddAsync(task, cancellationToken);
        }
        else if (task.IsDeleted)
        {
            task.Restore();
        }

        task.WorkspaceId = workspaceId;
        task.Kind = WorkItemKind.Task;
        task.Description = description;
        task.WorkflowStageId = workflowStageId;
        task.Status = status;
        task.Priority = priority;
        task.IsBlocked = false;
        task.BlockedReason = null;
        task.PrimaryAssigneeUserId = ownerUserId;
        task.ProgressPercent = progressPercent;
        task.SortKey = progressPercent + 1000L;
        task.SortOrder = progressPercent;
        task.CreatedByUserId = ownerUserId;
        if (status != TaskItemStatus.WaitingReview)
        {
            task.ReviewStatus = TaskReviewStatus.None;
            task.ReviewSubmittedAt = null;
            task.ReviewerUserId = null;
        }
        if (status != TaskItemStatus.Completed)
        {
            task.CompletedAt = null;
        }
        return task;
    }

    private static async Task EnsureResearchPlanAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.ResearchPlans.SingleOrDefaultAsync(candidate => candidate.TaskItemId == taskId, cancellationToken);
        if (plan is null)
        {
            plan = new ResearchPlan
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                TaskItemId = taskId,
                VersionNo = 1
            };
            await dbContext.ResearchPlans.AddAsync(plan, cancellationToken);
        }

        if (plan.CurrentRevisionId.HasValue)
        {
            return;
        }

        var revision = new ResearchPlanRevision
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            TaskItemId = taskId,
            ResearchPlanId = plan.Id,
            RevisionNo = 1,
            CreatedByUserId = ownerUserId,
            CreatedAtUtc = ReferenceUtc
        };
        var step = new ResearchPlanStep
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            TaskItemId = taskId,
            ResearchPlanId = plan.Id,
            ResearchPlanRevisionId = revision.Id,
            SortOrder = 1,
            Title = "Review the synthetic source",
            Objective = "Build a durable report from the authorized Issue #483 demo attachment.",
            ScopeSummary = "One synthetic text file; no Web retrieval or external provider.",
            Status = ResearchPlanStepStatus.Planned
        };
        plan.CurrentRevisionId = revision.Id;
        await dbContext.ResearchPlanRevisions.AddAsync(revision, cancellationToken);
        await dbContext.ResearchPlanSteps.AddAsync(step, cancellationToken);
    }

    private static async Task EnsureConversationAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        User owner,
        User observer,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.WorkspaceId == workspaceId && candidate.Title == ConversationTitle,
            cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                Type = ConversationType.DirectMessage,
                Title = ConversationTitle,
                CreatedByUserId = owner.Id
            };
            await dbContext.Conversations.AddAsync(conversation, cancellationToken);
        }
        else
        {
            conversation.Type = ConversationType.DirectMessage;
            conversation.CreatedByUserId = owner.Id;
            conversation.IsArchived = false;
            conversation.IsLocked = false;
        }

        await EnsureConversationMemberAsync(dbContext, tenantId, conversation, owner, ConversationMemberRole.Admin, cancellationToken);
        await EnsureConversationMemberAsync(dbContext, tenantId, conversation, observer, ConversationMemberRole.Member, cancellationToken);

        var message = await dbContext.Messages.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.ConversationId == conversation.Id && candidate.Body == ConversationMessage,
            cancellationToken);
        if (message is null)
        {
            await dbContext.Messages.AddAsync(new Message
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ConversationId = conversation.Id,
                AuthorUserId = owner.Id,
                Body = ConversationMessage,
                Version = 1
            }, cancellationToken);
        }
        else if (message.IsDeleted)
        {
            message.Restore();
        }
    }

    private static async Task EnsureConversationMemberAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Conversation conversation,
        User user,
        ConversationMemberRole role,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.ConversationMembers.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.ConversationId == conversation.Id && candidate.UserId == user.Id,
            cancellationToken);
        if (member is null)
        {
            await dbContext.ConversationMembers.AddAsync(new ConversationMember
            {
                TenantId = tenantId,
                ConversationId = conversation.Id,
                UserId = user.Id,
                Role = role,
                CanRead = true,
                CanPost = true,
                CanManageMembers = role == ConversationMemberRole.Admin,
                CanCreateThread = true,
                JoinedAt = ReferenceUtc
            }, cancellationToken);
            return;
        }

        member.Role = role;
        member.CanRead = true;
        member.CanPost = true;
        member.CanManageMembers = role == ConversationMemberRole.Admin;
        member.CanCreateThread = true;
        member.LeftAt = null;
        member.RemovedAt = null;
        member.RemovedByUserId = null;
    }

    private static async Task EnsureAnnouncementsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        User owner,
        CancellationToken cancellationToken)
    {
        var published = await dbContext.Announcements.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.WorkspaceId == workspaceId && candidate.Title == PublishedAnnouncementTitle,
            cancellationToken);
        if (published is null)
        {
            published = new Announcement
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                AuthorUserId = owner.Id,
                Title = PublishedAnnouncementTitle,
                Body = "[issue-483-demo] Published synthetic announcement.",
                Priority = AnnouncementPriority.Normal,
                PublishedAt = ReferenceUtc
            };
            await dbContext.Announcements.AddAsync(published, cancellationToken);
        }
        else
        {
            published.AuthorUserId = owner.Id;
            published.Body = "[issue-483-demo] Published synthetic announcement.";
            published.Priority = AnnouncementPriority.Normal;
            published.PublishedAt = ReferenceUtc;
            published.ExpiresAt = null;
            if (published.IsDeleted)
            {
                published.Restore();
            }
        }

        await EnsureAnnouncementDraftAsync(
            dbContext,
            tenantId,
            workspaceId,
            owner.Id,
            DraftAnnouncementTitle,
            AnnouncementDraftStatus.Draft,
            null,
            cancellationToken);
        await EnsureAnnouncementDraftAsync(
            dbContext,
            tenantId,
            workspaceId,
            owner.Id,
            ScheduledAnnouncementTitle,
            AnnouncementDraftStatus.Scheduled,
            ScheduledForUtc,
            cancellationToken);
    }

    private static async Task EnsureAnnouncementDraftAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        Guid ownerUserId,
        string title,
        AnnouncementDraftStatus status,
        DateTimeOffset? scheduledForUtc,
        CancellationToken cancellationToken)
    {
        var draft = await dbContext.AnnouncementDrafts.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId && candidate.WorkspaceId == workspaceId && candidate.Title == title,
            cancellationToken);
        if (draft is null)
        {
            draft = new AnnouncementDraft
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                AuthorUserId = ownerUserId,
                Title = title
            };
            await dbContext.AnnouncementDrafts.AddAsync(draft, cancellationToken);
        }

        draft.AuthorUserId = ownerUserId;
        draft.Body = $"[issue-483-demo] {status} synthetic announcement.";
        draft.Priority = AnnouncementPriority.Normal;
        draft.IsPinned = false;
        draft.RequiresReadConfirmation = false;
        draft.ExpiresAt = null;
        draft.Status = status;
        draft.VersionNo = 1;
        draft.ScheduledForUtc = scheduledForUtc;
        draft.ScheduleTimeZoneId = scheduledForUtc.HasValue ? "UTC" : null;
        draft.ScheduleLocalDateTime = scheduledForUtc?.UtcDateTime;
        draft.ScheduleUtcOffsetMinutes = scheduledForUtc.HasValue ? 0 : null;
        draft.PublishedAnnouncementId = null;
        draft.PublishedAtUtc = null;
        draft.PublicationClaimOwner = null;
        draft.PublicationClaimToken = null;
        draft.PublicationClaimExpiresAtUtc = null;
        draft.NextPublicationAttemptAtUtc = null;
        draft.PublicationAttemptCount = 0;
        draft.LastPublicationFailureCode = null;
    }

    private static async Task EnsureAuditAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var audit = await dbContext.AuditLogs.SingleOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.Action == "DemoDatasetProvisioned" &&
            candidate.ProjectId == projectId,
            cancellationToken);
        if (audit is null)
        {
            audit = new AuditLog
            {
                TenantId = tenantId,
                Action = "DemoDatasetProvisioned",
                EntityType = "DemoDataset",
                EntityId = projectId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                ActorUserId = ownerUserId,
                CreatedAt = ReferenceUtc
            };
            await dbContext.AuditLogs.AddAsync(audit, cancellationToken);
        }

        audit.ActorUserId = ownerUserId;
        audit.EntityType = "DemoDataset";
        audit.EntityId = projectId;
        audit.WorkspaceId = workspaceId;
        audit.ProjectId = projectId;
        audit.Summary = "[issue-483-demo] Synthetic evidence for the repeatable demo dataset.";
        audit.MetadataJson = "{\"dataset\":\"issue-483-demo\",\"claim\":\"synthetic-demo-only\",\"evidence\":\"authorized-source-present\"}";
        audit.CorrelationId = Namespace;
        audit.CreatedAt = ReferenceUtc;
    }

    private static void EnsureOwnedDescription(string? current, string expected, string resourceType)
    {
        if (!string.Equals(current, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The reserved Issue #483 demo {resourceType} is not owned by this dataset. Run the isolated reset command instead of overwriting it.");
        }
    }
}
