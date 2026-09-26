namespace Coglatas.Domain.Enums;

/// <summary>
/// A bounded planning annotation for an immutable Research Plan step. These
/// values describe the saved plan, not the Task lifecycle or runtime state.
/// </summary>
public enum ResearchPlanStepStatus
{
    Planned = 0,
    Ready = 1,
    Blocked = 2,
    Deferred = 3
}
