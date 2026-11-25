using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Explicit Scenario provisioning for a disposable development PostgreSQL database.</summary>
public static class ProjectScenarioDevelopmentSchema
{
    public static async Task ProvisionAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await ProjectIdeDevelopmentSchema.EnsureExistsAsync(context, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS projectide_development.projectide_scenarios (
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "ScenarioId" uuid NOT NULL,
                "RevisionId" uuid NOT NULL,
                "Identity" jsonb NOT NULL,
                "CanonicalOverlay" text NOT NULL,
                CONSTRAINT "PK_projectide_scenarios" PRIMARY KEY ("TenantId", "ProjectId", "ScenarioId"),
                CONSTRAINT "FK_projectide_scenarios_projects_ProjectId_TenantId"
                    FOREIGN KEY ("ProjectId", "TenantId") REFERENCES public.projects ("Id", "TenantId") ON DELETE RESTRICT,
                CONSTRAINT "CK_projectide_scenarios_source_scope" CHECK (
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
                )
            );
            CREATE INDEX IF NOT EXISTS "IX_projectide_scenarios_ProjectId_TenantId"
                ON projectide_development.projectide_scenarios ("ProjectId", "TenantId");

            CREATE OR REPLACE FUNCTION projectide_development.prevent_projectide_scenario_update()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'A saved Scenario version is immutable';
            END;
            $$;
            CREATE OR REPLACE TRIGGER projectide_scenarios_immutable
                BEFORE UPDATE ON projectide_development.projectide_scenarios
                FOR EACH ROW EXECUTE FUNCTION projectide_development.prevent_projectide_scenario_update();
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
