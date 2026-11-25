using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Explicit setup for separately composed ProjectIDE development fixtures.</summary>
public static class ProjectIdeDevelopmentSchema
{
    public const string Name = "projectide_development";

    public static Task EnsureExistsAsync(AppDbContext context, CancellationToken cancellationToken = default) =>
        context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS projectide_development", cancellationToken);
}
