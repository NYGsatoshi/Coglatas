using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class UiShellRepository(AppDbContext dbContext) : IUiShellRepository
{
    public async Task<IReadOnlyList<FeatureModule>> ListModulesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.FeatureModules.AsNoTracking().OrderBy(module => module.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PanelDefinition>> ListPanelsAsync(string? moduleKey, CancellationToken cancellationToken = default)
    {
        var query = dbContext.PanelDefinitions
            .AsNoTracking()
            .Include(panel => panel.FeatureModule)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(moduleKey))
        {
            query = query.Where(panel => panel.FeatureModule!.Key == moduleKey);
        }

        return await query.OrderBy(panel => panel.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetPanelKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = await dbContext.PanelDefinitions.AsNoTracking().Select(panel => panel.Key).ToListAsync(cancellationToken);
        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<UserLayout>> ListLayoutsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.UserLayouts
            .AsNoTracking()
            .Where(layout => layout.UserId == userId)
            .OrderBy(layout => layout.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<UserLayout?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken = default)
    {
        return dbContext.UserLayouts.FirstOrDefaultAsync(layout => layout.Id == layoutId, cancellationToken);
    }

    public Task<UserLayout?> GetCurrentLayoutAsync(Guid userId, LayoutScopeType scopeType, Guid? scopeId, CancellationToken cancellationToken = default)
    {
        return dbContext.UserLayouts
            .AsNoTracking()
            .Where(layout => layout.UserId == userId && layout.ScopeType == scopeType && layout.ScopeId == scopeId)
            .OrderByDescending(layout => layout.IsDefault)
            .ThenByDescending(layout => layout.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddLayoutAsync(UserLayout layout, CancellationToken cancellationToken = default)
    {
        await dbContext.UserLayouts.AddAsync(layout, cancellationToken);
    }

    public void RemoveLayout(UserLayout layout)
    {
        dbContext.UserLayouts.Remove(layout);
    }

    public async Task<IReadOnlyList<CommandDefinition>> ListCommandsAsync(CommandContextType? contextType, CancellationToken cancellationToken = default)
    {
        var query = dbContext.CommandDefinitions
            .AsNoTracking()
            .Include(command => command.FeatureModule)
            .Where(command => command.IsEnabled);

        if (contextType.HasValue)
        {
            query = query.Where(command => command.ContextType == contextType.Value || command.ContextType == CommandContextType.Global);
        }

        return await query.OrderBy(command => command.SortOrder).ToListAsync(cancellationToken);
    }

    public Task<RadialMenuProfile?> GetDefaultRadialMenuAsync(CommandContextType contextType, CancellationToken cancellationToken = default)
    {
        return dbContext.RadialMenuProfiles
            .AsNoTracking()
            .Include(profile => profile.Items)
            .FirstOrDefaultAsync(profile => profile.ContextType == contextType && profile.IsDefault, cancellationToken);
    }
}
