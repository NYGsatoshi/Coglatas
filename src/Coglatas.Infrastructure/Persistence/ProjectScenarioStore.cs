using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Scoped PostgreSQL storage. The owning use case authorizes metadata and private values separately.</summary>
public sealed class ProjectScenarioStore(AppDbContext context) : IProjectScenarioStore
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public bool CanStartSave() => context.Database.CurrentTransaction is null &&
        System.Transactions.Transaction.Current is null && !context.ChangeTracker.HasChanges();

    public async Task StageAsync(SavedProjectScenario scenario, string canonicalOverlay, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Saving a Scenario requires the shared idempotency transaction.");

        var tenantId = scenario.Basis.Identity.TenantId;
        var projectId = scenario.Basis.Identity.ProjectId;
        var identity = JsonSerializer.Serialize(scenario, PayloadOptions);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projectide_development.projectide_scenarios
                ("TenantId", "ProjectId", "ScenarioId", "RevisionId", "Identity", "CanonicalOverlay")
            VALUES ({tenantId}, {projectId}, {scenario.Id.Value}, {scenario.Revision.Value}, CAST({identity} AS jsonb), {canonicalOverlay})
            """, cancellationToken);
    }

    public async Task<SavedProjectScenario?> FindAsync(Guid tenantId, Guid projectId, ScenarioId scenarioId,
        CancellationToken cancellationToken)
    {
        var metadata = await context.Database.SqlQuery<string>($"""
            SELECT "Identity"::text AS "Value"
            FROM projectide_development.projectide_scenarios
            WHERE "TenantId" = {tenantId} AND "ProjectId" = {projectId} AND "ScenarioId" = {scenarioId.Value}
            """).SingleOrDefaultAsync(cancellationToken);
        if (metadata is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<SavedProjectScenario>(metadata, PayloadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task<string?> FindOverlayAsync(Guid tenantId, Guid projectId, ScenarioId scenarioId,
        ScenarioRevisionId revision, CancellationToken cancellationToken) =>
        context.Database.SqlQuery<string>($"""
            SELECT "CanonicalOverlay" AS "Value"
            FROM projectide_development.projectide_scenarios
            WHERE "TenantId" = {tenantId} AND "ProjectId" = {projectId}
                AND "ScenarioId" = {scenarioId.Value} AND "RevisionId" = {revision.Value}
            """).SingleOrDefaultAsync(cancellationToken);
}
