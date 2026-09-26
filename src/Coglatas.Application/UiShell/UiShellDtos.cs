using Coglatas.Domain.Enums;

namespace Coglatas.Application.UiShell;

public sealed record FeatureModuleResponse(
    Guid Id,
    string ModuleKey,
    string DisplayName,
    string? Description,
    string DefaultRoute,
    string? Icon,
    int SortOrder);

public sealed record PanelDefinitionResponse(
    Guid Id,
    string PanelKey,
    string Title,
    string ModuleKey,
    int DefaultWidth,
    int DefaultHeight,
    string DefaultPosition,
    bool IsDockable,
    bool IsClosable,
    int SortOrder);

public sealed record UserLayoutResponse(
    Guid Id,
    string LayoutName,
    LayoutScopeType ScopeType,
    Guid? ScopeId,
    string LayoutJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveUserLayoutRequest(string LayoutName, LayoutScopeType ScopeType, Guid? ScopeId, string LayoutJson);

public sealed record CommandDefinitionResponse(
    Guid Id,
    string CommandKey,
    string Label,
    string? Icon,
    string? ModuleKey,
    CommandActionType ActionType,
    string? Route,
    CommandContextType ContextType,
    int SortOrder);

public sealed record RadialMenuResponse(string ProfileKey, string Name, CommandContextType ContextType, IReadOnlyList<RadialMenuItemResponse> Items);

public sealed record RadialMenuItemResponse(
    RadialMenuDirection Direction,
    string CommandKey,
    string Label,
    string? Icon,
    bool IsEnabled,
    int SortOrder);
