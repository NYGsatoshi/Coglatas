using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Tests.Support;

/// <summary>
/// Explicit workflow fixture for tests that model an already-operational Project.
/// WPC-02D removed Project-insert workflow provisioning from AppDbContext, so
/// operational test graphs must now state this prerequisite directly.
/// </summary>
internal static class TaskWorkflowTestSeed
{
    public static TaskWorkflowDefinition AddDefault(AppDbContext context, Project project)
    {
        var definition = new TaskWorkflowDefinition
        {
            TenantId = project.TenantId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Name = "Default",
            ReviewEnforcementEnabled = true,
            VersionNo = 1
        };
        context.TaskWorkflowDefinitions.Add(definition);

        var stages = new (string Name, TaskStageCategory Category, bool Initial, bool Terminal)[]
        {
            ("Backlog", TaskStageCategory.Backlog, true, false),
            ("Todo", TaskStageCategory.Todo, false, false),
            ("In Progress", TaskStageCategory.InProgress, false, false),
            ("Review", TaskStageCategory.Review, false, false),
            ("Done", TaskStageCategory.Done, false, true),
            ("Cancelled", TaskStageCategory.Cancelled, false, true)
        };

        for (var index = 0; index < stages.Length; index++)
        {
            var stage = stages[index];
            context.TaskWorkflowStages.Add(new TaskWorkflowStage
            {
                TenantId = project.TenantId,
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                DefinitionId = definition.Id,
                Name = stage.Name,
                InternalCategory = stage.Category,
                SortKey = (index + 1) * 1000L,
                IsInitialStage = stage.Initial,
                IsTerminalStage = stage.Terminal,
                VersionNo = 1
            });
        }

        return definition;
    }
}
