using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
        await context.Set<ProjectScenarioRow>().AddAsync(new()
        {
            TenantId = scenario.Basis.Identity.TenantId,
            ProjectId = scenario.Basis.Identity.ProjectId,
            ScenarioId = scenario.Id.Value,
            RevisionId = scenario.Revision.Value,
            Identity = JsonSerializer.Serialize(scenario, PayloadOptions),
            CanonicalOverlay = canonicalOverlay
        }, cancellationToken);
    }

    public async Task<SavedProjectScenario?> FindAsync(Guid tenantId, Guid projectId, ScenarioId scenarioId,
        CancellationToken cancellationToken)
    {
        var metadata = await Scoped(tenantId, projectId, scenarioId)
            .Select(row => row.Identity)
            .SingleOrDefaultAsync(cancellationToken);
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
        Scoped(tenantId, projectId, scenarioId)
            .Where(row => row.RevisionId == revision.Value)
            .Select(row => row.CanonicalOverlay)
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<ProjectScenarioRow> Scoped(Guid tenantId, Guid projectId, ScenarioId scenarioId) =>
        context.Set<ProjectScenarioRow>().AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.ProjectId == projectId && row.ScenarioId == scenarioId.Value);
}

internal sealed class ProjectScenarioRow
{
    public Guid TenantId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid ScenarioId { get; init; }
    public Guid RevisionId { get; init; }
    public string Identity { get; init; } = "";
    public string CanonicalOverlay { get; init; } = "";
}

internal sealed class ProjectScenarioRowConfiguration : IEntityTypeConfiguration<ProjectScenarioRow>
{
    public void Configure(EntityTypeBuilder<ProjectScenarioRow> builder)
    {
        builder.ToTable("projectide_scenarios", table => table.HasCheckConstraint("CK_projectide_scenarios_source_scope", """
            (jsonb_typeof("Identity") = 'object'
            AND "Identity" ->> 'Id' = "ScenarioId"::text
            AND "Identity" ->> 'Revision' = "RevisionId"::text
            AND "Identity" #>> '{Basis,Identity,TenantId}' = "TenantId"::text
            AND "Identity" #>> '{Basis,Identity,ProjectId}' = "ProjectId"::text
            AND "Identity" #>> '{Input,Identity,TenantId}' = "TenantId"::text
            AND "Identity" #>> '{Input,Identity,ProjectId}' = "ProjectId"::text
            AND "Identity" #>> '{Input,Identity,ScenarioId}' = "ScenarioId"::text
            AND "Identity" #>> '{Input,Identity,ScenarioRevisionId}' = "RevisionId"::text
            AND "Identity" #>> '{Input,Identity,OverlayDigest}' ~ '^[0-9a-f]{64}$') IS TRUE
            """));
        builder.HasKey(row => new { row.TenantId, row.ProjectId, row.ScenarioId });
        builder.Property(row => row.Identity).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CanonicalOverlay).HasColumnType("text").IsRequired();
        builder.HasOne<Project>().WithMany().HasForeignKey(row => new { row.ProjectId, row.TenantId })
            .HasPrincipalKey(project => new { project.Id, project.TenantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
