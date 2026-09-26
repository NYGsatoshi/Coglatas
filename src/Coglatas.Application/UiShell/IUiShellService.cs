using Coglatas.Application.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.UiShell;

public interface IUiShellService
{
    Task<Result<IReadOnlyList<FeatureModuleResponse>>> ListModulesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PanelDefinitionResponse>>> ListPanelsAsync(string? moduleKey, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UserLayoutResponse>>> ListLayoutsAsync(CancellationToken cancellationToken = default);

    Task<Result<UserLayoutResponse>> GetCurrentLayoutAsync(LayoutScopeType scopeType, Guid? scopeId, CancellationToken cancellationToken = default);

    Task<Result<UserLayoutResponse>> CreateLayoutAsync(SaveUserLayoutRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserLayoutResponse>> UpdateLayoutAsync(Guid layoutId, SaveUserLayoutRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteLayoutAsync(Guid layoutId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CommandDefinitionResponse>>> ListCommandsAsync(CommandContextType? contextType, Guid? contextId, CancellationToken cancellationToken = default);

    Task<Result<RadialMenuResponse>> GetRadialMenuAsync(CommandContextType contextType, Guid? contextId, CancellationToken cancellationToken = default);
}
