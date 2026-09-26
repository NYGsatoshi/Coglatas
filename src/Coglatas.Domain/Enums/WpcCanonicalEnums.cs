namespace Coglatas.Domain.Enums;

/// <summary>
/// Canonical delegated-capability scope kinds. Capability-specific services
/// remain responsible for validating the resource represented by ScopeId.
/// </summary>
public enum CapabilityScopeType
{
    Tenant = 0,
    Workspace = 1
}

public enum ConversationVisibility
{
    PublicWithinScope = 0,
    Private = 1
}

/// <summary>
/// Internal identity for product-owned default Conversations. Display names
/// are intentionally not identity.
/// </summary>
public enum ConversationDefaultKind
{
    WorkspaceGeneral = 0,
    ProjectGeneral = 1
}
