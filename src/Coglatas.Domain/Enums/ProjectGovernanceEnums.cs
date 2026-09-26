namespace Coglatas.Domain.Enums;

/// <summary>
/// Canonical Project visibility values. A persisted null is the internal
/// LegacyUnknown migration state and is intentionally not a public enum value.
/// </summary>
public enum ProjectVisibility
{
    WorkspaceVisible = 0,
    MembersOnly = 1,
    Restricted = 2
}

/// <summary>
/// Provenance for the explicit Project activation command. Existing Projects
/// whose prior activation cannot be proven are migrated to LegacyUnknown.
/// </summary>
public enum ProjectActivationState
{
    LegacyUnknown = 0,
    NeverActivated = 1,
    Activated = 2
}
