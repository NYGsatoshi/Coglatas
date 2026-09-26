using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Interfaces;

public interface IUiShellRepository
{
    Task<IReadOnlyList<FeatureModule>> ListModulesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PanelDefinition>> ListPanelsAsync(string? moduleKey, CancellationToken cancellationToken = default);

    Task<HashSet<string>> GetPanelKeysAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserLayout>> ListLayoutsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserLayout?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken = default);

    Task<UserLayout?> GetCurrentLayoutAsync(Guid userId, LayoutScopeType scopeType, Guid? scopeId, CancellationToken cancellationToken = default);

    Task AddLayoutAsync(UserLayout layout, CancellationToken cancellationToken = default);

    void RemoveLayout(UserLayout layout);

    Task<IReadOnlyList<CommandDefinition>> ListCommandsAsync(CommandContextType? contextType, CancellationToken cancellationToken = default);

    Task<RadialMenuProfile?> GetDefaultRadialMenuAsync(CommandContextType contextType, CancellationToken cancellationToken = default);
}
